using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// An embedded image entry in a GameMaker data file. This is GMS2 only.<br/>
/// Not to be confused with the other "embedded" resources, this is a bit different.
/// </summary>
/// <remarks>Rough structure:<code>
/// EMBI - stands for "embedded images"?
/// Couldn't figure out how to fill the chunk with any data by editing a project and recompiling...
/// ---
/// &lt;normal FourCC and chunk length found in every chunk&gt;
///
/// Int32 - literally just the number 1, always (unknown)
///
/// Array of some kind:
/// Int32 - count of items in array
///
/// Each item:
/// 32-bit string pointer, some kind of texture page identifier?
/// 32-bit pointer to something relating to a texture page entry?
/// </code>
/// <see href="https://github.com/UnderminersTeam/GameMakerModTool/issues/4#issuecomment-421844420"/>.</remarks>
public class GameMakerEmbeddedImage : GameMakerNamedResource, IStaticChildObjectsSize, IDisposable
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 8;

    /// <summary>
    /// The name of the <see cref="GameMakerEmbeddedImage"/>.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The <see cref="GameMakerTexturePageItem"/> of this <see cref="GameMakerEmbeddedImage"/>.
    /// </summary>
    public GameMakerTexturePageItem? TextureEntry { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.WriteGameMakerObjectPointer(TextureEntry);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        TextureEntry = reader.ReadGameMakerObjectPointer<GameMakerTexturePageItem>();
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
        TextureEntry = null;
    }
}