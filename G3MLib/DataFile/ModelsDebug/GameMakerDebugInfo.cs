using System.Collections.ObjectModel;
using System.IO;

namespace G3MLib.DataFile.ModelsDebug;

public class GameMakerDebugInfo : ObservableCollection<GameMakerDebugInfo.DebugInfoPair>, GameMakerResource
{
    public class DebugInfoPair
    {
        public uint BytecodeOffset { get; set; }
        public uint SourceCodeOffset { get; set; }
    }

    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(checked((uint)((long)Count * 2)));
        foreach (var pair in this)
        {
            writer.Write(pair.BytecodeOffset);
            writer.Write(pair.SourceCodeOffset);
        }
    }

    public void Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Clear();
        var valueCount = reader.ReadUInt32();
        if (valueCount % 2 != 0)
            throw new IOException("Debug info entry has an odd value count.");
        if ((long)valueCount * sizeof(uint) > reader.Length - reader.Position)
            throw new IOException("Debug info values exceed the available input.");
        var count = valueCount / 2;
        for (uint i = 0; i < count; i++)
            Add(new DebugInfoPair() { BytecodeOffset = reader.ReadUInt32(), SourceCodeOffset = reader.ReadUInt32() });
    }

    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var valueCount = reader.ReadUInt32();
        if (valueCount % 2 != 0)
            throw new IOException("Debug info entry has an odd value count.");
        if ((long)valueCount * sizeof(uint) > reader.Length - reader.Position)
            throw new IOException("Debug info values exceed the available input.");
        reader.Position += (long)valueCount * sizeof(uint);
        return 0;
    }
}
