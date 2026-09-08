using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using G3MLib.DataFile.Util;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Project.SerializableAssets;

namespace G3MLib.DataFile.Models;

public enum AnimSpeedType : uint
{
    FramesPerSecond = 0,
    FramesPerGameFrame = 1
}

/// <summary>
/// Sprite entry in the data file.
/// </summary>
public class GameMakerSprite : GameMakerNamedResource, IProjectAsset, PrePaddedObject, INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// The name of the sprite.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The width of the sprite.
    /// </summary>
    public uint Width { get; set; }

    /// <summary>
    /// The height of the sprite.
    /// </summary>
    public uint Height { get; set; }

    /// <summary>
    /// The left margin of the sprite.
    /// </summary>
    public int MarginLeft { get; set; }

    /// <summary>
    /// The right margin of the sprite.
    /// </summary>
    public int MarginRight { get; set; }

    /// <summary>
    /// The bottom margin of the sprite.
    /// </summary>
    public int MarginBottom { get; set; }

    /// <summary>
    /// The top margin of the sprite.
    /// </summary>
    public int MarginTop { get; set; }

    /// <summary>
    /// Whether the sprite should be transparent.
    /// </summary>
    public bool Transparent { get; set; }

    /// <summary>
    /// Whether the sprite should get smoothed.
    /// </summary>
    public bool Smooth { get; set; }

    /// <summary>
    /// Whether the sprite should get preloaded.
    /// </summary>
    public bool Preload { get; set; }

    /// <summary>
    /// The bounding box mode of the sprite: automatic (0), full image (1), or manual (2).
    /// </summary>
    public uint BBoxMode { get; set; }

    /// <summary>
    /// The separation mask type this sprite has.
    /// </summary>
    public SepMaskType SepMasks { get; set; }


    /// <summary>
    /// The x-coordinate of the origin of the sprite.
    /// </summary>
    public int OriginX { get; set; }

    /// <summary>
    /// The y-coordinate of the origin of the sprite.
    /// </summary>
    public int OriginY { get; set; }

    /// <summary>
    /// A <see cref="OriginX"/> wrapper that also sets <see cref="V2Sequence"/> accordingly.
    /// </summary>
    /// <remarks>
    /// This attribute is used only in GameMakerModTool and doesn't exist in GameMaker.
    /// </remarks>
    public int OriginXWrapper
    {
        get => OriginX;
        set
        {
            OriginX = value;

            if (IsSpecialType && SVersion > 1 && V2Sequence is not null)
                V2Sequence.OriginX = value;
        }
    }

    /// <summary>
    /// A <see cref="OriginY"/> wrapper that also sets <see cref="V2Sequence"/> accordingly.
    /// </summary>
    /// <remarks>
    /// This attribute is used only in GameMakerModTool and doesn't exist in GameMaker.
    /// </remarks>
    public int OriginYWrapper
    {
        get => OriginY;
        set
        {
            OriginY = value;

            if (IsSpecialType && SVersion > 1 && V2Sequence is not null)
                V2Sequence.OriginY = value;
        }
    }

    /// <summary>
    /// The frames of the sprite.
    /// </summary>
    public GameMakerSimpleList<TextureEntry> Textures { get; set; } = new GameMakerSimpleList<TextureEntry>();

    /// <summary>
    /// The collision masks of the sprite.
    /// </summary>
    public ObservableCollection<MaskEntry> CollisionMasks { get; set; } = new ObservableCollection<MaskEntry>();

    // Special sprite types (always used in GMS2)
    public uint SVersion { get; set; } = 1;
    public SpriteType SSpriteType { get; set; }
    public float GMS2PlaybackSpeed { get; set; } = 15.0f;
    public AnimSpeedType GMS2PlaybackSpeedType { get; set; } = 0;
    public bool IsSpecialType { get; set; } = false;

    public int SpineVersion { get; set; }
    public int SpineCacheVersion { get; set; }
    public string SpineJSON { get; set; }
    public string SpineAtlas { get; set; }
    public GameMakerSimpleList<GameMakerSpineTextureEntry> SpineTextures { get; set; }

    public bool IsSpineSprite { get => SpineJSON != null && SpineAtlas != null && SpineTextures != null; }
    public bool SpineHasTextureData { get; set; } = true;
    public bool IsYYSWFSprite { get => YYSWF != null; }

    private int _SWFVersion;
    private GameMakerYYSWF _YYSWF;

    public int SWFVersion { get => _SWFVersion; set { _SWFVersion = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SWFVersion))); } }
    public GameMakerYYSWF YYSWF { get => _YYSWF; set { _YYSWF = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(YYSWF))); } }

    public int VectorVersion { get; set; }
    public GameMakerObservableList<GameMakerVectorShapeData> VectorShapes { get; set; }
    public int VectorCollisionMaskWidth { get; set; }
    public int VectorCollisionMaskHeight { get; set; }
    public GameMakerObservableList<byte[]> VectorCollisionMaskRLEData { get; set; }
    public GameMakerObservableList<int> VectorFrameToShapeMap { get; set; }

    public GameMakerSequence V2Sequence { get; set; }

    public NineSlice V3NineSlice { get; set; }

    /// <inheritdoc />
    public event PropertyChangedEventHandler PropertyChanged;

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content + " (" + GetType().Name + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (TextureEntry entry in Textures)
            entry?.Dispose();
        foreach (MaskEntry entry in CollisionMasks)
            entry?.Dispose();
        if (SpineTextures is not null)
        {
            foreach (var spineEntry in SpineTextures)
                spineEntry?.Dispose();
        }

        Textures = new();
        CollisionMasks.Clear();
        SpineTextures = null;
        _YYSWF = null;
        V2Sequence = null;
        V3NineSlice = null;
        VectorShapes = null;
        VectorCollisionMaskRLEData = null;
        VectorFrameToShapeMap = null;
    }

    /// <inheritdoc/>
    ISerializableProjectAsset IProjectAsset.GenerateSerializableProjectAsset(ProjectContext projectContext)
    {
        SerializableSprite serializable = new();
        serializable.PopulateFromData(projectContext, this);
        return serializable;
    }

    /// <inheritdoc/>
    public string ProjectName => Name?.Content ?? "<unknown name>";

    /// <inheritdoc/>
    public SerializableAssetType ProjectAssetType => SerializableAssetType.Sprite;

    /// <inheritdoc/>
    public bool ProjectExportable
    {
        get
        {
            if (Name?.Content is null)
            {
                return false;
            }
            if (IsSpecialType && SSpriteType is not SpriteType.Normal)
            {
                return false;
            }
            if ((Textures?.Count ?? 0) == 0)
            {
                return false;
            }
            if (Textures.Any(t => t?.Texture is null))
            {
                return false;
            }
            if (CollisionMasks.Any(c => c?.Data is null))
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Creates a new mask entry for this sprite.
    /// </summary>
    /// <param name="data">Data that this sprite is part of, for checking the GameMaker version.</param>
    /// <returns>The new mask entry.</returns>
    public MaskEntry NewMaskEntry(GameMakerData data = null)
    {
        int width, height;
        if (data is not null)
        {
            // Support for 2024.6+ (modern code path)
            (width, height) = CalculateMaskDimensions(data);
        }
        else
        {
            // Legacy code path (for scripts that haven't been updated to support 2024.6+ yet)
            (width, height) = ((int)Width, (int)Height);
        }

        if (width < 0 || height < 0)
            throw new InvalidOperationException("Negative collision mask dimension.");

        int len = checked((int)((((long)width + 7) / 8) * height));
        return new MaskEntry(new byte[len], width, height);
    }

    /// <summary>
    /// Different formats a sprite can have.
    /// </summary>
    public enum SpriteType : uint
    {
        /// <summary>
        /// Normal format.
        /// </summary>
        Normal = 0,
        /// <summary>
        /// SWF format.
        /// </summary>
        SWF = 1,
        /// <summary>
        /// Spine format.
        /// </summary>
        Spine = 2,
        /// <summary>
        /// Vector format (e.g., SVGs).
        /// </summary>
        Vector = 3
    }

    /// <summary>
    /// Different Separation mask types a sprite can have.
    /// </summary>
    public enum SepMaskType : uint
    {
        AxisAlignedRect = 0,
        Precise = 1,
        RotatedRect = 2
    }

    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class TextureEntry : GameMakerObject, IStaticChildObjectsSize, IDisposable
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 4;

        public GameMakerTexturePageItem? Texture { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.WriteGameMakerObjectPointer(Texture);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Texture = reader.ReadGameMakerObjectPointer<GameMakerTexturePageItem>();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Texture = null;
        }
    }

    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class MaskEntry : IDisposable
    {
        public byte[] Data { get; set; }

        /// <summary>
        /// Width of this sprite mask. UTMT only.
        /// </summary>
        public int Width { get; set; }
        /// <summary>
        /// Height of this sprite mask. UTMT only.
        /// </summary>
        public int Height { get; set; }

        public MaskEntry()
        {
        }

        public MaskEntry(byte[] data, int width, int height)
        {
            this.Data = data;
            this.Width = width;
            this.Height = height;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Data = null;
        }
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write(MarginLeft);
        writer.Write(MarginRight);
        writer.Write(MarginBottom);
        writer.Write(MarginTop);
        writer.Write(Transparent);
        writer.Write(Smooth);
        writer.Write(Preload);
        writer.Write(BBoxMode);
        writer.Write((uint)SepMasks);
        writer.Write(OriginX);
        writer.Write(OriginY);
        if (IsSpecialType)
        {
            uint sequencePatchPos = 0;
            uint nineSlicePatchPos = 0;

            writer.Write(-1);
            writer.Write(SVersion);
            writer.Write((uint)SSpriteType);
            if (writer.gameMakerData.GeneralInfo?.Major >= 2)
            {
                writer.Write(GMS2PlaybackSpeed);
                writer.Write((uint)GMS2PlaybackSpeedType);
                if (SVersion >= 2)
                {
                    sequencePatchPos = writer.Position;
                    writer.Write(0);
                    if (SVersion >= 3)
                    {
                        nineSlicePatchPos = writer.Position;
                        writer.Write(0);
                    }
                }
            }

            switch (SSpriteType)
            {
                case SpriteType.Normal:
                    writer.WriteGameMakerObject(Textures);
                    WriteMaskData(writer);
                    break;
                case SpriteType.SWF:
                    writer.Write(SWFVersion);
                    if (SWFVersion == 8) writer.WriteGameMakerObject(Textures);
                    writer.WriteGameMakerObject(YYSWF);
                    break;
                case SpriteType.Spine:
                    writer.Align(4);

                    byte[] encodedJson = EncodeSpineBlob(Encoding.UTF8.GetBytes(SpineJSON));
                    byte[] encodedAtlas = EncodeSpineBlob(Encoding.UTF8.GetBytes(SpineAtlas));

                    if (writer.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
                        writer.WriteGameMakerObject(Textures);

                    // the header.
                    writer.Write(SpineVersion);
                    if (SpineVersion >= 3) writer.Write(SpineCacheVersion);
                    writer.Write(encodedJson.Length);
                    writer.Write(encodedAtlas.Length);

                    switch (SpineVersion)
                    {
                        case 1:
                        {
                            GameMakerSpineTextureEntry atlas = SpineTextures[0]; // will throw an exception if the list is null, what I want!
                            writer.Write(atlas.TexBlob.Length);
                            writer.Write(atlas.PageWidth);
                            writer.Write(atlas.PageHeight);

                            // the data.
                            writer.Write(encodedJson);
                            writer.Write(encodedAtlas);

                            // the one and only atlas.
                            writer.Write(atlas.TexBlob);

                            break;
                        }
                        case 2:
                        case 3:
                        {
                            writer.Write(SpineTextures.Count);

                            // the data.
                            writer.Write(encodedJson);
                            writer.Write(encodedAtlas);

                            // the length is stored in the header, so we can't use the list's method.
                            foreach (var tex in SpineTextures)
                            {
                                writer.WriteGameMakerObject(tex);
                            }

                            break;
                        }
                    }

                    break;
                case SpriteType.Vector:
                    writer.Write(VectorVersion);

                    writer.WriteGameMakerObject(Textures);

                    if (VectorVersion >= 4)
                    {
                        writer.Write(VectorFrameToShapeMap.Count);
                        foreach (var n in VectorFrameToShapeMap)
                        {
                            writer.Write(n);
                        }
                        writer.Write(VectorShapes.Count);
                        foreach (var s in VectorShapes)
                        {
                            writer.WriteGameMakerObject(s);
                        }
                    }
                    else
                    {
                        DebugUtil.Assert(VectorShapes.Count == 1, "There must be only 1 vector collision mask before vector version 4");
                        writer.WriteGameMakerObject(VectorShapes[0]);
                    }

                    writer.Write(VectorCollisionMaskRLEData.Count);
                    writer.Write(VectorCollisionMaskWidth);
                    writer.Write(VectorCollisionMaskHeight);
                    if (VectorCollisionMaskRLEData.Count > 0)
                    {
                        if (VectorVersion <= 3)
                        {
                            DebugUtil.Assert(VectorCollisionMaskRLEData.Count == 1, "There must be only 1 vector collision mask before vector version 4");
                        }
                        foreach (var data in VectorCollisionMaskRLEData)
                        {
                            writer.Write(data.Length);
                            writer.Write(data);
                            writer.Align(4);
                        }
                    }

                    break;
            }

            // Sequence + nine slice
            if (sequencePatchPos != 0 && V2Sequence != null) // Normal compiler also checks for sprite type to be normal, but whatever!
            {
                uint returnTo = writer.Position;
                writer.Position = sequencePatchPos;
                writer.Write(returnTo);
                writer.Position = returnTo;
                writer.Write(1);
                writer.WriteGameMakerObject(V2Sequence);
            }
            if (nineSlicePatchPos != 0 && V3NineSlice != null)
            {
                uint returnTo = writer.Position;
                writer.Position = nineSlicePatchPos;
                writer.Write(returnTo);
                writer.Position = returnTo;
                writer.WriteGameMakerObject(V3NineSlice);
            }
        }
        else
        {
            writer.WriteGameMakerObject(Textures);
            WriteMaskData(writer);
        }
    }

    private void WriteMaskData(GameMakerWriter writer)
    {
        writer.Write((uint)CollisionMasks.Count);
        long total = 0;
        foreach (var mask in CollisionMasks)
        {
            writer.Write(mask.Data);
            total += mask.Data.Length;
        }

        while (total % 4 != 0)
        {
            writer.Write((byte)0);
            total++;
        }

        (int width, int height) = CalculateMaskDimensions(writer.gameMakerData);
        Util.DebugUtil.Assert(total == CalculateMaskDataSize(width, height, (uint)CollisionMasks.Count), "Invalid mask data for sprite");
    }

    private static byte[] DecodeSpineBlob(byte[] blob)
    {
        // don't ask.
        uint k = 42;
        for (int i = 0; i < blob.Length; i++)
        {
            blob[i] -= (byte)k;
            k *= k + 1;
        }
        return blob;
    }

    private static byte[] EncodeSpineBlob(byte[] blob)
    {
        // don't ask.
        uint k = 42;
        for (int i = 0; i < blob.Length; i++)
        {
            blob[i] += (byte)k;
            k *= k + 1;
        }
        return blob;
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Width = reader.ReadUInt32();
        Height = reader.ReadUInt32();
        Textures = new();
        CollisionMasks = new();
        MarginLeft = reader.ReadInt32();
        MarginRight = reader.ReadInt32();
        MarginBottom = reader.ReadInt32();
        MarginTop = reader.ReadInt32();
        Transparent = reader.ReadBoolean();
        Smooth = reader.ReadBoolean();
        Preload = reader.ReadBoolean();
        BBoxMode = reader.ReadUInt32();
        SepMasks = (SepMaskType)reader.ReadUInt32();
        OriginX = reader.ReadInt32();
        OriginY = reader.ReadInt32();
        SVersion = 1;
        SSpriteType = SpriteType.Normal;
        GMS2PlaybackSpeed = 15.0f;
        GMS2PlaybackSpeedType = 0;
        IsSpecialType = false;
        SpineVersion = 0;
        SpineCacheVersion = 0;
        SpineJSON = null;
        SpineAtlas = null;
        SpineTextures = null;
        SpineHasTextureData = true;
        SWFVersion = 0;
        YYSWF = null;
        VectorVersion = 0;
        VectorShapes = null;
        VectorCollisionMaskWidth = 0;
        VectorCollisionMaskHeight = 0;
        VectorCollisionMaskRLEData = null;
        VectorFrameToShapeMap = null;
        V2Sequence = null;
        V3NineSlice = null;
        if (reader.ReadInt32() == -1) // technically this seems to be able to occur on older versions, for special sprite types
        {
            int sequenceOffset = 0;
            int nineSliceOffset = 0;

            IsSpecialType = true;
            SVersion = reader.ReadUInt32();
            SSpriteType = (SpriteType)reader.ReadUInt32();
            if (reader.gameMakerData.IsGameMaker2())
            {
                GMS2PlaybackSpeed = reader.ReadSingle();
                GMS2PlaybackSpeedType = (AnimSpeedType)reader.ReadUInt32();
                if (SVersion >= 2)
                {
                    sequenceOffset = reader.ReadInt32();
                    if (SVersion >= 3)
                    {
                        if (!reader.gameMakerData.IsVersionAtLeast(2, 3, 2))
                            reader.gameMakerData.SetGMS2Version(2, 3, 2);
                        nineSliceOffset = reader.ReadInt32();
                    }
                }
            }

            switch (SSpriteType)
            {
                case SpriteType.Normal:
                    Textures = reader.ReadGameMakerObject<GameMakerSimpleList<TextureEntry>>();
                    ReadMaskData(reader);
                    break;
                case SpriteType.SWF:
                {
                    SWFVersion = reader.ReadInt32();
                    Util.DebugUtil.Assert(SWFVersion == 8 || SWFVersion == 7, "Invalid SWF sprite format, expected 7 or 8, got " + SWFVersion);

                    if (SWFVersion == 8)
                    {
                        Textures = reader.ReadGameMakerObject<GameMakerSimpleList<TextureEntry>>();
                    }

                    YYSWF = reader.ReadGameMakerObjectNoPool<GameMakerYYSWF>();
                }
                    break;
                case SpriteType.Spine:
                {
                    reader.Align(4);

                    if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
                    {
                        Textures = reader.ReadGameMakerObject<GameMakerSimpleList<TextureEntry>>();
                        SpineHasTextureData = false;
                    }

                    SpineVersion = reader.ReadInt32();
                    if (SpineVersion >= 3)
                    {
                        SpineCacheVersion = reader.ReadInt32();
                        Util.DebugUtil.Assert(SpineCacheVersion == 1, "Invalid Spine cache format version number, expected 1, got " + SpineCacheVersion);
                    }
                    Util.DebugUtil.Assert(SpineVersion <= 3 && SpineVersion >= 1,
                                          "Invalid Spine format version number, expected 3, 2 or 1, got " + SpineVersion);
                    int jsonLength = reader.ReadInt32();
                    int atlasLength = reader.ReadInt32();
                    int textures = reader.ReadInt32(); // count in v2(and newer) and size in bytes in v1.
                    if (jsonLength < 0 || atlasLength < 0 || textures < 0)
                        throw new IOException("Negative Spine sprite data length.");
                    long minimumSize = (long)jsonLength + atlasLength + (SpineVersion == 1 ? 8L + textures : (long)textures * 12);
                    if (minimumSize > reader.Length - reader.Position)
                        throw new IOException("Spine sprite data exceeds the available input.");
                    SpineTextures = new GameMakerSimpleList<GameMakerSpineTextureEntry>();

                    switch (SpineVersion)
                    {
                        // Version 1 - only one single PNG atlas.
                        // Version 2 - can be multiple atlases.
                        // Version 3 - an atlas can be a QOI blob.
                        case 1:
                        {
                            GameMakerSpineTextureEntry atlas = new GameMakerSpineTextureEntry();
                            int atlasWidth = reader.ReadInt32();
                            int atlasHeight = reader.ReadInt32();
                            SpineJSON = Encoding.UTF8.GetString(DecodeSpineBlob(reader.ReadBytes(jsonLength)));
                            SpineAtlas = Encoding.UTF8.GetString(DecodeSpineBlob(reader.ReadBytes(atlasLength)));

                            atlas.PageWidth = atlasWidth;
                            atlas.PageHeight = atlasHeight;
                            atlas.TexBlob = reader.ReadBytes(textures);
                            SpineTextures.InternalAdd(atlas);
                            break;
                        }
                        case 2:
                        case 3:
                        {
                            SpineJSON = Encoding.UTF8.GetString(DecodeSpineBlob(reader.ReadBytes(jsonLength)));
                            SpineAtlas = Encoding.UTF8.GetString(DecodeSpineBlob(reader.ReadBytes(atlasLength)));

                            SpineTextures.SetCapacity(textures);

                            // the length is stored before json and atlases so we can't use ReadGameMakerObjectList
                            // same goes for serialization.
                            for (int t = 0; t < textures; t++)
                            {
                                SpineTextures.InternalAdd(reader.ReadGameMakerObject<GameMakerSpineTextureEntry>());
                            }

                            break;
                        }
                    }
                }
                    break;
                case SpriteType.Vector:
                {
                    VectorVersion = reader.ReadInt32();
                    DebugUtil.Assert(VectorVersion == 3 || VectorVersion == 4, "Invalid vector format version number, expected 3 or 4, got " + VectorVersion);
                    if (VectorVersion >= 4 && !reader.gameMakerData.IsVersionAtLeast(2024, 14))
                        reader.gameMakerData.SetGMS2Version(2024, 14);

                    Textures = reader.ReadGameMakerObject<GameMakerSimpleList<TextureEntry>>();

                    if (VectorVersion >= 4)
                    {
                        int mapCount = reader.ReadInt32();
                        if (mapCount < 0 || (long)mapCount * sizeof(int) > reader.Length - reader.Position)
                            throw new IOException("Invalid vector frame-to-shape map count.");
                        VectorFrameToShapeMap = new(mapCount);
                        for (int i = 0; i < mapCount; i++)
                        {
                            VectorFrameToShapeMap.InternalAdd(reader.ReadInt32());
                        }

                        int shapeCount = reader.ReadInt32();
                        if (shapeCount < 0 || (long)shapeCount * 24 > reader.Length - reader.Position)
                            throw new IOException("Invalid vector shape count.");
                        VectorShapes = new(shapeCount);
                        for (int i = 0; i < shapeCount; i++)
                        {
                            VectorShapes.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVectorShapeData>());
                        }
                    }
                    else
                    {
                        VectorFrameToShapeMap = new(){ 0 };
                        VectorShapes = new() { reader.ReadGameMakerObjectNoPool<GameMakerVectorShapeData>() };
                    }

                    int collisionMaskCount;
                    if (VectorVersion >= 4)
                    {
                        collisionMaskCount = reader.ReadInt32();
                    }
                    else
                    {
                        collisionMaskCount = reader.ReadBoolean() ? 1 : 0;
                    }
                    VectorCollisionMaskWidth = reader.ReadInt32();
                    VectorCollisionMaskHeight = reader.ReadInt32();
                    if (collisionMaskCount < 0 || (long)collisionMaskCount * sizeof(int) > reader.Length - reader.Position)
                        throw new IOException("Invalid vector collision mask count.");
                    VectorCollisionMaskRLEData = new(collisionMaskCount);
                    for (int i = 0; i < collisionMaskCount; i++)
                    {
                        int dataLength = reader.ReadInt32();
                        if (dataLength < 0)
                            throw new IOException("Negative vector collision mask length.");
                        VectorCollisionMaskRLEData.InternalAdd(reader.ReadBytes(dataLength));
                        reader.Align(4);
                    }

                    break;
                }
            }

            if (sequenceOffset != 0)
            {
                if (reader.ReadInt32() != 1)
                    throw new GameMakerSerializationException("Sequence data unserialization error - expected 1");
                V2Sequence = reader.ReadGameMakerObject<GameMakerSequence>();
            }

            if (nineSliceOffset != 0)
            {
                V3NineSlice = reader.ReadGameMakerObject<NineSlice>();
            }
        }
        else
        {
            reader.Position -= 4;
            Textures = reader.ReadGameMakerObject<GameMakerSimpleList<TextureEntry>>();
            ReadMaskData(reader);
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Align(4);

        uint count = 0;

        reader.Position += 4; // "Name"
        uint width = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        int marginLeft = reader.ReadInt32();
        int marginRight = reader.ReadInt32();
        int marginBottom = reader.ReadInt32();
        int marginTop = reader.ReadInt32();

        reader.Position += 28;

        if (reader.ReadInt32() == -1)
        {
            int sequenceOffset = 0;
            int nineSliceOffset = 0;

            uint sVersion = reader.ReadUInt32();
            SpriteType sSpriteType = (SpriteType)reader.ReadUInt32();
            if (reader.gameMakerData.IsGameMaker2())
            {
                reader.Position += 8; // playback speed values

                if (sVersion >= 2)
                {
                    sequenceOffset = reader.ReadInt32();
                    if (sVersion >= 3)
                    {
                        if (!reader.gameMakerData.IsVersionAtLeast(2, 3, 2))
                            reader.gameMakerData.SetGMS2Version(2, 3, 2);
                        nineSliceOffset = reader.ReadInt32();
                    }
                }
            }

            switch (sSpriteType)
            {
                case SpriteType.Normal:
                    count += 1 + GameMakerSimpleList<TextureEntry>.UnserializeChildObjectCount(reader);
                    SkipMaskData(reader, (int)width, (int)height, marginRight, marginLeft, marginBottom, marginTop);
                    break;

                case SpriteType.SWF:
                    int swfVersion = reader.ReadInt32();
                    if (swfVersion == 8)
                        count += 1 + GameMakerSimpleList<TextureEntry>.UnserializeChildObjectCount(reader);

                    // If we have a sequence, jump directly to its offset (not worth parsing effort to count everything not included in the pool)
                    if (sequenceOffset != 0)
                    {
                        reader.AbsPosition = sequenceOffset;
                    }
                    break;

                case SpriteType.Spine:
                {
                    reader.Align(4);

                    if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
                        count += 1 + GameMakerSimpleList<TextureEntry>.UnserializeChildObjectCount(reader);

                    int spineVersion = reader.ReadInt32();
                    if (spineVersion >= 3)
                        reader.Position += 4; // "SpineCacheVersion"
                    Util.DebugUtil.Assert(spineVersion <= 3 && spineVersion >= 1,
                                          "Invalid Spine format version number, expected 3, 2 or 1, got " + spineVersion);

                    int jsonLength = reader.ReadInt32();
                    int atlasLength = reader.ReadInt32();
                    int textures = reader.ReadInt32();

                    switch (spineVersion)
                    {
                        case 1:
                            reader.Position += 8 + (uint)jsonLength + (uint)atlasLength + (uint)textures;
                            break;

                        case 2:
                        case 3:
                        {
                            reader.Position += (uint)jsonLength + (uint)atlasLength;

                            for (int i = 0; i < textures; i++)
                                GameMakerSpineTextureEntry.UnserializeChildObjectCount(reader);

                            count += (uint)textures;
                        }
                            break;
                    }
                }
                    break;

                case SpriteType.Vector:
                    reader.Position += 4; // skip version
                    count += 1 + GameMakerSimpleList<TextureEntry>.UnserializeChildObjectCount(reader);

                    // If we have a sequence, jump directly to its offset (not worth parsing effort to count everything not included in the pool)
                    if (sequenceOffset != 0)
                    {
                        reader.AbsPosition = sequenceOffset;
                    }
                    break;
            }

            if (sequenceOffset != 0)
            {
                if (reader.ReadInt32() != 1)
                    throw new GameMakerSerializationException($"Sequence data count unserialization error - expected 1");
                count += 1 + GameMakerSequence.UnserializeChildObjectCount(reader);
            }

            if (nineSliceOffset != 0)
            {
                reader.Position += NineSlice.ChildObjectsSize;
                count++;
            }
        }
        else
        {
            reader.Position -= 4;
            count += 1 + GameMakerSimpleList<TextureEntry>.UnserializeChildObjectCount(reader);
            SkipMaskData(reader, (int)width, (int)height, marginRight, marginLeft, marginBottom, marginTop);
        }

        return count;
    }

    /// <summary>
    /// Returns the width and height of the collision mask for this sprite, which changes depending on GameMaker version.
    /// </summary>
    public (int Width, int Height) CalculateMaskDimensions(GameMakerData data)
    {
        if (data.IsNonLTSVersionAtLeast(2024, 6))
        {
            return CalculateBboxMaskDimensions(MarginRight, MarginLeft, MarginBottom, MarginTop);
        }
        return CalculateFullMaskDimensions((int)Width, (int)Height);
    }

    /// <summary>
    /// Calculates the width and height of a collision mask from the given margin/bounding box.
    /// This method is used to calculate collision mask dimensions in GameMaker 2024.6 and above.
    /// </summary>
    public static (int Width, int Height) CalculateBboxMaskDimensions(int marginRight, int marginLeft, int marginBottom, int marginTop)
    {
        return (marginRight - marginLeft + 1, marginBottom - marginTop + 1);
    }

    /// <summary>
    /// Calculates the width and height of a collision mask from a given sprite's full width and height.
    /// This method is used to calculate collision mask dimensions prior to GameMaker 2024.6.
    /// </summary>
    /// <remarks>
    /// This simply returns the width and height supplied, but is intended for clarity in the code.
    /// </remarks>
    public static (int Width, int Height) CalculateFullMaskDimensions(int width, int height)
    {
        return (width, height);
    }

    private void ReadMaskData(GameMakerReader reader)
    {
        // Initialize mask list
        uint maskCount = reader.ReadUInt32();
        List<MaskEntry> newMasks = [];

        // Read in mask data
        (int width, int height) = CalculateMaskDimensions(reader.gameMakerData);
        if (width < 0 || height < 0)
            throw new IOException("Negative collision mask dimension.");
        if (maskCount > int.MaxValue)
            throw new IOException("Collision mask count is too large.");
        long maskDataSize = CalculateMaskDataSize(width, height, maskCount);
        if (maskDataSize > reader.Length - reader.Position)
            throw new IOException("Collision mask data exceeds the available input.");
        int len = checked((int)((((long)width + 7) / 8) * height));
        long total = 0;
        for (uint i = 0; i < maskCount; i++)
        {
            newMasks.Add(new MaskEntry(reader.ReadBytes(len), width, height));
            total += len;
        }

        while ((total % 4) != 0)
        {
            if (reader.ReadByte() != 0)
            {
                throw new IOException("Mask padding");
            }
            total++;
        }
        if (total != maskDataSize)
        {
            throw new IOException("Mask data size incorrect");
        }

        // Assign masks to sprite
        CollisionMasks = new(newMasks);
    }

    private static void SkipMaskData(GameMakerReader reader, int width, int height, int marginRight, int marginLeft, int marginBottom, int marginTop)
    {
        uint maskCount = reader.ReadUInt32();
        if (reader.gameMakerData.IsNonLTSVersionAtLeast(2024, 6))
        {
            (width, height) = CalculateBboxMaskDimensions(marginRight, marginLeft, marginBottom, marginTop);
        }
        else
        {
            (width, height) = CalculateFullMaskDimensions(width, height);
        }
        if (width < 0 || height < 0)
            throw new IOException("Negative collision mask dimension.");
        int len = checked((int)((((long)width + 7) / 8) * height));

        long total = 0;
        for (uint i = 0; i < maskCount; i++)
        {
            reader.Position += len; // "new MaskEntry()"
            total += len;
        }

        // Skip padding
        int skipSize = 0;
        while (total % 4 != 0)
        {
            skipSize++;
            total++;
        }
        reader.Position += skipSize;
    }

    private static long CalculateMaskDataSize(int width, int height, uint maskCount)
    {
        long roundedWidth = checked((((long)width + 7) / 8) * 8); // round to multiple of 8
        long dataBits = checked(roundedWidth * height * maskCount);
        return checked(((dataBits + 31) / 32 * 32) / 8); // round to multiple of 4 bytes
    }

    /// <inheritdoc />
    public void SerializePrePadding(GameMakerWriter writer)
    {
        writer.Align(4);
    }

    /// <inheritdoc />
    public void UnserializePrePadding(GameMakerReader reader)
    {
        // If you are modifying this, you must also modify "UnserializeChildObjectCount()"
        reader.Align(4);
    }

    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class NineSlice : GameMakerObject, IStaticChildObjectsSize
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 40;

        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public bool Enabled { get; set; }
        public TileMode[] TileModes { get; set; } = new TileMode[5];

        public enum TileMode
        {
            Stretch = 0,
            Repeat = 1,
            Mirror = 2,
            BlankRepeat = 3,
            Hide = 4
        }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(Left);
            writer.Write(Top);
            writer.Write(Right);
            writer.Write(Bottom);
            writer.Write(Enabled);
            for (int i = 0; i < 5; i++)
                writer.Write((int)TileModes[i]);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Left = reader.ReadInt32();
            Top = reader.ReadInt32();
            Right = reader.ReadInt32();
            Bottom = reader.ReadInt32();
            Enabled = reader.ReadBoolean();
            for (int i = 0; i < 5; i++)
                TileModes[i] = (TileMode)reader.ReadInt32();
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerSpineTextureEntry : GameMakerObject, IDisposable
{
    /// <summary>
    /// The width of the Spine atlas in pixels.
    /// </summary>
    public int PageWidth { get; set; }

    /// <summary>
    /// The height of the Spine atlas in pixels.
    /// </summary>
    public int PageHeight { get; set; }

    /// <summary>
    /// The atlas as raw bytes, can be a GameMaker QOI texture or a PNG file. Null for versions >= 2023.1.
    /// </summary>
    public byte[] TexBlob { get; set; }

    /// <summary>
    /// The length of the corresponding sprite texture entry for versions >= 2023.1.
    /// </summary>
    public int TextureEntryLength { get; set; }

    /// <summary>
    /// Indicates whether <see cref="TexBlob"/> contains a GameMaker QOI texture (the header is qoif reversed).
    /// </summary>
    public bool IsQOI => TexBlob != null && TexBlob.Length > 7 && TexBlob[0] == 102/*f*/ && TexBlob[1] == 105/*i*/ && TexBlob[2] == 111/*o*/ && TexBlob[3] == 113/*q*/;

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(PageWidth);
        writer.Write(PageHeight);
        if (writer.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
        {
            writer.Write(TextureEntryLength);
        }
        else
        {
            writer.Write(TexBlob.Length);
            writer.Write(TexBlob);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        PageWidth = reader.ReadInt32();
        PageHeight = reader.ReadInt32();
        if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
        {
            TextureEntryLength = reader.ReadInt32();
            TexBlob = null;
        }
        else
        {
            int textureBlobLength = reader.ReadInt32();
            if (textureBlobLength < 0)
                throw new IOException("Negative Spine texture blob length.");
            TexBlob = reader.ReadBytes(textureBlobLength);
            TextureEntryLength = 0;
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 8;                        // Size
        if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
        {
            reader.Position += 4; // "TextureEntryLength"
        }
        else
        {
            uint texBlobLength = reader.ReadUInt32(); // "TexBlob"
            reader.Position += texBlobLength;
        }
        return 0;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"GameMakerSpineTextureEntry ({PageWidth};{PageHeight})";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        TexBlob = null;
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFMatrixColor : GameMakerObject
{
    private const int MATRIX_SIZE = 4;

    public int[] Additive { get; set; }
    public int[] Multiply { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        foreach (int v in Additive)
            writer.Write(v);
        foreach (int v in Multiply)
            writer.Write(v);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Additive = new int[MATRIX_SIZE];
        Multiply = new int[MATRIX_SIZE];
        for (int i = 0; i < Additive.Length; i++)
            Additive[i] = reader.ReadInt32();
        for (int i = 0; i < Multiply.Length; i++)
            Multiply[i] = reader.ReadInt32();
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorMatrix33 : GameMakerObject
{
    private const int MATRIX_SIZE = 9;
    public float[] Values { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        foreach (float v in Values)
            writer.Write(v);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Values = new float[MATRIX_SIZE];
        for (int i = 0; i < Values.Length; i++)
            Values[i] = reader.ReadSingle();
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFTimelineObject : GameMakerObject
{
    public int CharID { get; set; }
    public int CharIndex { get; set; }
    public int Depth { get; set; }
    public int ClippingDepth { get; set; }
    public GameMakerVectorMatrix33 TransformationMatrix { get; set; }
    public GameMakerYYSWFMatrixColor ColorMatrix { get; set; }
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(CharID);
        writer.Write(CharIndex);
        writer.Write(Depth);
        writer.Write(ClippingDepth);
        writer.WriteGameMakerObject(ColorMatrix);
        writer.Write(MinX);
        writer.Write(MaxX);
        writer.Write(MinY);
        writer.Write(MaxY);
        writer.WriteGameMakerObject(TransformationMatrix);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        CharID = reader.ReadInt32();
        CharIndex = reader.ReadInt32();
        Depth = reader.ReadInt32();
        ClippingDepth = reader.ReadInt32();
        ColorMatrix = reader.ReadGameMakerObjectNoPool<GameMakerYYSWFMatrixColor>();
        MinX = reader.ReadSingle();
        MaxX = reader.ReadSingle();
        MinY = reader.ReadSingle();
        MaxY = reader.ReadSingle();
        TransformationMatrix = reader.ReadGameMakerObjectNoPool<GameMakerVectorMatrix33>();
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFTimelineFrame : GameMakerObject
{
    public GameMakerObservableList<GameMakerYYSWFTimelineObject> FrameObjects { get; set; }
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(FrameObjects.Count);
        writer.Write(MinX);
        writer.Write(MaxX);
        writer.Write(MinY);
        writer.Write(MaxY);
        foreach (var frameObject in FrameObjects)
        {
            writer.WriteGameMakerObject(frameObject);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        int ii = reader.ReadInt32();
        if (ii < 0 || (long)ii * 76 > reader.Length - reader.Position)
            throw new IOException("Invalid SWF timeline frame object count.");
        MinX = reader.ReadSingle();
        MaxX = reader.ReadSingle();
        MinY = reader.ReadSingle();
        MaxY = reader.ReadSingle();
        FrameObjects = new GameMakerObservableList<GameMakerYYSWFTimelineObject>(ii);
        for (int i = 0; i < ii; i++)
        {
            FrameObjects.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerYYSWFTimelineObject>());
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFCollisionMask : GameMakerObject
{
    public byte[] RLEData { get; set; } // heavily compressed and pre-processed!

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        /*
         * uhhh, it'd make sense if it wrote zero if the RLE table is empty?????
         * but I guess padding bytes work as zeroes too?!?!?
         * even though it is unreliable??
         * but like, how can you even make a sprite with RLEData==null? so it makes sense??
         * argh.
         */

        if (RLEData != null)
        {
            writer.Write(RLEData.Length);
            writer.Write(RLEData);
        }

        writer.Align(4);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        int rlelen = reader.ReadInt32();
        if (rlelen < 0)
            throw new IOException("Negative SWF collision mask length.");
        RLEData = rlelen > 0 ? reader.ReadBytes(rlelen) : null;

        // why it's not aligned before the data is beyond my brain.
        reader.Align(4);
    }
}

public enum GameMakerYYSWFItemType
{
    ItemInvalid,
    ItemShape,
    ItemBitmap,
    ItemFont,
    ItemTextField,
    ItemSprite
}

public enum GameMakerVectorFillType
{
    FillInvalid,
    FillSolid,
    FillGradient,
    FillBitmap
}

public enum GameMakerVectorBitmapFillType
{
    FillRepeat,
    FillClamp,
    FillRepeatPoint,
    FillClampPoint
}

public enum GameMakerVectorGradientFillType
{
    FillLinear,
    FillRadial
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorColor : GameMakerObject
{
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }
    public byte Alpha { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(Red);
        writer.Write(Green);
        writer.Write(Blue);
        writer.Write(Alpha);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Red = reader.ReadByte();
        Green = reader.ReadByte();
        Blue = reader.ReadByte();
        Alpha = reader.ReadByte();
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorSolidFillData : GameMakerVectorColor
{
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorGradientRecord : GameMakerObject
{
    public int Ratio { get; set; }
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }
    public byte Alpha { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(Ratio);
        writer.Write(Red);
        writer.Write(Green);
        writer.Write(Blue);
        writer.Write(Alpha);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Ratio = reader.ReadInt32();
        Red = reader.ReadByte();
        Green = reader.ReadByte();
        Blue = reader.ReadByte();
        Alpha = reader.ReadByte();
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorGradientFillData : GameMakerObject
{
    public GameMakerVectorGradientFillType GradientFillType { get; set; }
    public GameMakerVectorMatrix33 TransformationMatrix { get; set; }
    public GameMakerSimpleList<GameMakerVectorGradientRecord> Records { get; set; }

    /// <summary>
    /// Unknown purpose. Probably to accomodate for new texture formats.
    /// </summary>
    /// <remarks>
    /// Presumably present in GM 2022.1+.
    /// </remarks>
    public int TPEIndex { get; set; } = -1;

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((int)GradientFillType);
        if (writer.gameMakerData.IsVersionAtLeast(2022, 1))
        {
            writer.Write(TPEIndex);
        }
        writer.WriteGameMakerObject(TransformationMatrix);
        writer.WriteGameMakerObject(Records);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        GradientFillType = (GameMakerVectorGradientFillType)reader.ReadInt32();
        if (reader.gameMakerData.IsVersionAtLeast(2022, 1))
        {
            TPEIndex = reader.ReadInt32();
        }
        else
        {
            TPEIndex = -1;
        }
        TransformationMatrix = reader.ReadGameMakerObjectNoPool<GameMakerVectorMatrix33>();

        int count = reader.ReadInt32();
        if (count < 0)
            throw new IOException("Negative vector gradient record count.");
        if ((long)count * 8 > reader.Length - reader.Position)
            throw new IOException("Vector gradient record data exceeds the available input.");
        Records = new GameMakerSimpleList<GameMakerVectorGradientRecord>();
        Records.SetCapacity(count);
        for (int i = 0; i < count; i++)
        {
            Records.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVectorGradientRecord>());
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorBitmapFillData : GameMakerObject
{
    public GameMakerVectorBitmapFillType BitmapFillType { get; set; }
    public int CharID { get; set; }
    public GameMakerVectorMatrix33 TransformationMatrix { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((int)BitmapFillType);
        writer.Write(CharID);
        writer.WriteGameMakerObject(TransformationMatrix);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        BitmapFillType = (GameMakerVectorBitmapFillType)reader.ReadInt32();
        CharID = reader.ReadInt32();
        TransformationMatrix = reader.ReadGameMakerObjectNoPool<GameMakerVectorMatrix33>();
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorFillData : GameMakerObject
{
    public GameMakerVectorFillType Type { get; set; }
    public GameMakerVectorBitmapFillData BitmapFillData { get; set; }
    public GameMakerVectorGradientFillData GradientFillData { get; set; }
    public GameMakerVectorSolidFillData SolidFillData { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((int)Type);
        switch (Type)
        {
            case GameMakerVectorFillType.FillBitmap:
            {
                writer.WriteGameMakerObject(BitmapFillData);
                break;
            }

            case GameMakerVectorFillType.FillGradient:
            {
                writer.WriteGameMakerObject(GradientFillData);
                break;
            }

            case GameMakerVectorFillType.FillSolid:
            {
                writer.WriteGameMakerObject(SolidFillData);
                break;
            }

            case GameMakerVectorFillType.FillInvalid:
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Type = (GameMakerVectorFillType)reader.ReadInt32();
        switch (Type)
        {
            case GameMakerVectorFillType.FillBitmap:
            {
                BitmapFillData = reader.ReadGameMakerObjectNoPool<GameMakerVectorBitmapFillData>();
                GradientFillData = null;
                SolidFillData = null;
                break;
            }

            case GameMakerVectorFillType.FillGradient:
            {
                GradientFillData = reader.ReadGameMakerObjectNoPool<GameMakerVectorGradientFillData>();
                BitmapFillData = null;
                SolidFillData = null;
                break;
            }

            case GameMakerVectorFillType.FillSolid:
            {
                SolidFillData = reader.ReadGameMakerObjectNoPool<GameMakerVectorSolidFillData>();
                BitmapFillData = null;
                GradientFillData = null;
                break;
            }

            case GameMakerVectorFillType.FillInvalid:
            default:
            {
                BitmapFillData = null;
                GradientFillData = null;
                SolidFillData = null;
                reader.SubmitWarning("Tried to read invalid fill data.");
                break;
            }
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorLineStyleData : GameMakerVectorColor
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"GameMakerVectorLineStyleData ({Red};{Green};{Blue};{Alpha})";
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVector2 : GameMakerObject
{
    public int X { get; set; }
    public int Y { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        X = reader.ReadInt32();
        Y = reader.ReadInt32();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"GameMakerVector2 ({X};{Y})";
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVector2F : GameMakerObject
{
    public float X { get; set; }
    public float Y { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        X = reader.ReadSingle();
        Y = reader.ReadSingle();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"GameMakerVector2F ({X};{Y})";
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFSubShapeData : GameMakerObject
{
    public int FillStyleOne { get; set; }
    public int FillStyleTwo { get; set; }
    public int LineStyle { get; set; }

    public GameMakerObservableList<GameMakerVector2F> Points { get; set; }
    public GameMakerObservableList<GameMakerVector2> Lines { get; set; }
    public GameMakerObservableList<int> Triangles { get; set; }

    public GameMakerObservableList<GameMakerVector2F> LinePoints { get; set; }
    public GameMakerObservableList<int> LineTriangles { get; set; }

    public GameMakerObservableList<GameMakerVector2> AALines { get; set; }
    public GameMakerObservableList<GameMakerVector2F> AAVectors { get; set; }

    public GameMakerObservableList<GameMakerVector2> LineAALines { get; set; }
    public GameMakerObservableList<GameMakerVector2F> LineAAVectors { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(FillStyleOne);
        writer.Write(FillStyleTwo);
        writer.Write(LineStyle);
        writer.Write(Points.Count);
        writer.Write(Lines.Count);
        writer.Write(Triangles.Count / 3);
        writer.Write(LinePoints.Count);
        writer.Write(LineTriangles.Count / 3);
        writer.Write(AALines.Count);
        writer.Write(AAVectors.Count);
        writer.Write(LineAALines.Count);
        writer.Write(LineAAVectors.Count);

        foreach (var vec in Points)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var vec in Lines)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var tri in Triangles)
        {
            writer.Write(tri);
        }

        foreach (var vec in LinePoints)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var tri in LineTriangles)
        {
            writer.Write(tri);
        }

        foreach (var vec in AALines)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var vec in AAVectors)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var vec in LineAALines)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var vec in LineAAVectors)
        {
            writer.WriteGameMakerObject(vec);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        FillStyleOne = reader.ReadInt32();
        FillStyleTwo = reader.ReadInt32();
        LineStyle = reader.ReadInt32();

        int points = reader.ReadInt32();
        int lines = reader.ReadInt32();
        int triangleGroups = reader.ReadInt32();
        int linepoints = reader.ReadInt32();
        int lineTriangleGroups = reader.ReadInt32();
        int aalines = reader.ReadInt32();
        int aavectors = reader.ReadInt32();
        int lineaalines = reader.ReadInt32();
        int lineaavectors = reader.ReadInt32();
        if (points < 0 || lines < 0 || triangleGroups < 0 || linepoints < 0 || lineTriangleGroups < 0 ||
            aalines < 0 || aavectors < 0 || lineaalines < 0 || lineaavectors < 0)
            throw new IOException("Negative SWF subshape count.");
        long minimumSize = (long)points * 8 + (long)lines * 8 + (long)triangleGroups * 12 +
                           (long)linepoints * 8 + (long)lineTriangleGroups * 12 + (long)aalines * 8 +
                           (long)aavectors * 8 + (long)lineaalines * 8 + (long)lineaavectors * 8;
        if (minimumSize > reader.Length - reader.Position)
            throw new IOException("SWF subshape data exceeds the available input.");
        int triangles = checked(triangleGroups * 3);
        int linetriangles = checked(lineTriangleGroups * 3);

        Points = new GameMakerObservableList<GameMakerVector2F>(points);
        for (int i = 0; i < points; i++)
        {
            Points.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2F>());
        }

        Lines = new GameMakerObservableList<GameMakerVector2>(lines);
        for (int i = 0; i < lines; i++)
        {
            Lines.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2>());
        }

        Triangles = new GameMakerObservableList<int>(triangles);
        for (int i = 0; i < triangles; i++)
        {
            Triangles.InternalAdd(reader.ReadInt32());
        }

        LinePoints = new GameMakerObservableList<GameMakerVector2F>(linepoints);
        for (int i = 0; i < linepoints; i++)
        {
            LinePoints.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2F>());
        }

        LineTriangles = new GameMakerObservableList<int>(linetriangles);
        for (int i = 0; i < linetriangles; i++)
        {
            LineTriangles.InternalAdd(reader.ReadInt32());
        }

        AALines = new GameMakerObservableList<GameMakerVector2>(aalines);
        for (int i = 0; i < aalines; i++)
        {
            AALines.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2>());
        }

        AAVectors = new GameMakerObservableList<GameMakerVector2F>(aavectors);
        for (int i = 0; i < aavectors; i++)
        {
            AAVectors.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2F>());
        }

        LineAALines = new GameMakerObservableList<GameMakerVector2>(lineaalines);
        for (int i = 0; i < lineaalines; i++)
        {
            LineAALines.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2>());
        }

        LineAAVectors = new GameMakerObservableList<GameMakerVector2F>(lineaavectors);
        for (int i = 0; i < lineaavectors; i++)
        {
            LineAAVectors.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2F>());
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorSubShapeData : GameMakerObject
{
    public int FillStyleOne { get; set; }
    public int FillStyleTwo { get; set; }
    public int LineStyle { get; set; }

    public GameMakerObservableList<GameMakerVector2F> Points { get; set; }
    public GameMakerObservableList<uint> PointColors { get; set; }
    public GameMakerObservableList<int> Triangles { get; set; }

    public GameMakerObservableList<GameMakerVector2> AALines { get; set; }
    public GameMakerObservableList<GameMakerVector2F> AAVectors { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(FillStyleOne);
        writer.Write(FillStyleTwo);
        writer.Write(LineStyle);
        writer.Write(Points.Count);
        writer.Write(PointColors.Count);
        writer.Write(0);
        writer.Write(Triangles.Count / 3);
        writer.Write(0);
        writer.Write(0);
        writer.Write(AALines.Count);
        writer.Write(AAVectors.Count);
        writer.Write(0);
        writer.Write(0);

        foreach (var vec in Points)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (uint color in PointColors)
        {
            writer.Write(color);
        }

        foreach (int tri in Triangles)
        {
            writer.Write(tri);
        }

        foreach (var vec in AALines)
        {
            writer.WriteGameMakerObject(vec);
        }

        foreach (var vec in AAVectors)
        {
            writer.WriteGameMakerObject(vec);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        FillStyleOne = reader.ReadInt32();
        FillStyleTwo = reader.ReadInt32();
        LineStyle = reader.ReadInt32();

        int points = reader.ReadInt32();
        int pointcolors = reader.ReadInt32();
        reader.ReadInt32();
        int triangleGroups = reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        int aalines = reader.ReadInt32();
        int aavectors = reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        if (points < 0 || pointcolors < 0 || triangleGroups < 0 || aalines < 0 || aavectors < 0)
            throw new IOException("Negative vector subshape count.");
        long minimumSize = (long)points * 8 + (long)pointcolors * 4 + (long)triangleGroups * 12 +
                           (long)aalines * 8 + (long)aavectors * 8;
        if (minimumSize > reader.Length - reader.Position)
            throw new IOException("Vector subshape data exceeds the available input.");
        int triangles = checked(triangleGroups * 3);

        Points = new GameMakerObservableList<GameMakerVector2F>(points);
        for (int i = 0; i < points; i++)
        {
            Points.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2F>());
        }

        PointColors = new GameMakerObservableList<uint>(pointcolors);
        for (int i = 0; i < pointcolors; i++)
        {
            PointColors.InternalAdd(reader.ReadUInt32());
        }

        Triangles = new GameMakerObservableList<int>(triangles);
        for (int i = 0; i < triangles; i++)
        {
            Triangles.InternalAdd(reader.ReadInt32());
        }

        AALines = new GameMakerObservableList<GameMakerVector2>(aalines);
        for (int i = 0; i < aalines; i++)
        {
            AALines.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2>());
        }

        AAVectors = new GameMakerObservableList<GameMakerVector2F>(aavectors);
        for (int i = 0; i < aavectors; i++)
        {
            AAVectors.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVector2F>());
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerShapeData<T> : GameMakerObject where T : GameMakerObject, new()
{
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    public GameMakerSimpleList<GameMakerStyleGroup<T>> StyleGroups { get; set; }


    /// <inheritdoc />
    public virtual void Serialize(GameMakerWriter writer)
    {
        writer.Write(MinX);
        writer.Write(MaxX);
        writer.Write(MinY);
        writer.Write(MaxY);
        writer.WriteGameMakerObject(StyleGroups);
    }

    /// <inheritdoc />
    public virtual void Unserialize(GameMakerReader reader)
    {
        MinX = reader.ReadSingle();
        MaxX = reader.ReadSingle();
        MinY = reader.ReadSingle();
        MaxY = reader.ReadSingle();
        StyleGroups = new GameMakerSimpleList<GameMakerStyleGroup<T>>();
        int s = reader.ReadInt32();
        if (s < 0 || (long)s * 12 > reader.Length - reader.Position)
            throw new IOException("Invalid shape style group count.");
        for (int i = 0; i < s; i++)
        {
            StyleGroups.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerStyleGroup<T>>());
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVectorShapeData : GameMakerShapeData<GameMakerVectorSubShapeData>
{
    public int Version { get; set; }

    public override void Serialize(GameMakerWriter writer)
    {
        writer.Align(4);
        writer.Write(Version);
        base.Serialize(writer);
    }

    public override void Unserialize(GameMakerReader reader)
    {
        reader.Align(4);
        Version = reader.ReadInt32();
        Util.DebugUtil.Assert(Version == 3 || Version == 4, "Invalid shape format version number, expected 3 or 4, got " + Version);
        base.Unserialize(reader);
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerStyleGroup<T> : GameMakerObject where T : GameMakerObject, new()
{
    public GameMakerObservableList<GameMakerVectorFillData> FillStyles { get; set; }
    public GameMakerObservableList<GameMakerVectorLineStyleData> LineStyles { get; set; }
    public GameMakerObservableList<T> Subshapes { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(FillStyles.Count);
        writer.Write(LineStyles.Count);
        writer.Write(Subshapes.Count);

        foreach (var fill in FillStyles)
        {
            writer.WriteGameMakerObject(fill);
        }

        foreach (var line in LineStyles)
        {
            writer.WriteGameMakerObject(line);
        }

        foreach (var shape in Subshapes)
        {
            writer.WriteGameMakerObject(shape);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        int f = reader.ReadInt32();
        int l = reader.ReadInt32();
        int s = reader.ReadInt32();
        if (f < 0 || l < 0 || s < 0 || ((long)f + l + s) * 4 > reader.Length - reader.Position)
            throw new IOException("Invalid vector style group count.");

        FillStyles = new GameMakerObservableList<GameMakerVectorFillData>(f);
        LineStyles = new GameMakerObservableList<GameMakerVectorLineStyleData>(l);
        Subshapes = new GameMakerObservableList<T>(s);

        for (int i = 0; i < f; i++)
        {
            FillStyles.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVectorFillData>());
        }

        for (int i = 0; i < l; i++)
        {
            LineStyles.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerVectorLineStyleData>());
        }

        for (int i = 0; i < s; i++)
        {
            Subshapes.InternalAdd(reader.ReadGameMakerObjectNoPool<T>());
        }
    }
}

public enum GameMakerYYSWFBitmapType
{
    TypeJPEGNoHeader,
    TypeJPEG,
    TypeJPEGWithAlpha,
    TypePNG,
    TypeGIF,
    TypeLossless8bit,
    TypeLossless15bit,
    TypeLossless24bit,
    TypeLossless8bitA,
    TypeLossless32bit
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFBitmapData : GameMakerObject
{
    public GameMakerYYSWFBitmapType Type { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>
    /// Unknown purpose. Probably to accomodate for new texture formats.
    /// </summary>
    /// <remarks>
    /// Presumably present in GM 2022.1+.
    /// </remarks>
    public int? TPEIndex { get; set; }
    public byte[] ImageData { get; set; }
    public byte[] AlphaData { get; set; }
    public byte[] ColorPaletteData { get; set; }


    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((int)Type);
        writer.Write(Width);
        writer.Write(Height);

        if (TPEIndex is null)
        {
            writer.Write(ImageData is null ? 0 : ImageData.Length);
            writer.Write(AlphaData is null ? 0 : AlphaData.Length);
            writer.Write(ColorPaletteData is null ? 0 : ColorPaletteData.Length);

            if (ImageData != null)
                writer.Write(ImageData);
            if (AlphaData != null)
                writer.Write(AlphaData);
            if (ColorPaletteData != null)
                writer.Write(ColorPaletteData);

            writer.Align(4);
        }
        else
        {
            writer.Write(TPEIndex.Value);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Type = (GameMakerYYSWFBitmapType)reader.ReadInt32();
        Width = reader.ReadInt32();
        Height = reader.ReadInt32();

        if (reader.gameMakerData.IsVersionAtLeast(2022, 1))
        {
            TPEIndex = reader.ReadInt32();
            ImageData = null;
            AlphaData = null;
            ColorPaletteData = null;
        }
        else
        {
            TPEIndex = null;
            int iL = reader.ReadInt32();
            int aL = reader.ReadInt32();
            int cL = reader.ReadInt32();
            if (iL < 0 || aL < 0 || cL < 0)
                throw new IOException("Negative SWF bitmap data length.");

            ImageData = iL > 0 ? reader.ReadBytes(iL) : null;
            AlphaData = aL > 0 ? reader.ReadBytes(aL) : null;
            ColorPaletteData = cL > 0 ? reader.ReadBytes(cL) : null;

            reader.Align(4);
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFItem : GameMakerObject
{
    public int ID { get; set; }
    public GameMakerYYSWFItemType ItemType { get; set; }
    public GameMakerShapeData<GameMakerYYSWFSubShapeData> ShapeData { get; set; }
    public GameMakerYYSWFBitmapData BitmapData { get; set; }

    public GameMakerYYSWFItem()
    {
        ItemType = GameMakerYYSWFItemType.ItemInvalid;
        ID = -1;
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((int)ItemType);
        writer.Write(ID);

        switch (ItemType)
        {
            case GameMakerYYSWFItemType.ItemShape:
            {
                writer.WriteGameMakerObject(ShapeData);
                break;
            }

            case GameMakerYYSWFItemType.ItemBitmap:
            {
                writer.WriteGameMakerObject(BitmapData);
                break;
            }
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        ItemType = (GameMakerYYSWFItemType)reader.ReadInt32();
        ID = reader.ReadInt32();

        // I know, right?
        switch (ItemType)
        {
            case GameMakerYYSWFItemType.ItemShape:
            {
                ShapeData = reader.ReadGameMakerObjectNoPool<GameMakerShapeData<GameMakerYYSWFSubShapeData>>();
                BitmapData = null;
                break;
            }

            case GameMakerYYSWFItemType.ItemBitmap:
            {
                BitmapData = reader.ReadGameMakerObjectNoPool<GameMakerYYSWFBitmapData>();
                ShapeData = null;
                break;
            }

            case GameMakerYYSWFItemType.ItemFont:
            case GameMakerYYSWFItemType.ItemInvalid:
            case GameMakerYYSWFItemType.ItemTextField:
            case GameMakerYYSWFItemType.ItemSprite:
            default:
            {
                ShapeData = null;
                BitmapData = null;
                reader.SubmitWarning("Tried to read unknown YYSWFItem, " + ItemType);
                break;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"GameMakerYYSWFItem ({ItemType}, {ID})";
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWFTimeline : GameMakerObject
{
    public int Framerate { get; set; }
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    public int MaskWidth { get; set; }
    public int MaskHeight { get; set; }
    public GameMakerSimpleList<GameMakerYYSWFItem> UsedItems { get; set; }
    public GameMakerObservableList<GameMakerYYSWFTimelineFrame> Frames { get; set; }
    public GameMakerObservableList<GameMakerYYSWFCollisionMask> CollisionMasks { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerObject(UsedItems);
        writer.Write(Framerate);
        writer.Write(Frames.Count);
        writer.Write(MinX);
        writer.Write(MaxX);
        writer.Write(MinY);
        writer.Write(MaxY);
        writer.Write(CollisionMasks.Count);
        writer.Write(MaskWidth);
        writer.Write(MaskHeight);

        foreach (var yyswfFrame in Frames)
        {
            writer.WriteGameMakerObject(yyswfFrame);
        }

        foreach (var yyswfMask in CollisionMasks)
        {
            writer.WriteGameMakerObject(yyswfMask);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        int uc = reader.ReadInt32();
        if (uc < 0 || (long)uc * 8 > reader.Length - reader.Position)
            throw new IOException("Invalid SWF used item count.");
        UsedItems = new GameMakerSimpleList<GameMakerYYSWFItem>();
        UsedItems.SetCapacity(uc);
        for (int i = 0; i < uc; i++)
        {
            UsedItems.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerYYSWFItem>());
        }

        Framerate = reader.ReadInt32();
        int fc = reader.ReadInt32();
        MinX = reader.ReadSingle();
        MaxX = reader.ReadSingle();
        MinY = reader.ReadSingle();
        MaxY = reader.ReadSingle();
        int mc = reader.ReadInt32();
        MaskWidth = reader.ReadInt32();
        MaskHeight = reader.ReadInt32();
        if (fc < 0 || mc < 0 || (long)fc * 20 + (long)mc * 4 > reader.Length - reader.Position)
            throw new IOException("Invalid SWF timeline or collision mask count.");

        Frames = new GameMakerObservableList<GameMakerYYSWFTimelineFrame>(fc);
        for (int f = 0; f < fc; f++)
        {
            Frames.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerYYSWFTimelineFrame>());
        }

        CollisionMasks = new GameMakerObservableList<GameMakerYYSWFCollisionMask>(mc);
        for (int m = 0; m < mc; m++)
        {
            CollisionMasks.InternalAdd(reader.ReadGameMakerObjectNoPool<GameMakerYYSWFCollisionMask>());
        }
    }
}

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerYYSWF : GameMakerObject
{
    public byte[] JPEGTable { get; set; }
    public int Version { get; set; }
    public GameMakerYYSWFTimeline Timeline { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Align(4);
        int len = (JPEGTable?.Length ?? 0) | Int32.MinValue;

        writer.Write(len);
        writer.Write(Version);
        if (JPEGTable != null)
        {
            writer.Write(JPEGTable);
        }

        writer.Align(4);
        writer.WriteGameMakerObject(Timeline);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        reader.Align(4);
        int jpeglen = reader.ReadInt32() & (~Int32.MinValue); // the length is ORed with int.MinValue.
        Version = reader.ReadInt32();
        Util.DebugUtil.Assert(Version == 8 || Version == 7, "Invalid YYSWF version data! Expected 7 or 8, got " + Version);

        JPEGTable = jpeglen > 0 ? reader.ReadBytes(jpeglen) : null;

        reader.Align(4);
        Timeline = reader.ReadGameMakerObjectNoPool<GameMakerYYSWFTimeline>();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"GameMakerYYSWF ({Version})";
    }
}
