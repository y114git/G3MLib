using ImageMagick;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Util;

/// <summary>
/// Replaces frame pixels inside an existing texture page. The page is decoded once, edited in memory and encoded once by <see cref="Commit"/> in its own format.
/// Not thread safe.
/// </summary>
public sealed class PageEditor
{
    private readonly GameMakerEmbeddedTexture _page;
    private readonly byte[] _bgra;
    private readonly int _width;
    private readonly int _height;
    private bool _changed;

    private PageEditor(GameMakerEmbeddedTexture page, byte[] bgra, int width, int height)
    {
        _page = page;
        _bgra = bgra;
        _width = width;
        _height = height;
    }

    public static PageEditor Open(GameMakerEmbeddedTexture page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var image = page.TextureData?.Image ?? throw new InvalidOperationException("The page has no image.");
        using var decoded = image.GetMagickImage();
        return new PageEditor(page, decoded.GetPixels().ToByteArray(PixelMapping.BGRA), (int)decoded.Width, (int)decoded.Height);
    }

    /// <summary>Writes BGRA pixels over the item's rectangle. The size must equal the item's source size.</summary>
    public void Blit(GameMakerTexturePageItem item, ReadOnlySpan<byte> bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(item.TexturePage, _page))
            throw new ArgumentException("The item is not on this page.", nameof(item));
        if (width != item.SourceWidth || height != item.SourceHeight || bgra.Length != width * height * 4)
            throw new ArgumentException($"The pixels are {width}x{height} but the item is {item.SourceWidth}x{item.SourceHeight}.", nameof(bgra));
        if (item.SourceX + width > _width || item.SourceY + height > _height)
            throw new ArgumentException("The item lies outside the page.", nameof(item));
        for (var y = 0; y < height; y++)
        {
            bgra.Slice(y * width * 4, width * 4).CopyTo(_bgra.AsSpan(((item.SourceY + y) * _width + item.SourceX) * 4, width * 4));
        }
        _changed = true;
    }

    /// <summary>Encodes the page once and stores it in the page's format. Does nothing when nothing was blitted.</summary>
    public void Commit()
    {
        if (!_changed)
            return;
        var format = _page.TextureData.Image.Format;
        using MagickImage edited = new(_bgra, new PixelReadSettings((uint)_width, (uint)_height, StorageType.Char, PixelMapping.BGRA));
        _page.TextureData.Image = GMImage.FromMagickImage(edited).ConvertToFormat(format);
        _changed = false;
    }
}
