using System.Globalization;
using System.IO;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using static G3MLib.DataFile.Models.GameMakerRoom;

namespace G3MLib.DataFile;

public class GameMakerChunkFORM : GameMakerChunk
{
    public override string Name => "FORM";

    /// <summary>
    /// Bytes found after the end of the FORM chunk, written back unchanged.
    /// </summary>
    public byte[] TrailingBytes { get; set; } = [];

    // Written order set by AddChunk, RemoveChunk and SetChunkOrder; null while the dictionary order is the order.
    private List<string> _order;

    /// <summary>
    /// Lookup from a chunk name to its loaded instance. Enumeration order is the written order unless <see cref="AddChunk"/>,
    /// <see cref="RemoveChunk"/> or <see cref="SetChunkOrder"/> were used; use those to change the set of chunks, because a
    /// dictionary does not keep its order after a removal.
    /// </summary>
    public Dictionary<string, GameMakerChunk> Chunks
    {
        get;
        set
        {
            field = value;
            _order = null;
        }
    } = [];

    /// <summary>
    /// The order GameMaker writes its chunks in, for every chunk type this library knows. Unknown chunks are not in this list.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalOrder =
    [
        "GEN8", "OPTN", "LANG", "EXTN", "SOND", "AGRP", "SPRT", "BGND", "PATH", "SCPT", "GLOB", "GMEN", "SHDR", "FONT", "TMLN", "OBJT",
        "FEDS", "ACRV", "SEQN", "TAGS", "ROOM", "UILR", "DAFL", "EMBI", "PSEM", "PSYS", "TPAG", "TGIN", "CODE", "VARI", "FUNC", "FEAT",
        "STRG", "TXTR", "AUDO"
    ];

    /// <summary>
    /// The chunk names in the order they are written.
    /// </summary>
    public IReadOnlyList<string> ChunkOrder => EffectiveOrder();

    private List<string> EffectiveOrder()
    {
        if (_order is null)
        {
            return [.. Chunks.Keys];
        }
        List<string> order = [.. _order.Where(Chunks.ContainsKey)];
        order.AddRange(Chunks.Keys.Where(name => !order.Contains(name)));
        return order;
    }

    /// <summary>
    /// Adds a chunk, or replaces the chunk of the same name where it is. A new known chunk goes to its canonical place
    /// (<see cref="CanonicalOrder"/>); unknown chunks keep the place of their neighbours.
    /// </summary>
    public void AddChunk(GameMakerChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var order = EffectiveOrder();
        if (Chunks.TryGetValue(chunk.Name, out var old))
        {
            ChunksTypeDict.Remove(old.GetType());
        }
        else
        {
            var rank = CanonicalRank(chunk.Name);
            var at = order.Count;
            if (rank >= 0)
            {
                // Before the first known chunk that comes later in the canonical order
                for (var i = 0; i < order.Count; i++)
                {
                    if (CanonicalRank(order[i]) > rank)
                    {
                        at = i;
                        break;
                    }
                }
            }
            order.Insert(at, chunk.Name);
        }
        Chunks[chunk.Name] = chunk;
        ChunksTypeDict[chunk.GetType()] = chunk;
        _order = order;
    }

    /// <summary>
    /// Removes a chunk from the lookups and the written order.
    /// </summary>
    /// <returns><see langword="false"/> when no chunk has that name.</returns>
    public bool RemoveChunk(string name)
    {
        if (!Chunks.Remove(name, out var chunk))
        {
            return false;
        }
        ChunksTypeDict.Remove(chunk.GetType());
        _order = EffectiveOrder();
        _order.Remove(name);
        return true;
    }

    /// <summary>
    /// Sets the written order. Chunks not named keep their order and follow the named ones.
    /// </summary>
    /// <exception cref="ArgumentException">A name is not a loaded chunk. The order is not changed.</exception>
    public void SetChunkOrder(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        List<string> order = [];
        foreach (var name in names)
        {
            if (!Chunks.ContainsKey(name))
            {
                throw new ArgumentException($"No chunk named \"{name}\" is loaded.", nameof(names));
            }
            if (!order.Contains(name))
            {
                order.Add(name);
            }
        }
        order.AddRange(EffectiveOrder().Where(name => !order.Contains(name)));
        _order = order;
    }

    private static int CanonicalRank(string name)
    {
        for (var i = 0; i < CanonicalOrder.Count; i++)
        {
            if (CanonicalOrder[i] == name)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Lookup from a chunk type to its loaded instance.
    /// </summary>
    public Dictionary<Type, GameMakerChunk> ChunksTypeDict { get; set; } = [];

    /// <summary>
    /// Constructors for all chunk types.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<GameMakerChunk>> ChunkConstructors = new Dictionary<string, Func<GameMakerChunk>>()
    {
        { "GEN8", () => new GameMakerChunkGEN8() },
        { "OPTN", () => new GameMakerChunkOPTN() },
        { "LANG", () => new GameMakerChunkLANG() },
        { "EXTN", () => new GameMakerChunkEXTN() },
        { "SOND", () => new GameMakerChunkSOND() },
        { "AGRP", () => new GameMakerChunkAGRP() },
        { "SPRT", () => new GameMakerChunkSPRT() },
        { "BGND", () => new GameMakerChunkBGND() },
        { "PATH", () => new GameMakerChunkPATH() },
        { "SCPT", () => new GameMakerChunkSCPT() },
        { "GLOB", () => new GameMakerChunkGLOB() },
        { "GMEN", () => new GameMakerChunkGMEN() },
        { "SHDR", () => new GameMakerChunkSHDR() },
        { "FONT", () => new GameMakerChunkFONT() },
        { "TMLN", () => new GameMakerChunkTMLN() },
        { "OBJT", () => new GameMakerChunkOBJT() },
        { "ROOM", () => new GameMakerChunkROOM() },
        { "UILR", () => new GameMakerChunkUILR() },
        { "DAFL", () => new GameMakerChunkDAFL() },
        { "EMBI", () => new GameMakerChunkEMBI() },
        { "TPAG", () => new GameMakerChunkTPAG() },
        { "TGIN", () => new GameMakerChunkTGIN() },
        { "CODE", () => new GameMakerChunkCODE() },
        { "VARI", () => new GameMakerChunkVARI() },
        { "FUNC", () => new GameMakerChunkFUNC() },
        { "STRG", () => new GameMakerChunkSTRG() },
        { "TXTR", () => new GameMakerChunkTXTR() },
        { "AUDO", () => new GameMakerChunkAUDO() },
        { "ACRV", () => new GameMakerChunkACRV() },
        { "SEQN", () => new GameMakerChunkSEQN() },
        { "TAGS", () => new GameMakerChunkTAGS() },
        { "FEAT", () => new GameMakerChunkFEAT() },
        { "FEDS", () => new GameMakerChunkFEDS() },
        { "PSEM", () => new GameMakerChunkPSEM() },
        { "PSYS", () => new GameMakerChunkPSYS() },
    };

    public GameMakerChunkGEN8 GEN8 => Chunks.GetValueOrDefault("GEN8") as GameMakerChunkGEN8;
    public GameMakerChunkOPTN OPTN => Chunks.GetValueOrDefault("OPTN") as GameMakerChunkOPTN;
    public GameMakerChunkLANG LANG => Chunks.GetValueOrDefault("LANG") as GameMakerChunkLANG;
    public GameMakerChunkEXTN EXTN => Chunks.GetValueOrDefault("EXTN") as GameMakerChunkEXTN;
    public GameMakerChunkSOND SOND => Chunks.GetValueOrDefault("SOND") as GameMakerChunkSOND;
    public GameMakerChunkAGRP AGRP => Chunks.GetValueOrDefault("AGRP") as GameMakerChunkAGRP;
    public GameMakerChunkSPRT SPRT => Chunks.GetValueOrDefault("SPRT") as GameMakerChunkSPRT;
    public GameMakerChunkBGND BGND => Chunks.GetValueOrDefault("BGND") as GameMakerChunkBGND;
    public GameMakerChunkPATH PATH => Chunks.GetValueOrDefault("PATH") as GameMakerChunkPATH;
    public GameMakerChunkSCPT SCPT => Chunks.GetValueOrDefault("SCPT") as GameMakerChunkSCPT;
    public GameMakerChunkGLOB GLOB => Chunks.GetValueOrDefault("GLOB") as GameMakerChunkGLOB;
    public GameMakerChunkGMEN GMEN => Chunks.GetValueOrDefault("GMEN") as GameMakerChunkGMEN;
    public GameMakerChunkSHDR SHDR => Chunks.GetValueOrDefault("SHDR") as GameMakerChunkSHDR;
    public GameMakerChunkFONT FONT => Chunks.GetValueOrDefault("FONT") as GameMakerChunkFONT;
    public GameMakerChunkTMLN TMLN => Chunks.GetValueOrDefault("TMLN") as GameMakerChunkTMLN;
    public GameMakerChunkOBJT OBJT => Chunks.GetValueOrDefault("OBJT") as GameMakerChunkOBJT;
    public GameMakerChunkROOM ROOM => Chunks.GetValueOrDefault("ROOM") as GameMakerChunkROOM;
    public GameMakerChunkUILR UILR => Chunks.GetValueOrDefault("UILR") as GameMakerChunkUILR;
    public GameMakerChunkDAFL DAFL => Chunks.GetValueOrDefault("DAFL") as GameMakerChunkDAFL;
    public GameMakerChunkEMBI EMBI => Chunks.GetValueOrDefault("EMBI") as GameMakerChunkEMBI;
    public GameMakerChunkTPAG TPAG => Chunks.GetValueOrDefault("TPAG") as GameMakerChunkTPAG;
    public GameMakerChunkTGIN TGIN => Chunks.GetValueOrDefault("TGIN") as GameMakerChunkTGIN;
    public GameMakerChunkCODE CODE => Chunks.GetValueOrDefault("CODE") as GameMakerChunkCODE;
    public GameMakerChunkVARI VARI => Chunks.GetValueOrDefault("VARI") as GameMakerChunkVARI;
    public GameMakerChunkFUNC FUNC => Chunks.GetValueOrDefault("FUNC") as GameMakerChunkFUNC;
    public GameMakerChunkSTRG STRG => Chunks.GetValueOrDefault("STRG") as GameMakerChunkSTRG;
    public GameMakerChunkTXTR TXTR => Chunks.GetValueOrDefault("TXTR") as GameMakerChunkTXTR;
    public GameMakerChunkAUDO AUDO => Chunks.GetValueOrDefault("AUDO") as GameMakerChunkAUDO;
    // GMS 2.3+ for the below chunks
    public GameMakerChunkACRV ACRV => Chunks.GetValueOrDefault("ACRV") as GameMakerChunkACRV;
    public GameMakerChunkSEQN SEQN => Chunks.GetValueOrDefault("SEQN") as GameMakerChunkSEQN;
    public GameMakerChunkTAGS TAGS => Chunks.GetValueOrDefault("TAGS") as GameMakerChunkTAGS;
    public GameMakerChunkFEAT FEAT => Chunks.GetValueOrDefault("FEAT") as GameMakerChunkFEAT;
    // GMS 2.3.6+
    public GameMakerChunkFEDS FEDS => Chunks.GetValueOrDefault("FEDS") as GameMakerChunkFEDS;
    // GM 2023.2+
    public GameMakerChunkPSEM PSEM => Chunks.GetValueOrDefault("PSEM") as GameMakerChunkPSEM;
    public GameMakerChunkPSYS PSYS => Chunks.GetValueOrDefault("PSYS") as GameMakerChunkPSYS;

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        var done = 0;
        foreach (var name in EffectiveOrder())
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            writer.Write(Chunks[name]);
            writer.Progress?.Report(new GameMakerProgress(GameMakerIO.ChunkPhaseShare * ++done / Chunks.Count, name));
        }
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        var startPos = reader.Position;

        // First, find the last chunk in the file because of padding changes
        // (also, calculate all present chunks while we're at it)
        reader.AllChunkNames = [];
        var lastChunk = "";
        var formEnd = startPos + Length;
        while (reader.Position < formEnd)
        {
            if (formEnd - reader.Position < 8)
                throw new IOException("FORM ends in an incomplete chunk header.");

            lastChunk = reader.ReadChars(4);
            reader.AllChunkNames.Add(lastChunk);
            var length = reader.ReadUInt32();
            if (length > formEnd - reader.Position)
                throw new IOException($"Chunk \"{lastChunk}\" exceeds FORM bounds.");

            reader.Position += length;
        }
        reader.LastChunkName = lastChunk;
        reader.Position = startPos;

        // Now, parse the chunks
        while (reader.Position < formEnd)
        {
            reader.CancellationToken.ThrowIfCancellationRequested();
            var chunk = reader.ReadGameMakerChunk();
            if (chunk is not null)
            {
                reader.Progress?.Report(new GameMakerProgress(GameMakerIO.ChunkPhaseShare * (reader.Position - startPos) / Length, chunk.Name));
                if (!Chunks.ContainsKey(chunk.Name))
                {
                    throw new IOException($"Missed chunk on object count pass \"{chunk.Name}\"");
                }

                if (reader.ReadOnlyGEN8 && chunk.Name == "GEN8")
                {
                    return;
                }
            }
        }

        if (reader.gameMakerData.IsVersionAtLeast(2023, 1) &&
            reader.gameMakerData.GeneralInfo.Branch == GameMakerGeneralInfo.BranchType.Pre2022_0)
        {
            reader.gameMakerData.SetLTS(true);
        }
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        uint totalCount = 0;

        var startPos = reader.Position;
        reader.AllChunkNames = [];
        var formEnd = startPos + Length;
        while (reader.Position < formEnd)
        {
            if (formEnd - reader.Position < 8)
                throw new IOException("FORM ends in an incomplete chunk header.");

            var chunkName = reader.ReadChars(4);
            reader.AllChunkNames.Add(chunkName);
            var length = reader.ReadUInt32();
            if (length > formEnd - reader.Position)
                throw new IOException($"Chunk \"{chunkName}\" exceeds FORM bounds.");

            reader.Position += length;
        }
        reader.Position = startPos;

        // Read some basic data from GEN8 for version info, etc.
        if (reader.AllChunkNames[0] == "GEN8")
        {
            GameMakerChunkGEN8 gen8Chunk = new();
            gen8Chunk.UnserializeGeneralData(reader);
            Chunks.Add(gen8Chunk.Name, gen8Chunk);
            ChunksTypeDict.Add(gen8Chunk.GetType(), gen8Chunk);

            reader.Position = startPos;
        }

        // Read object counts for all chunks
        while (reader.Position < formEnd)
        {
            (var count, var chunk) = reader.CountChunkChildObjects();
            totalCount += count;

            // Don't register a new chunk for GEN8 specifically
            if (chunk.Name != "GEN8")
            {
                Chunks.Add(chunk.Name, chunk);
                // Raw chunks share one type, so they are looked up by name only.
                if (chunk is not GameMakerRawChunk)
                    ChunksTypeDict.Add(chunk.GetType(), chunk);
            }
        }

        return totalCount;
    }
}

