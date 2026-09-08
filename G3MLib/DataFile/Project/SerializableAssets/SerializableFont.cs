using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project.Json;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Project.SerializableAssets;

/// <summary>
/// A serializable version of <see cref="GameMakerFont"/>.
/// </summary>
internal sealed class SerializableFont : ISerializableTextureProjectAsset
{
    /// <inheritdoc/>
    public string DataName { get; set; }

    /// <inheritdoc cref="GameMakerFont.DisplayName"/>
    public string DisplayName { get; set; }

    /// <inheritdoc cref="GameMakerFont.EmSizeIsFloat"/>
    public bool EmSizeIsFloat { get; set; }

    /// <inheritdoc cref="GameMakerFont.EmSize"/>
    public float EmSize { get; set; }

    /// <inheritdoc cref="GameMakerFont.Bold"/>
    public bool Bold { get; set; }

    /// <inheritdoc cref="GameMakerFont.Italic"/>
    public bool Italic { get; set; }

    /// <inheritdoc cref="GameMakerFont.RangeStart"/>
    public ushort RangeStart { get; set; }

    /// <inheritdoc cref="GameMakerFont.Charset"/>
    public byte Charset { get; set; }

    /// <inheritdoc cref="GameMakerFont.AntiAliasing"/>
    public byte AntiAliasing { get; set; }

    /// <inheritdoc cref="GameMakerFont.RangeEnd"/>
    public uint RangeEnd { get; set; }

    /// <inheritdoc cref="GameMakerFont.ScaleX"/>
    public float ScaleX { get; set; }

    /// <inheritdoc cref="GameMakerFont.ScaleY"/>
    public float ScaleY { get; set; }

    /// <inheritdoc cref="GameMakerFont.Ascender"/>
    public uint Ascender { get; set; }

    /// <inheritdoc cref="GameMakerFont.SDFSpread"/>
    public uint SDFSpread { get; set; }

    /// <inheritdoc cref="GameMakerFont.LineHeight"/>
    public uint LineHeight { get; set; }

    /// <inheritdoc cref="GameMakerFont.AscenderOffset"/>
    public int AscenderOffset { get; set; }

    /// <inheritdoc cref="GameMakerFont.Glyphs"/>
    [JsonConverter(typeof(NoPrettyPrintJsonConverter<List<Glyph>>))]
    public List<Glyph> Glyphs { get; set; }

