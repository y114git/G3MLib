using Microsoft.CodeAnalysis;
using System;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Project.SerializableAssets;
using static G3MLib.DataFile.Models.GameMakerSprite;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A shader entry for a data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerShader : GameMakerNamedResource, IProjectAsset, IDisposable
{
    /// <summary>
    /// The vertex shader attributes a shader can have.
    /// </summary>
    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class VertexShaderAttribute : GameMakerObject, IStaticChildObjectsSize, IDisposable
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 4;

        /// <summary>
        /// The name of the vertex shader attribute.
        /// </summary>
        public GameMakerString Name { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.WriteGameMakerString(Name);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Name = reader.ReadGameMakerString();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Name = null;
        }
    }

    public uint EntryEnd { get; set; }

    /// <summary>
    /// The name of the shader.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The type the shader uses.
    /// </summary>
    public ShaderType Type { get; set; }

    /// <summary>
    /// The GLSL ES vertex code this shader uses.
    /// </summary>
    public GameMakerString GLSL_ES_Vertex { get; set; }

    /// <summary>
    /// The GLSL ES fragment code this shader uses.
    /// </summary>
    public GameMakerString GLSL_ES_Fragment { get; set; }

    /// <summary>
    /// The GLSL vertex code this shader uses.
    /// </summary>
    public GameMakerString GLSL_Vertex { get; set; }

    /// <summary>
    /// The GLSL fragment code this shader uses.
    /// </summary>
    public GameMakerString GLSL_Fragment { get; set; }

    /// <summary>
    /// The HLSL9 vertex code this shader uses.
    /// </summary>
    public GameMakerString HLSL9_Vertex { get; set; }

    /// <summary>
    /// The HLSL9 fragment code this shader uses.
    /// </summary>
    public GameMakerString HLSL9_Fragment { get; set; }

    /// <summary>
    /// The version of this shader entry.
    /// </summary>
    public int Version { get; set; } = 2;

    /// <summary>
    /// The HLSL11 Vertex data of this shader.
    /// </summary>
    public GameMakerRawShaderData HLSL11_VertexData { get; set; }

    /// <summary>
    /// The HLSL11 Pixel data of this shader.
    /// </summary>
    public GameMakerRawShaderData HLSL11_PixelData { get; set; }

    /// <summary>
    /// The PSSL Vertex data of this shader.
    /// </summary>
    public GameMakerRawShaderData PSSL_VertexData { get; set; }

    /// <summary>
    /// The PSSL Pixel data of this shader.
    /// </summary>
    public GameMakerRawShaderData PSSL_PixelData { get; set; }

    /// <summary>
    /// The Cg_PSVita Vertex data of this shader.
    /// </summary>
    public GameMakerRawShaderData Cg_PSVita_VertexData { get; set; }

    /// <summary>
    /// The Cg_PSVita Pixel data of this shader.
    /// </summary>
    public GameMakerRawShaderData Cg_PSVita_PixelData { get; set; }

    /// <summary>
    /// The Cg_PS3 Vertex data of this shader.
    /// </summary>
    public GameMakerRawShaderData Cg_PS3_VertexData { get; set; }

    /// <summary>
    /// The Cg_PS3 Pixel data of this shader.
    /// </summary>
    public GameMakerRawShaderData Cg_PS3_PixelData { get; set; }

    /// <summary>
    /// A list of Vertex Shader attributes this shader uses.
    /// </summary>
    public GameMakerSimpleList<VertexShaderAttribute> VertexShaderAttributes { get; set; } = new GameMakerSimpleList<VertexShaderAttribute>();

    /// <summary>
    /// Initializes a new instance of <see cref="GameMakerShader"/>.
    /// </summary>
    public GameMakerShader()
    {
        HLSL11_VertexData = new GameMakerRawShaderData();
        HLSL11_PixelData = new GameMakerRawShaderData();
        PSSL_VertexData = new GameMakerRawShaderData();
        PSSL_PixelData = new GameMakerRawShaderData();
        Cg_PSVita_VertexData = new GameMakerRawShaderData();
        Cg_PSVita_PixelData = new GameMakerRawShaderData();
        Cg_PS3_VertexData = new GameMakerRawShaderData();
        Cg_PS3_PixelData = new GameMakerRawShaderData();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content + " (" + GetType().Name + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        HLSL11_VertexData?.Dispose();
        HLSL11_PixelData?.Dispose();
        PSSL_VertexData?.Dispose();
        PSSL_PixelData?.Dispose();
        Cg_PSVita_VertexData?.Dispose();
        Cg_PSVita_PixelData?.Dispose();
        Cg_PS3_VertexData?.Dispose();
        Cg_PS3_PixelData?.Dispose();
        foreach (var attr in VertexShaderAttributes)
            attr?.Dispose();

        Name = null;
        GLSL_ES_Vertex = null;
        GLSL_ES_Fragment = null;
        GLSL_Vertex = null;
        GLSL_Fragment = null;
        HLSL9_Vertex = null;
        HLSL9_Fragment = null;
        HLSL11_VertexData = null;
        HLSL11_PixelData = null;
        PSSL_VertexData = null;
        PSSL_PixelData = null;
        Cg_PSVita_VertexData = null;
        Cg_PSVita_PixelData = null;
        Cg_PS3_VertexData = null;
        Cg_PS3_PixelData = null;
        VertexShaderAttributes = new();
    }

    private static void WritePadding(GameMakerWriter writer, int amount)
    {
        while ((writer.Position & amount) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void ReadPadding(GameMakerReader reader, int amount)
    {
        while ((reader.AbsPosition & amount) != 0)
        {
            if (reader.ReadByte() != 0)
                throw new GameMakerSerializationException("Failed to read shader padding: should be some zero bytes");
        }
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write((uint)Type | 0x80000000u);

        writer.WriteGameMakerString(GLSL_ES_Vertex);
        writer.WriteGameMakerString(GLSL_ES_Fragment);
        writer.WriteGameMakerString(GLSL_Vertex);
        writer.WriteGameMakerString(GLSL_Fragment);
        writer.WriteGameMakerString(HLSL9_Vertex);
        writer.WriteGameMakerString(HLSL9_Fragment);

        HLSL11_VertexData.Serialize(writer, false);
        HLSL11_PixelData.Serialize(writer, false);

        writer.WriteGameMakerObject(VertexShaderAttributes);

        if (writer.gameMakerData.GeneralInfo.BytecodeVersion > 13)
        {
            writer.Write(Version);

            PSSL_VertexData.Serialize(writer);
            PSSL_PixelData.Serialize(writer);
            Cg_PSVita_VertexData.Serialize(writer);
            Cg_PSVita_PixelData.Serialize(writer);

            if (Version >= 2)
            {
                Cg_PS3_VertexData.Serialize(writer);
                Cg_PS3_PixelData.Serialize(writer);
            }
        }
        else
        {
            PSSL_VertexData.IsNull = true;
            PSSL_PixelData.IsNull = true;
            Cg_PSVita_VertexData.IsNull = true;
            Cg_PSVita_PixelData.IsNull = true;
            Cg_PS3_VertexData.IsNull = true;
            Cg_PS3_PixelData.IsNull = true;
        }

        if (!HLSL11_VertexData.IsNull)
        {
            WritePadding(writer, 7);

            HLSL11_VertexData.WriteData(writer);
        }
        if (!HLSL11_PixelData.IsNull)
        {
            WritePadding(writer, 7);

            HLSL11_PixelData.WriteData(writer);
        }

        if (!PSSL_VertexData.IsNull)
        {
            WritePadding(writer, 7);

            PSSL_VertexData.WriteData(writer);
        }
        if (!PSSL_PixelData.IsNull)
        {
            WritePadding(writer, 7);

            PSSL_PixelData.WriteData(writer);
        }

        if (!Cg_PSVita_VertexData.IsNull)
        {
            WritePadding(writer, 7);

            Cg_PSVita_VertexData.WriteData(writer);
        }
        if (!Cg_PSVita_PixelData.IsNull)
        {
            WritePadding(writer, 7);

            Cg_PSVita_PixelData.WriteData(writer);
        }

        if (Version >= 2)
        {
            if (!Cg_PS3_VertexData.IsNull)
            {
                WritePadding(writer, 15);

                Cg_PS3_VertexData.WriteData(writer);
            }
            if (!Cg_PS3_PixelData.IsNull)
            {
                WritePadding(writer, 15);

                Cg_PS3_PixelData.WriteData(writer);
            }
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Type = (ShaderType)(reader.ReadUInt32() & 0x7FFFFFFFu);

        GLSL_ES_Vertex = reader.ReadGameMakerString();
        GLSL_ES_Fragment = reader.ReadGameMakerString();
        GLSL_Vertex = reader.ReadGameMakerString();
        GLSL_Fragment = reader.ReadGameMakerString();
        HLSL9_Vertex = reader.ReadGameMakerString();
        HLSL9_Fragment = reader.ReadGameMakerString();

        HLSL11_VertexData.Unserialize(reader, false);
        HLSL11_PixelData.Unserialize(reader, false);

        VertexShaderAttributes = reader.ReadGameMakerObject<GameMakerSimpleList<VertexShaderAttribute>>();

        if (reader.gameMakerData.GeneralInfo.BytecodeVersion > 13)
        {
            Version = reader.ReadInt32();

            PSSL_VertexData.Unserialize(reader);
            PSSL_PixelData.Unserialize(reader);
            Cg_PSVita_VertexData.Unserialize(reader);
            Cg_PSVita_PixelData.Unserialize(reader);
            if (Version >= 2)
            {
                Cg_PS3_VertexData.Unserialize(reader);
                Cg_PS3_PixelData.Unserialize(reader);
            }
        }
        else
        {
            Version = 2;
            PSSL_VertexData = new();
            PSSL_PixelData = new();
            Cg_PSVita_VertexData = new();
            Cg_PSVita_PixelData = new();
            Cg_PS3_VertexData = new();
            Cg_PS3_PixelData = new();
        }

        if (!HLSL11_VertexData.IsNull)
        {
            ReadPadding(reader, 7);

            // Calculate length of data
            uint next;
            if (!HLSL11_PixelData.IsNull)
                next = HLSL11_PixelData._Position;
            else
                next = EntryEnd;
            int length = (int)(next - reader.AbsPosition);
            HLSL11_VertexData.ReadData(reader, length, HLSL11_PixelData.IsNull);
        }
        if (!HLSL11_PixelData.IsNull)
        {
            ReadPadding(reader, 7);

            // Calculate length of data
            uint next;
            if (!PSSL_VertexData.IsNull)
                next = PSSL_VertexData._Position;
            else
                next = EntryEnd;
            int length = (int)(next - reader.AbsPosition);
            HLSL11_PixelData.ReadData(reader, length, PSSL_VertexData.IsNull);
        }

        if (!PSSL_VertexData.IsNull)
        {
            ReadPadding(reader, 7);

            // Calculate length of data
            uint next;
            if (!PSSL_PixelData.IsNull)
                next = PSSL_PixelData._Position;
            else
                next = EntryEnd;
            int length = (int)(next - reader.AbsPosition);
            PSSL_VertexData.ReadData(reader, length, PSSL_PixelData.IsNull);
        }
        if (!PSSL_PixelData.IsNull)
        {
            ReadPadding(reader, 7);

            // Calculate length of data
            uint next;
            if (!Cg_PSVita_VertexData.IsNull)
                next = Cg_PSVita_VertexData._Position;
            else
                next = EntryEnd;
            int length = (int)(next - reader.AbsPosition);
            PSSL_PixelData.ReadData(reader, length, Cg_PSVita_VertexData.IsNull);
        }

        if (!Cg_PSVita_VertexData.IsNull)
        {
            ReadPadding(reader, 7);

            // Calculate length of data
            uint next;
            if (!Cg_PSVita_PixelData.IsNull)
                next = Cg_PSVita_PixelData._Position;
            else
                next = EntryEnd;
            int length = (int)(next - reader.AbsPosition);
            Cg_PSVita_VertexData.ReadData(reader, length, Cg_PSVita_PixelData.IsNull);
        }
        if (!Cg_PSVita_PixelData.IsNull)
        {
            ReadPadding(reader, 7);

            // Calculate length of data
            uint next;
            if (!Cg_PS3_VertexData.IsNull)
                next = Cg_PS3_VertexData._Position;
            else
                next = EntryEnd;
            int length = (int)(next - reader.AbsPosition);
            Cg_PSVita_PixelData.ReadData(reader, length, Cg_PS3_VertexData.IsNull);
        }

        if (Version >= 2)
        {
            if (!Cg_PS3_VertexData.IsNull)
            {
                ReadPadding(reader, 15);

                // Calculate length of data
                uint next;
                if (!Cg_PS3_PixelData.IsNull)
                    next = Cg_PS3_PixelData._Position;
                else
                    next = EntryEnd;
                int length = (int)(next - reader.AbsPosition);
                Cg_PS3_VertexData.ReadData(reader, length, Cg_PS3_PixelData.IsNull);
            }
            if (!Cg_PS3_PixelData.IsNull)
            {
                ReadPadding(reader, 15);

                // Calculate length of data
                uint next = EntryEnd; // final possible data, nothing else to check for
                int length = (int)(next - reader.AbsPosition);
                Cg_PS3_PixelData.ReadData(reader, length, true);
            }
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 40;

        // Since shaders are stored in a pointer list, and there are no
        // more child objects that are in the pool, then there is no
        // need to unserializing remaining elements
        return 1 + GameMakerSimpleList<VertexShaderAttribute>.UnserializeChildObjectCount(reader);
    }

    /// <summary>
    /// Possible shader types a shader can have.
    /// </summary>
    /// <remarks>All console shaders (and HLSL11?) are compiled using confidential SDK tools when
    /// GMAssetCompiler builds the game (for PSVita it's psp2cgc shader compiler).</remarks>
    public enum ShaderType : uint
    {
        /// <summary>
        /// Shader uses GLSL_ES
        /// </summary>
        GLSL_ES = 1,
        /// <summary>
        /// Shader uses GLSL
        /// </summary>
        GLSL = 2,
        /// <summary>
        /// Shader uses HLSL9
        /// </summary>
        HLSL9 = 3,
        /// <summary>
        /// Shader uses HLSL11
        /// </summary>
        HLSL11 = 4,
        /// <summary>
        /// Shader uses PSSL
        /// </summary>
        /// <remarks>PSSL is a shading language used only in PS4, based on HLSL11.</remarks>
        PSSL = 5,
        /// <summary>
        /// Shader uses for the PSVita
        /// </summary>
        /// <remarks>Cg stands for "C for graphics" made by NVIDIA and used in PSVita and PS3 (they have their own variants of Cg), based on HLSL9.</remarks>
        Cg_PSVita = 6,
        /// <summary>
        /// Shader uses Cg for the PS3
        /// </summary>
        /// <remarks>Cg stands for "C for graphics" made by NVIDIA and used in PSVita and PS3 (they have their own variants of Cg), based on HLSL9.</remarks>
        Cg_PS3 = 7
    }

    public class GameMakerRawShaderData : IDisposable
    {
        internal uint _Position;
        internal uint _PointerLocation;
        internal uint _Length; // note: this is not always an accurate value, use Data.Length if necessary
        public byte[] Data { get; set; }
        public bool IsNull { get; set; } = true;

        public void Serialize(GameMakerWriter writer, bool writeLength = true)
        {
            _PointerLocation = writer.Position;

            // This value is *very important*, as this is the default and is commonly used
            writer.Write(0x00000000);

            if (writeLength)
                writer.Write((Data == null) ? 0 : Data.Length);
        }

        public void Unserialize(GameMakerReader reader, bool readLength = true)
        {
            _PointerLocation = (uint)reader.AbsPosition;
            _Position = reader.ReadUInt32();
            if (readLength)
                _Length = reader.ReadUInt32();
            else
                _Length = 0;

            IsNull = (_Position == 0x00000000u);
            if (IsNull)
            {
                _Length = 0;
                Data = null;
            }
        }

        public void WriteData(GameMakerWriter writer)
        {
            // Technically should not happen
            if (IsNull)
                throw new GameMakerSerializationException("Cannot write null shader data");

            // Update the pointer (manually)
            _Position = writer.Position;
            writer.Position = _PointerLocation;
            writer.Write(_Position);
            writer.Position = _Position;

            writer.Write(Data);
        }

        public void ReadData(GameMakerReader reader, int length, bool isLast = false)
        {
            if (_Length != 0) // _Length is the "expected" length here
            {
                if (_Length > length)
                    throw new GameMakerSerializationException("Failed to compute length of shader data: instructed to read less data than expected.");
                else if (_Length < length)
                {
                    if (isLast && ((reader.AbsPosition + length) % 16) == 0) // Normal for the last element due to chunk padding, just trust the system
                    {
                        length = (int)_Length;
                    }
                    else if (!isLast && ((reader.AbsPosition + length) % 8) == 0) // Normal for 8-byte alignment to occur on all elements prior to the last one
                    {
                        length = (int)_Length;
                    }
                    else
                    {
                        throw new GameMakerSerializationException("Failed to compute length of shader data: instructed to read more data than expected." +
                            (isLast ? " Shader data was the last in the shader, but given length was incorrectly padded." : ""));
                    }
                }
            }

            _Length = (uint)length;
            Data = reader.ReadBytes(length);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Data = null;
        }
    }

    /// <inheritdoc/>
    ISerializableProjectAsset IProjectAsset.GenerateSerializableProjectAsset(ProjectContext projectContext)
    {
        SerializableShader serializable = new();
        serializable.PopulateFromData(projectContext, this);
        return serializable;
    }

    /// <inheritdoc/>
    public string ProjectName => Name?.Content ?? "<unknown name>";

    /// <inheritdoc/>
    public SerializableAssetType ProjectAssetType => SerializableAssetType.Shader;

    /// <inheritdoc/>
    public bool ProjectExportable
    {
        get
        {
            if (Name?.Content is null)
            {
                return false;
            }
            if (VertexShaderAttributes is null)
            {
                return false;
            }
            return true;
        }
    }
}