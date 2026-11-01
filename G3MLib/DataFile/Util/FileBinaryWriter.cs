using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace G3MLib.DataFile.Util;

public class FileBinaryWriter : BinaryWriter
{
    public uint Position
    {
        get => (uint)OutStream.Position;
        set => OutStream.Position = value;
    }

    public Encoding Encoding { get; } = new UTF8Encoding(false);

    public FileBinaryWriter(Stream stream, Encoding? encoding = null) : base(stream)
    {
        if (encoding is not null)
            Encoding = encoding;
    }

    public void Write(MemoryStream value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.CopyTo(OutStream);
    }

    public override void Write(char[] chars)
    {
        ArgumentNullException.ThrowIfNull(chars);
        foreach (var c in chars)
            OutStream.WriteByte(Convert.ToByte(c));
    }

    public void WriteInt24(int value)
    {
        Span<byte> buffer = [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF)];
        OutStream.Write(buffer);
    }

    public void WriteUInt24(uint value)
    {
        Span<byte> buffer = [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF)];
        OutStream.Write(buffer);
    }

    public void WriteGMString(string value)
    {
        var len = Encoding.GetByteCount(value);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, len);
        OutStream.Write(buf);

        if (len > 1024)
        {
            var stringBuffer = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                Encoding.GetBytes(value.AsSpan(), stringBuffer);
                OutStream.Write(stringBuffer, 0, len);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(stringBuffer);
            }
        }
        else
        {
            Span<byte> stringBuf = stackalloc byte[len];
            Encoding.GetBytes(value.AsSpan(), stringBuf);
            OutStream.Write(stringBuf);
        }
        OutStream.WriteByte(0);
    }
}
