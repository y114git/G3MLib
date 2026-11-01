using ImageMagick;
using System.Collections.Concurrent;
using System.IO;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Util;

/// <summary>
/// Helper class used to manage and cache textures.
/// </summary>
public class TextureWorker : IDisposable
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".dds", ".qoi"];

    private ConcurrentDictionary<GameMakerEmbeddedTexture, Lazy<MagickImage>> embeddedDictionary = new(ReferenceEqualityComparer.Instance);

    // Decoded BGRA pages for frame cutting; a null page means the Magick crop path is used.
    private readonly ConcurrentDictionary<GameMakerEmbeddedTexture, Lazy<RawPage>> rawPages = new(ReferenceEqualityComparer.Instance);

    private sealed record RawPage(byte[] Bgra, int Width, int Height);

    // Encoded PNGs by frame geometry: frames that share a page rectangle are encoded once.
    private readonly ConcurrentDictionary<FrameKey, Lazy<byte[]>> encodedFrames = new();

    private readonly record struct FrameKey(GameMakerEmbeddedTexture Page, int SourceX, int SourceY, int SourceWidth, int SourceHeight,
        int TargetX, int TargetY, int TargetWidth, int TargetHeight, int BoundingWidth, int BoundingHeight, bool IncludePadding);

    /// <summary>
    /// Retrieves an image representing the supplied texture page.
    /// </summary>
    /// <remarks>
    /// The returned image will be cached for this <see cref="TextureWorker"/> instance.
    /// </remarks>
    /// <param name="embeddedTexture">Texture to get an image from.</param>
    /// <returns><see cref="MagickImage"/> with the contents of the given texture's image.</returns>
    public MagickImage GetEmbeddedTexture(GameMakerEmbeddedTexture embeddedTexture)
    {
        // The lazy value keeps the decode out of any shared lock, so different pages decode in parallel.
        return embeddedDictionary.GetOrAdd(embeddedTexture, static t => new Lazy<MagickImage>(() => t.TextureData.Image.GetMagickImage())).Value;
    }

    private RawPage GetRawPage(GameMakerEmbeddedTexture embeddedTexture)
    {
        return rawPages.GetOrAdd(embeddedTexture, static t => new Lazy<RawPage>(() => DecodeRawPage(t))).Value;
    }

    private static RawPage DecodeRawPage(GameMakerEmbeddedTexture embeddedTexture)
    {
        var source = embeddedTexture.TextureData?.Image;
        if (source is null)
        {
            return null;
        }
        switch (source.Format)
        {
            case GMImage.ImageFormat.RawBgra:
            case GMImage.ImageFormat.Qoi:
            case GMImage.ImageFormat.Bz2Qoi:
                {
                    var raw = source.ConvertToRawBgra();
                    var bytes = raw.GetRawImageData().ToArray();
                    return bytes.Length == (long)raw.Width * raw.Height * 4 ? new RawPage(bytes, raw.Width, raw.Height) : null;
                }
            case GMImage.ImageFormat.Png:
                {
                    using var image = source.GetMagickImage();
                    var bytes = image.ToByteArray();
                    return bytes.Length == (long)image.Width * image.Height * 4 ? new RawPage(bytes, (int)image.Width, (int)image.Height) : null;
                }
            default:
                return null;
        }
    }

    // Cuts a rectangle out of a raw BGRA page without touching shared Magick state, so callers can run in parallel.
    private static MagickImage CutFromRaw(RawPage page, GameMakerTexturePageItem item)
    {
        int width = item.SourceWidth;
        int height = item.SourceHeight;
        var buffer = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(page.Bgra, ((item.SourceY + y) * page.Width + item.SourceX) * 4, buffer, y * width * 4, width * 4);
        }
        return new MagickImage(buffer, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA));
    }

    /// <summary>
    /// Cuts a frame as raw BGRA bytes (width * height * 4) without creating an image. With padding the result has the bounding size and the frame at its target offset.
    /// </summary>
    public ReadOnlyMemory<byte> CutBgra(GameMakerTexturePageItem item, bool padding, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(item);
        var page = GetRawPage(item.TexturePage);
        int boundingWidth = item.BoundingWidth;
        int boundingHeight = item.BoundingHeight;
        var plain = page is not null && item.SourceWidth == item.TargetWidth && item.SourceHeight == item.TargetHeight
            && item.SourceWidth > 0 && item.SourceHeight > 0 && item.SourceX >= 0 && item.SourceY >= 0
            && item.SourceX + item.SourceWidth <= page.Width && item.SourceY + item.SourceHeight <= page.Height;
        var fits = !padding || item.TargetX >= 0 && item.TargetY >= 0 && item.TargetX + item.TargetWidth <= boundingWidth && item.TargetY + item.TargetHeight <= boundingHeight;
        if (!plain || !fits)
        {
            using var image = GetTextureFor(item, "frame", padding);
            width = (int)image.Width;
            height = (int)image.Height;
            return image.GetPixels().ToByteArray(PixelMapping.BGRA);
        }

        width = padding ? boundingWidth : item.SourceWidth;
        height = padding ? boundingHeight : item.SourceHeight;
        var offsetX = padding ? item.TargetX : 0;
        var offsetY = padding ? item.TargetY : 0;
        var buffer = new byte[width * height * 4];
        for (var y = 0; y < item.SourceHeight; y++)
        {
            Buffer.BlockCopy(page.Bgra, ((item.SourceY + y) * page.Width + item.SourceX) * 4, buffer, ((offsetY + y) * width + offsetX) * 4, item.SourceWidth * 4);
        }
        return buffer;
    }

    /// <summary>Frees the decoded page, its cached image and the encoded frames of that page.</summary>
    public void Evict(GameMakerEmbeddedTexture page)
    {
        if (embeddedDictionary is null)
        {
            return;
        }
        if (embeddedDictionary.TryRemove(page, out var image) && image.IsValueCreated)
        {
            image.Value.Dispose();
        }
        rawPages.TryRemove(page, out _);
        foreach (var key in encodedFrames.Keys)
        {
            if (ReferenceEquals(key.Page, page))
            {
                encodedFrames.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Exports the given texture page item to disk, as a PNG, to the supplied path. (With or without padding.)
    /// </summary>
    /// <param name="texPageItem">Texture page item to export.</param>
    /// <param name="filePath">File path to export to.</param>
    /// <param name="imageName">Image name to be used when throwing exceptions, or null to use the filename from the path.</param>
    /// <param name="includePadding">True if padding should be exported; false otherwise.</param>
    public void ExportAsPNG(GameMakerTexturePageItem texPageItem, string filePath, string? imageName = null, bool includePadding = false)
    {
        ArgumentNullException.ThrowIfNull(texPageItem);
        FrameKey key = new(texPageItem.TexturePage, texPageItem.SourceX, texPageItem.SourceY, texPageItem.SourceWidth, texPageItem.SourceHeight,
            texPageItem.TargetX, texPageItem.TargetY, texPageItem.TargetWidth, texPageItem.TargetHeight,
            texPageItem.BoundingWidth, texPageItem.BoundingHeight, includePadding);
        var png = encodedFrames.GetOrAdd(key, _ => new Lazy<byte[]>(() =>
        {
            using var image = GetTextureFor(texPageItem, imageName ?? Path.GetFileNameWithoutExtension(filePath), includePadding);
            using MemoryStream stream = new();
            image.Write(stream, MagickFormat.Png32);
            return stream.ToArray();
        })).Value;
        File.WriteAllBytes(filePath, png);
    }

    /// <summary>
    /// Creates an image representing the sole texture page item supplied, with or without padding.
    /// </summary>
    /// <param name="texPageItem">Texture page item to get the image of.</param>
    /// <param name="imageName">Image name to be used when throwing exceptions.</param>
    /// <param name="includePadding">True if padding should be used in the returned image; false otherwise.</param>
    /// <returns>An image with the contents of the given texture page item's portion of its texture page.</returns>
    public IMagickImage<byte> GetTextureFor(GameMakerTexturePageItem texPageItem, string imageName, bool includePadding = false)
    {
        ArgumentNullException.ThrowIfNull(texPageItem);
        // Ensure texture is no larger than its bounding box
        int exportWidth = texPageItem.BoundingWidth; // sprite.Width
        int exportHeight = texPageItem.BoundingHeight; // sprite.Height
        if (includePadding && (texPageItem.TargetWidth > exportWidth || texPageItem.TargetHeight > exportHeight))
        {
            throw new InvalidDataException($"{imageName}'s texture is larger than its bounding box!");
        }

        // Create an image cropped from the item's part of the texture page
        IMagickImage<byte> image;
        var raw = GetRawPage(texPageItem.TexturePage);
        if (raw is not null && texPageItem.SourceWidth > 0 && texPageItem.SourceHeight > 0
            && texPageItem.SourceX >= 0 && texPageItem.SourceY >= 0
            && texPageItem.SourceX + texPageItem.SourceWidth <= raw.Width && texPageItem.SourceY + texPageItem.SourceHeight <= raw.Height)
        {
            image = CutFromRaw(raw, texPageItem);
        }
        else
        {
            var embeddedImage = GetEmbeddedTexture(texPageItem.TexturePage);
            lock (embeddedImage)
            {
                image = embeddedImage.CloneArea(texPageItem.SourceX, texPageItem.SourceY, texPageItem.SourceWidth, texPageItem.SourceHeight);
            }
        }

        // Resize the image, if necessary
        if (texPageItem.SourceWidth != texPageItem.TargetWidth || texPageItem.SourceHeight != texPageItem.TargetHeight)
        {
            uint resizeWidth = texPageItem.TargetWidth;
            uint resizeHeight = texPageItem.TargetHeight;
            if (image.Width != resizeWidth || image.Height != resizeHeight)
            {
                image.InterpolativeResize(resizeWidth, resizeHeight, PixelInterpolateMethod.Bilinear);
            }
        }

        // Put it in the final holder image, if necessary
        if (includePadding)
        {
            // Based on a benchmark, Extent is faster than creating a new image and using Composite
            image.Compose = CompositeOperator.Copy;
            image.Extent(new MagickGeometry(-texPageItem.TargetX, -texPageItem.TargetY, (uint)exportWidth, (uint)exportHeight), MagickColors.Transparent);
        }

        // Strip the image, removing unnecessary metadata
        image.Strip();

        return image;
    }

    /// <summary>
    /// Reads an image from the given file path (of arbitrary format, as supported by <see cref="MagickImage(string, IMagickReadSettings{byte})"/>).
    /// </summary>
    /// <remarks>
    /// Image color format will always be converted to BGRA, with no compression.
    /// </remarks>
    /// <param name="filePath">File path to read the image from.</param>
    /// <returns>An image, in uncompressed BGRA format, containing the contents of the image file at the given path.</returns>
    public static MagickImage ReadBGRAImageFromFile(string filePath)
    {
        MagickReadSettings settings = new()
        {
            ColorSpace = ColorSpace.sRGB,
        };
        MagickImage image = new(filePath, settings);
        image.Alpha(AlphaOption.Set);
        image.Format = MagickFormat.Bgra;
        image.Depth = 8;
        image.SetCompression(CompressionMethod.NoCompression);
        return image;
    }

    /// <summary>
    /// Finds a common image format exported beside an asset file, preferring the conventional PNG name.
    /// </summary>
    public static string FindImageFile(string directory, string filenameWithoutExtension)
    {
        string? firstImagePath = null;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), filenameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = Path.GetExtension(path);
            if (Array.IndexOf(ImageExtensions, extension) < 0 && Array.IndexOf(ImageExtensions, extension.ToLowerInvariant()) < 0)
                continue;
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                return path;

            firstImagePath ??= path;
        }

        if (firstImagePath is not null)
            return firstImagePath;

        var pngPath = Path.Join(directory, filenameWithoutExtension + ".png");
        throw new FileNotFoundException($"Could not find image file named \"{filenameWithoutExtension}\".", pngPath);
    }

    /// <summary>
    /// Performs a resize of the given image, if required, using the specified interpolation (bilinear by default). Always returns a new image.
    /// </summary>
    /// <param name="image">Image to be resized (without being modified).</param>
    /// <param name="width">Desired width to resize to.</param>
    /// <param name="height">Desired height to resize to.</param>
    /// <param name="interpolateMethod">Pixel interpolation method to use, or specify none to use bilinear interpolation.</param>
    /// <returns>A copy of the provided image, which is resized to the given dimensions when required.</returns>
    public static IMagickImage<byte> ResizeImage(IMagickImage<byte> image, int width, int height, PixelInterpolateMethod interpolateMethod = PixelInterpolateMethod.Bilinear)
    {
        ArgumentNullException.ThrowIfNull(image);
        // Clone image
        var newImage = image.Clone();

        // If the image already has the correct dimensions, skip resizing
        if (image.Width == width && image.Height == height)
        {
            return newImage;
        }

        // Resize using bilinear interpolation
        newImage.InterpolativeResize((uint)width, (uint)height, interpolateMethod);
        return newImage;
    }

    /// <summary>
    /// Reads collision mask data from the given file path, and required width/height.
    /// </summary>
    /// <param name="filePath">Image file to read the mask data from (usually a black-and-white PNG).</param>
    /// <param name="requiredWidth">The width that the collision mask must be (e.g., sprite width or bbox width, depending on version).</param>
    /// <param name="requiredHeight">The height that the collision mask must be (e.g., sprite height or bbox height, depending on version).</param>
    /// <returns>A byte array, encoding the collision mask as a 1-bit-per-pixel image.</returns>
    /// <exception cref="ArgumentException">If the loaded image dimensions do not match the required width/height</exception>
    public static byte[] ReadMaskData(string filePath, int requiredWidth, int requiredHeight)
    {
        using var image = ReadBGRAImageFromFile(filePath);
        if (image.Width != requiredWidth || image.Height != requiredHeight)
        {
            throw new ArgumentException($"{filePath} is not the proper size to be imported! The proper dimensions are width: {requiredWidth} px, height: {requiredHeight} px.", nameof(filePath));
        }
        return MaskBuilder.ReadWhite(image.GetPixels().GetReadOnlyArea(0, 0, image.Width, image.Height), requiredWidth, requiredHeight);
    }

    /// <summary>
    /// Generates and returns a black-and-white image representing a given sprite's collision mask,
    /// and with the given width/height.
    /// </summary>
    /// <param name="mask">Mask entry to generate the image from.</param>
    /// <param name="maskWidth">Width of the image to generate (and to interpret the collision mask with).</param>
    /// <param name="maskHeight">Height of the image to generate (and to interpret the collision mask with).</param>
    /// <returns>A new black-and-white image representing the specified collision mask.</returns>
    public static IMagickImage<byte> GetCollisionMaskImage(GameMakerSprite.MaskEntry mask, int maskWidth, int maskHeight)
    {
        ArgumentNullException.ThrowIfNull(mask);
        // Create image to draw on
        MagickImage image = new(MagickColor.FromRgba(0, 0, 0, 255), (uint)maskWidth, (uint)maskHeight);
        var pixels = image.GetPixels();

        // Get black/white colors to use for drawing
        ReadOnlySpan<byte> black = MagickColors.Black.ToByteArray().AsSpan();
        ReadOnlySpan<byte> white = MagickColors.White.ToByteArray().AsSpan();

        // Draw white pixels if a given bit is set; black pixels otherwise
        var maskData = mask.Data;
        for (var y = 0; y < maskHeight; y++)
        {
            var rowStart = y * ((maskWidth + 7) / 8);
            for (var x = 0; x < maskWidth; x++)
            {
                var temp = maskData[rowStart + x / 8];
                var pixelBit = (temp & (0b1 << (7 - x % 8))) != 0b0;
                pixels.SetPixel(x, y, pixelBit ? white : black);
            }
        }

        image.Strip();

        return image;
    }

    /// <summary>
    /// Exports a collision mask entry from a given sprite's collision mask, as a PNG file, at the specified path, and with the given width/height.
    /// </summary>
    /// <param name="mask">Mask entry to export the image from.</param>
    /// <param name="filePath">File path to export to.</param>
    /// <param name="maskWidth">Width of the image to export (and to interpret the collision mask with).</param>
    /// <param name="maskHeight">Height of the image to export (and to interpret the collision mask with).</param>
    public static void ExportCollisionMaskPNG(GameMakerSprite.MaskEntry mask, string filePath, int maskWidth, int maskHeight)
    {
        using var image = GetCollisionMaskImage(mask, maskWidth, maskHeight);
        SaveImageToFile(image, filePath);
    }

    /// <summary>
    /// Saves the provided image as a PNG file, at the specified path.
    /// </summary>
    /// <param name="image">Image to save.</param>
    /// <param name="filePath">File path to save the image to.</param>
    public static void SaveImageToFile(IMagickImage<byte> image, string filePath)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var stream = new FileStream(filePath, FileMode.Create);
        image.Write(stream, MagickFormat.Png32);
    }

    /// <summary>
    /// Returns the width and height of the image stored at the given path, or -1 width/height if the file fails to parse as an image.
    /// </summary>
    /// <param name="filePath">File path to get the image size from.</param>
    /// <returns>Width and height of the image stored at the file path, or -1 for both values if invalid.</returns>
    public static (int width, int height) GetImageSizeFromFile(string filePath)
    {
        try
        {
            MagickImageInfo info = new(filePath);
            return ((int)info.Width, (int)info.Height);
        }
        catch (Exception)
        {
            return (-1, -1);
        }
    }

    /// <inheritdoc/>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        encodedFrames.Clear();
        if (embeddedDictionary is not null)
        {
            foreach (var img in embeddedDictionary.Values)
            {
                if (img.IsValueCreated)
                {
                    img.Value.Dispose();
                }
            }
            embeddedDictionary.Clear();
            embeddedDictionary = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
