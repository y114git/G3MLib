using System.Globalization;
using System.Collections;
using System.IO;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile;

public abstract class GameMakerChunk
{
    public abstract string Name { get; }
    public uint Length { get; internal set; }

    /// <summary>
    /// The absolute offset of the start of the chunk header (its name) in the file the chunk was read from. Zero for chunks that were not read.
    /// </summary>
    public long Offset { get; internal set; }

    internal abstract void SerializeChunk(GameMakerWriter writer);
    internal abstract void UnserializeChunk(GameMakerReader reader);
    internal abstract uint UnserializeObjectCount(GameMakerReader reader);

    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        try
        {
            Util.DebugUtil.Assert(Name != null);
            writer.Write(Name.ToCharArray());
            var lenWriter = writer.WriteLengthHere();

            writer.SubmitMessage("Writing chunk " + Name);
            lenWriter.FromHere();
            SerializeChunk(writer);

            if (Name != "FORM" && Name != writer.LastChunkName)
            {
                if (writer.IsDebugData)
                {
                    while (writer.Position % 16 != 0)
                        writer.Write((byte)0);
                }
                else
                {
                    var generalInfo = Name == "GEN8" ? ((GameMakerChunkGEN8)this).Object : writer.gameMakerData?.GeneralInfo;
                    // These versions introduced new padding
                    // all chunks now start on 16-byte boundaries
                    // (but the padding is included with length of previous chunk)
                    if (generalInfo != null && (generalInfo.Major >= 2 || generalInfo.Major == 1 && generalInfo.Build >= 9999))
                    {
                        var e = writer.gameMakerData.PaddingAlignException;
                        var pad = e == -1 ? 16 : (uint)e;
                        while (writer.Position % pad != 0)
                        {
                            writer.Write((byte)0);
                        }
                    }
                }
            }

            Length = lenWriter.ToHere();
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException(e.Message + " in chunk " + Name, e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GameMakerSerializationException(e.Message + "\nat " + writer.Position.ToString("X8", CultureInfo.InvariantCulture) + " while writing chunk " + Name, e);
        }
    }

