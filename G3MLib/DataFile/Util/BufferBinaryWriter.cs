using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace G3MLib.DataFile.Util;

public class BufferBinaryWriter : IDisposable
{
    private const int BaseBufferSize = 1024 * 1024 * 32;

    private readonly Stream stream;
    private byte[] buffer;
    private long offset;
    private int currentSize;

    public uint Position
    {
        get => (uint)offset; set
        {
            ResizeToFit((int)value);
            offset = value;
        }
    }

    public byte[] RawBuffer => buffer;
    public Encoding Encoding { get; }

    public BufferBinaryWriter(Stream stream)
    {
        this.stream = stream;
        buffer = new byte[BaseBufferSize];
        currentSize = 0;
        offset = 0;

        Encoding = new UTF8Encoding(false);
    }

    private void ResizeToFit(int size)
    {
        while (size > buffer.Length)
        {
            var newSize = (long)buffer.Length * 2;
            if (newSize is < 0 or > (int.MaxValue - 1024))
            {
                Array.Resize(ref buffer, int.MaxValue - 1024);
                break;
            }
            else
            {
                Array.Resize(ref buffer, (int)newSize);
            }
        }
        if (currentSize < size)
            currentSize = size;
    }

    public void Write(byte value)
    {
        ResizeToFit((int)offset + 1);
        buffer[offset++] = value;
    }

    public void Write(sbyte value)
    {
        ResizeToFit((int)offset + 1);
        buffer[offset++] = (byte)value;
    }

    public virtual void Write(bool value)
    {
        ResizeToFit((int)offset + 1);
        buffer[offset++] = (byte)(value ? 1 : 0);
    }

    public void Write(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ResizeToFit((int)offset + value.Length);
        Buffer.BlockCopy(value, 0, buffer, (int)offset, value.Length);
        offset += value.Length;
    }

    public void Write(MemoryStream value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ResizeToFit((int)(offset + value.Length));
        Buffer.BlockCopy(value.GetBuffer(), 0, buffer, (int)offset, (int)value.Length);
        offset += value.Length;
    }

    public void Write(char[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ResizeToFit((int)offset + value.Length);
        foreach (var c in value)
            buffer[offset++] = Convert.ToByte(c);
    }

    public void Write(ushort value)
    {
        ResizeToFit((int)offset + 2);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
    }

    public void Write(short value)
    {
        ResizeToFit((int)offset + 2);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
    }

    public void Write(float value)
    {
        ResizeToFit((int)offset + 4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan((int)offset, 4), BitConverter.SingleToInt32Bits(value));
        offset += 4;
    }
    public void WriteInt24(int value)
    {
        ResizeToFit((int)offset + 3);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
    }

    public void WriteUInt24(uint value)
    {
        ResizeToFit((int)offset + 3);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
    }

    public void Write(int value)
    {
        ResizeToFit((int)offset + 4);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
        buffer[offset++] = (byte)((value >> 24) & 0xFF);
    }

    public void Write(uint value)
    {
        ResizeToFit((int)offset + 4);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
        buffer[offset++] = (byte)((value >> 24) & 0xFF);
    }

    public void Write(double value)
    {
        ResizeToFit((int)offset + 8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan((int)offset, 8), BitConverter.DoubleToInt64Bits(value));
        offset += 8;
    }

    public void Write(ulong value)
    {
        ResizeToFit((int)offset + 8);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
        buffer[offset++] = (byte)((value >> 24) & 0xFF);
        buffer[offset++] = (byte)((value >> 32) & 0xFF);
        buffer[offset++] = (byte)((value >> 40) & 0xFF);
        buffer[offset++] = (byte)((value >> 48) & 0xFF);
        buffer[offset++] = (byte)((value >> 56) & 0xFF);
    }

    public void Write(long value)
    {
        ResizeToFit((int)offset + 8);
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)((value >> 8) & 0xFF);
        buffer[offset++] = (byte)((value >> 16) & 0xFF);
        buffer[offset++] = (byte)((value >> 24) & 0xFF);
        buffer[offset++] = (byte)((value >> 32) & 0xFF);
        buffer[offset++] = (byte)((value >> 40) & 0xFF);
        buffer[offset++] = (byte)((value >> 48) & 0xFF);
        buffer[offset++] = (byte)((value >> 56) & 0xFF);
    }

    public void WriteGMString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var len = Encoding.GetByteCount(value);
        ResizeToFit((int)offset + len + 5);
        buffer[offset++] = (byte)(len & 0xFF);
        buffer[offset++] = (byte)((len >> 8) & 0xFF);
        buffer[offset++] = (byte)((len >> 16) & 0xFF);
        buffer[offset++] = (byte)((len >> 24) & 0xFF);
        Encoding.GetBytes(value, 0, value.Length, buffer, (int)offset);
        offset += len;
        buffer[offset++] = 0;
    }

    public virtual void Flush()
    {
        stream.Write(buffer, 0, currentSize);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        stream.Close();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
