using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Project.SerializableAssets;

/// <summary>
/// A serializable version of <see cref="GameMakerSprite"/>.
/// </summary>
internal sealed class SerializableSprite : ISerializableTextureProjectAsset
{
    /// <inheritdoc/>
    public string DataName { get; set; }

    /// <inheritdoc cref="GameMakerSprite.Width"/>
    public uint Width { get; set; }

    /// <inheritdoc cref="GameMakerSprite.Height"/>
    public uint Height { get; set; }

    /// <inheritdoc cref="GameMakerSprite.MarginLeft"/>
    public int MarginLeft { get; set; }

    /// <inheritdoc cref="GameMakerSprite.MarginRight"/>
    public int MarginRight { get; set; }

    /// <inheritdoc cref="GameMakerSprite.MarginBottom"/>
    public int MarginBottom { get; set; }

    /// <inheritdoc cref="GameMakerSprite.MarginTop"/>
    public int MarginTop { get; set; }

    /// <inheritdoc cref="GameMakerSprite.Transparent"/>
    public bool Transparent { get; set; }

    /// <inheritdoc cref="GameMakerSprite.Smooth"/>
    public bool Smooth { get; set; }

    /// <inheritdoc cref="GameMakerSprite.Preload"/>
    public bool Preload { get; set; }

    /// <inheritdoc cref="GameMakerSprite.BBoxMode"/>
    public uint BBoxMode { get; set; }

    /// <inheritdoc cref="GameMakerSprite.SepMasks"/>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CollisionKinds CollisionKind { get; set; }

    /// <inheritdoc cref="GameMakerSprite.OriginX"/>
    public int OriginX { get; set; }

    /// <inheritdoc cref="GameMakerSprite.OriginY"/>
    public int OriginY { get; set; }

    /// <summary>
    /// Number of textures for the sprite, to be put on disk.
    /// </summary>
    public int TextureCount { get; set; }

    /// <summary>
    /// Number of collision masks for the sprite, to be put on disk.
    /// </summary>
    public int MaskCount { get; set; }

    /// <summary>
    /// Extra properties, optionally defined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ExtraProperties Extra { get; set; }

    /// <summary>
    /// Sequence contained within the sprite, optionally defined.
    /// </summary>
    public SerializableSequence Sequence { get; set; }

    /// <summary>
    /// Nine-slice properties, optionally defined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NineSliceProperties NineSlice { get; set; }

    /// <summary>
    /// Texture importing properties, optionally defined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TextureImportProperties TextureImport { get; set; }

    /// <summary>
    /// Extra properties attached to sprites, but not always present in older versions of GameMaker.
    /// </summary>
    public struct ExtraProperties()
    {
        /// <inheritdoc cref="GameMakerSprite.SVersion"/>
        public uint ExtraVersion { get; set; }

        /// <inheritdoc cref="GameMakerSprite.SSpriteType"/>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExtraSpriteType SpriteType { get; set; }

        /// <inheritdoc cref="GameMakerSprite.GMS2PlaybackSpeed"/>
        public float PlaybackSpeed { get; set; }

        /// <inheritdoc cref="GameMakerSprite.GMS2PlaybackSpeedType"/>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExtraAnimSpeedType PlaybackSpeedType { get; set; }
    }

    /// <summary>
    /// Nine-slice properties attached to sprites, optionally.
    /// </summary>
    public struct NineSliceProperties()
    {
        /// <inheritdoc cref="GameMakerSprite.NineSlice.Enabled"/>
        public bool Enabled { get; set; }

        /// <inheritdoc cref="GameMakerSprite.NineSlice.Left"/>
        public int Left { get; set; }

        /// <inheritdoc cref="GameMakerSprite.NineSlice.Top"/>
        public int Top { get; set; }

        /// <inheritdoc cref="GameMakerSprite.NineSlice.Right"/>
        public int Right { get; set; }

        /// <inheritdoc cref="GameMakerSprite.NineSlice.Bottom"/>
        public int Bottom { get; set; }

        /// <inheritdoc cref="GameMakerSprite.NineSlice.TileModes"/>
        public List<TileMode> TileModes { get; set; }

        /// <inheritdoc cref="GameMakerSprite.NineSlice.TileMode"/>
        [JsonConverter(typeof(JsonStringEnumConverter<TileMode>))]
        public enum TileMode
        {
            Stretch = 0,
            Repeat = 1,
            Mirror = 2,
            BlankRepeat = 3,
            Hide = 4
        }
    }

    /// <summary>
    /// Texture (group) information attached to sprites, optionally.
    /// Not mirrored directly in GameMaker's data format, but will affect importing.
    /// </summary>
    public struct TextureImportProperties()
    {
        /// <summary>
        /// Whether cropping should be allowed when placing textures on the atlas during importing.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool DisableCrop { get; set; } = false;
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public SerializableAssetType AssetType => SerializableAssetType.Sprite;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IndividualDirectory => true;

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OverrideOrder { get; set; }