    /// <inheritdoc cref="GameMakerFont.Glyph"/>
    public sealed class Glyph
    {
        /// <inheritdoc cref="GameMakerFont.Glyph.Character"/>
        [JsonPropertyName("C")]
        public ushort Character { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.SourceX"/>
        [JsonPropertyName("X")]
        public ushort SourceX { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.SourceY"/>
        [JsonPropertyName("Y")]
        public ushort SourceY { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.SourceWidth"/>
        [JsonPropertyName("W")]
        public ushort SourceWidth { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.SourceHeight"/>
        [JsonPropertyName("H")]
        public ushort SourceHeight { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.Shift"/>
        [JsonPropertyName("S")]
        public short Shift { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.Offset"/>
        [JsonPropertyName("O")]
        public short Offset { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.Kerning"/>
        [JsonPropertyName("K")]
        public List<GlyphKerning> Kerning { get; set; }
    }

    /// <inheritdoc cref="GameMakerFont.Glyph.GlyphKerning"/>
    public sealed class GlyphKerning
    {
        /// <inheritdoc cref="GameMakerFont.Glyph.GlyphKerning.Character"/>
        [JsonPropertyName("C")]
        public short Character { get; set; }

        /// <inheritdoc cref="GameMakerFont.Glyph.GlyphKerning.ShiftModifier"/>
        [JsonPropertyName("S")]
        public short ShiftModifier { get; set; }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public SerializableAssetType AssetType => SerializableAssetType.Font;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IndividualDirectory => true;

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OverrideOrder { get; set; }

    // Data asset that was located during pre-import.
    private GameMakerFont _dataAsset = null;

    // Texture image created for the asset during texture import.
    private GameMakerTexturePageItem _textureImage = null;

    /// <summary>
    /// Populates this serializable font with data from an actual font.
    /// </summary>
    internal void PopulateFromData(ProjectContext projectContext, GameMakerFont fnt)
    {
        // Update all main properties
        DataName = fnt.Name.Content;
        DisplayName = fnt.DisplayName?.Content;
        EmSizeIsFloat = fnt.EmSizeIsFloat;
        EmSize = fnt.EmSize;
        Bold = fnt.Bold;
        Italic = fnt.Italic;
        RangeStart = fnt.RangeStart;
        Charset = fnt.Charset;
        AntiAliasing = fnt.AntiAliasing;
        RangeEnd = fnt.RangeEnd;
        ScaleX = fnt.ScaleX;
        ScaleY = fnt.ScaleY;
        Ascender = fnt.Ascender;
        SDFSpread = fnt.SDFSpread;
        LineHeight = fnt.LineHeight;
        AscenderOffset = fnt.AscenderOffset;

        // Update glyphs
        Glyphs = new(fnt.Glyphs.Count);
        foreach (GameMakerFont.Glyph glyph in fnt.Glyphs)
        {
            Glyph newGlyph = new()
            {
                Character = glyph.Character,
                SourceX = glyph.SourceX,
                SourceY = glyph.SourceY,
                SourceWidth = glyph.SourceWidth,
                SourceHeight = glyph.SourceHeight,
                Shift = glyph.Shift,
                Offset = glyph.Offset,
                Kerning = new(glyph.Kerning.Count)
            };
            foreach (GameMakerFont.Glyph.GlyphKerning kerning in glyph.Kerning)
            {
                newGlyph.Kerning.Add(new()
                {
                    Character = kerning.Character,
                    ShiftModifier = kerning.ShiftModifier
                });
            }
            Glyphs.Add(newGlyph);
        }

        _dataAsset = fnt;
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
        if (projectContext.Data.Fonts.ByName(DataName) is GameMakerFont existing)
        {
            // Font found
            _dataAsset = existing;
        }
        else
        {
            // No font found; create new one
            _dataAsset = new()
            {
                Name = projectContext.MakeString(DataName)
            };
            projectContext.Data.Fonts.Add(_dataAsset);
        }
    }

    /// <inheritdoc/>
    public IProjectAsset Import(ProjectContext projectContext)
    {
        GameMakerFont fnt = _dataAsset;

        // Update all main properties
        fnt.DisplayName = projectContext.MakeString(DisplayName);
        fnt.EmSizeIsFloat = EmSizeIsFloat;
        fnt.EmSize = EmSize;
        fnt.Bold = Bold;
        fnt.Italic = Italic;
        fnt.RangeStart = RangeStart;
        fnt.Charset = Charset;
        fnt.AntiAliasing = AntiAliasing;
        fnt.RangeEnd = RangeEnd;
        fnt.ScaleX = ScaleX;
        fnt.ScaleY = ScaleY;
        fnt.Ascender = Ascender;
        fnt.SDFSpread = SDFSpread;
        fnt.LineHeight = LineHeight;
        fnt.AscenderOffset = AscenderOffset;

        // Update glyphs
        fnt.Glyphs = new(Glyphs?.Count ?? 0);
        foreach (Glyph glyph in Glyphs ?? [])
        {
            GameMakerFont.Glyph newGlyph = new()
            {
                Character = glyph.Character,
                SourceX = glyph.SourceX,
                SourceY = glyph.SourceY,
                SourceWidth = glyph.SourceWidth,
                SourceHeight = glyph.SourceHeight,
                Shift = glyph.Shift,
                Offset = glyph.Offset,
                Kerning = new(glyph.Kerning?.Count ?? 0)
            };
            foreach (GlyphKerning kerning in glyph.Kerning ?? [])
            {
                newGlyph.Kerning.Add(new()
                {
                    Character = kerning.Character,
                    ShiftModifier = kerning.ShiftModifier
                });
            }
            fnt.Glyphs.Add(newGlyph);
        }

        // Assign texture image
        fnt.Texture = _textureImage;

        return fnt;
    }

    /// <inheritdoc/>
    public void ImportTextures(ProjectContext projectContext, TextureGroupPacker texturePacker)
    {
        // Get JSON filename (of main asset file)
        if (!projectContext.AssetDataNamesToPaths.TryGetValue(new(DataName, AssetType), out string jsonFilename))
        {
            throw new ProjectException("Failed to get font asset path");
        }

        // Awaiting official Underminers resolution: project assets do not preserve texture-group or external-page metadata.
        string directory = Path.GetDirectoryName(jsonFilename);
        string filename = TextureWorker.FindImageFile(directory, Path.GetFileNameWithoutExtension(jsonFilename));
        try
        {
            // Add image to packer
            _textureImage = texturePacker.AddImage(TextureWorker.ReadBGRAImageFromFile(filename),
                                                   TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.ExtraBorder,
                                                   false);
        }
        catch (Exception e)
        {
            throw new ProjectException($"Failed to import font image file named \"{Path.GetFileName(filename)}\": {e.Message}", e);
        }
    }
}