    public static GameMakerChunk Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var name = "(unknown)";
        try
        {
            // Read name and length
            var offset = reader.AbsPosition;
            name = reader.ReadChars(4);
            var length = reader.ReadUInt32();

            // Find chunk instance, or create one if not already created (when errors occur during object counting)
            if (!reader.gameMakerData.FORM.Chunks.TryGetValue(name, out var chunk))
            {
                chunk = GameMakerChunkFORM.ChunkConstructors.TryGetValue(name, out var instantiator)
                    ? instantiator()
                    : new GameMakerRawChunk(name);
                reader.gameMakerData.FORM.Chunks[name] = chunk;
            }
            Util.DebugUtil.Assert(chunk.Name == name,
                                  $"Chunk name mismatch: expected \"{name}\", got \"{chunk.Name}\".");
            chunk.Length = length;
            chunk.Offset = offset;

            // Read chunk contents
            reader.SubmitMessage("Reading chunk " + chunk.Name);
            var lenReader = reader.EnsureLengthFromHere(chunk.Length);
            reader.CopyChunkToBuffer(length);
            reader.gameMakerData.ReadingChunkName = name;
            try
            {
                if (chunk is GameMakerRawChunk)
                    reader.SubmitWarning($"Unknown chunk \"{name}\" is kept as raw data and not modelled", false, true);
                chunk.UnserializeChunk(reader);
            }
            finally
            {
                reader.gameMakerData.ReadingChunkName = null;
            }

            // Process padding
            reader.SwitchReaderType(false);
            if (name != "FORM" && name != reader.LastChunkName)
            {
                var generalInfo = name == "GEN8" ? ((GameMakerChunkGEN8)chunk).Object : reader.gameMakerData.GeneralInfo;

                // These versions introduced new padding
                // all chunks now start on 16-byte boundaries
                // (but the padding is included with length of previous chunk)
                if (generalInfo.Major >= 2 || generalInfo.Major == 1 && generalInfo.Build >= 9999)
                {
                    var e = reader.gameMakerData.PaddingAlignException;
                    var pad = e == -1 ? 16 : (uint)e;
                    while (reader.Position % pad != 0)
                    {
                        if (reader.ReadByte() != 0)
                        {
                            reader.Position -= 1;
                            if (reader.Position % 4 == 0)
                                reader.gameMakerData.PaddingAlignException = 4;
                            else
                                reader.gameMakerData.PaddingAlignException = 1;
                            break;
                        }
                    }
                }
            }

            // Ensure full length was read
            lenReader.ToHere();

            return chunk;
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException(e.Message + " in chunk " + name, e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GameMakerSerializationException(e.Message + "\nat " + reader.AbsPosition.ToString("X8", CultureInfo.InvariantCulture) + " while reading chunk " + name, e);
        }
    }
    public static (uint, GameMakerChunk) CountChunkChildObjects(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var name = "(unknown)";
        try
        {
            // Read name and length
            var offset = reader.AbsPosition;
            name = reader.ReadChars(4);
            var length = reader.ReadUInt32();

            // Create chunk instance
            var chunk = GameMakerChunkFORM.ChunkConstructors.TryGetValue(name, out var instantiator)
                ? instantiator()
                : new GameMakerRawChunk(name);
            Util.DebugUtil.Assert(chunk.Name == name,
                                  $"Chunk name mismatch: expected \"{name}\", got \"{chunk.Name}\".");
            chunk.Length = length;
            chunk.Offset = offset;

            // Count objects in chunk
            var chunkStart = reader.Position;
            reader.SubmitMessage("Counting objects of chunk " + chunk.Name);
            reader.CopyChunkToBuffer(length);
            reader.gameMakerData.ReadingChunkName = name;
            uint count;
            try
            {
                count = chunk.UnserializeObjectCount(reader);
            }
            finally
            {
                reader.gameMakerData.ReadingChunkName = null;
            }

            // Advance beyond chunk length (parts of the chunk may have been skipped)
            reader.SwitchReaderType(false);
            reader.Position = chunkStart + chunk.Length;

            return (count, chunk);
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException(e.Message + " in chunk " + name, e);
        }
        catch (Exception e)
        {
            throw new GameMakerSerializationException(e.Message + "\nat " + reader.AbsPosition.ToString("X8", CultureInfo.InvariantCulture) + " while counting objects of chunk " + name, e);
        }
    }
}

public interface IGameMakerSingleChunk
{
    GameMakerObject GetObject();
}
public interface IGameMakerListChunk
{
    IList GetList();
    void GenerateIndexDict();
    void ClearIndexDict();
}
public interface IGameMakerSimpleListChunk
{
    IList GetList();
}


public abstract class GameMakerSingleChunk<T> : GameMakerChunk, IGameMakerSingleChunk where T : GameMakerObject, new()
{
    public T Object { get; set; }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        writer.WriteGameMakerObject(Object);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        Object = reader.ReadGameMakerObject<T>();
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        uint count = 1;

        count += reader.GetChildObjectCount<T>();

        return count;
    }

    public GameMakerObject GetObject() => Object;

    public override string ToString()
    {
        return Object.ToString();
    }
}

public abstract class GameMakerListChunk<T> : GameMakerChunk, IGameMakerListChunk where T : GameMakerObject, new()
{
    public GameMakerPointerList<T> List { get; set; } = [];
    public Dictionary<T, int> IndexDict { get; set; }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        List.Serialize(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        List.Unserialize(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        return GameMakerPointerList<T>.UnserializeChildObjectCount(reader);
    }

    public IList GetList() => List;
    public void GenerateIndexDict()
    {
        IndexDict = new(List.Count);
        for (var i = 0; i < List.Count; i++)
        {
            if (List[i] is not null)
                IndexDict[List[i]] = i;
        }
    }
    public void ClearIndexDict()
    {
        IndexDict = null;
    }
}

public abstract class GameMakerAlignUpdatedListChunk<T> : GameMakerListChunk<T> where T : GameMakerObject, new()
{
    public bool Align { get; set; } = true;
    protected int Alignment = 4;

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        writer.Write(List.Count);
        var baseAddr = writer.Position;
        for (var i = 0; i < List.Count; i++)
            writer.Write(0);
        for (var i = 0; i < List.Count; i++)
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            if (Align)
            {
                while (writer.Position % Alignment != 0)
                    writer.Write((byte)0);
            }
            if (List[i] is null)
                continue;
            var returnTo = writer.Position;
            writer.Position = baseAddr + (uint)i * 4;
            writer.Write(returnTo);
            writer.Position = returnTo;
            writer.WriteGameMakerObject(List[i]);
        }
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        var count = reader.ReadUInt32();
        if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
            throw new IOException("Aligned list pointers exceed the available input.");
        List.SetCapacity(count);
        var realCount = count;
        BitArray? gm2024_11_WhatToSkip = null;
        var preserveNullEntries = reader.gameMakerData.IsVersionAtLeast(2024, 11);
        if (preserveNullEntries && count > 0)
            gm2024_11_WhatToSkip = new((int)count, false);