    // Data asset that was located during pre-import.
    private GameMakerSprite _dataAsset = null;

    // Texture images created for the asset during texture import.
    private List<GameMakerTexturePageItem> _textureImages = null;

    // Mask blobs created for the asset during texture import.
    private List<byte[]> _maskData = null;

    /// <inheritdoc cref="GameMakerSprite.SepMaskType"/>
    public enum CollisionKinds : uint
    {
        AxisAlignedRect = 0,
        Precise = 1,
        RotatedRect = 2
    }

    /// <inheritdoc cref="GameMakerSprite.SpriteType"/>
    public enum ExtraSpriteType : uint
    {
        /// <inheritdoc cref="GameMakerSprite.SpriteType.Normal"/>
        Normal = 0,

        /// <inheritdoc cref="GameMakerSprite.SpriteType.SWF"/>
        SWF = 1,

        /// <inheritdoc cref="GameMakerSprite.SpriteType.Spine"/>
        Spine = 2,

        /// <inheritdoc cref="GameMakerSprite.SpriteType.Vector"/>
        Vector = 3
    }

    /// <inheritdoc cref="AnimSpeedType"/>
    public enum ExtraAnimSpeedType : uint
    {
        FramesPerSecond = 0,
        FramesPerGameFrame = 1
    }

    /// <summary>
    /// Populates this serializable sprite with data from an actual sprite.
    /// </summary>
    internal void PopulateFromData(ProjectContext projectContext, GameMakerSprite spr)
    {
        // Update all main properties
        DataName = spr.Name.Content;
        Width = spr.Width;
        Height = spr.Height;
        MarginLeft = spr.MarginLeft;
        MarginRight = spr.MarginRight;
        MarginBottom = spr.MarginBottom;
        MarginTop = spr.MarginTop;
        Transparent = spr.Transparent;
        Smooth = spr.Smooth;
        Preload = spr.Preload;
        BBoxMode = spr.BBoxMode;
        CollisionKind = (CollisionKinds)spr.SepMasks;
        OriginX = spr.OriginX;
        OriginY = spr.OriginY;
        TextureCount = spr.Textures.Count;
        MaskCount = spr.CollisionMasks.Count;

        // Update extra properties, if they exist
        if (spr.IsSpecialType)
        {
            Extra = new()
            {
                ExtraVersion = spr.SVersion,
                SpriteType = (ExtraSpriteType)spr.SSpriteType,
                PlaybackSpeed = spr.GMS2PlaybackSpeed,
                PlaybackSpeedType = (ExtraAnimSpeedType)spr.GMS2PlaybackSpeedType
            };
        }

        // Update sequence and nine slice properties, if they exist
        if (spr.SVersion >= 2 && spr.V2Sequence is GameMakerSequence sequence)
        {
            Sequence = (SerializableSequence)sequence.GenerateSerializableProjectAsset(projectContext);
        }
        if (spr.SVersion >= 3 && spr.V3NineSlice is GameMakerSprite.NineSlice nineSlice)
        {
            NineSlice = new()
            {
                Enabled = nineSlice.Enabled,
                Left = nineSlice.Left,
                Top = nineSlice.Top,
                Right = nineSlice.Right,
                Bottom = nineSlice.Bottom,
                TileModes = [.. nineSlice.TileModes.Select((mode) => (NineSliceProperties.TileMode)mode)]
            };
        }

        _dataAsset = spr;
    }

    /// <inheritdoc/>
    public void Serialize(ProjectContext projectContext, string destinationFile)
    {
        // Write main JSON
        using (FileStream fs = new(destinationFile, FileMode.Create))
        {
            JsonSerializer.Serialize<ISerializableProjectAsset>(fs, this, ProjectContext.JsonOptions);
        }

        // Write images and masks as PNGs
        string directory = Path.GetDirectoryName(destinationFile);
        string baseFilename = Path.GetFileNameWithoutExtension(destinationFile);
        for (int i = 0; i < TextureCount; i++)
        {
            string filename = $"{baseFilename}_{i}.png";
            projectContext.TextureWorker.ExportAsPNG(_dataAsset.Textures[i].Texture, Path.Join(directory, filename), DataName, true);
        }
        (int maskWidth, int maskHeight) = _dataAsset.CalculateMaskDimensions(projectContext.Data);
        for (int i = 0; i < MaskCount; i++)
        {
            string filename = $"{baseFilename}_{i}.mask.png";
            TextureWorker.ExportCollisionMaskPNG(_dataAsset.CollisionMasks[i], Path.Join(directory, filename), maskWidth, maskHeight);
        }
    }

