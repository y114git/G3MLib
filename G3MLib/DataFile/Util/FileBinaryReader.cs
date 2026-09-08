using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace G3MLib.DataFile.Util
{
    // Reimplemented based on DogScepter's implementation
    public class FileBinaryReader : IBinaryReader
    {
        private const string BoundsMessage = "Reading out of bounds";

        private readonly byte[] buffer = new byte[16];

        private readonly Encoding encoding = new UTF8Encoding(false);
        public Stream Stream { get; set; }

        private readonly long _length;
        public long Length { get => _length; }

        public long Position
        {
            get => Stream.Position;
            set
            {
                if (value < 0 || value > Length)
                    throw new IOException("Reading out of bounds.");
                Stream.Position = value;
            }
        }

        public FileBinaryReader(Stream stream, Encoding encoding = null)
        {
            _length = stream.Length;
            Stream = stream;

            if (stream.Position != 0)
                stream.Seek(0, SeekOrigin.Begin);

            if (encoding is not null)
                this.encoding = encoding;
        }

        private ReadOnlySpan<byte> ReadToBuffer(int count)
        {
            Stream.ReadExactly(buffer, 0, count);
            return buffer;
        }

        private string ReadString(int length)
        {
            if (length > 1024)
            {
                byte[] stringBuffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    Stream.ReadExactly(stringBuffer, 0, length);
                    return encoding.GetString(stringBuffer, 0, length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(stringBuffer);
                }
            }

            Span<byte> buffer = stackalloc byte[length];
            Stream.ReadExactly(buffer);
            return encoding.GetString(buffer);
        }

        internal void ReadExactly(Span<byte> destination)
        {
            if (destination.Length > _length - Stream.Position)
                throw new IOException(BoundsMessage);

            Stream.ReadExactly(destination);
        }

        public byte ReadByte()
        {
            if (1 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return (byte)Stream.ReadByte();
        }

        public virtual bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        public string ReadChars(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return ReadString(count);
        }

        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            byte[] val = new byte[count];
            Stream.ReadExactly(val, 0, count);
            return val;
        }

        public short ReadInt16()
        {
            if (2 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BinaryPrimitives.ReadInt16LittleEndian(ReadToBuffer(2));
        }

        public ushort ReadUInt16()
        {
            if (2 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BinaryPrimitives.ReadUInt16LittleEndian(ReadToBuffer(2));
        }

        public int ReadInt24()
        {
            if (3 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            ReadToBuffer(3);
            return buffer[0] | buffer[1] << 8 | (sbyte)buffer[2] << 16;
        }

        public uint ReadUInt24()
        {
            if (3 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            ReadToBuffer(3);
            return (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16);
        }

        public int ReadInt32()
        {
            if (4 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BinaryPrimitives.ReadInt32LittleEndian(ReadToBuffer(4));
        }

        public uint ReadUInt32()
        {
            if (4 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(ReadToBuffer(4));
        }

        public float ReadSingle()
        {
            if (4 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(ReadToBuffer(4)));
        }

        public double ReadDouble()
        {
            if (8 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(ReadToBuffer(8)));
        }

        public long ReadInt64()
        {
            if (8 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BinaryPrimitives.ReadInt64LittleEndian(ReadToBuffer(8));
        }

        public ulong ReadUInt64()
        {
            if (8 > _length - Stream.Position)
            {
                throw new IOException("Reading out of bounds");
            }

            return BinaryPrimitives.ReadUInt64LittleEndian(ReadToBuffer(8));
        }

        public string ReadGMString()
        {
            if (5 > _length - Stream.Position)
                throw new IOException("Reading out of bounds");

            int length = BinaryPrimitives.ReadInt32LittleEndian(ReadToBuffer(4));

            if (length < 0)
                throw new IOException("Invalid string length");
            if ((long)length + 1 > _length - Stream.Position)
                throw new IOException("Reading out of bounds");

            string res = ReadString(length);

            if (Stream.ReadByte() != 0)
            {
                throw new IOException("String not null terminated!");
            }

            return res;
        }

        public void SkipGMString()
        {
            if (sizeof(int) > _length - Stream.Position)
                throw new IOException("Reading out of bounds");

            int length = BinaryPrimitives.ReadInt32LittleEndian(ReadToBuffer(4));
            if (length < 0)
                throw new IOException("Invalid string length");
            Position += (uint)length + 1;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            if (Stream?.CanRead == true)
            {
                Stream.Close();
                Stream.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
