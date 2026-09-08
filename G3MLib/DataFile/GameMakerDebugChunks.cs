using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.ModelsDebug;

namespace G3MLib.DataFile
{
    public class GameMakerDebugFORM : GameMakerChunk
    {
        public override string Name => "FORM";

        public Dictionary<string, GameMakerChunk> Chunks { get; set; } = new Dictionary<string, GameMakerChunk>();

        private static readonly Dictionary<string, Func<GameMakerChunk>> ChunkConstructors = new()
        {
            ["SCPT"] = () => new GameMakerDebugChunkSCPT(),
            ["DBGI"] = () => new GameMakerDebugChunkDBGI(),
            ["INST"] = () => new GameMakerDebugChunkINST(),
            ["LOCL"] = () => new GameMakerDebugChunkLOCL(),
            ["STRG"] = () => new GameMakerDebugChunkSTRG()
        };

        public GameMakerDebugChunkSCPT SCPT => Chunks.GetValueOrDefault("SCPT") as GameMakerDebugChunkSCPT;
        public GameMakerDebugChunkDBGI DBGI => Chunks.GetValueOrDefault("DBGI") as GameMakerDebugChunkDBGI;
        public GameMakerDebugChunkINST INST => Chunks.GetValueOrDefault("INST") as GameMakerDebugChunkINST;
        public GameMakerDebugChunkLOCL LOCL => Chunks.GetValueOrDefault("LOCL") as GameMakerDebugChunkLOCL;
        public GameMakerDebugChunkSTRG STRG => Chunks.GetValueOrDefault("STRG") as GameMakerDebugChunkSTRG;

        internal override void SerializeChunk(GameMakerWriter writer)
        {
            foreach (var chunk in Chunks)
            {
                writer.Write(chunk.Value);
            }
        }

        internal override void UnserializeChunk(GameMakerReader reader)
        {
            Chunks.Clear();
            long startPos = reader.Position;
            long formEnd = startPos + Length;
            while (reader.Position < formEnd)
            {
                GameMakerChunk chunk = ReadChunk(reader, formEnd);
                if (Chunks.ContainsKey(chunk.Name))
                    throw new IOException("Duplicate chunk " + chunk.Name);
                Chunks.Add(chunk.Name, chunk);
            }
        }

        internal override uint UnserializeObjectCount(GameMakerReader reader)
        {
            uint totalCount = 0;
            long startPos = reader.Position;
            long formEnd = startPos + Length;

            while (reader.Position < formEnd)
            {
                (uint count, GameMakerChunk chunk) = CountDebugChunkChildObjects(reader, formEnd);
                totalCount += count;
                Chunks.Add(chunk.Name, chunk);
            }

            return totalCount;
        }

        private static GameMakerChunk ReadChunk(GameMakerReader reader, long formEnd)
        {
            string name = "(unknown)";
            try
            {
                name = reader.ReadChars(4);
                uint length = reader.ReadUInt32();
                if (length > formEnd - reader.Position)
                    throw new IOException("Debug chunk exceeds FORM bounds.");

                GameMakerChunk chunk = CreateChunk(name);
                chunk.Length = length;

                reader.CopyChunkToBuffer(length);
                GameMakerReader.EnsureLengthOperation lengthReader = reader.EnsureLengthFromHere(length);
                chunk.UnserializeChunk(reader);
                lengthReader.ToHere();
                reader.SwitchReaderType(false);
                return chunk;
            }
            catch (Exception exception)
            {
                throw new IOException($"Failed to read debug chunk \"{name}\".", exception);
            }
        }

        private static (uint, GameMakerChunk) CountDebugChunkChildObjects(GameMakerReader reader, long formEnd)
        {
            string name = reader.ReadChars(4);
            uint length = reader.ReadUInt32();
            if (length > formEnd - reader.Position)
                throw new IOException("Debug chunk exceeds FORM bounds.");

            GameMakerChunk chunk = CreateChunk(name);
            chunk.Length = length;
            long startPos = reader.Position;

            try
            {
                reader.CopyChunkToBuffer(length);
                uint count = chunk.UnserializeObjectCount(reader);
                return (count, chunk);
            }
            catch (Exception exception)
            {
                throw new IOException($"Failed to count debug chunk \"{name}\".", exception);
            }
            finally
            {
                reader.SwitchReaderType(false);
                reader.Position = startPos + length;
            }
        }

        private static GameMakerChunk CreateChunk(string name)
        {
            return ChunkConstructors.TryGetValue(name, out Func<GameMakerChunk> constructor)
                ? constructor()
                : new GameMakerDebugUnsupportedChunk(name);
        }
    }

