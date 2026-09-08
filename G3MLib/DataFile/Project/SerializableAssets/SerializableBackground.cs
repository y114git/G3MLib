using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project.Json;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Project.SerializableAssets;

/// <summary>
/// A serializable version of <see cref="GameMakerBackground"/>.
/// </summary>
internal sealed class SerializableBackground : ISerializableTextureProjectAsset
{
    /// <inheritdoc/>
    public string DataName { get; set; }

    /// <inheritdoc cref="GameMakerBackground.Transparent"/>
    public bool Transparent { get; set; }

    /// <inheritdoc cref="GameMakerBackground.Smooth"/>
    public bool Smooth { get; set; }

    /// <inheritdoc cref="GameMakerBackground.Preload"/>
    public bool Preload { get; set; }

    /// <summary>
    /// GMS2+ tileset properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TilesetProperties Tileset { get; set; }

    /// <summary>
    /// Struct for GMS2+ tileset properties.
    /// </summary>
    public struct TilesetProperties()
    {
        /// <inheritdoc cref="GameMakerBackground.GMS2TilesetVersion"/>
        public uint VersionNumber { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2TileWidth"/>
        public uint TileWidth { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2TileWidth"/>
        public uint TileHeight { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2OutputBorderX"/>
        public uint OutputBorderX { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2OutputBorderY"/>
        public uint OutputBorderY { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2TileColumns"/>
        public uint TileColumns { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2ItemsPerTileCount"/>
        public uint ItemsPerTileCount { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2TileCount"/>
        public uint TileCount { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2ExportedSprite"/>
        public string ExportedSprite { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2FrameLength"/>
        public long FrameLength { get; set; }

        /// <inheritdoc cref="GameMakerBackground.GMS2TileIds"/>
        [JsonConverter(typeof(NoPrettyPrintJsonConverter<List<uint>>))]
        public List<uint> TileIDs { get; set; }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public SerializableAssetType AssetType => SerializableAssetType.Background;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IndividualDirectory => true;

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OverrideOrder { get; set; }

    // Data asset that was located during pre-import.
    private GameMakerBackground _dataAsset = null;

    // Texture image created for the asset during texture import.
    private GameMakerTexturePageItem _textureImage = null;

    /// <summary>
    /// Populates this serializable background with data from an actual background.
    /// </summary>
    internal void PopulateFromData(ProjectContext projectContext, GameMakerBackground bg)
    {
        // Update all main properties
        DataName = bg.Name.Content;
        Transparent = bg.Transparent;
        Smooth = bg.Smooth;
        Preload = bg.Preload;

        // Update GMS2+ properties
        if (projectContext.Data.IsGameMaker2())
        {
            Tileset = new()
            {
                VersionNumber = bg.GMS2TilesetVersion,
                TileWidth = bg.GMS2TileWidth,
                TileHeight = bg.GMS2TileHeight,
                OutputBorderX = bg.GMS2OutputBorderX,
                OutputBorderY = bg.GMS2OutputBorderY,
                TileColumns = bg.GMS2TileColumns,
                ItemsPerTileCount = bg.GMS2ItemsPerTileCount,
                TileCount = bg.GMS2TileCount,
                ExportedSprite = bg.GMS2ExportedSprite?.Name?.Content,
                FrameLength = bg.GMS2FrameLength,
                TileIDs = bg.GMS2TileIds.Select((GameMakerBackground.TileID tileId) => tileId.ID).ToList()
            };
        }

        _dataAsset = bg;
    }

    /// <inheritdoc/>
    public void Serialize(ProjectContext projectContext, string destinationFile)
    {
        // Write main JSON
        using FileStream fs = new(destinationFile, FileMode.Create);
        JsonSerializer.Serialize<ISerializableProjectAsset>(fs, this, ProjectContext.JsonOptions);

        // Write image as a PNG
        string filename = $"{Path.GetFileNameWithoutExtension(destinationFile)}.png";
        string directory = Path.GetDirectoryName(destinationFile);
        projectContext.TextureWorker.ExportAsPNG(_dataAsset.Texture, Path.Join(directory, filename), DataName, true);
    }

    /// <inheritdoc/>
    public void PreImport(ProjectContext projectContext)
    {
        if (projectContext.Data.Backgrounds.ByName(DataName) is GameMakerBackground existing)
        {
            // Background found
            _dataAsset = existing;
        }
        else
        {
            // No background found; create new one
            _dataAsset = new()
            {
                Name = projectContext.MakeString(DataName)
            };
            projectContext.Data.Backgrounds.Add(_dataAsset);
        }
    }

    /// <inheritdoc/>
    public IProjectAsset Import(ProjectContext projectContext)
    {
        GameMakerBackground bg = _dataAsset;

        // Update all main properties
        bg.Transparent = Transparent;
        bg.Smooth = Smooth;
        bg.Preload = Preload;

        // Update GMS2+ properties
        if (projectContext.Data.IsGameMaker2())
        {
            bg.GMS2TilesetVersion = Tileset.VersionNumber;
            bg.GMS2TileWidth = Tileset.TileWidth;
            bg.GMS2TileHeight = Tileset.TileHeight;
            bg.GMS2OutputBorderX = Tileset.OutputBorderX;
            bg.GMS2OutputBorderY = Tileset.OutputBorderY;
            bg.GMS2TileColumns = Tileset.TileColumns;
            bg.GMS2ItemsPerTileCount = Tileset.ItemsPerTileCount;
            bg.GMS2TileCount = Tileset.TileCount;
            if (projectContext.Data.IsVersionAtLeast(2023, 8))
            {
                bg.GMS2ExportedSprite = projectContext.FindSprite(Tileset.ExportedSprite, this);
            }
            bg.GMS2FrameLength = Tileset.FrameLength;
            bg.GMS2TileIds = new(Tileset.TileIDs.Select((uint tileId) => new GameMakerBackground.TileID() { ID = tileId }).ToList());
        }

        // Assign texture image
        bg.Texture = _textureImage;

        return bg;
    }

    /// <inheritdoc/>
    public void ImportTextures(ProjectContext projectContext, TextureGroupPacker texturePacker)
    {
        // Get JSON filename (of main asset file)
        if (!projectContext.AssetDataNamesToPaths.TryGetValue(new(DataName, AssetType), out string jsonFilename))
        {
            throw new ProjectException("Failed to get background asset path");
        }

        // Awaiting official Underminers resolution: project assets do not preserve texture-group or external-page metadata.
        string directory = Path.GetDirectoryName(jsonFilename);
        string filename = TextureWorker.FindImageFile(directory, Path.GetFileNameWithoutExtension(jsonFilename));
        try
        {
            // Add image to packer
            bool allowCrop = !projectContext.Data.IsGameMaker2();
            _textureImage = texturePacker.AddImage(TextureWorker.ReadBGRAImageFromFile(filename),
                                                   TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.ExtraBorder,
                                                   allowCrop);
        }
        catch (Exception e)
        {
            throw new ProjectException($"Failed to import background image file named \"{Path.GetFileName(filename)}\": {e.Message}", e);
        }
    }
}
