using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace G3MLib.DataFile.Util
{
    // Initial implementation was based on DogScepter's implementation
    public class BufferBinaryReader : IBinaryReader
    {

        // A faster implementation of "MemoryStream"
        private sealed class ChunkBuffer
        {
            private byte[] _buffer = [];

            private int _length;
            public int Position { get; set; }
            public int Length { get => _length; }
            public int Capacity { get; }

            public ChunkBuffer(int capacity)
            {
                Capacity = capacity;
            }

            public void Prepare(int length)
            {
                if (_buffer.Length < length)
                    _buffer = new byte[length];
                _length = 0;
                Position = 0;
            }

            public void Read(byte[] buffer, int count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                if (count == 0)
                    return;

                int n = _length - Position;
                if (n < count)
                    throw new IOException("Reading out of chunk bounds");
                n = count;

                if (n <= 8)
                {
                    int byteCount = n;
                    while (--byteCount >= 0)
                        buffer[byteCount] = _buffer[Position + byteCount];
                }
                else
                    Buffer.BlockCopy(_buffer, Position, buffer, 0, n);
                Position += n;

            }
            public void Read(Span<byte> buffer)
            {
                if (buffer.IsEmpty)
                    return;

                int n = _length - Position;
                if (n < buffer.Length)
                    throw new IOException("Reading out of chunk bounds");
                n = buffer.Length;

                new Span<byte>(_buffer, Position, n).CopyTo(buffer);

                Position += n;
            }
            public ReadOnlySpan<byte> ReadSpan(int count)
            {
                int position = Position;
                if (count > _length - position)
                    throw new IOException("Reading out of chunk bounds");

                Position = position + count;
                return _buffer.AsSpan(position, count);
            }
            public byte ReadByte()
            {
                int currPos = Position;
                int newPos = Position + 1;
                if (newPos > _length)
                {
                    throw new IOException("Reading out of chunk bounds");
                }

                Position = newPos;
                return _buffer[currPos];
            }

            public void Write(byte[] buffer, int count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);

                int i = Position + count;
                if (i < 0)
                    throw new IOException("Writing out of the chunk buffer bounds.");

                // "MemoryStream" also extends the buffer if
                // the length becomes greater than the capacity
                _length = i;

                if ((count <= 8) && (buffer != _buffer))
                {
                    int byteCount = count;
                    while (--byteCount >= 0)
                    {
                        _buffer[Position + byteCount] = buffer[byteCount];
                    }
                }
                else
                {
                    Buffer.BlockCopy(buffer, 0, _buffer, Position, count);
                }

                Position = i;
            }
        }


        private readonly ChunkBuffer chunkBuffer;
        private readonly byte[] chunkCopyBuffer = new byte[81920];

        private readonly Encoding encoding = new UTF8Encoding(false);
        public Stream Stream { get; set; }

        private readonly long _length;
        public long Length { get => _length; }

        public long Position
        {
            get => chunkBuffer.Position;
            set
            {
                if (value < 0 || value > chunkBuffer.Length)
                    throw new IOException("Reading out of chunk bounds.");
                chunkBuffer.Position = (int)value;
            }
        }
        public long ChunkStartPosition { get; set; }

        public BufferBinaryReader(Stream stream, Encoding encoding = null)
        {
            _length = stream.Length;
            Stream = stream;

            // Check data file length
            if (Length >= 12 * 1024 * 1024) // 12 MB
                chunkBuffer = new(12 * 1024 * 1024 + 8);
            else
                chunkBuffer = new((int)Length);

            if (stream.Position != 0)
                stream.Seek(0, SeekOrigin.Begin);

            if (encoding is not null)
                this.encoding = encoding;
        }

        public void CopyChunkToBuffer(uint length)
        {
            long copyLength = (long)length + 8; // Chunk name + length
            if (copyLength > chunkBuffer.Capacity)
                throw new IOException("Chunk exceeds the reader buffer capacity.");
            if (Stream.Position < 8 || copyLength > Stream.Length - (Stream.Position - 8))
                throw new IOException("Reading out of chunk bounds");

            Stream.Position -= 8; // Chunk name + length
            chunkBuffer.Prepare((int)copyLength);

            // Source - https://stackoverflow.com/a/13022108/12136394
            int read;
            int remaining = (int)copyLength;
            while (remaining > 0 &&
                   (read = Stream.Read(chunkCopyBuffer, 0, Math.Min(chunkCopyBuffer.Length, remaining))) > 0)
            {
                chunkBuffer.Write(chunkCopyBuffer, read);
                remaining -= read;
            }
            if (remaining != 0)
                throw new IOException("Reading out of chunk bounds");

            Stream.Position -= length;
            chunkBuffer.Position -= (int)length;

            ChunkStartPosition = Stream.Position;
        }
        private string ReadString(int length)
        {
            if (length > 1024)
            {
                byte[] stringBuffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    chunkBuffer.Read(stringBuffer, length);
                    return encoding.GetString(stringBuffer, 0, length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(stringBuffer);
                }
            }

            Span<byte> buffer = stackalloc byte[length];
            if (!buffer.IsEmpty)
                chunkBuffer.Read(buffer);
            return encoding.GetString(buffer);
        }

        internal void ReadExactly(Span<byte> destination)
        {
            chunkBuffer.Read(destination);
        }

        public byte ReadByte()
        {
            return chunkBuffer.ReadByte();
        }

        public virtual bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        public string ReadChars(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > chunkBuffer.Length - chunkBuffer.Position)
            {
                throw new IOException("Reading out of chunk bounds");
            }

            return ReadString(count);
        }

        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > chunkBuffer.Length - chunkBuffer.Position)
            {
                throw new IOException("Reading out of chunk bounds");
            }

            byte[] val = new byte[count];
            if (count > 0)
                chunkBuffer.Read(val, count);
            return val;
        }

        public short ReadInt16()
        {
            return BinaryPrimitives.ReadInt16LittleEndian(chunkBuffer.ReadSpan(2));
        }

        public ushort ReadUInt16()
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(chunkBuffer.ReadSpan(2));
        }

        public int ReadInt24()
        {
            ReadOnlySpan<byte> value = chunkBuffer.ReadSpan(3);
            return value[0] | value[1] << 8 | (sbyte)value[2] << 16;
        }

        public uint ReadUInt24()
        {
            ReadOnlySpan<byte> value = chunkBuffer.ReadSpan(3);
            return (uint)(value[0] | value[1] << 8 | value[2] << 16);
        }

        public int ReadInt32()
        {
            return BinaryPrimitives.ReadInt32LittleEndian(chunkBuffer.ReadSpan(4));
        }

        public uint ReadUInt32()
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(chunkBuffer.ReadSpan(4));
        }

        public float ReadSingle()
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(chunkBuffer.ReadSpan(4)));
        }

        public double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(chunkBuffer.ReadSpan(8)));
        }

        public long ReadInt64()
        {
            return BinaryPrimitives.ReadInt64LittleEndian(chunkBuffer.ReadSpan(8));
        }

        public ulong ReadUInt64()
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(chunkBuffer.ReadSpan(8));
        }

        public string ReadGMString()
        {
            if (5 > chunkBuffer.Length - chunkBuffer.Position)
                throw new IOException("Reading out of chunk bounds");

            int length = BinaryPrimitives.ReadInt32LittleEndian(chunkBuffer.ReadSpan(4));

            if (length < 0)
                throw new IOException("Invalid string length");
            if ((long)length + 1 > chunkBuffer.Length - chunkBuffer.Position)
                throw new IOException("Reading out of chunk bounds");

            string res = ReadString(length);

            if (ReadByte() != 0)
            {
                throw new IOException("String not null terminated!");
            }

            return res;
        }

        public void SkipGMString()
        {
            if (sizeof(int) > chunkBuffer.Length - chunkBuffer.Position)
                throw new IOException("Reading out of chunk bounds");

            int length = BinaryPrimitives.ReadInt32LittleEndian(chunkBuffer.ReadSpan(4));
            if (length < 0)
                throw new IOException("Invalid string length");
            Position += (uint)length + 1;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
