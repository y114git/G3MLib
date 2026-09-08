using ImageMagick;
using System;
using System.ComponentModel;
using System.Drawing;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A texture page item in a data file.
/// </summary>
/// <remarks>The way a texture page item works is: <br/>
/// It renders in a box of size <see cref="BoundingWidth"/> x <see cref="BoundingHeight"/> at some position. <br/>
/// <see cref="TargetX"/>, <see cref="TargetY"/>, <see cref="TargetWidth"/> and <see cref="TargetHeight"/> are relative to the bounding box,
/// anything outside of that is just transparent. <br/>
/// <see cref="SourceX"/>, <see cref="SourceY"/>, <see cref="SourceWidth"/> and <see cref="SourceHeight"/> are part of the texture page which
/// are drawn over <see cref="TargetX"/>, <see cref="TargetY"/>, <see cref="TargetWidth"/>, <see cref="TargetHeight"/>.</remarks>
public class GameMakerTexturePageItem : GameMakerNamedResource, INotifyPropertyChanged, IStaticChildObjectsSize, IDisposable
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 22;

    /// <summary>
    /// A synthetic name assigned by the reader; texture page items do not store names in a data file.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The x coordinate of the item on the texture page.
    /// </summary>
    public ushort SourceX { get; set; }

    /// <summary>
    /// The y coordinate of the item on the texture page.
    /// </summary>
    public ushort SourceY { get; set; }

    /// <summary>
    /// The width of the item on the texture page.
    /// </summary>
    public ushort SourceWidth { get; set; }

    /// <summary>
    /// The height of the item on the texture page.
    /// </summary>
    public ushort SourceHeight { get; set; }

    /// <summary>
    /// The x coordinate of the item in the bounding rectangle.
    /// </summary>
    public ushort TargetX { get; set; }

    /// <summary>
    /// The y coordinate of the item in the bounding rectangle.
    /// </summary>
    public ushort TargetY { get; set; }

    /// <summary>
    /// The width of the item in the bounding rectangle.
    /// </summary>
    public ushort TargetWidth { get; set; }

    /// <summary>
    /// The height of the item in the bounding rectangle.
    /// </summary>
    public ushort TargetHeight { get; set; }

    /// <summary>
    /// The width of the bounding rectangle.
    /// </summary>
    public ushort BoundingWidth { get; set; }

    /// <summary>
    /// The height of the bounding rectangle.
    /// </summary>
    public ushort BoundingHeight { get; set; }

    private GameMakerResourceById<GameMakerEmbeddedTexture, GameMakerChunkTXTR> _texturePage = new GameMakerResourceById<GameMakerEmbeddedTexture, GameMakerChunkTXTR>();

    /// <summary>
    /// The texture page this item is referencing
    /// </summary>
    public GameMakerEmbeddedTexture TexturePage { get => _texturePage.Resource; set { _texturePage.Resource = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TexturePage))); } }

    /// <inheritdoc />
    public event PropertyChangedEventHandler PropertyChanged;

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(SourceX);
        writer.Write(SourceY);
        writer.Write(SourceWidth);
        writer.Write(SourceHeight);
        writer.Write(TargetX);
        writer.Write(TargetY);
        writer.Write(TargetWidth);
        writer.Write(TargetHeight);
        writer.Write(BoundingWidth);
        writer.Write(BoundingHeight);
        writer.Write((short)_texturePage.SerializeById(writer));
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        SourceX = reader.ReadUInt16();
        SourceY = reader.ReadUInt16();
        SourceWidth = reader.ReadUInt16();
        SourceHeight = reader.ReadUInt16();
        TargetX = reader.ReadUInt16();
        TargetY = reader.ReadUInt16();
        TargetWidth = reader.ReadUInt16();
        TargetHeight = reader.ReadUInt16();
        BoundingWidth = reader.ReadUInt16();
        BoundingHeight = reader.ReadUInt16();
        _texturePage = new GameMakerResourceById<GameMakerEmbeddedTexture, GameMakerChunkTXTR>();
        _texturePage.UnserializeById(reader, reader.ReadInt16()); // This one is special as it uses a short instead of int
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return (Name?.Content ?? "PageItem Unknown Index") + " (" + GetType().Name + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _texturePage.Dispose();
        Name = null;
    }

    /// <summary>
    /// Replaces the current image of this texture page item to hold a new image.
    /// </summary>
    /// <param name="replaceImage">The new image that shall be applied to this texture page item.</param>
    public void ReplaceTexture(MagickImage replaceImage)
    {
        // Resize image to bounds on texture page
        using IMagickImage<byte> finalImage = TextureWorker.ResizeImage(replaceImage, SourceWidth, SourceHeight);

        // Apply the image to the texture page
        lock (TexturePage.TextureData)
        {
            using TextureWorker worker = new();
            MagickImage embImage = worker.GetEmbeddedTexture(TexturePage);

            embImage.Composite(finalImage, SourceX, SourceY, CompositeOperator.Copy);

            // Replace original texture with the new version, in the original texture format
            TexturePage.TextureData.Image = GMImage.FromMagickImage(embImage)
                                                   .ConvertToFormat(TexturePage.TextureData.Image.Format);
        }

        TargetWidth = (ushort)replaceImage.Width;
        TargetHeight = (ushort)replaceImage.Height;
    }
}