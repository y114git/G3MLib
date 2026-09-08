using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A texture group info entry in a data file.
/// </summary>
/// <remarks>This is a new chunk since Bytecode 17. It's probably related to performance improvements mentioned in the release notes for that runtime.
/// Here is the specification:
/// <code>
/// TGIN structure - likely stands for Texture Group Info
/// Chunk introduced in 2.2.1 with new texture functions
/// ---
///
/// Int32 - probably chunk format version number, always 1
///
/// PointerList&lt;T&gt; structure. Each item represents a texture group:
///     32-bit string pointer - Name
///     32-bit pointer #1
///     32-bit pointer #2
///     32-bit pointer #3
///     32-bit pointer #4
///     32-bit pointer #5
///
///     #1 leads here:
///     SimpleList&lt;int&gt; of texture page IDs the group has
///
///     #2 leads here:
///     SimpleList&lt;int&gt; of sprite IDs the group has
///
///     #3 leads here:
///     SimpleList&lt;int&gt; of Spine sprite IDs (normal sprite ID, just this has Spine sprites separated) the group has
///
///     #4 leads here:
///     SimpleList&lt;int&gt; of font IDs the group has
///
///     #5 leads here:
///     SimpleList&lt;int&gt; of tileset IDs the group has
/// </code>
/// <see href="https://github.com/UnderminersTeam/GameMakerModTool/wiki/Bytecode-17#tgin-new-chunk"/>.</remarks>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerTextureGroupInfo : GameMakerNamedResource, IDisposable
{
    /// <summary>
    /// The name of the texture group info entry.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// A list of texture pages the group has.
    /// </summary>
    public GameMakerSimpleResourcesList<GameMakerEmbeddedTexture, GameMakerChunkTXTR> TexturePages { get; set; }

    /// <summary>
    /// A list of sprites the group has.
    /// </summary>
    public GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT> Sprites { get; set; }

    /// <summary>
    /// A list of spine sprites the group has.
    /// </summary>
    public GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT> SpineSprites { get; set; }

    /// <summary>
    /// A list of fonts the group has.
    /// </summary>
    public GameMakerSimpleResourcesList<GameMakerFont, GameMakerChunkFONT> Fonts { get; set; }

    /// <summary>
    /// A list of tilesets this group has.
    /// </summary>
    public GameMakerSimpleResourcesList<GameMakerBackground, GameMakerChunkBGND> Tilesets { get; set; }

    /// <summary>
    /// Directory of the texture on disk. 2022.9+ only.
    /// </summary>
    public GameMakerString Directory { get; set; }

    /// <summary>
    /// File extension of the texture on disk. 2022.9+ only.
    /// </summary>
    public GameMakerString Extension { get; set; }

    /// <summary>
    /// The load type of the texture. 2022.9+ only.
    /// </summary>
    public TextureGroupLoadType LoadType { get; set; }

    /// <summary>
    /// The possible load types of a texture in 2022.9 and above. Old versions default to "InFile".
    /// </summary>
    public enum TextureGroupLoadType
    {
        /// <summary>
        /// The texture data is located inside this file.
        /// </summary>
        InFile = 0,
        /// <summary>
        /// The textures of the group this belongs to are located externally
        /// May mean more specifically that textures for one texture group are all in one file.
        /// </summary>
        SeparateGroup = 1,
        /// <summary>
        /// The textures of the group this belongs to are located externally.
        /// May mean more specifically that textures are separated into different files, within the group.
        /// </summary>
        SeparateTextures = 2
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GameMakerTextureGroupInfo"/>.
    /// </summary>
    public GameMakerTextureGroupInfo()
    {
        TexturePages = new GameMakerSimpleResourcesList<GameMakerEmbeddedTexture, GameMakerChunkTXTR>();
        Sprites = new GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT>();
        SpineSprites = new GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT>();
        Fonts = new GameMakerSimpleResourcesList<GameMakerFont, GameMakerChunkFONT>();
        Tilesets = new GameMakerSimpleResourcesList<GameMakerBackground, GameMakerChunkBGND>();
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);

        if (writer.gameMakerData.IsVersionAtLeast(2022, 9))
        {
            writer.WriteGameMakerString(Directory);
            writer.WriteGameMakerString(Extension);
            writer.Write((int)LoadType);
        }

        writer.WriteGameMakerObjectPointer(TexturePages);
        writer.WriteGameMakerObjectPointer(Sprites);
        if (!writer.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
            writer.WriteGameMakerObjectPointer(SpineSprites);
        writer.WriteGameMakerObjectPointer(Fonts);
        writer.WriteGameMakerObjectPointer(Tilesets);

        writer.WriteGameMakerObject(TexturePages);
        writer.WriteGameMakerObject(Sprites);
        if (!writer.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
            writer.WriteGameMakerObject(SpineSprites);
        writer.WriteGameMakerObject(Fonts);
        writer.WriteGameMakerObject(Tilesets);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();

        if (reader.gameMakerData.IsVersionAtLeast(2022, 9))
        {
            Directory = reader.ReadGameMakerString();
            Extension = reader.ReadGameMakerString();
            LoadType = (TextureGroupLoadType)reader.ReadInt32();
        }
        else
        {
            Directory = null;
            Extension = null;
            LoadType = TextureGroupLoadType.InFile;
        }

        // Read the pointers
        TexturePages = reader.ReadGameMakerObjectPointer<GameMakerSimpleResourcesList<GameMakerEmbeddedTexture, GameMakerChunkTXTR>>();
        Sprites = reader.ReadGameMakerObjectPointer<GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT>>();
        if (!reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
            SpineSprites = reader.ReadGameMakerObjectPointer<GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT>>();
        else
            SpineSprites = null;
        Fonts = reader.ReadGameMakerObjectPointer<GameMakerSimpleResourcesList<GameMakerFont, GameMakerChunkFONT>>();
        Tilesets = reader.ReadGameMakerObjectPointer<GameMakerSimpleResourcesList<GameMakerBackground, GameMakerChunkBGND>>();

        // Read the objects, throwing an error if the pointers are invalid
        reader.ReadGameMakerObject(TexturePages);
        reader.ReadGameMakerObject(Sprites);
        if (!reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
            reader.ReadGameMakerObject(SpineSprites);
        reader.ReadGameMakerObject(Fonts);
        reader.ReadGameMakerObject(Tilesets);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        reader.Position += 4; // "Name"

        if (reader.gameMakerData.IsVersionAtLeast(2022, 9))
            reader.Position += 12;

        uint texPagesPtr = reader.ReadUInt32();
        uint spritesPtr = reader.ReadUInt32();
        uint spineSpritesPtr = 0;
        if (!reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
            spineSpritesPtr = reader.ReadUInt32();
        uint fontsPtr = reader.ReadUInt32();
        uint tilesetsPtr = reader.ReadUInt32();

        reader.AbsPosition = texPagesPtr;
        count += 1 + GameMakerSimpleResourcesList<GameMakerEmbeddedTexture, GameMakerChunkTXTR>
                     .UnserializeChildObjectCount(reader);

        reader.AbsPosition = spritesPtr;
        count += 1 + GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT>
                     .UnserializeChildObjectCount(reader);

        if (!reader.gameMakerData.IsNonLTSVersionAtLeast(2023, 1))
        {
            reader.AbsPosition = spineSpritesPtr;
            count += 1 + GameMakerSimpleResourcesList<GameMakerSprite, GameMakerChunkSPRT>
                         .UnserializeChildObjectCount(reader);
        }

        reader.AbsPosition = fontsPtr;
        count += 1 + GameMakerSimpleResourcesList<GameMakerFont, GameMakerChunkFONT>
                     .UnserializeChildObjectCount(reader);

        reader.AbsPosition = tilesetsPtr;
        count += 1 + GameMakerSimpleResourcesList<GameMakerBackground, GameMakerChunkBGND>
                     .UnserializeChildObjectCount(reader);

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

        Name = null;
        TexturePages = null;
        Sprites = null;
        SpineSprites = null;
        Fonts = null;
        Tilesets = null;
    }
}