using System;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Project.SerializableAssets;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A font entry of a data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerFont : GameMakerNamedResource, IProjectAsset, IDisposable
{
    /// <summary>
    /// The name of the font.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The display name of the font.
    /// </summary>
    public GameMakerString DisplayName { get; set; }

    /// <summary>
    /// A helper variable on whether the Em size is a float.
    /// </summary>
    public bool EmSizeIsFloat { get; set; }

    /// <summary>
    /// The font size in Ems. In Game Maker: Studio 2.3 and above, this is a float instead. On versions below, it is an uint.
    /// </summary>
    public float EmSize { get; set; }

    /// <summary>
    /// Whether to display the font in bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Whether to display the font in italics
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// The start of the character range for this font.
    /// </summary>
    public ushort RangeStart { get; set; }

    /// <summary>
    /// Legacy font charset identifier, retained for compatibility with older runtimes.
    /// </summary>
    public byte Charset { get; set; }

    /// <summary>
    /// The level of anti-aliasing that is applied. 0 for none, Game Maker: Studio 2 has 1 for <c>on</c>, while
    /// GameMaker: Studio 1 and earlier have values 1-3 for different anti-aliasing levels.
    /// </summary>
    public byte AntiAliasing { get; set; }

    /// <summary>
    /// The end of the character range for this font.
    /// </summary>
    public uint RangeEnd { get; set; }

    /// <summary>
    /// The <see cref="GameMakerTexturePageItem"/> object that contains the texture for this font.
    /// </summary>
    public GameMakerTexturePageItem Texture { get; set; }

    /// <summary>
    /// The x scale this font uses.
    /// </summary>
    public float ScaleX { get; set; } = 1;

    /// <summary>
    /// The y scale this font uses.
    /// </summary>
    public float ScaleY { get; set; } = 1;

    /// <summary>
    /// Ascender metric used by modern GameMaker font layout.
    /// </summary>
    /// <remarks>
    /// Was introduced in GM 2022.2.
    /// </remarks>
    public uint Ascender { get; set; }

    /// <summary>
    /// Spread radius for signed-distance-field (SDF) font rendering.
    /// </summary>
    /// <remarks>
    /// Was introduced in GM 2023.2.
    /// </remarks>
    /// <value><c>0</c> if SDF is disabled for this font.</value>
    public uint SDFSpread { get; set; }

    /// <remarks>
    /// Line height used by modern GameMaker font layout. Introduced in GM 2023.6.
    /// </remarks>
    public uint LineHeight { get; set; }

    /// <summary>
    /// The glyphs that this font uses.
    /// </summary>
    public GameMakerPointerList<Glyph> Glyphs { get; set; } = new GameMakerPointerList<Glyph>();

    /// <summary>
    /// The maximum offset from the baseline to the top of the font.
    /// </summary>
    /// <remarks>
    /// Exists since bytecode 17, but seems to be only get checked in GM 2022.2+.
    /// </remarks>
    public int AscenderOffset { get; set; }


    /// <summary>
    /// Glyphs that a font can use.
    /// </summary>
    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class Glyph : GameMakerObject, IDisposable
    {
        /// <summary>
        /// The code point of character for the glyph.
        /// </summary>
        public ushort Character { get; set; }

        /// <summary>
        /// The x position in the <see cref="GameMakerFont.Texture"/> where the glyph can be found.
        /// </summary>
        public ushort SourceX { get; set; }

        /// <summary>
        /// The y position in the <see cref="GameMakerFont.Texture"/> where the glyph can be found.
        /// </summary>
        public ushort SourceY { get; set; }

        /// <summary>
        /// The width of the glyph in pixels.
        /// </summary>
        public ushort SourceWidth { get; set; }

        /// <summary>
        /// The height of the glyph in pixels.
        /// </summary>
        public ushort SourceHeight { get; set; }


        /// <summary>
        /// The number of pixels to shift right when advancing to the next character.
        /// </summary>
        public short Shift { get; set; }

        /// <summary>
        /// The number of pixels to horizontally offset the rendering of this glyph.
        /// </summary>
        public short Offset { get; set; }

        /// <summary>
        /// The kerning for each glyph.
        /// </summary>
        public GameMakerSimpleListShort<GlyphKerning> Kerning { get; set; } = new GameMakerSimpleListShort<GlyphKerning>();

        /// <summary>
        /// Purpose unknown, always 0.
        /// </summary>
        /// <remarks>
        /// Was introduced in GM 2024.11.
        /// </remarks>
        public short UnknownAlwaysZero { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(Character);
            writer.Write(SourceX);
            writer.Write(SourceY);
            writer.Write(SourceWidth);
            writer.Write(SourceHeight);
            writer.Write(Shift);
            writer.Write(Offset);
            if (writer.gameMakerData.IsNonLTSVersionAtLeast(2024, 11))
                writer.Write(UnknownAlwaysZero);
            writer.WriteGameMakerObject(Kerning);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Character = reader.ReadUInt16();
            SourceX = reader.ReadUInt16();
            SourceY = reader.ReadUInt16();
            SourceWidth = reader.ReadUInt16();
            SourceHeight = reader.ReadUInt16();
            Shift = reader.ReadInt16();
            Offset = reader.ReadInt16(); // Potential assumption, see the conversation at https://github.com/UnderminersTeam/GameMakerModTool/issues/40#issuecomment-440208912
            if (reader.gameMakerData.IsNonLTSVersionAtLeast(2024, 11))
                UnknownAlwaysZero = reader.ReadInt16();
            else
                UnknownAlwaysZero = 0;
            Kerning = reader.ReadGameMakerObject<GameMakerSimpleListShort<GlyphKerning>>();
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            reader.Position += 14;
            if (reader.gameMakerData.IsNonLTSVersionAtLeast(2024, 11))
                reader.Position += 2; // UnknownAlwaysZero

            return 1 + GameMakerSimpleListShort<GlyphKerning>.UnserializeChildObjectCount(reader);
        }

        /// <summary>
        /// A class representing kerning for a glyph.
        /// </summary>
        public class GlyphKerning : GameMakerObject, IStaticChildObjectsSize
        {
            /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
            public static readonly uint ChildObjectsSize = 4;

            /// <summary>
            /// The code point of the preceding character.
            /// </summary>
            public short Character { get; set; }

            /// <summary>
            /// An amount of pixels to add to the existing <see cref="Shift"/>.
            /// </summary>
            public short ShiftModifier { get; set; }

            /// <inheritdoc />
            public void Serialize(GameMakerWriter writer)
            {
                writer.Write(Character);
                writer.Write(ShiftModifier);
            }

            /// <inheritdoc />
            public void Unserialize(GameMakerReader reader)
            {
                Character = reader.ReadInt16();
                ShiftModifier = reader.ReadInt16();
            }

            /// <summary>
            /// Makes a copy of this <see cref="GlyphKerning"/>.
            /// </summary>
            /// <returns>The copy.</returns>
            public GlyphKerning Clone()
            {
                return new GlyphKerning() { ShiftModifier = this.ShiftModifier, Character = this.Character };
            }
        }

        /// <summary>
        /// Makes a copy of this <see cref="Glyph"/>.
        /// </summary>
        /// <returns>The copy.</returns>
        public Glyph Clone()
        {
            var kerning = new GameMakerSimpleListShort<GlyphKerning>();
            foreach (var kern in Kerning)
                kerning.InternalAdd(kern.Clone());

            return new Glyph()
            {
                Character = this.Character,
                SourceX = this.SourceX,
                SourceY = this.SourceY,
                SourceWidth = this.SourceWidth,
                SourceHeight = this.SourceHeight,
                Shift = this.Shift,
                Offset = this.Offset,
                UnknownAlwaysZero = this.UnknownAlwaysZero,
                Kerning = kerning
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Kerning = new();
        }
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.WriteGameMakerString(DisplayName);
        if (EmSizeIsFloat)
        {
            // cast to a float and negate.
            writer.Write(0.0f - EmSize);
        }
        else
        {
            // pre-GMS2.3
            writer.Write((uint)EmSize);
        }

        writer.Write(Bold);
        writer.Write(Italic);
        writer.Write(RangeStart);
        writer.Write(Charset);
        writer.Write(AntiAliasing);
        writer.Write(RangeEnd);
        writer.WriteGameMakerObjectPointer(Texture);
        writer.Write(ScaleX);
        writer.Write(ScaleY);
        if (writer.gameMakerData.GeneralInfo?.BytecodeVersion >= 17)
            writer.Write(AscenderOffset);
        if (writer.gameMakerData.IsVersionAtLeast(2022, 2))
            writer.Write(Ascender);
        if (writer.gameMakerData.IsNonLTSVersionAtLeast(2023, 2))
            writer.Write(SDFSpread);
        if (writer.gameMakerData.IsVersionAtLeast(2023, 6))
            writer.Write(LineHeight);
        writer.WriteGameMakerObject(Glyphs);
        if (writer.gameMakerData.IsVersionAtLeast(2024, 14))
            writer.Align(4);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        DisplayName = reader.ReadGameMakerString();
        uint readEmSize = reader.ReadUInt32();
        EmSizeIsFloat = false;

        // since the float is always written negated, it has the first bit set.
        if ((readEmSize & (1 << 31)) != 0)
        {
            float fsize = -BitConverter.ToSingle(BitConverter.GetBytes(readEmSize), 0);
            EmSize = fsize;
            EmSizeIsFloat = true;
        }
        else
        {
            EmSize = readEmSize;
        }

        Bold = reader.ReadBoolean();
        Italic = reader.ReadBoolean();
        RangeStart = reader.ReadUInt16();
        Charset = reader.ReadByte();
        AntiAliasing = reader.ReadByte();
        RangeEnd = reader.ReadUInt32();
        Texture = reader.ReadGameMakerObjectPointer<GameMakerTexturePageItem>();
        ScaleX = reader.ReadSingle();
        ScaleY = reader.ReadSingle();
        if (reader.gameMakerData.GeneralInfo?.BytecodeVersion >= 17)
            AscenderOffset = reader.ReadInt32();
        else
            AscenderOffset = 0;
        if (reader.gameMakerData.IsVersionAtLeast(2022, 2))
            Ascender = reader.ReadUInt32();
        else
            Ascender = 0;
        if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 2))
            SDFSpread = reader.ReadUInt32();
        else
            SDFSpread = 0;
        if (reader.gameMakerData.IsVersionAtLeast(2023, 6))
            LineHeight = reader.ReadUInt32();
        else
            LineHeight = 0;
        Glyphs = reader.ReadGameMakerObject<GameMakerPointerList<Glyph>>();
        if (reader.gameMakerData.IsVersionAtLeast(2024, 14))
            reader.Align(4);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        int skipSize = 40;
        if (reader.gameMakerData.GeneralInfo?.BytecodeVersion >= 17)
            skipSize += 4; // AscenderOffset
        if (reader.gameMakerData.IsVersionAtLeast(2022, 2))
            skipSize += 4; // Ascender
        if (reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 2))
            skipSize += 4; // SDFSpread
        if (reader.gameMakerData.IsVersionAtLeast(2023, 6))
            skipSize += 4; // LineHeight

        reader.Position += skipSize;

        uint count = 1 + GameMakerPointerList<Glyph>.UnserializeChildObjectCount(reader);

        if (reader.gameMakerData.IsVersionAtLeast(2024, 14))
            reader.Align(4);

        return count;
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

        foreach (Glyph glyph in Glyphs)
            glyph?.Dispose();
        Name = null;
        DisplayName = null;
        Texture = null;
        Glyphs = new();
    }

    /// <inheritdoc/>
    ISerializableProjectAsset IProjectAsset.GenerateSerializableProjectAsset(ProjectContext projectContext)
    {
        SerializableFont serializable = new();
        serializable.PopulateFromData(projectContext, this);
        return serializable;
    }

    /// <inheritdoc/>
    public string ProjectName => Name?.Content ?? "<unknown name>";

    /// <inheritdoc/>
    public SerializableAssetType ProjectAssetType => SerializableAssetType.Font;

    /// <inheritdoc/>
    public bool ProjectExportable => Name?.Content is not null && Texture is not null;
}