    /// <inheritdoc/>
    public void PreImport(ProjectContext projectContext)
    {
        if (projectContext.Data.Sprites.ByName(DataName) is GameMakerSprite existing)
        {
            // Sprite found
            _dataAsset = existing;
        }
        else
        {
            // No sprite found; create new one
            _dataAsset = new()
            {
                Name = projectContext.MakeString(DataName)
            };
            projectContext.Data.Sprites.Add(_dataAsset);
        }

        GameMakerSprite spr = _dataAsset;

        // Update all main properties
        spr.Width = Width;
        spr.Height = Height;
        spr.MarginLeft = MarginLeft;
        spr.MarginRight = MarginRight;
        spr.MarginBottom = MarginBottom;
        spr.MarginTop = MarginTop;
        spr.Transparent = Transparent;
        spr.Smooth = Smooth;
        spr.Preload = Preload;
        spr.BBoxMode = BBoxMode;
        spr.SepMasks = (GameMakerSprite.SepMaskType)CollisionKind;
        spr.OriginX = OriginX;
        spr.OriginY = OriginY;
    }

    /// <inheritdoc/>
    public IProjectAsset Import(ProjectContext projectContext)
    {
        GameMakerSprite spr = _dataAsset;

        // Update extra properties, if they exist
        if (!Extra.Equals(default(ExtraProperties)))
        {
            spr.IsSpecialType = true;
            spr.SVersion = Extra.ExtraVersion;
            spr.SSpriteType = (GameMakerSprite.SpriteType)Extra.SpriteType;
            spr.GMS2PlaybackSpeed = Extra.PlaybackSpeed;
            spr.GMS2PlaybackSpeedType = (AnimSpeedType)Extra.PlaybackSpeedType;
        }
        else
        {
            spr.IsSpecialType = false;
        }

        // Update sequence and nine slice properties, if they exist
        if (Extra.ExtraVersion >= 2)
        {
            spr.V2Sequence = Sequence?.ImportSubResource(projectContext);
        }
        if (Extra.ExtraVersion >= 3)
        {
            if (NineSlice.Equals(default(NineSliceProperties)))
            {
                spr.V3NineSlice = null;
            }
            else
            {
                spr.V3NineSlice = new()
                {
                    Enabled = NineSlice.Enabled,
                    Left = NineSlice.Left,
                    Top = NineSlice.Top,
                    Right = NineSlice.Right,
                    Bottom = NineSlice.Bottom,
                    TileModes = [.. NineSlice.TileModes.Select((mode) => (GameMakerSprite.NineSlice.TileMode)mode)]
                };
            }
        }

        // Assign texture images and collision masks
        spr.Textures.Clear();
        foreach (GameMakerTexturePageItem textureImage in _textureImages)
        {
            spr.Textures.Add(new() { Texture = textureImage });
        }
        spr.CollisionMasks.Clear();
        foreach (byte[] mask in _maskData)
        {
            GameMakerSprite.MaskEntry maskEntry = spr.NewMaskEntry(projectContext.Data);
            maskEntry.Data = mask;
            spr.CollisionMasks.Add(maskEntry);
        }

        return spr;
    }

    /// <inheritdoc/>
    public void ImportTextures(ProjectContext projectContext, TextureGroupPacker texturePacker)
    {
        // Get JSON filename (of main asset file)
        if (!projectContext.AssetDataNamesToPaths.TryGetValue(new(DataName, AssetType), out string jsonFilename))
        {
            throw new ProjectException("Failed to get sprite asset path");
        }

        // Awaiting official Underminers resolution: project assets do not preserve texture-group or external-page metadata.

        bool allowCrop = true;
        if (TextureImport.DisableCrop)
        {
            allowCrop = false;
        }

        string baseFilename = Path.GetFileNameWithoutExtension(jsonFilename);
        string directory = Path.GetDirectoryName(jsonFilename);

        // Load PNGs from disk, to be imported
        _textureImages = new(TextureCount);
        for (int i = 0; i < TextureCount; i++)
        {
            string filename = TextureWorker.FindImageFile(directory, $"{baseFilename}_{i}");
            try
            {
                // Add image to packer
                _textureImages.Add(texturePacker.AddImage(TextureWorker.ReadBGRAImageFromFile(filename),
                                                          TextureGroupPacker.BorderFlags.Enabled, allowCrop));
            }
            catch (Exception e)
            {
                throw new ProjectException($"Failed to import sprite image file named \"{Path.GetFileName(filename)}\": {e.Message}", e);
            }
        }

        // Load collision masks from disk, to be imported
        (int maskWidth, int maskHeight) = _dataAsset.CalculateMaskDimensions(projectContext.Data);
        _maskData = new(MaskCount);
        for (int i = 0; i < MaskCount; i++)
        {
            string filename = $"{baseFilename}_{i}.mask.png";
            try
            {
                // Add image to packer
                _maskData.Add(TextureWorker.ReadMaskData(Path.Join(directory, filename), maskWidth, maskHeight));
            }
            catch (Exception e)
            {
                throw new ProjectException($"Failed to import sprite mask image file named \"{filename}\": {e.Message}", e);
            }
        }
    }
}
