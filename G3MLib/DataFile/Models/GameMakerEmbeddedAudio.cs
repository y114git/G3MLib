using System;
using System.IO;

namespace G3MLib.DataFile.Models;

/// <summary>
/// An embedded audio entry in a data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerEmbeddedAudio : GameMakerNamedResource, PaddedObject, IDisposable
{
    /// <summary>
    /// The name of the embedded audio entry.
    /// </summary>
    /// <remarks>This is a UTMT only attribute. GameMaker does not store names for them.</remarks>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The audio data of the embedded audio entry.
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((uint)Data.Length);
        writer.Write(Data);
    }

    /// <inheritdoc />
    public void SerializePadding(GameMakerWriter writer)
    {
        while (writer.Position % 4 != 0)
            writer.Write((byte)0);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        uint len = reader.ReadUInt32();
        if (len > int.MaxValue || len > reader.Length - reader.Position)
            throw new IOException("Embedded audio data exceeds the available input.");
        Data = reader.ReadBytes((int)len);
        Util.DebugUtil.Assert(Data.Length == len);
    }

    /// <inheritdoc />
    public void UnserializePadding(GameMakerReader reader)
    {
        while (reader.AbsPosition % 4 != 0)
            if (reader.ReadByte() != 0)
                throw new IOException("Padding error!");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name?.Content ?? "EmbeddedSound Unknown Index"} ({GetType().Name})";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        Data = Array.Empty<byte>();
    }
}