public class GameMakerChunkGEN8 : GameMakerSingleChunk<GameMakerGeneralInfo>
{
    public override string Name => "GEN8";

    public void UnserializeGeneralData(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Object = new GameMakerGeneralInfo();

        reader.Position += 8; // Chunk name + length

        reader.Position++; // "IsDebuggerDisabled"
        Object.BytecodeVersion = reader.ReadByte();
        reader.gameMakerData.UnsupportedBytecodeVersion
            = Object.BytecodeVersion is < 13 or > 17;
        reader.Bytecode14OrLower = Object.BytecodeVersion <= 14;

        reader.Position += 42;

        var storedVersion = Object.Version;
        storedVersion.Unserialize(reader);
        Object.Version = GameMakerGeneralInfo.TestForCommonGMSVersions(reader, storedVersion, out var source);
        Object.VersionSource = source;
    }
}

public class GameMakerChunkOPTN : GameMakerSingleChunk<GameMakerOptions>
{
    public override string Name => "OPTN";
}

public class GameMakerChunkLANG : GameMakerSingleChunk<GameMakerLanguage>
{
    public override string Name => "LANG";

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        return 1;
    }
}

public class GameMakerChunkEXTN : GameMakerListChunk<GameMakerExtension>
{
    public override string Name => "EXTN";
    public List<byte[]> productIdData { get; set; } = [];

    private bool checkedFor2022_6;
    private bool checkedFor2023_4;
    private void CheckFor2022_6(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsVersionAtLeast(2, 3) || reader.gameMakerData.IsVersionAtLeast(2022, 6))
        {
            checkedFor2022_6 = true;
            return;
        }

        var definitely2022_6 = true;
        var returnPosition = reader.AbsPosition;

        var extCount = reader.ReadInt32();
        if (extCount > 0)
        {
            var firstExtPtr = reader.ReadUInt32();
            var firstExtEndPtr = (extCount >= 2) ? reader.ReadUInt32() /* second ptr */ : (uint)(returnPosition + Length);

            reader.AbsPosition = firstExtPtr + 12;
            var newPointer1 = reader.ReadUInt32();
            var newPointer2 = reader.ReadUInt32();

            if (newPointer1 != reader.AbsPosition)
                definitely2022_6 = false; // first pointer mismatch
            else if (newPointer2 <= reader.AbsPosition || newPointer2 >= returnPosition + Length)
                definitely2022_6 = false; // second pointer out of bounds
            else
            {
                // Check ending position
                reader.AbsPosition = newPointer2;
                var optionCount = reader.ReadUInt32();
                if (optionCount > 0)
                {
                    var newOffsetCheck = reader.AbsPosition + (long)(optionCount - 1) * sizeof(uint);
                    if (newOffsetCheck >= returnPosition + Length)
                    {
                        // Option count would place us out of bounds
                        definitely2022_6 = false;
                    }
                    else
                    {
                        reader.Position += (long)(optionCount - 1) * sizeof(uint);
                        newOffsetCheck = reader.ReadUInt32() + 12; // jump past last option
                        if (newOffsetCheck >= returnPosition + Length)
                        {
                            // Pointer list element would place us out of bounds
                            definitely2022_6 = false;
                        }
                        else
                        {
                            reader.AbsPosition = (uint)newOffsetCheck;
                        }
                    }
                }
                if (definitely2022_6)
                {
                    if (extCount == 1)
                    {
                        reader.Position += 16; // skip GUID data (only one of them)
                        if (reader.AbsPosition % 16 != 0)
                            reader.Position += 16 - reader.AbsPosition % 16; // align to chunk end
                    }
                    if (reader.AbsPosition != firstExtEndPtr)
                        definitely2022_6 = false;
                }
            }
        }
        else
            definitely2022_6 = false;

        reader.AbsPosition = returnPosition;

        if (definitely2022_6)
            reader.gameMakerData.SetGMS2Version(2022, 6);