        for (uint i = 0; i < count; i++)
        {
            var readValue = reader.ReadUInt32();
            Align &= readValue % Alignment == 0;
            if (readValue != 0) continue;

            if (preserveNullEntries && gm2024_11_WhatToSkip is not null)
            {
                // This is "normal" and is likely a object removed by GMAC.
                gm2024_11_WhatToSkip.Set((int)i, true);
                continue;
            }

            realCount--;
        }

        for (uint i = 0; i < (preserveNullEntries ? count : realCount); i++)
        {
            reader.CancellationToken.ThrowIfCancellationRequested();
            if (gm2024_11_WhatToSkip is not null && gm2024_11_WhatToSkip.Get((int)i))
            {
                List.InternalAdd(default);
                continue;
            }

            if (Align)
            {
                while (reader.AbsPosition % Alignment != 0)
                    if (reader.ReadByte() != 0)
                        throw new IOException("AlignUpdatedListChunk padding error");
            }
            List.InternalAdd(reader.ReadGameMakerObject<T>());
        }
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        uint claimedCount = reader.ReadUInt32(), count = claimedCount;
        if (count == 0)
            return 0;

        for (uint i = 0; i < claimedCount; i++)
        {
            var readValue = reader.ReadUInt32();
            Align &= readValue % Alignment == 0;
            if (readValue != 0) continue;

            if (reader.gameMakerData.GeneralInfo.BytecodeVersion >= 13 && !reader.gameMakerData.IsVersionAtLeast(2024, 11))
            {
                reader.SubmitWarning("Zero values in an AlignUpdatedListChunk encountered on potential pre-2024.11 Bytecode 13+!");
            }
            count--;
        }
        if (count == 0)
            return 0;

        var t = typeof(T);
        if (t != typeof(GameMakerBackground) && t != typeof(GameMakerString))
            throw new InvalidOperationException(
                "\"GameMakerAlignUpdatedListChunk<T>\" supports the count unserialization only for backgrounds and strings.");

        return count;
    }
}

public abstract class GameMakerSimpleListChunk<T> : GameMakerChunk, IGameMakerSimpleListChunk where T : GameMakerObject, new()
{
    public GameMakerSimpleList<T> List { get; set; } = [];

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        List.Serialize(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        List.Unserialize(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        var count = reader.ReadUInt32();
        var type = typeof(T);
        var minimumObjectSize = type.IsAssignableTo(typeof(IStaticChildObjectsSize))
            ? reader.GetStaticChildObjectsSize(type)
            : sizeof(uint);
        if ((ulong)count * minimumObjectSize > (ulong)(reader.Length - reader.Position))
            throw new IOException("Simple list data exceeds the available input.");

        return count;
    }

    public IList GetList() => List;
}

public abstract class GameMakerEmptyChunk : GameMakerChunk
{
    internal override void SerializeChunk(GameMakerWriter writer)
    {
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader) => 0;
}

public abstract class GameMakerUnsupportedChunk : GameMakerChunk
{
    public byte[] RawData { get; set; }
    internal override void SerializeChunk(GameMakerWriter writer)
    {
        writer.Write(RawData);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        RawData = reader.ReadBytes(checked((int)Length));
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader) => 0;
}

/// <summary>
/// A chunk this library does not know, kept with its name and bytes so it is written back in place.
/// </summary>
public sealed class GameMakerRawChunk(string name) : GameMakerUnsupportedChunk
{
    public override string Name { get; } = name;
}