    internal sealed class GameMakerDebugUnsupportedChunk(string name) : GameMakerUnsupportedChunk
    {
        public override string Name { get; } = name;
    }

    public class GameMakerDebugChunkSCPT : GameMakerListChunk<GameMakerScriptSource>
    {
        public override string Name => "SCPT";
    }

    public class GameMakerDebugChunkDBGI : GameMakerChunk
    {
        public override string Name => "DBGI";

        public List<uint> CodeEntryIndices { get; } = [];
        public List<GameMakerDebugInfo> List { get; } = [];

        internal override void SerializeChunk(GameMakerWriter writer)
        {
            if (CodeEntryIndices.Count == 0 || CodeEntryIndices[^1] != List.Count)
                throw new InvalidOperationException("Debug info code-entry index table must end with debug info count.");

            uint previousIndex = 0;
            foreach (uint index in CodeEntryIndices)
            {
                if (index < previousIndex)
                    throw new InvalidOperationException("Debug info code-entry index table must be ordered.");
                previousIndex = index;
            }

            writer.Write((uint)(CodeEntryIndices.Count - 1));
            foreach (uint codeEntryIndex in CodeEntryIndices)
                writer.Write(codeEntryIndex);
            foreach (GameMakerDebugInfo entry in List)
                writer.WriteGameMakerObjectPointer(entry);
            foreach (GameMakerDebugInfo entry in List)
                writer.WriteGameMakerObject(entry);
        }

        internal override void UnserializeChunk(GameMakerReader reader)
        {
            uint count = reader.ReadUInt32();
            if (((long)count + 1) * sizeof(uint) > reader.Length - reader.Position)
                throw new IOException("Debug info index table exceeds the available input.");
            CodeEntryIndices.Clear();
            for (uint i = 0; i < count; i++)
                CodeEntryIndices.Add(reader.ReadUInt32());
            CodeEntryIndices.Add(reader.ReadUInt32());

            uint debugInfoCount = CodeEntryIndices[^1];
            for (int i = 1; i < CodeEntryIndices.Count; i++)
            {
                if (CodeEntryIndices[i] < CodeEntryIndices[i - 1])
                    throw new IOException("Debug info code-entry index table is unordered.");
            }
            if ((long)debugInfoCount * sizeof(uint) > reader.Length - reader.Position)
                throw new IOException("Debug info pointer table exceeds the available input.");
            List.Clear();
            for (uint i = 0; i < debugInfoCount; i++)
                List.Add(reader.ReadGameMakerObjectPointer<GameMakerDebugInfo>());

            foreach (GameMakerDebugInfo entry in List.Where(entry => entry is not null))
                reader.ReadGameMakerObject(entry);
        }

        internal override uint UnserializeObjectCount(GameMakerReader reader)
        {
            uint count = reader.ReadUInt32();
            if (((long)count + 1) * sizeof(uint) > reader.Length - reader.Position)
                throw new IOException("Debug info index table exceeds the available input.");
            uint debugInfoCount = 0;
            uint previousIndex = 0;
            for (uint i = 0; i < count; i++)
            {
                debugInfoCount = reader.ReadUInt32();
                if (debugInfoCount < previousIndex)
                    throw new IOException("Debug info code-entry index table is unordered.");
                previousIndex = debugInfoCount;
            }
            debugInfoCount = reader.ReadUInt32();
            if (debugInfoCount < previousIndex)
                throw new IOException("Debug info code-entry index table is unordered.");

            long pointerListPosition = reader.AbsPosition;
            if ((long)debugInfoCount * sizeof(uint) > reader.Length - reader.Position)
                throw new IOException("Debug info pointer table exceeds the available input.");
            uint totalCount = 0;
            for (uint i = 0; i < debugInfoCount; i++)
            {
                reader.AbsPosition = pointerListPosition + ((long)i * sizeof(uint));
                uint pointer = reader.ReadUInt32();
                if (pointer == 0)
                    continue;

                try
                {
                    reader.AbsPosition = pointer;
                    totalCount = checked(totalCount + 1 + reader.GetChildObjectCount<GameMakerDebugInfo>());
                }
                catch (Exception exception)
                {
                    throw new IOException($"Failed to count debug info entry {i} at 0x{pointer:X8}.", exception);
                }
            }
            return totalCount;
        }
    }

    public class GameMakerDebugChunkINST : GameMakerListChunk<GameMakerInstanceVars>
    {
        public override string Name => "INST";
    }

    public class GameMakerDebugChunkLOCL : GameMakerListChunk<GameMakerCodeLocals>
    {
        public override string Name => "LOCL";
    }

    public class GameMakerDebugChunkSTRG : GameMakerAlignUpdatedListChunk<GameMakerString>
    {
        public override string Name => "STRG";
    }
}