        checkedFor2022_6 = true;
    }
    private void CheckFor2023_4(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsVersionAtLeast(2022, 6) || reader.gameMakerData.IsVersionAtLeast(2023, 4))
        {
            checkedFor2023_4 = true;
            return;
        }

        var returnPosition = reader.Position;

        var extCount = reader.ReadInt32();
        if (extCount > 0)
        {
            // Go to the first extension
            reader.AbsPosition = reader.ReadUInt32();

            // Skip the minimal amount of strings
            reader.Position += 4 * 3;

            var filesPtr = reader.ReadUInt32();
            var optionsPtr = reader.ReadUInt32();

            // The file list pointer should be less than the option list pointer.
            // If it's not true, then "filesPtr" is actually a string pointer, so it's GM 2023.4+.
            if (filesPtr > optionsPtr)
                reader.gameMakerData.SetGMS2Version(2023, 4);
        }

        reader.Position = returnPosition;

        checkedFor2023_4 = true;
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2022_6)
            CheckFor2022_6(reader);
        if (!checkedFor2023_4)
            CheckFor2023_4(reader);

        base.UnserializeChunk(reader);

        // Strange data for each extension, some kind of unique identifier based on
        // the product ID for each of them
        productIdData = [];
        if (GameMakerExtension.ProductDataEligible(reader.gameMakerData))
        {
            for (var i = 0; i < List.Count; i++)
            {
                productIdData.Add(reader.ReadBytes(16));
            }
        }
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (List.Any(extension => extension is null))
            throw new GameMakerSerializationException("EXTN does not support null pointer slots.");

        base.SerializeChunk(writer);

        if (!GameMakerExtension.ProductDataEligible(writer.gameMakerData))
            return;

        productIdData ??= [];
        while (productIdData.Count < List.Count)
            productIdData.Add(new byte[16]);
        if (productIdData.Count > List.Count)
            productIdData.RemoveRange(List.Count, productIdData.Count - List.Count);

        // (read above comment)
        foreach (var data in productIdData)
        {
            var Len = data.Length;
            if (Len != 16)
            {
                throw new IOException("Can't write EXTN product id data of invalid length, expected 16, got " + Len);
            }

            writer.Write(data);
        }
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2022_6 = false;

        CheckFor2022_6(reader);
        CheckFor2023_4(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkSOND : GameMakerListChunk<GameMakerSound>
{
    public override string Name => "SOND";

    private bool checkedFor2024_6;
    private void CheckForGM2024_6(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 2) || reader.gameMakerData.IsVersionAtLeast(2024, 6))
        {
            checkedFor2024_6 = true;
            return;
        }

        var returnTo = reader.Position;

        var possibleSoundCount = reader.ReadUInt32();
        uint firstSoundPointer = 0;
        uint secondSoundPointer = 0;
        uint soundCount = 0;
        for (uint i = 0; i < possibleSoundCount; i++)
        {
            var soundPointer = reader.ReadUInt32();
            if (soundPointer == 0)
                continue;

            soundCount++;
            if (soundCount == 1)
            {
                firstSoundPointer = soundPointer;
            }
            else if (soundCount == 2)
            {
                secondSoundPointer = soundPointer;
            }
        }
        if (soundCount >= 2)
        {
            // If first sound's theoretical (old) end offset is below the start offset of
            // the next sound by exactly 4 bytes, then this is 2024.6.
            if (firstSoundPointer + 4 * 9 == secondSoundPointer - 4)
            {
                reader.gameMakerData.SetGMS2Version(2024, 6);
            }
        }
        else if (soundCount == 1)
        {
            // If there's a nonzero value where padding should be at the
            // end of the sound, then this is 2024.6.
            reader.AbsPosition = firstSoundPointer + 4 * 9;
            if (reader.AbsPosition % 16 != 4)
            {
                // If this occurs, then something weird has happened at the start of the chunk?
                throw new IOException("Expected to be on specific alignment at this point");
            }
            if (reader.ReadUInt32() != 0)
            {
                reader.gameMakerData.SetGMS2Version(2024, 6);
            }
        }

        reader.Position = returnTo;
        checkedFor2024_6 = true;
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2024_6)
            CheckForGM2024_6(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2024_6 = false;

        CheckForGM2024_6(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkAGRP : GameMakerListChunk<GameMakerAudioGroup>
{
    public override string Name => "AGRP";

    private bool checkedFor2024_14;
    private void CheckForGM2024_14(GameMakerReader reader)
    {
        checkedFor2024_14 = true;

        // Only perform check if at least 2024.13 (trivially detected by now), and if not already detected
        if (!reader.gameMakerData.IsVersionAtLeast(2024, 13) || reader.gameMakerData.IsVersionAtLeast(2024, 14))
        {
            return;
        }

        // Check for new field added in 2024.14
        var returnTo = reader.Position;
        var chunkEndPos = reader.AbsPosition + Length;

        var agrpCount = reader.ReadUInt32();
        if (agrpCount == 0)
        {
            // No way to check when there's no audio groups... abort
            reader.Position = returnTo;
            return;
        }

        // Scan for up to two valid audio group pointers
        uint firstGroupPosition = 0, secondGroupPosition = 0;

        // Scan until we find a non-null first pointer...
        uint i = 0;
        while (i < agrpCount)
        {
            firstGroupPosition = reader.ReadUInt32();
            i++;

            if (firstGroupPosition != 0)
            {
                break;
            }
        }

        // Scan until we find a non-null second pointer...
        while (i < agrpCount)
        {
            secondGroupPosition = reader.ReadUInt32();
            i++;

            if (secondGroupPosition != 0)
            {
                break;
            }
        }

        // Handle 0 audio groups (can't check anything)
        if (firstGroupPosition == 0)
        {
            reader.Position = returnTo;
            return;
        }

        // Separately handle the case with 1 audio group only, and cases with at least 2
        if (secondGroupPosition == 0)
        {
            // Look for non-null bytes in the 4 bytes after the audio group name (and within bounds of the chunk)
            reader.AbsPosition = firstGroupPosition + 4;

            // Make sure the new field can fit in the remaining chunk space
            if (reader.AbsPosition + 4 > chunkEndPos)
            {
                reader.Position = returnTo;
                return;
            }

            // If the field data is zero, it's not 2024.14
            var pathPtr = reader.ReadUInt32();
            if (pathPtr == 0)
            {
                reader.Position = returnTo;
                return;
            }
        }
        else
        {
            // Compare offsets of two audio groups. If the difference is 4, then it's not 2024.14.
            // Otherwise, it is at least 2024.14.
            if (secondGroupPosition - firstGroupPosition == 4)
            {
                reader.Position = returnTo;
                return;
            }
        }

        // 2024.14 detected
        reader.Position = returnTo;
        reader.gameMakerData.SetGMS2Version(2024, 14);
        var newSize = GameMakerAudioGroup.ChildObjectsSize + 4;
        reader.SetStaticChildObjectsSize(typeof(GameMakerAudioGroup), newSize);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2024_14)
            CheckForGM2024_14(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2024_14 = false;
        CheckForGM2024_14(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkSPRT : GameMakerListChunk<GameMakerSprite>
{
    public override string Name => "SPRT";

    private bool checkedFor2024_6;
    private void CheckForGM2024_6(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 2) || reader.gameMakerData.IsVersionAtLeast(2024, 6))
        {
            checkedFor2024_6 = true;
            return;
        }

        var returnTo = reader.Position;
        var chunkStartPos = reader.AbsPosition;

        // Calculate the expected end position of the first sprite where the bbox size differs from width/height
        var spriteCount = reader.ReadUInt32();
        for (uint i = 0; i < spriteCount; i++)
        {
            // Go to sprite's start position
            reader.Position = returnTo + 4 + 4L * i;
            var spritePtr = reader.ReadUInt32();
            if (spritePtr == 0)
                continue;
            uint nextSpritePtr = 0;
            var j = i;
            while (nextSpritePtr == 0 && ++j < spriteCount)
                nextSpritePtr = reader.ReadUInt32();
            reader.AbsPosition = spritePtr + 4; // Skip past "Name"

            // Check if bbox size differs from width/height
            var width = reader.ReadUInt32();
            var height = reader.ReadUInt32();
            var marginLeft = reader.ReadInt32();
            var marginRight = reader.ReadInt32();
            var marginBottom = reader.ReadInt32();
            var marginTop = reader.ReadInt32();
            (var bboxWidth, var bboxHeight) = GameMakerSprite.CalculateBboxMaskDimensions(marginRight, marginLeft, marginBottom, marginTop);
            (var normalWidth, var normalHeight) = GameMakerSprite.CalculateFullMaskDimensions((int)width, (int)height);
            if (bboxWidth == normalWidth && bboxHeight == normalHeight)
            {
                // We can't determine anything from this sprite
                continue;
            }

            reader.Position += 28;

            if (reader.ReadInt32() != -1)
            {
                throw new IOException("Expected special sprite type");
            }

            var sVersion = reader.ReadUInt32();
            var sSpriteType = (GameMakerSprite.SpriteType)reader.ReadUInt32();

            if (sSpriteType != GameMakerSprite.SpriteType.Normal)
            {
                // We can't determine anything from this sprite
                continue;
            }

            reader.Position += 8; // Playback speed values

            if (sVersion != 3)
            {
                throw new IOException("Expected sprite version 3");
            }
            var sequenceOffset = reader.ReadUInt32();
            var nineSliceOffset = reader.ReadUInt32();

            // Skip past texture pointers
            var textureCount = reader.ReadUInt32();
            reader.Position += (long)textureCount * sizeof(uint);

            // Calculate how much space the "full" and "bbox" mask data take up
            var maskCount = reader.ReadUInt32();
            if (maskCount == 0)
            {
                // We can't determine anything from this sprite
                continue;
            }
            var fullLength = ((long)normalWidth + 7) / 8 * normalHeight;
            fullLength *= maskCount;
            if (fullLength % 4 != 0)
                fullLength += 4 - fullLength % 4;
            var bboxLength = ((long)bboxWidth + 7) / 8 * bboxHeight;
            bboxLength *= maskCount;
            if (bboxLength % 4 != 0)
                bboxLength += 4 - bboxLength % 4;

            // Calculate expected end offset
            long expectedEndOffset;
            var endOffsetLenient = false;
            if (sequenceOffset != 0)
            {
                expectedEndOffset = sequenceOffset;
            }
            else if (nineSliceOffset != 0)
            {
                expectedEndOffset = nineSliceOffset;
            }
            else if (nextSpritePtr != 0)
            {
                expectedEndOffset = nextSpritePtr;
            }
            else
            {
                // Use chunk length, and be lenient with it (due to chunk padding)
                endOffsetLenient = true;
                expectedEndOffset = chunkStartPos + Length;
            }

            // If the "full" mask data runs past the expected end offset, and the "bbox" mask data does not, then this is 2024.6.
            // Otherwise, stop processing and assume this is not 2024.6.
            var fullEndPos = reader.AbsPosition + fullLength;
            if (fullEndPos == expectedEndOffset)
            {
                // "Full" mask data is valid
                break;
            }
            if (endOffsetLenient && fullEndPos % 16 != 0 && fullEndPos + (16 - fullEndPos % 16) == expectedEndOffset)
            {
                // "Full" mask data doesn't exactly line up, but works if rounded up to the next chunk padding
                break;
            }

            var bboxEndPos = reader.AbsPosition + bboxLength;
            if (bboxEndPos == expectedEndOffset)
            {
                // "Bbox" mask data is valid
                if (!reader.gameMakerData.IsLTSBranch())
                    reader.gameMakerData.SetGMS2Version(2024, 6);
                break;
            }
            if (endOffsetLenient && bboxEndPos % 16 != 0 && bboxEndPos + (16 - bboxEndPos % 16) == expectedEndOffset)
            {
                // "Bbox" mask data doesn't exactly line up, but works if rounded up to the next chunk padding
                if (!reader.gameMakerData.IsLTSBranch())
                    reader.gameMakerData.SetGMS2Version(2024, 6);
                break;
            }

            // Neither option seems to have worked...
            throw new IOException("Failed to detect mask type in 2024.6 detection");
        }

        reader.Position = returnTo;
        checkedFor2024_6 = true;
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2024_6)
            CheckForGM2024_6(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2024_6 = false;

        CheckForGM2024_6(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkBGND : GameMakerAlignUpdatedListChunk<GameMakerBackground>
{
    public override string Name => "BGND";

    private bool checkedFor2024_14_1;
    private void CheckForGM2024_14_1(GameMakerReader reader)
    {
        checkedFor2024_14_1 = true;

        if (!reader.gameMakerData.IsVersionAtLeast(2024, 13) || reader.gameMakerData.IsVersionAtLeast(2024, 14, 1))
        {
            return;
        }

        var returnTo = reader.Position;
        var chunkStartPos = reader.AbsPosition;

        // Go through each background, and check to see if it ends at the expected position. If not, this is probably 2024.14.1.
        var bgCount = reader.ReadUInt32();
        for (uint i = 0; i < bgCount; i++)
        {
            // Find background's start position, and calculate next background position (if available).
            reader.Position = returnTo + 4 + 4L * i;
            var bgPtr = reader.ReadUInt32();
            if (bgPtr == 0)
            {
                // Removed asset
                continue;
            }
            uint nextBgPtr = 0;
            var j = i;
            while (nextBgPtr == 0 && ++j < bgCount)
            {
                // Try next pointer in list
                nextBgPtr = reader.ReadUInt32();
            }

            // Skip all the way to "GMS2ItemsPerTileCount" (at its pre-2024.14.1 location), which is what we actually care about.
            reader.AbsPosition = bgPtr + 11 * 4;
            var itemsPerTileCount = reader.ReadUInt32();
            var tileCount = reader.ReadUInt32();

            // Calculate the theoretical end position given the above info, and compare to the actual end position (with padding).
            var theoreticalEndPos = (long)bgPtr + 16 * sizeof(uint) + (long)itemsPerTileCount * tileCount * sizeof(uint);
            if (nextBgPtr == 0)
            {
                // Align to 16 bytes, and compare against chunk end position
                if (theoreticalEndPos % 16 != 0)
                {
                    theoreticalEndPos += 16 - theoreticalEndPos % 16;
                }
                var chunkEndPos = chunkStartPos + Length;
                if (theoreticalEndPos != chunkEndPos)
                {
                    // Probably 2024.14.1!
                    reader.gameMakerData.SetGMS2Version(2024, 14, 1);
                    break;
                }
            }
            else
            {
                // Align to 8 bytes, and compare against next background start position
                if (theoreticalEndPos % 8 != 0)
                {
                    theoreticalEndPos += 8 - theoreticalEndPos % 8;
                }
                if (theoreticalEndPos != nextBgPtr)
                {
                    // Probably 2024.14.1!
                    reader.gameMakerData.SetGMS2Version(2024, 14, 1);
                    break;
                }
            }
        }

        reader.Position = returnTo;
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        Alignment = 8;
        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        Alignment = 8;

        if (!checkedFor2024_14_1)
        {
            CheckForGM2024_14_1(reader);
        }

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2024_14_1 = false;

        CheckForGM2024_14_1(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkPATH : GameMakerListChunk<GameMakerPath>
{
    public override string Name => "PATH";
}

public class GameMakerChunkSCPT : GameMakerListChunk<GameMakerScript>
{
    public override string Name => "SCPT";
}

public class GameMakerChunkGLOB : GameMakerSimpleListChunk<GameMakerGlobalInit>
{
    public override string Name => "GLOB";
}

public class GameMakerChunkGMEN : GameMakerSimpleListChunk<GameMakerGlobalInit>
{
    public override string Name => "GMEN";
}

public class GameMakerChunkSHDR : GameMakerListChunk<GameMakerShader>
{
    public override string Name => "SHDR";

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        var chunkEnd = reader.AbsPosition + Length;

        var beginPosition = reader.Position;

        // Figure out where the starts/ends of each shader object are
        var count = reader.ReadInt32();
        if (count < 0 || (long)count * sizeof(uint) > chunkEnd - reader.AbsPosition)
            throw new IOException("Invalid shader entry count.");
        var objectLocations = new uint[count + 1];
        for (var i = 0; i < count; i++)
        {
            var objectLocation = reader.ReadUInt32();
            if (objectLocation == 0)
            {
                i--;
                count--;
                continue;
            }
            objectLocations[i] = objectLocation;
        }
        objectLocations[count] = (uint)chunkEnd;

        var objPool = reader.GetOffsetMap();
        var objPoolRev = reader.GetOffsetMapRev();

        // Setup base shader objects with boundaries set. Load into object pool
        // so that they don't immediately discard.
        for (var i = 0; i < count; i++)
        {
            var s = new GameMakerShader { EntryEnd = objectLocations[i + 1] };
            objPool.Add(objectLocations[i], s);
            objPoolRev.Add(s, objectLocations[i]);
        }

        reader.Position = beginPosition;
        base.UnserializeChunk(reader);
    }
}

public class GameMakerChunkFONT : GameMakerListChunk<GameMakerFont>
{
    public override string Name => "FONT";
    public byte[] Padding { get; set; }

    private bool checkedFor2022_2;
    private bool checkedFor2023_6And2024_11;
    private bool checkedFor2024_14;
    private void CheckForGM2022_2(GameMakerReader reader)
    {
        /* This code performs four checks to identify GM2022.2.
         * First, as you've seen, is the bytecode version.
         * Second, we assume it is. If there are no Glyphs, we are vindicated by the impossibility of null values there.
         * Third, we check that the Glyph Length is less than the chunk length. If it's going outside the chunk, that means
         * that the length was misinterpreted.
         * Fourth, in case of a terrible fluke causing this to appear valid erroneously, we verify that each pointer leads into the next.
         * And if someone builds their game so the first pointer is absolutely valid length data and the next font is valid glyph data-
         * screw it, call Jacky720 when someone constructs that and you want to mod it.
         * Maybe try..catch on the whole shebang?
         */
        if (reader.gameMakerData.GeneralInfo?.BytecodeVersion < 17 || reader.gameMakerData.IsVersionAtLeast(2022, 2))
        {
            checkedFor2022_2 = true;
            return;
        }

        var positionToReturn = reader.Position;
        var GMS2022_2 = false;

        var possibleFontCount = reader.ReadUInt32();
        if (possibleFontCount > 0)
        {
            uint firstFontPointer = 0;
            for (uint i = 0; i < possibleFontCount; i++)
            {
                var fontPointer = reader.ReadUInt32();
                if (fontPointer != 0)
                {
                    firstFontPointer = fontPointer;
                    break;
                }
            }
            if (firstFontPointer != 0)
            {
                reader.AbsPosition = firstFontPointer + 48; // There are 48 bytes of existing metadata.
                var glyphsLength = reader.ReadUInt32();
                GMS2022_2 = true;
                if ((ulong)glyphsLength * sizeof(uint) > Length)
                {
                    GMS2022_2 = false;
                }
                else if (glyphsLength != 0)
                {
                    List<uint> glyphPointers = [];
                    for (uint i = 0; i < glyphsLength; i++)
                    {
                        var glyphPointer = reader.ReadUInt32();
                        if (glyphPointer == 0)
                            throw new IOException("One of the glyph pointers is null?");
                        glyphPointers.Add(glyphPointer);
                    }
                    foreach (var pointer in glyphPointers)
                    {
                        if (reader.AbsPosition != pointer)
                        {
                            GMS2022_2 = false;
                            break;
                        }

                        reader.Position += 14;
                        var kerningLength = reader.ReadUInt16();
                        reader.Position += (uint)4 * kerningLength; // combining read/write would apparently break
                    }
                }
            }
        }
        if (GMS2022_2)
            reader.gameMakerData.SetGMS2Version(2022, 2);
        reader.Position = positionToReturn;

        checkedFor2022_2 = true;
    }

    private void CheckForGM2023_6AndGM2024_11(GameMakerReader reader)
    {
        /*
            We already know whether the version is more or less than 2022.8 due to the FEAT chunk being present.
            Taking advantage of that, this is basically the same as the 2022.2 check, but it:
            - Checks for the LineHeight value instead of Ascender (added in 2023.6)
                PSEM (2023.2) is not used, as it would return a false negative on LTS (2022.9+ equivalent with no particles).
            - Checks for UnknownAlwaysZero in Glyphs (added in 2024.11)
                It's possible for the null pointer check planted in UTPointerList deserialisation to not be triggered:
                for example, if SDF is enabled for any fonts, the shaders related to SDF will not be stripped;
                it's also possible to prevent audiogroup_default from being stripped by doing
                    audio_group_name(audiogroup_default)
                So we check for the presence of UnknownAlwaysZero as a last resort.
        */
        if (!reader.gameMakerData.IsVersionAtLeast(2022, 8) ||
            reader.gameMakerData.IsVersionAtLeast(2023, 6) && !reader.gameMakerData.IsVersionAtLeast(2024, 6) ||
            reader.gameMakerData.IsVersionAtLeast(2024, 11))
        {
            checkedFor2023_6And2024_11 = true;
            return;
        }

        var positionToReturn = reader.AbsPosition;
        var GMS2023_6 = true;
        var GMS2024_11 = false;

        var possibleFontCount = reader.ReadUInt32();
        if (possibleFontCount <= 0)
        {
            // No way to know anything
            reader.AbsPosition = positionToReturn;
            checkedFor2023_6And2024_11 = true;
            return;
        }

        List<long> firstAndNextFontPointers = new(2);
        for (uint i = 0; i < possibleFontCount; i++)
        {
            var fontPointer = reader.ReadUInt32();
            if (fontPointer != 0)
            {
                firstAndNextFontPointers.Add(fontPointer);
                if (firstAndNextFontPointers.Count == 2)
                    break;
            }
        }

        if (firstAndNextFontPointers.Count == 0)
        {
            // No way to know anything
            reader.AbsPosition = positionToReturn;
            checkedFor2023_6And2024_11 = true;
            return;
        }

        if (firstAndNextFontPointers.Count == 1)
        {
            // Add in the position of the padding i.e. the end of the font list
            firstAndNextFontPointers.Add(positionToReturn + Length - 512);
        }

        reader.AbsPosition = firstAndNextFontPointers[0] + 52;      // Also the LineHeight value. 48 + 4 = 52.
        if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 2))   // SDFSpread is present from 2023.2 non-LTS onward
            reader.AbsPosition += 4;                                // (detected by PSEM/PSYS chunk existence)

        var glyphsLength = reader.ReadUInt32();
        var availableGlyphData = firstAndNextFontPointers[1] - reader.AbsPosition;
        if (availableGlyphData < 0 || (ulong)glyphsLength * sizeof(uint) > (ulong)availableGlyphData)
        {
            GMS2023_6 = false;
        }
        else if (glyphsLength != 0)
        {
            List<uint> glyphPointers = [];
            for (uint i = 0; i < glyphsLength; i++)
            {
                var glyphPointer = reader.ReadUInt32();
                if (glyphPointer == 0)
                    throw new IOException("One of the glyph pointers is null?");
                glyphPointers.Add(glyphPointer);
            }

            // When this is set to true the detection logic will not run again
            var GMS2024_11_Failed = false;
            for (var i = 0; i < glyphPointers.Count; i++)
            {
                if (reader.AbsPosition != glyphPointers[i])
                {
                    GMS2023_6 = false;
                    GMS2024_11 = false;
                    break;
                }

                reader.Position += 14;
                var kerningLength = reader.ReadUInt16();
                if (!GMS2024_11_Failed)
                {
                    if (!GMS2024_11)
                    {
                        // Hopefully the last thing in a UTFont is the glyph list
                        var pointerNextGlyph = i < glyphPointers.Count - 1 ? glyphPointers[i + 1] : firstAndNextFontPointers[1];
                        // And hopefully the last thing in a glyph is the kerning list
                        // Glyph kerning entries occupy four bytes each.
                        var pointerAfterKerningList = reader.AbsPosition + 4 * kerningLength;
                        // If we don't land on the next glyph/font after skipping the Kerning list,
                        // kerningLength is probably bogus and UnknownAlwaysZero may be present
                        if (pointerAfterKerningList != pointerNextGlyph)
                        {
                            // Discard last read, which would be of UnknownAlwaysZero
                            kerningLength = reader.ReadUInt16();
                            pointerAfterKerningList = reader.AbsPosition + 4 * kerningLength;
                            if (pointerAfterKerningList != pointerNextGlyph)
                                reader.SubmitWarning("There appears to be more/less values than UnknownAlwaysZero before " +
                                                        "the kerning list in a UTFont.Glyph - potential data loss");
                            GMS2024_11 = true;
                        }
                        else
                        {
                            GMS2024_11_Failed = true;
                        }
                    }
                    else
                    {
                        // Discard last read, which would be of UnknownAlwaysZero
                        kerningLength = reader.ReadUInt16();
                    }
                }
                reader.Position += 4 * kerningLength; // combining read/write would apparently break
            }
        }

        if (GMS2024_11 && !reader.gameMakerData.IsVersionAtLeast(2024, 11))
        {
            reader.gameMakerData.SetGMS2Version(2024, 11);
        }
        else if (GMS2023_6 && !reader.gameMakerData.IsVersionAtLeast(2023, 6))
        {
            reader.gameMakerData.SetGMS2Version(2023, 6);
        }

        reader.AbsPosition = positionToReturn;
        checkedFor2023_6And2024_11 = true;
    }

    private void CheckForGM2024_14(GameMakerReader reader)
    {
        checkedFor2024_14 = true;

        if (!reader.gameMakerData.IsVersionAtLeast(2024, 13) || reader.gameMakerData.IsVersionAtLeast(2024, 14))
        {
            return;
        }

        // Check for new padding added (and final chunk "padding" removed) in 2024.14
        var returnTo = reader.Position;
        var chunkEndPos = reader.AbsPosition + Length;

        // Scan for up to two valid font pointers
        var fontCount = reader.ReadUInt32();
        uint lastFontPosition = 0;

        // Scan to find the last non-null pointer...
        for (uint i = 0; i < fontCount; i++)
        {
            var ptr = reader.ReadUInt32();
            if (ptr != 0)
            {
                lastFontPosition = ptr;
            }
        }

        // If we have a last font, advance to the end of its data (ignoring the new alignment added in 2024.14)
        if (lastFontPosition != 0)
        {
            reader.AbsPosition = lastFontPosition + 56;

            // Advance to last glyph in pointer list
            var glyphCount = reader.ReadUInt32();
            if (glyphCount == 0)
            {
                reader.Position = returnTo;
                return;
            }

            reader.Position += ((long)glyphCount - 1) * sizeof(uint);
            reader.AbsPosition = reader.ReadUInt32() +
                (reader.gameMakerData.IsVersionAtLeast(2024, 11) ? 18 : 16);

            // Advance past kerning
            var kerningCount = reader.ReadUInt16();
            reader.Position += (long)kerningCount * sizeof(uint);
        }

        // Check for the final chunk padding being missing
        if (reader.AbsPosition + 512 > chunkEndPos)
        {
            // No padding can fit, so this is 2024.14
            reader.gameMakerData.SetGMS2Version(2024, 14);
        }

        reader.Position = returnTo;
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        base.SerializeChunk(writer);

        if (!writer.gameMakerData.IsVersionAtLeast(2024, 14))
        {
            if (Padding == null)
            {
                for (ushort i = 0; i < 0x80; i++)
                    writer.Write(i);
                for (ushort i = 0; i < 0x80; i++)
                    writer.Write((ushort)0x3f);
            }
            else
            {
                writer.Write(Padding);
            }
        }
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2022_2)
            CheckForGM2022_2(reader);
        if (!checkedFor2023_6And2024_11)
            CheckForGM2023_6AndGM2024_11(reader);
        if (!checkedFor2024_14)
            CheckForGM2024_14(reader);

        base.UnserializeChunk(reader);

        if (!reader.gameMakerData.IsVersionAtLeast(2024, 14))
        {
            Padding = reader.ReadBytes(512);
        }
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2022_2 = false;
        checkedFor2023_6And2024_11 = false;
        checkedFor2024_14 = false;

        CheckForGM2022_2(reader);
        CheckForGM2023_6AndGM2024_11(reader);
        CheckForGM2024_14(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkTMLN : GameMakerListChunk<GameMakerTimeline>
{
    public override string Name => "TMLN";
}

public class GameMakerChunkOBJT : GameMakerListChunk<GameMakerGameObject>
{
    public override string Name => "OBJT";

    private bool checkedForManagedField;

    // Simple chunk parser to check for the Managed field, assumes it is absent until shown otherwise.
    private void CheckForManagedField(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsVersionAtLeast(2, 3) ||
            reader.gameMakerData.IsVersionAtLeast(2022, 5) && !reader.gameMakerData.IsVersionAtLeast(2024, 13))
        {
            checkedForManagedField = true;
            return;
        }

        var positionToReturn = reader.Position;
        var managedFieldPresent = false;
        var hasObjects = false;

        if (reader.ReadUInt32() > 0) // Object count
        {
            hasObjects = true;
            var firstObjectPointer = reader.ReadUInt32();
            reader.AbsPosition = firstObjectPointer + 64;
            var vertexCount = reader.ReadUInt32();

            // If any of these checks fail, the Managed field is probably present.
            managedFieldPresent = true;
            // Bounds check on vertex data
            var vertexDataSize = 12L + (long)vertexCount * 8;
            var chunkEnd = positionToReturn + Length;
            if (vertexDataSize <= chunkEnd - reader.Position - sizeof(uint) * 2)
            {
                reader.Position += vertexDataSize;
                // A pointer list of events
                if (reader.ReadUInt32() == GameMakerGameObject.EventTypeCount)
                {
                    var subEventPointer = reader.ReadUInt32();
                    // Should start right after the list
                    if (reader.AbsPosition + 56 == subEventPointer)
                        managedFieldPresent = false;
                }
            }
        }
        if (managedFieldPresent)
        {
            if (!reader.gameMakerData.IsVersionAtLeast(2022, 5))
                reader.gameMakerData.SetGMS2Version(2022, 5);
        }
        else if (hasObjects && reader.gameMakerData.IsVersionAtLeast(2024, 13)) // no objects, no evidence
        {
            reader.gameMakerData.SetGMS2Version(2026, 1);
        }

        reader.Position = positionToReturn;

        checkedForManagedField = true;
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedForManagedField)
            CheckForManagedField(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedForManagedField = false;

        CheckForManagedField(reader);

        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkROOM : GameMakerListChunk<GameMakerRoom>
{
    public override string Name => "ROOM";

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2024_2)
            CheckForTileCompression(reader);

        if (!checkedFor2022_1)
            CheckForEffectData(reader);

        if (!checkedForGMS2_2_2_302)
            CheckForImageSpeed(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2022_1 = false;
        checkedFor2024_2 = false;
        checkedFor2024_4 = false;
        checkedForGMS2_2_2_302 = false;

        CheckForTileCompression(reader);
        CheckForEffectData(reader);
        CheckForImageSpeed(reader);

        if (reader.gameMakerData.GeneralInfo?.BytecodeVersion >= 16)
        {
            // "GameObject._preCreateCode"

            var gameObjType = typeof(GameObject);

            var newValue = GameObject.ChildObjectsSize + 4;
            reader.SetStaticChildObjectsSize(gameObjType, newValue);
        }

        return base.UnserializeObjectCount(reader);
    }

    private bool checkedFor2022_1;
    private void CheckForEffectData(GameMakerReader reader)
    {
        // Do a length check on room layers to see if this is 2022.1 or higher
        if (!reader.gameMakerData.IsVersionAtLeast(2, 3) || reader.gameMakerData.IsVersionAtLeast(2022, 1))
        {
            checkedFor2022_1 = true;
            return;
        }
        var returnTo = reader.Position;

        // Iterate over all rooms until a length check is performed
        var roomCount = reader.ReadInt32();
        if (roomCount < 0)
            throw new IOException("Negative room count.");
        for (var roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            // Advance to room data we're interested in (and grab pointer for next room)
            reader.Position = returnTo + 4 + 4L * roomIndex;
            var roomPtr = (uint)reader.ReadInt32();
            reader.AbsPosition = roomPtr + 22 * 4;

            // Get the pointer for this room's layer list, as well as pointer to sequence list
            var layerListPtr = (uint)reader.ReadInt32();
            var seqnPtr = reader.ReadInt32();
            reader.AbsPosition = layerListPtr;
            var layerCount = reader.ReadInt32();
            if (layerCount <= 0)
            {
                // No layers, try the next room
                continue;
            }
            // Get pointer into the individual layer data (plus 8 bytes) for the first layer in the room
            var jumpOffset = reader.ReadInt32() + 8;

            // Find the offset for the end of this layer
            int nextOffset;
            if (layerCount == 1)
                nextOffset = seqnPtr;
            else
                nextOffset = reader.ReadInt32(); // (pointer to next element in the layer list)

            // Actually perform the length checks, depending on layer data
            reader.AbsPosition = jumpOffset;

            var layerType = (LayerType)reader.ReadInt32();
            // This is the only way to repeat the loop, because each successful switch case terminates the loop
            if (!Enum.IsDefined(layerType) || layerType is LayerType.Path or LayerType.Path2)
                continue;

            switch (layerType)
            {
                case LayerType.Background:
                    if (nextOffset - reader.AbsPosition > 16 * 4)
                        reader.gameMakerData.SetGMS2Version(2022, 1);
                    break;
                case LayerType.Instances:
                    reader.Position += 6 * 4;
                    var instanceCount = reader.ReadInt32();
                    if (instanceCount < 0)
                        throw new IOException("Negative room layer instance count.");
                    if (nextOffset - reader.AbsPosition != (long)instanceCount * 4)
                        reader.gameMakerData.SetGMS2Version(2022, 1);
                    break;
                case LayerType.Assets:
                    reader.Position += 6 * 4;
                    var tileOffset = reader.ReadInt32();
                    if (tileOffset != reader.AbsPosition + 8 && tileOffset != reader.AbsPosition + 12)
                        reader.gameMakerData.SetGMS2Version(2022, 1);
                    break;
                case LayerType.Tiles:
                    reader.Position += 7 * 4;
                    var tileMapWidth = reader.ReadInt32();
                    var tileMapHeight = reader.ReadInt32();
                    if (tileMapWidth < 0 || tileMapHeight < 0)
                        throw new IOException("Negative room tile map dimension.");
                    if (nextOffset - reader.AbsPosition != (long)tileMapWidth * tileMapHeight * 4)
                        reader.gameMakerData.SetGMS2Version(2022, 1);
                    break;
                case LayerType.Effect:
                    reader.Position += 7 * 4;
                    var propertyCount = reader.ReadInt32();
                    if (propertyCount < 0)
                        throw new IOException("Negative room layer property count.");
                    if (nextOffset - reader.AbsPosition != (long)propertyCount * 3 * 4)
                        reader.gameMakerData.SetGMS2Version(2022, 1);
                    break;
            }
            // Check complete, found and tested a layer.
            break;
        }

        reader.Position = returnTo;


        checkedFor2022_1 = true;
    }

    private bool checkedForGMS2_2_2_302;
    private void CheckForImageSpeed(GameMakerReader reader)
    {
        // Check the size of the first GameObject in a room
        if (!reader.gameMakerData.IsGameMaker2() || reader.gameMakerData.IsVersionAtLeast(2, 2, 2, 302))
        {
            checkedForGMS2_2_2_302 = true;
            return;
        }
        var returnTo = reader.Position;

        // Iterate over all rooms until a length check is performed
        var roomCount = reader.ReadInt32();
        if (roomCount < 0)
            throw new IOException("Negative room count.");
        for (var roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            // Advance to room data we're interested in (and grab pointer for next room)
            reader.Position = returnTo + 4 + 4L * roomIndex;
            var roomPtr = (uint)reader.ReadInt32();
            reader.AbsPosition = roomPtr + 12 * 4;

            // Get the pointer for this room's object list, as well as pointer to tile list
            var objectListPtr = (uint)reader.ReadInt32();
            var tileListPtr = reader.ReadInt32();
            reader.AbsPosition = objectListPtr;
            var objectCount = reader.ReadInt32();
            if (objectCount <= 0)
            {
                // No objects, try the next room
                continue;
            }
            // Compare position of first object to second
            var firstPtr = reader.ReadInt32();
            int secondPtr;
            if (objectCount == 1) // Tile list starts right after, so it works as an alternate
                secondPtr = tileListPtr;
            else
                secondPtr = reader.ReadInt32();

            if ((long)secondPtr - firstPtr == 48)
            {
                reader.gameMakerData.SetGMS2Version(2, 2, 2, 302);

                var newSize = GameObject.ChildObjectsSize + 8;
                reader.SetStaticChildObjectsSize(typeof(GameObject), newSize);
            }
            // Check performed, one way or the other
            break;
        }

        reader.Position = returnTo;

        checkedForGMS2_2_2_302 = true;
    }

    private bool checkedFor2024_2;
    private bool checkedFor2024_4;
    private void CheckForTileCompression(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsVersionAtLeast(2023, 2) || reader.gameMakerData.IsVersionAtLeast(2024, 4))
        {
            checkedFor2024_2 = true;
            checkedFor2024_4 = true;
            return;
        }
        if (reader.gameMakerData.IsVersionAtLeast(2024, 2))
        {
            checkedFor2024_2 = true;
        }

        // Do a length check on room layers to see if this is 2024.2 or higher
        var returnTo = reader.Position;

        // Iterate over all rooms
        var roomCount = reader.ReadInt32();
        if (roomCount < 0)
            throw new IOException("Negative room count.");
        var foundAnyNonAlignedLayers = false;
        for (var roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            // Advance to room data we're interested in (and grab pointer for next room)
            reader.Position = returnTo + 4 + 4L * roomIndex;
            var roomPtr = (uint)reader.ReadInt32();
            reader.AbsPosition = roomPtr + 22 * 4;

            // Get the pointer for this room's layer list, as well as pointer to sequence list
            var layerListPtr = (uint)reader.ReadInt32();
            var seqnPtr = reader.ReadInt32();
            reader.AbsPosition = layerListPtr;
            var layerCount = reader.ReadInt32();
            if (layerCount <= 0)
            {
                // No layers, try the next room
                continue;
            }

            var checkNextLayerOffset = false;
            for (var layerNum = 0; layerNum < layerCount; layerNum++)
            {
                var layerPtr = layerListPtr + 4 * layerNum;
                if (checkNextLayerOffset && layerPtr % 4 != 0)
                {
                    foundAnyNonAlignedLayers = true;
                }

                reader.AbsPosition = layerPtr + 4;

                // Get pointer into the individual layer data (plus 8 bytes)
                var jumpOffset = reader.ReadInt32() + 8;

                // Find the offset for the end of this layer
                int nextOffset;
                if (layerNum == layerCount - 1)
                    nextOffset = seqnPtr;
                else
                    nextOffset = reader.ReadInt32(); // (pointer to next element in the layer list)

                // Actually perform the length checks
                reader.AbsPosition = jumpOffset;

                var layerType = (LayerType)reader.ReadInt32();
                if (layerType != LayerType.Tiles)
                {
                    checkNextLayerOffset = false;
                    continue;
                }
                checkNextLayerOffset = true;

                reader.Position += 32;
                var effectCount = reader.ReadInt32();
                if (effectCount < 0)
                    throw new IOException("Negative room layer effect count.");
                reader.Position += 4L + (long)effectCount * 12;

                var tileMapWidth = reader.ReadInt32();
                var tileMapHeight = reader.ReadInt32();
                if (tileMapWidth < 0 || tileMapHeight < 0)
                    throw new IOException("Negative room tile map dimension.");
                var expectedTileMapSize = (long)tileMapWidth * tileMapHeight * 4;
                if (!checkedFor2024_2 && nextOffset - reader.AbsPosition != expectedTileMapSize)
                {
                    // Check complete, found and tested a layer.
                    reader.gameMakerData.SetGMS2Version(2024, 2);
                    checkedFor2024_2 = true;
                }
            }
        }

        if (!checkedFor2024_4 && reader.gameMakerData.IsVersionAtLeast(2024, 2) && !foundAnyNonAlignedLayers)
        {
            // We found no layer that suggests we're not using 2024.4
            // This can rarely lead to false positives, though (in which case it's just 2024.2)
            reader.gameMakerData.SetGMS2Version(2024, 4);
        }

        reader.Position = returnTo;
        checkedFor2024_2 = true;
        checkedFor2024_4 = true;
    }
}

public class GameMakerChunkUILR : GameMakerListChunk<GameMakerUIRootNode>
{
    public override string Name => "UILR";
}

public class GameMakerChunkDAFL : GameMakerEmptyChunk // DataFiles
{
    public override string Name => "DAFL";
}

public class GameMakerChunkTPAG : GameMakerListChunk<GameMakerTexturePageItem>
{
    public override string Name => "TPAG";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        base.SerializeChunk(writer);

        if (writer.gameMakerData.IsTPAG4ByteAligned)
        {
            // padding present in ARM platforms apparently
            while (writer.Position % 0x4 != 0)
                writer.Write((byte)0);
        }
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (Length % 0x4 == 0)
        {
            reader.gameMakerData.IsTPAG4ByteAligned = true;
        }

        base.UnserializeChunk(reader);

        for (var index = 0; index < List.Count; index++)
        {
            if (List[index] is { } item)
                item.Name = new GameMakerString("PageItem " + index.ToString(CultureInfo.InvariantCulture)); // not Data.MakeString
        }

        if (reader.gameMakerData.IsTPAG4ByteAligned)
        {
            while (reader.Position % 4 != 0)
            {
                if (reader.ReadByte() != 0)
                {
                    reader.SubmitWarning("Missing expected TPAG padding");
                    reader.Position--;
                    break;
                }
            }
        }
    }
}

public class GameMakerChunkCODE : GameMakerListChunk<GameMakerCode>
{
    public override string Name => "CODE";

    /// <summary>
    /// Code count as determined during object counting.
    /// </summary>
    internal int CodeCount { get; private set; } = -1;

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (List == null)
            return;
        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (Length == 0) // YYC, bytecode <= 16, chunk is empty but exists
        {
            List = null;
            return;
        }

        base.UnserializeChunk(reader);
        reader.BytecodeAddresses = null;
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (Length == 0) // YYC, bytecode <= 16, chunk is empty but exists
        {
            return 0;
        }

        if (reader.gameMakerData.UnsupportedBytecodeVersion)
        {
            // In unsupported bytecode versions, there's no instructions parsed (so count is equivalent to the code count)
            var codeCount = reader.ReadUInt32();
            CodeCount = checked((int)codeCount);
            return codeCount;
        }

        var pointerCount = reader.ReadUInt32();
        if (pointerCount > int.MaxValue || (long)pointerCount * sizeof(uint) > reader.Length - reader.Position)
            throw new IOException("Code entry pointer table exceeds the available input.");
        CodeCount = (int)pointerCount;
        reader.Position -= 4;

        reader.BytecodeAddresses = new(CodeCount);
        var count = base.UnserializeObjectCount(reader);

        return count;
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerChunkVARI : GameMakerChunk
{
    public override string Name => "VARI";

    public uint VarCount1 { get; set; }

    public uint VarCount2 { get; set; }
    public uint MaxLocalVarCount { get; set; }
    public bool DifferentVarCounts { get; set; }
    public List<GameMakerVariable> List { get; set; } = [];

    // Number of variables when the chunk was read; more at write time means variables were added without touching the counters.
    private int _countAtRead = -1;

    public uint InstanceVarCount { get => VarCount1; set => VarCount1 = value; }
    public uint InstanceVarCountAgain { get => VarCount2; set => VarCount2 = value; }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (List == null)
            return;

        if (writer.gameMakerData.UnsupportedBytecodeVersion)
            return;

        GameMakerInstruction.SerializeReferenceChain(writer, writer.gameMakerData.Code, List);

        if (!writer.Bytecode14OrLower)
        {
            if (_countAtRead >= 0 && List.Count > _countAtRead)
            {
                // Only ever raise the counters here: they must not shrink below what the file declared
                var needed = DataRepair.RecomputeVariableCounters(writer.gameMakerData);
                VarCount1 = Math.Max(VarCount1, needed.VarCount1);
                VarCount2 = Math.Max(VarCount2, needed.VarCount2);
                MaxLocalVarCount = Math.Max(MaxLocalVarCount, needed.MaxLocalVarCount);
                _countAtRead = List.Count;
            }
            writer.Write(VarCount1);
            writer.Write(VarCount2);
            writer.Write(MaxLocalVarCount);
        }
        foreach (var var in List)
            writer.WriteGameMakerObject(var);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (Length == 0) // YYC, bytecode <= 16, chunk is empty but exists
        {
            List = null;
            return;
        }

        if (reader.gameMakerData.UnsupportedBytecodeVersion)
            return;
        var startPosition = reader.Position;
        uint varLength;
        if (!reader.Bytecode14OrLower)
        {
            if (Length < 12)
                throw new IOException("VARI chunk is shorter than its header.");

            VarCount1 = reader.ReadUInt32();
            VarCount2 = reader.ReadUInt32();
            DifferentVarCounts = VarCount1 != VarCount2;
            MaxLocalVarCount = reader.ReadUInt32();
            varLength = 20;
        }
        else
            varLength = 12;
        List.Clear();
        List.Capacity = (int)(Length / varLength);
        while (reader.Position - startPosition <= (long)Length - varLength)
            List.Add(reader.ReadGameMakerObject<GameMakerVariable>());
        _countAtRead = List.Count;
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (Length == 0)
            return 0;

        if (reader.gameMakerData.UnsupportedBytecodeVersion)
            return 0;

        if (!reader.Bytecode14OrLower)
        {
            if (Length < 12)
                throw new IOException("VARI chunk is shorter than its header.");

            reader.Position += 12;
            return (Length - 12) / 20;
        }
        else
            return Length / 12;
    }
}

public class GameMakerChunkFUNC : GameMakerChunk
{
    public override string Name => "FUNC";

    public GameMakerSimpleList<GameMakerFunction> Functions { get; set; } = [];
    public GameMakerSimpleList<GameMakerCodeLocals> CodeLocals { get; set; } = [];

    private bool checkedFor2024_8;

    private void CheckFor2024_8(GameMakerReader reader)
    {
        if (reader.gameMakerData.IsVersionAtLeast(2024, 8)
            || reader.Bytecode14OrLower || Length == 0) // Irrelevant or non-deductible
        {
            checkedFor2024_8 = true;
            return;
        }

        var returnPos = reader.Position;
        var chunkEndPos = returnPos + Length;

        // The CodeLocals list was removed in 2024.8, so we check if Functions
        // is the only thing in here.
        var funcCount = reader.ReadUInt32();
        // Skip over the (Simple)List
        // (3*4 is the size of an GameMakerFunction object)
        reader.Position += (long)funcCount * 3 * sizeof(uint);
        if (reader.Position == chunkEndPos)
        {
            // Directly reached the end of the chunk after the function list, so code locals are *definitely* missing
            reader.gameMakerData.SetGMS2Version(2024, 8);
            reader.Position = returnPos;
            checkedFor2024_8 = true;
            return;
        }

        // Then align the position
        var specAlign = reader.gameMakerData.PaddingAlignException;
        var paddingBytesRead = 0;
        while ((reader.AbsPosition & ((specAlign == -1 ? 16 : specAlign) - 1)) != 0)
        {
            if (reader.Position >= chunkEndPos || reader.ReadByte() != 0)
            {
                // If we hit a non-zero byte (or exceed chunk boundaries), it can't be padding
                reader.Position = returnPos;
                checkedFor2024_8 = true;
                return;
            }
            paddingBytesRead++;
        }

        // If we're at the end of the chunk after aligning padding, code locals are either empty
        // or do not exist altogether. If we read at least 4 padding bytes, we don't know for sure
        // unless we have at least one code entry.
        if (reader.Position == chunkEndPos && (paddingBytesRead < 4 || reader.gameMakerData.FORM.CODE.CodeCount > 0))
        {
            reader.gameMakerData.SetGMS2Version(2024, 8);
        }

        reader.Position = returnPos;
        checkedFor2024_8 = true;
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (Functions is null && CodeLocals is null)
            return;

        GameMakerInstruction.SerializeReferenceChain(writer, writer.gameMakerData.Code, Functions);

        if (writer.Bytecode14OrLower)
        {
            foreach (var f in Functions)
                writer.WriteGameMakerObject(f);
        }
        else
        {
            writer.WriteGameMakerObject(Functions);
            if (!writer.gameMakerData.IsVersionAtLeast(2024, 8))
                writer.WriteGameMakerObject(CodeLocals);
        }
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedFor2024_8)
            CheckFor2024_8(reader);

        if (Length == 0 && reader.gameMakerData.GeneralInfo?.BytecodeVersion > 14) // YYC, 14 < bytecode <= 16, chunk is empty but exists
        {
            Functions = null;
            CodeLocals = null;
            return;
        }

        if (reader.gameMakerData.UnsupportedBytecodeVersion)
            return;
        if (reader.Bytecode14OrLower)
        {
            var startPosition = reader.Position;
            Functions.Clear();
            Functions.SetCapacity(Length / 12);
            while (reader.Position - startPosition <= (long)Length - 12)
                Functions.Add(reader.ReadGameMakerObject<GameMakerFunction>());
            CodeLocals = null;
        }
        else
        {
            Functions = reader.ReadGameMakerObject<GameMakerSimpleList<GameMakerFunction>>();
            if (!reader.gameMakerData.IsVersionAtLeast(2024, 8))
                CodeLocals = reader.ReadGameMakerObject<GameMakerSimpleList<GameMakerCodeLocals>>();
            else
                CodeLocals = null;
        }
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2024_8 = false;
        CheckFor2024_8(reader);

        if (Length == 0 && reader.gameMakerData.GeneralInfo?.BytecodeVersion > 14)
            return 0;

        uint count = 0;

        if (!reader.Bytecode14OrLower)
        {
            count += 1 + GameMakerSimpleList<GameMakerFunction>.UnserializeChildObjectCount(reader);
            if (!reader.gameMakerData.IsVersionAtLeast(2024, 8))
                count += 1 + GameMakerSimpleList<GameMakerCodeLocals>.UnserializeChildObjectCount(reader);
        }
        else
            count = Length / 12;

        return count;
    }
}

public class GameMakerChunkSTRG : GameMakerAlignUpdatedListChunk<GameMakerString>
{
    public override string Name => "STRG";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        base.SerializeChunk(writer);

        // padding
        while (writer.Position % 0x80 != 0)
            writer.Write((byte)0);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        base.UnserializeChunk(reader);

        // padding
        while (reader.AbsPosition % 0x80 != 0)
            if (reader.ReadByte() != 0)
                throw new IOException("Padding error in STRG");
    }

    // There's no need to check padding in "UnserializeObjectCount()"
}

public class GameMakerChunkTXTR : GameMakerListChunk<GameMakerEmbeddedTexture>
{
    public override string Name => "TXTR";

    private bool checkedFor2022_3;
    private bool checkedFor2_0_6;

    private void CheckFor2022_3And5(GameMakerReader reader)
    {
        // Detect GM2022.3
        if (!reader.gameMakerData.IsVersionAtLeast(2, 3) || reader.gameMakerData.IsVersionAtLeast(2022, 3))
        {
            checkedFor2022_3 = true;
            return;
        }
        var positionToReturn = reader.Position;

        // Check for 2022.3 format
        var isGM2022_3 = false;
        var texCount = reader.ReadUInt32();
        if (texCount == 1) // If no textures exist, this could false positive.
        {
            reader.Position += 16; // Jump to either padding or length, depending on version
            if (reader.ReadUInt32() > 0) // Check whether it's padding or length
                isGM2022_3 = true;
        }
        else if (texCount > 1)
        {
            var firstTex = reader.ReadUInt32();
            var secondTex = reader.ReadUInt32();
            if (firstTex + 16 == secondTex)
                isGM2022_3 = true;
        }

        if (isGM2022_3)
        {
            reader.gameMakerData.SetGMS2Version(2022, 3);
            // Also check for 2022.5 format
            reader.Position = positionToReturn + 4;
            for (uint i = 0; i < texCount; i++)
            {
                // Go to each texture, and then to each texture's data
                reader.Position = positionToReturn + 4 + i * 4;
                reader.AbsPosition = reader.ReadUInt32() + 12; // Go to texture, at an offset
                reader.AbsPosition = reader.ReadUInt32(); // Go to texture data
                ReadOnlySpan<byte> header = reader.ReadBytes(4);
                if (!header.SequenceEqual(GMImage.MagicBz2Qoi))
                {
                    // Nothing useful, check the next texture
                    continue;
                }
                reader.Position += 4; // Skip width/height
                var is2022_5 = false;
                // Now check the actual BZ2 headers
                if (reader.ReadByte() != (byte)'B')
                    is2022_5 = true;
                else if (reader.ReadByte() != (byte)'Z')
                    is2022_5 = true;
                else if (reader.ReadByte() != (byte)'h')
                    is2022_5 = true;
                else
                {
                    reader.Position++;
                    if (reader.ReadUInt24() != 0x594131) // Digits of pi... (block header)
                        is2022_5 = true;
                    else if (reader.ReadUInt24() != 0x595326)
                        is2022_5 = true;
                }

                if (is2022_5)
                    reader.gameMakerData.SetGMS2Version(2022, 5);

                // Checked one QOI+BZ2 texture. No need to check any more
                break;
            }
        }

        reader.Position = positionToReturn;

        checkedFor2022_3 = true;
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        base.SerializeChunk(writer);

        // texture blobs
        foreach (var obj in List)
            obj?.SerializeBlob(writer);

        // Texture blobs are followed by 4-byte alignment padding.
        while (writer.Position % 4 != 0)
            writer.Write((byte)0);
    }

    // GMS 2.0.6.96 is the oldest available runtime version,
    // so this actually could be some other version between GMS 2.0 - 2.0.6.
    // (the oldest copy of "Zeus-Runtime.rss" on web.archive.org has this version as first one)
    private void CheckForGMS2_0_6(GameMakerReader reader)
    {
        var atLeastGMS2_0 = reader.gameMakerData.IsGameMaker2();
        if (!atLeastGMS2_0 || reader.gameMakerData.IsVersionAtLeast(2, 0, 6))
        {
            checkedFor2_0_6 = true;
            return;
        }

        var returnPos = reader.Position;
        var noGeneratedMips = false;

        var count = reader.ReadUInt32();
        if (count == 0)
        {
            reader.Position = returnPos;
            return;
        }

        if (count >= 2)
        {
            var firstPtr = reader.ReadUInt32();
            var secondPtr = reader.ReadUInt32();

            if (atLeastGMS2_0 && secondPtr - firstPtr == 8) // 2 < version < 2.2; "Scaled" + "_textureData" -> 8
            {
                noGeneratedMips = true;
            }
        }
        else
        {
            // Go to the first texture pointer (+ minimal texture entry size)
            reader.AbsPosition = reader.ReadUInt32() + 8;

            // If there is a zero instead of texture data pointer (which cannot be zero)
            if (reader.ReadUInt32() == 0)
                noGeneratedMips = true;
        }

        if (!noGeneratedMips)
            reader.gameMakerData.SetGMS2Version(2, 0, 6);

        reader.Position = returnPos;
        checkedFor2_0_6 = true;
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        var startPosition = reader.AbsPosition;

        if (!checkedFor2022_3)
            CheckFor2022_3And5(reader);

        if (!checkedFor2_0_6)
            CheckForGMS2_0_6(reader);

        base.UnserializeChunk(reader);
        reader.SwitchReaderType(false);

        // texture blobs
        for (var index = 0; index < List.Count; index++)
        {
            var obj = List[index];
            if (obj is null)
                continue;

            // Figure out max end of stream position for the texture, if it's embedded in the file
            if (!obj.TextureExternal)
            {
                var recordedSize = obj.GetTextureBlockSize();
                if (recordedSize > 0)
                {
                    // The size is stored in the file (in modern GM versions), so use it
                    long startPositionOfTextureData = reader.GetOffsetMapRev()[obj.TextureData];
                    obj.TextureData.SetMaxEndOfStreamPosition(startPositionOfTextureData + recordedSize);
                }
                else
                {
                    // Calculate maximum end stream position for this blob
                    var searchIndex = index + 1;
                    long maxEndOfStreamPosition = -1;
                    while (searchIndex < List.Count)
                    {
                        var searchObj = List[searchIndex];

                        if (searchObj is null || searchObj.TextureExternal)
                        {
                            // Skip this texture, as it's external
                            searchIndex++;
                            continue;
                        }

                        // Use start address of this blob
                        maxEndOfStreamPosition = reader.GetOffsetMapRev()[searchObj.TextureData];
                        break;
                    }

                    if (maxEndOfStreamPosition == -1)
                    {
                        // At end of list, so just use the end of the chunk
                        maxEndOfStreamPosition = startPosition + Length;
                    }
                    obj.TextureData.SetMaxEndOfStreamPosition(maxEndOfStreamPosition);
                }
            }

            obj.UnserializeBlob(reader);
            obj.Name = new GameMakerString("Texture " + index.ToString(CultureInfo.InvariantCulture));
        }

        // padding
        // (not "AbsPosition" because of "reader.SwitchReaderType(false)")
        while (reader.Position % 4 != 0)
            if (reader.ReadByte() != 0)
                throw new IOException("Padding error!");
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2022_3 = false;
        checkedFor2_0_6 = false;

        CheckFor2022_3And5(reader);
        CheckForGMS2_0_6(reader);

        // Texture blobs are already included in the count
        return base.UnserializeObjectCount(reader);
    }
}

public class GameMakerChunkAUDO : GameMakerListChunk<GameMakerEmbeddedAudio>
{
    public override string Name => "AUDO";

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        base.UnserializeChunk(reader);

        for (var index = 0; index < List.Count; index++)
        {
            if (List[index] is { } item)
                item.Name = new GameMakerString("EmbeddedSound " + index.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        // Though "GameMakerEmbeddedAudio" has dynamic child objects size,
        // there's still no need to unserialize the count for each object.
        var count = reader.ReadUInt32();
        if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
            throw new IOException("Embedded audio pointers exceed the available input.");

        return count;
    }
}

// GMS2 only
public class GameMakerChunkEMBI : GameMakerSimpleListChunk<GameMakerEmbeddedImage>
{
    public override string Name => "EMBI";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();
        writer.Write((uint)1); // Apparently hardcoded 1, see https://github.com/UnderminersTeam/GameMakerModTool/issues/4#issuecomment-421844420
        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();
        if (reader.ReadUInt32() != 1)
            throw new GameMakerSerializationException("Expected EMBI version 1");
        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (reader.ReadUInt32() != 1)
            throw new GameMakerSerializationException("Expected EMBI version 1");

        return base.UnserializeObjectCount(reader);
    }
}

// GMS2.2.1+ only
public class GameMakerChunkTGIN : GameMakerListChunk<GameMakerTextureGroupInfo>
{
    public override string Name => "TGIN";

    private bool checkedFor2022_9;
    private void CheckFor2022_9And2023(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsVersionAtLeast(2, 3)
            || reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
        {
            checkedFor2022_9 = true;
            return;
        }

        // Check for 2022.9
        var returnPosition = reader.AbsPosition;

        var isGM2022_9 = false;
        var tginCount = reader.ReadUInt32();
        if (tginCount > 0)
        {
            var tginPtr = reader.ReadUInt32();
            var secondTginPtr = (tginCount >= 2) ? reader.ReadUInt32() : (uint)(returnPosition + Length);
            reader.AbsPosition = tginPtr + 4;

            // Check to see if the pointer located at this address points within this object
            // If not, then we know we're using a new format!
            var ptr = reader.ReadUInt32();
            if (ptr < tginPtr || ptr >= secondTginPtr)
            {
                isGM2022_9 = true;
                if (!reader.gameMakerData.IsVersionAtLeast(2022, 9))
                    reader.gameMakerData.SetGMS2Version(2022, 9);
            }
        }

        reader.AbsPosition = returnPosition;

        // Check for 2023.1
        if (isGM2022_9)
        {
            reader.Position += 4; // Skip "tginCount"

            var firstEntryPtr = reader.ReadUInt32();
            // Go to the the 4th list pointer of the first TGIN entry.
            // (either to "Fonts" or "SpineTextures" depending on the version)
            reader.AbsPosition = firstEntryPtr + 16 + sizeof(uint) * 3; // +16 = "TexturePages" pointer
            var fourthPtr = reader.ReadUInt32();

            // If there's a "TexturePages" count instead of the 5th list pointer.
            // The count can't be greater than the pointer.
            // (the list could be either "Tilesets" or "Fonts").
            if (reader.ReadUInt32() <= fourthPtr)
            {
                reader.gameMakerData.SetGMS2Version(2023, 1, 0, 0, false);
            }
        }

        reader.AbsPosition = returnPosition;

        checkedFor2022_9 = true;
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        writer.Write((uint)1); // Version

        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (reader.ReadUInt32() != 1)
            throw new IOException("Expected TGIN version 1");

        if (!checkedFor2022_9)
            CheckFor2022_9And2023(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedFor2022_9 = false;

        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (reader.ReadUInt32() != 1)
            throw new IOException("Expected TGIN version 1");

        CheckFor2022_9And2023(reader);

        return base.UnserializeObjectCount(reader);
    }
}

internal static class GameMakerChunkVersion
{
    public static void WritePaddedVersion1(GameMakerWriter writer)
    {
        while (writer.Position % 4 != 0)
            writer.Write((byte)0);
        writer.Write((uint)1);
    }

    public static uint ReadPaddedVersion(GameMakerReader reader)
    {
        while (reader.AbsPosition % 4 != 0)
            if (reader.ReadByte() != 0)
                throw new IOException("Padding error!");
        return reader.ReadUInt32();
    }
}

// GMS2.3+ only
public class GameMakerChunkACRV : GameMakerListChunk<GameMakerAnimationCurve>
{
    public override string Name => "ACRV";

    private bool checkedForGMS2_3_1;

    // See also a similar check in GameMakerAnimationCurve.cs, necessary for embedded animation curves.
    private void CheckForGMS2_3_1(GameMakerReader reader)
    {
        if (reader.gameMakerData.IsVersionAtLeast(2, 3, 1))
        {
            checkedForGMS2_3_1 = true;
            return;
        }

        var returnTo = reader.Position;

        var count = reader.ReadUInt32();
        if (count == 0)
        {
            reader.Position = returnTo;
            checkedForGMS2_3_1 = true;
            return;
        }

        reader.AbsPosition = reader.ReadUInt32(); // Go to the first "Point"
        reader.Position += 8;

        if (reader.ReadUInt32() != 0) // In 2.3 an int with the value of 0 would be set here,
        {                             // It cannot be version 2.3 if this value isn't 0
            reader.gameMakerData.SetGMS2Version(2, 3, 1);
        }
        else
        {
            if (reader.ReadUInt32() == 0)                      // At all points (besides the first one)
                reader.gameMakerData.SetGMS2Version(2, 3, 1);  // if BezierX0 equals to 0 (the above check)
                                                               // then BezierY0 equals to 0 as well (the current check)
        }

        reader.Position = returnTo;

        checkedForGMS2_3_1 = true;
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        GameMakerChunkVersion.WritePaddedVersion1(writer);

        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (GameMakerChunkVersion.ReadPaddedVersion(reader) != 1)
            throw new IOException("Expected ACRV version 1");

        if (!checkedForGMS2_3_1)
            CheckForGMS2_3_1(reader);

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        checkedForGMS2_3_1 = false;

        if (GameMakerChunkVersion.ReadPaddedVersion(reader) != 1)
            throw new IOException("Expected ACRV version 1");

        CheckForGMS2_3_1(reader);

        return base.UnserializeObjectCount(reader);
    }
}

// GMS2.3+ only
public class GameMakerChunkSEQN : GameMakerListChunk<GameMakerSequence>
{
    public override string Name => "SEQN";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        GameMakerChunkVersion.WritePaddedVersion1(writer);

        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        // Apparently SEQN can be empty
        if (Length == 0)
            return;

        var version = GameMakerChunkVersion.ReadPaddedVersion(reader);
        if (version != 1)
            throw new IOException("Expected SEQN version 1, got " + version.ToString(CultureInfo.InvariantCulture));

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        // Apparently SEQN can be empty
        if (Length == 0)
            return 0;

        var version = GameMakerChunkVersion.ReadPaddedVersion(reader);
        if (version != 1)
            throw new IOException("Expected SEQN version 1, got " + version.ToString(CultureInfo.InvariantCulture));

        return base.UnserializeObjectCount(reader);
    }
}

// GMS2.3+ only
public class GameMakerChunkTAGS : GameMakerSingleChunk<GameMakerTags>
{
    public override string Name => "TAGS";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        GameMakerChunkVersion.WritePaddedVersion1(writer);

        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (GameMakerChunkVersion.ReadPaddedVersion(reader) != 1)
            throw new IOException("Expected TAGS version 1");

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (GameMakerChunkVersion.ReadPaddedVersion(reader) != 1)
            throw new IOException("Expected TAGS version 1");

        return base.UnserializeObjectCount(reader);
    }
}

// GMS2.3.6+ only
public class GameMakerChunkFEDS : GameMakerListChunk<GameMakerFilterEffect>
{
    public override string Name => "FEDS";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        GameMakerChunkVersion.WritePaddedVersion1(writer);

        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        if (GameMakerChunkVersion.ReadPaddedVersion(reader) != 1)
            throw new IOException("Expected FEDS version 1");

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        var version = GameMakerChunkVersion.ReadPaddedVersion(reader);
        if (version != 1)
            throw new IOException("Expected FEDS version 1, got " + version.ToString(CultureInfo.InvariantCulture));

        return base.UnserializeObjectCount(reader);
    }
}

// GM2022.8+ only
public class GameMakerChunkFEAT : GameMakerSingleChunk<GameMakerFeatureFlags>
{
    public override string Name => "FEAT";

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        if (!writer.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        while (writer.Position % 4 != 0)
            writer.Write((byte)0);

        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        // Padding
        while (reader.AbsPosition % 4 != 0)
            if (reader.ReadByte() != 0)
                throw new IOException("Padding error!");

        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        if (!reader.gameMakerData.IsGameMaker2())
            throw new InvalidOperationException();

        // Padding
        while (reader.AbsPosition % 4 != 0)
            if (reader.ReadByte() != 0)
                throw new IOException("Padding error!");

        return base.UnserializeObjectCount(reader);
    }
}

public abstract class GameMakerChunk2023List<T> : GameMakerListChunk<T> where T : GameMakerObject, new()
{
    protected abstract string VersionedChunkName { get; }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        EnsureSupportedVersion(writer.gameMakerData);
        writer.Align(4);
        writer.Write((uint)1);
        base.SerializeChunk(writer);
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        EnsureSupportedVersion(reader.gameMakerData);
        reader.Align(4);
        if (reader.ReadUInt32() != 1)
            throw new IOException("Expected " + VersionedChunkName + " version 1");

        BeforeUnserialize(reader);
        base.UnserializeChunk(reader);
    }

    internal override uint UnserializeObjectCount(GameMakerReader reader)
    {
        EnsureSupportedVersion(reader.gameMakerData);
        reader.Align(4);
        var version = reader.ReadUInt32();
        if (version != 1)
            throw new IOException("Expected " + VersionedChunkName + " version 1, got " + version.ToString(CultureInfo.InvariantCulture));

        BeforeUnserializeObjectCount(reader);
        return base.UnserializeObjectCount(reader);
    }

    protected virtual void BeforeUnserialize(GameMakerReader reader) { }
    protected virtual void BeforeUnserializeObjectCount(GameMakerReader reader) { }

    private static void EnsureSupportedVersion(GameMakerData data)
    {
        if (!data.IsVersionAtLeast(2023, 2))
            throw new InvalidOperationException();
    }
}

// GM2023.2+ only
public class GameMakerChunkPSEM : GameMakerChunk2023List<GameMakerParticleSystemEmitter>
{
    public override string Name => "PSEM";
    protected override string VersionedChunkName => Name;
    private bool checkedPsemVersion;
    private uint psemFormatMinor;

    private void SetPsemFormat(GameMakerReader reader, uint minor)
    {
        psemFormatMinor = minor;
        if (!reader.gameMakerData.IsVersionAtLeast(2023, minor))
            reader.gameMakerData.SetGMS2Version(2023, minor);
    }

    private void WithPsemFormat(GameMakerData data, Action action)
    {
        if (psemFormatMinor == 0)
        {
            action();
            return;
        }

        var generalInfo = data.GeneralInfo;
        (var major, var minor, var release, var build) = (generalInfo.Major, generalInfo.Minor, generalInfo.Release, generalInfo.Build);
        try
        {
            generalInfo.Major = 2023;
            generalInfo.Minor = psemFormatMinor;
            generalInfo.Release = 0;
            generalInfo.Build = 0;
            action();
        }
        finally
        {
            (generalInfo.Major, generalInfo.Minor, generalInfo.Release, generalInfo.Build) = (major, minor, release, build);
        }
    }

    private void CheckPsemVersion(GameMakerReader reader)
    {
        // Particle system emitters had the good grace to change three times in three versions
        // Three versions which are only detectable by optional features
        // This function checks for 2023.4, 2023.6, and 2023.8
        if (checkedPsemVersion)
        {
            return;
        }

        var positionToReturn = reader.AbsPosition;

        var count = reader.ReadUInt32();
        uint firstEmitterPointer = 0;
        uint secondEmitterPointer = 0;
        uint emitterCount = 0;
        var hasNullPointer = false;
        for (uint i = 0; i < count; i++)
        {
            var pointer = reader.ReadUInt32();
            if (pointer != 0)
            {
                emitterCount++;
                if (emitterCount == 1)
                    firstEmitterPointer = pointer;
                else if (emitterCount == 2)
                    secondEmitterPointer = pointer;
            }
            else
                hasNullPointer = true;
        }

        // Null pointer slots were introduced after the final PSEM layout change.
        if (hasNullPointer)
        {
            if (!reader.gameMakerData.IsVersionAtLeast(2024, 11))
                reader.gameMakerData.SetGMS2Version(2024, 11);
            SetPsemFormat(reader, 8);
            reader.AbsPosition = positionToReturn;
            checkedPsemVersion = true;
            return;
        }

        if (count < 11) // 2023.2 automatically adds eleven, later versions don't
        {
            SetPsemFormat(reader, 4);
        }

        if (emitterCount == 0) // Nothing more to do here, unfortunately
        {
            reader.AbsPosition = positionToReturn;
            checkedPsemVersion = true;
            return;
        }
        else if (emitterCount == 1) // Special case
        {
            // Fortunately, consistent padding means we need no parsing here
            var lengthWithoutNullSlots = Length - ((long)count - emitterCount) * sizeof(uint);
            if (lengthWithoutNullSlots == 0xF8)
            {
                SetPsemFormat(reader, 8);
            }
            else if (lengthWithoutNullSlots == 0xD8)
            {
                SetPsemFormat(reader, 6);
            }
            else if (lengthWithoutNullSlots == 0xC8)
            {
                SetPsemFormat(reader, 4);
            }
            else
            {
                reader.AbsPosition = positionToReturn;
                throw new IOException("Unrecognized PSEM size with only one element");
            }

            reader.AbsPosition = positionToReturn;
            checkedPsemVersion = true;
            return;
        }

        // More than one emitter
        var emitterSpacing = (long)secondEmitterPointer - firstEmitterPointer;
        if (emitterSpacing == 0xEC)
        {
            SetPsemFormat(reader, 8);
        }
        else if (emitterSpacing == 0xC0)
        {
            SetPsemFormat(reader, 6);
        }
        else if (emitterSpacing == 0xBC)
        {
            SetPsemFormat(reader, 4);
        }
        else if (emitterSpacing != 0xB0) // 2023.2
        {
            reader.AbsPosition = positionToReturn;
            throw new IOException("Unrecognized PSEM size with " + emitterCount + " non-null elements");
        }
        else
        {
            SetPsemFormat(reader, 2);
        }

        reader.AbsPosition = positionToReturn;
        checkedPsemVersion = true;
    }

    protected override void BeforeUnserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!checkedPsemVersion)
            CheckPsemVersion(reader);
    }

    internal override void SerializeChunk(GameMakerWriter writer)
    {
        WithPsemFormat(writer.gameMakerData, () => base.SerializeChunk(writer));
    }

    internal override void UnserializeChunk(GameMakerReader reader)
    {
        if (!checkedPsemVersion)
            CheckPsemVersion(reader);
        WithPsemFormat(reader.gameMakerData, () => base.UnserializeChunk(reader));
    }

    protected override void BeforeUnserializeObjectCount(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        checkedPsemVersion = false;
        CheckPsemVersion(reader);
    }
}
public class GameMakerChunkPSYS : GameMakerChunk2023List<GameMakerParticleSystem>
{
    public override string Name => "PSYS";
    protected override string VersionedChunkName => Name;
}
