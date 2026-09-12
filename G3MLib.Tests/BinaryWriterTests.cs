using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using G3MLib.DataFile.Util;
using Xunit;

namespace G3MLib.Tests;

public sealed class BinaryWriterTests
{
    [Fact]
    public void BufferedChunksGrowAndRejectStaleBytesAfterShrinking()
    {
        const int maximumChunk = 12 * 1024 * 1024;
        using var stream = new MemoryStream(new byte[maximumChunk + 8]);
        using var reader = new BufferBinaryReader(stream);
        foreach (int length in new[] { 4, maximumChunk, 1 })
        {
            stream.Position = 8;
            stream.WriteByte(42);
            stream.Position = 8;
            reader.CopyChunkToBuffer((uint)length);
            Assert.Equal(42, reader.ReadByte());
            reader.Position = length + 8;
            Assert.Throws<IOException>(() => reader.ReadByte());
        }
    }

    [Fact("BinaryWriterTests.cs", 9)]
    public void FileBinaryWriterWritesLittleEndianValuesAndGameMakerStrings()
    {
        using MemoryStream stream = new();
        using (FileBinaryWriter writer = new(stream))
        {
            writer.WriteInt24(-2);
            writer.WriteUInt24(10597059u);
            writer.WriteGMString("G3M");
        }
        byte[] written = stream.ToArray();
        Assert.Equal(new byte[6] { 254, 255, 255, 195, 178, 161 }, written[..6]);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(6)));
        Assert.Equal("G3M", Encoding.UTF8.GetString(written, 10, 3));
        Assert.Equal(0, written[13]);
    }

    [Fact("BinaryWriterTests.cs", 27)]
    public void BufferBinaryWriterFlushesWrittenBytesOnly()
    {
        using MemoryStream stream = new();
        using (BufferBinaryWriter writer = new(stream))
        {
            writer.Write((ushort)48879);
            writer.WriteInt24(-2);
            writer.WriteUInt24(10597059u);
            writer.WriteGMString("G3M");
            writer.Flush();
        }
        byte[] written = stream.ToArray();
        Assert.Equal(16, written.Length);
        Assert.Equal(48879, BinaryPrimitives.ReadUInt16LittleEndian(written));
        Assert.Equal(new byte[6] { 254, 255, 255, 195, 178, 161 }, written.AsSpan(2, 6).ToArray());
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(8)));
        Assert.Equal("G3M", Encoding.UTF8.GetString(written, 12, 3));
        Assert.Equal(0, written[15]);
    }

    [Fact("BinaryWriterTests.cs", 50)]
    public void BufferBinaryWriterWritesFloatingPointValuesAsLittleEndianBits()
    {
        using MemoryStream stream = new();
        using (BufferBinaryWriter writer = new(stream))
        {
            writer.Write(1.5f);
            writer.Write(-2.25d);
            writer.Flush();
        }

        Assert.Equal(new byte[12] { 0, 0, 192, 63, 0, 0, 0, 0, 0, 0, 2, 192 }, stream.ToArray());
    }

    [Fact("BinaryWriterTests.cs", 64)]
    public void FileBinaryWriterWritesLongUtf8GameMakerStrings()
    {
        string value = new('\u00E9', 1025);
        using MemoryStream stream = new();
        using (FileBinaryWriter writer = new(stream))
        {
            writer.WriteGMString(value);
        }

        byte[] written = stream.ToArray();
        Assert.Equal(2050, BinaryPrimitives.ReadInt32LittleEndian(written));
        Assert.Equal(value, Encoding.UTF8.GetString(written, 4, 2050));
        Assert.Equal(0, written[2054]);
    }

    [Fact("BinaryWriterTests.cs", 81)]
    public void FileBinaryReaderReadsLongUtf8GameMakerStrings()
    {
        string value = new('\u00E9', 1025);
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        byte[] input = new byte[encoded.Length + 5];
        BinaryPrimitives.WriteInt32LittleEndian(input, encoded.Length);
        encoded.CopyTo(input, 4);

        using FileBinaryReader reader = new(new MemoryStream(input));
        Assert.Equal(value, reader.ReadGMString());
    }

    [Fact("BinaryWriterTests.cs", 96)]
    public void BufferBinaryReaderReadsBufferedChunkValues()
    {
        string value = new('\u00E9', 1025);
        byte[] chunk;
        using (MemoryStream stream = new())
        {
            using (FileBinaryWriter writer = new(stream))
            {
                writer.Write((short)-2);
                writer.Write((ushort)48879);
                writer.WriteInt24(-2);
                writer.WriteUInt24(10597059u);
                writer.Write(2712847316u);
                writer.Write(1.5f);
                writer.Write(-2.25d);
                writer.WriteGMString(value);
            }
            chunk = stream.ToArray();
        }

        byte[] input = new byte[chunk.Length + 8];
        Encoding.ASCII.GetBytes("TEST").CopyTo(input, 0);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4), chunk.Length);
        chunk.CopyTo(input, 8);

        using MemoryStream inputStream = new(input);
        using BufferBinaryReader reader = new(inputStream);
        inputStream.Position = 8;
        reader.CopyChunkToBuffer((uint)chunk.Length);
        Assert.Equal(-2, reader.ReadInt16());
        Assert.Equal(48879, reader.ReadUInt16());
        Assert.Equal(-2, reader.ReadInt24());
        Assert.Equal(10597059u, reader.ReadUInt24());
        Assert.Equal(2712847316u, reader.ReadUInt32());
        Assert.Equal(1.5f, reader.ReadSingle());
        Assert.Equal(-2.25d, reader.ReadDouble());
        Assert.Equal(value, reader.ReadGMString());
    }
}
