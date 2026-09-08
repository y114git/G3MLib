using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using G3MLib.DataFile.Models;
using static G3MLib.DataFile.GameMakerReader;

namespace G3MLib.DataFile
{
    public abstract class GameMakerChunk
    {
        public abstract string Name { get; }
        public uint Length { get; internal set; }

        internal abstract void SerializeChunk(GameMakerWriter writer);
        internal abstract void UnserializeChunk(GameMakerReader reader);
        internal abstract uint UnserializeObjectCount(GameMakerReader reader);

        public void Serialize(GameMakerWriter writer)
        {
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
                        GameMakerGeneralInfo generalInfo = Name == "GEN8" ? ((GameMakerChunkGEN8)this).Object : writer.gameMakerData?.GeneralInfo;
                        // These versions introduced new padding
                        // all chunks now start on 16-byte boundaries
                        // (but the padding is included with length of previous chunk)
                        if (generalInfo != null && (generalInfo.Major >= 2 || (generalInfo.Major == 1 && generalInfo.Build >= 9999)))
                        {
                            int e = writer.gameMakerData.PaddingAlignException;
                            uint pad = (e == -1 ? 16 : (uint)e);
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
            catch (Exception e)
            {
                throw new GameMakerSerializationException(e.Message + "\nat " + writer.Position.ToString("X8") + " while writing chunk " + Name, e);
            }
        }

        public static GameMakerChunk Unserialize(GameMakerReader reader)
        {
            string name = "(unknown)";
            try
            {
                // Read name and length
                name = reader.ReadChars(4);
                uint length = reader.ReadUInt32();

                // Find chunk instance, or create one if not already created (when errors occur during object counting)
                if (!reader.gameMakerData.FORM.Chunks.TryGetValue(name, out GameMakerChunk chunk))
                {
                    if (!GameMakerChunkFORM.ChunkConstructors.TryGetValue(name, out Func<GameMakerChunk> instantiator))
                    {
                        throw new IOException($"Unknown chunk \"{name}\"");
                    }
                    chunk = instantiator();
                    reader.gameMakerData.FORM.Chunks[name] = chunk;
                }
                Util.DebugUtil.Assert(chunk.Name == name,
                                      $"Chunk name mismatch: expected \"{name}\", got \"{chunk.Name}\".");
                chunk.Length = length;

                // Read chunk contents
                reader.SubmitMessage("Reading chunk " + chunk.Name);
                EnsureLengthOperation lenReader = reader.EnsureLengthFromHere(chunk.Length);
                reader.CopyChunkToBuffer(length);
                chunk.UnserializeChunk(reader);

                // Process padding
                reader.SwitchReaderType(false);
                if (name != "FORM" && name != reader.LastChunkName)
                {
                    GameMakerGeneralInfo generalInfo = name == "GEN8" ? ((GameMakerChunkGEN8)chunk).Object : reader.gameMakerData.GeneralInfo;

                    // These versions introduced new padding
                    // all chunks now start on 16-byte boundaries
                    // (but the padding is included with length of previous chunk)
                    if (generalInfo.Major >= 2 || (generalInfo.Major == 1 && generalInfo.Build >= 9999))
                    {
                        int e = reader.gameMakerData.PaddingAlignException;
                        uint pad = (e == -1 ? 16 : (uint)e);
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
            catch (Exception e)
            {
                throw new GameMakerSerializationException(e.Message + "\nat " + reader.AbsPosition.ToString("X8") + " while reading chunk " + name, e);
            }
        }
        public static (uint, GameMakerChunk) CountChunkChildObjects(GameMakerReader reader)
        {
            string name = "(unknown)";
            try
            {
                // Read name and length
                name = reader.ReadChars(4);
                uint length = reader.ReadUInt32();

                // Create chunk instance
                if (!GameMakerChunkFORM.ChunkConstructors.TryGetValue(name, out Func<GameMakerChunk> instantiator))
                {
                    throw new IOException($"Unknown chunk \"{name}\"");
                }
                GameMakerChunk chunk = instantiator();
                Util.DebugUtil.Assert(chunk.Name == name,
                                      $"Chunk name mismatch: expected \"{name}\", got \"{chunk.Name}\".");
                chunk.Length = length;

                // Count objects in chunk
                long chunkStart = reader.Position;
                reader.SubmitMessage("Counting objects of chunk " + chunk.Name);
                reader.CopyChunkToBuffer(length);
                uint count = chunk.UnserializeObjectCount(reader);

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
                throw new GameMakerSerializationException(e.Message + "\nat " + reader.AbsPosition.ToString("X8") + " while counting objects of chunk " + name, e);
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
        public GameMakerPointerList<T> List { get; set; } = new GameMakerPointerList<T>();
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
            for (int i = 0; i < List.Count; i++)
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
            uint baseAddr = writer.Position;
            for (int i = 0; i < List.Count; i++)
                writer.Write(0);
            for (int i = 0; i < List.Count; i++)
            {
                if (Align)
                {
                    while (writer.Position % Alignment != 0)
                        writer.Write((byte)0);
                }
                if (List[i] is null)
                    continue;
                uint returnTo = writer.Position;
                writer.Position = baseAddr + ((uint)i * 4);
                writer.Write(returnTo);
                writer.Position = returnTo;
                writer.WriteGameMakerObject(List[i]);
            }
        }

        internal override void UnserializeChunk(GameMakerReader reader)
        {
            uint count = reader.ReadUInt32();
            if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
                throw new IOException("Aligned list pointers exceed the available input.");
            List.SetCapacity(count);
            uint realCount = count;
            BitArray gm2024_11_WhatToSkip = null;
            bool preserveNullEntries = reader.gameMakerData.IsVersionAtLeast(2024, 11);
            if (preserveNullEntries && count > 0)
                gm2024_11_WhatToSkip = new((int)count, false);

            for (uint i = 0; i < count; i++)
            {
                uint readValue = reader.ReadUInt32();
                Align &= (readValue % Alignment == 0);
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
                uint readValue = reader.ReadUInt32();
                Align &= (readValue % Alignment == 0);
                if (readValue != 0) continue;

                if (reader.gameMakerData.GeneralInfo.BytecodeVersion >= 13 && !reader.gameMakerData.IsVersionAtLeast(2024, 11))
                {
                    reader.SubmitWarning("Zero values in an AlignUpdatedListChunk encountered on potential pre-2024.11 Bytecode 13+!");
                }
                count--;
            }
            if (count == 0)
                return 0;

            Type t = typeof(T);
            if (t != typeof(GameMakerBackground) && t != typeof(GameMakerString))
                throw new InvalidOperationException(
                    "\"GameMakerAlignUpdatedListChunk<T>\" supports the count unserialization only for backgrounds and strings.");

            return count;
        }
    }

    public abstract class GameMakerSimpleListChunk<T> : GameMakerChunk, IGameMakerSimpleListChunk where T : GameMakerObject, new()
    {
        public GameMakerSimpleList<T> List { get; set; } = new GameMakerSimpleList<T>();

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
            uint count = reader.ReadUInt32();
            Type type = typeof(T);
            uint minimumObjectSize = type.IsAssignableTo(typeof(IStaticChildObjectsSize))
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
}
