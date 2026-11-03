using System.IO;
using ImageMagick;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Util;

/// <summary>
/// Helper class used to pack images into textures for a single texture group.
/// </summary>
public sealed class TextureGroupPacker
{
    /// <summary>
    /// Maximum allowed texture page size (in terms of width and height) to generate.
    /// </summary>
    public int MaxTextureSize { get; }

    /// <summary>
    /// The amount of extra border pixels each texture entry should be extended by.
    /// </summary>
    public int Border { get; }

    /// <summary>
    /// Whether finished pages are cut to power of two sizes. When false they are cut to the used size.
    /// </summary>
    public bool PowerOfTwoPages { get; }

    /// <summary>
    /// Whether the texture group being packed allows cropping to occur.
    /// </summary>
    public bool AllowCrop { get; }

    /// <summary>
    /// Image format to be used by generated textures.
    /// </summary>
    public GMImage.ImageFormat Format { get; }

    // Next unique ID used for pack images (for determinism when sorting)
    private int _nextPackImageId;

    // List of pages generated for the texture group
    private List<GameMakerEmbeddedTexture>? _pages;

    // List of texture items generated for the texture group
    private readonly List<GameMakerTexturePageItem> _textureItems = new(128);

    // Lookup of images that are similar to each other (based on a very rough 64-bit hash)
    private readonly Dictionary<long, List<PackImage>> _similarPackImages = new(128);

    // List of images to be packed
    private readonly List<PackImage> _images = new(128);

    // List of duplicate images that don't need to be packed
    private readonly List<PackImageDuplicate> _duplicateImages = new(64);

    /// <summary>
    /// Initializes a new texture group packer with the provided texture group settings.
    /// </summary>
    public TextureGroupPacker(int maxTextureSize = 2048, int border = 2, bool allowCrop = true, GMImage.ImageFormat format = GMImage.ImageFormat.Png, bool powerOfTwoPages = true)
    {
        // Make sure max texture size is valid
        if (maxTextureSize is < 16 or > GMImage.MaxImageDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTextureSize));
        }
        if (powerOfTwoPages && (maxTextureSize & (maxTextureSize - 1)) != 0)
        {
            throw new ArgumentException("Maximum texture size is not a power of two", nameof(maxTextureSize));
        }

        // Make sure border is valid
        if (border < 0 || border >= maxTextureSize / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(border));
        }

        MaxTextureSize = maxTextureSize;
        Border = border;
        AllowCrop = allowCrop;
        Format = format;
        PowerOfTwoPages = powerOfTwoPages;
    }

    /// <summary>
    /// Adds an image to be packed by the texture group packer, and returns a texture page item for it.
    /// </summary>
    public GameMakerTexturePageItem AddImage(MagickImage image, BorderFlags borderFlags, bool allowCrop = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        // Make sure this is used *before* packing only...
        if (_pages is not null)
        {
            throw new InvalidOperationException("Cannot add images after packing");
        }

        // Ensure image is in a valid format
        int uncroppedWidth = (int)image.Width, uncroppedHeight = (int)image.Height;
        image.Format = MagickFormat.Rgba;
        image.SetBitDepth(8);
        image.SetCompression(CompressionMethod.NoCompression);
        image.Alpha(AlphaOption.Set);

        // Crop the image, if allowed
        int minX = 0, minY = 0;
        if (allowCrop && AllowCrop)
        {
            // Scan image for min/max X/Y pixels with nonzero alpha
            minX = uncroppedWidth;
            minY = uncroppedHeight;
            int maxX = 0, maxY = 0;
            var pixels = image.GetPixels().GetReadOnlyArea(0, 0, image.Width, image.Height);
            for (var y = 0; y < uncroppedHeight; y++)
            {
                for (var x = 0; x < uncroppedWidth; x++)
                {
                    if (pixels[4 * (x + y * uncroppedWidth) + 3] != 0)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            if (minX > maxX || minY > maxY)
            {
                // Image is literally just empty
                image.Dispose();
                image = new(MagickColors.Transparent, 1, 1)
                {
                    Format = MagickFormat.Rgba
                };
                image.SetBitDepth(8);
                image.SetCompression(CompressionMethod.NoCompression);
                image.Alpha(AlphaOption.Set);
            }
            else
            {
                // Crop image
                image.Crop(new MagickGeometry(minX, minY, (uint)(maxX - minX + 1), (uint)(maxY - minY + 1)));
            }
        }

        // Ensure image can actually fix on page
        if (image.Width + Border * 2 > MaxTextureSize || image.Height + Border * 2 > MaxTextureSize)
        {
            throw new ArgumentException($"Cannot fit texture item of size {image.Width}x{image.Height} with max texture size {MaxTextureSize}, border {Border}", nameof(image));
        }

        // Create pack image and associated texture page item
        GameMakerTexturePageItem textureItem = new();
        _textureItems.Add(textureItem);
        PackImage packImage = new(image, textureItem, _nextPackImageId++, (minX, minY), (uncroppedWidth, uncroppedHeight), borderFlags);

        // Check for duplicate, and use original if so
        var hash = packImage.ComputeHash();
        if (_similarPackImages.TryGetValue(hash, out var similarImages))
        {
            // Check all similar images (with the same hash)
            foreach (var similarImage in similarImages)
            {
                if (similarImage.IsDuplicateOf(packImage))
                {
                    // Found a duplicate!
                    PackImageDuplicate duplicateImage = new(packImage.TextureItem, similarImage.TextureItem,
                                                            packImage.CroppedOffsetX, packImage.CroppedOffsetY,
                                                            packImage.UncroppedWidth, packImage.UncroppedHeight);
                    packImage.Dispose();
                    _duplicateImages.Add(duplicateImage);
                    return duplicateImage.TextureItem;
                }
            }

            // No duplicates; add another similar image
            similarImages.Add(packImage);
        }
        else
        {
            // No similar images; create new list
            _similarPackImages.Add(hash, [packImage]);
        }

        // Return texture item from this pack image, and add to list
        _images.Add(packImage);
        return packImage.TextureItem;
    }

    /// <summary>
    /// Packs and generates textures for all texture pages for the packer after all images have been added.
    /// </summary>
    public void PackPages()
    {
        // Sort images by area, and then by unique ID
        _images.Sort((a, b) =>
        {
            var area = (int)a.Width * (int)a.Height - (int)b.Width * (int)b.Height;
            if (area != 0)
            {
                return area;
            }
            return b.UniqueID - a.UniqueID;
        });

        // Perform packing algorithm
        _pages = new(4);
        while (_images.Count > 0)
        {
            _pages.Add(PackSinglePage());
        }

        // Resolve all duplicate images
        foreach (var duplicateImage in _duplicateImages)
        {
            var dupTextureItem = duplicateImage.TextureItem;
            var origTextureItem = duplicateImage.OriginalTextureItem;
            dupTextureItem.TexturePage = origTextureItem.TexturePage;
            dupTextureItem.SourceX = origTextureItem.SourceX;
            dupTextureItem.SourceY = origTextureItem.SourceY;
            dupTextureItem.SourceWidth = origTextureItem.SourceWidth;
            dupTextureItem.SourceHeight = origTextureItem.SourceHeight;
            dupTextureItem.TargetX = checked((ushort)duplicateImage.CroppedOffsetX);
            dupTextureItem.TargetY = checked((ushort)duplicateImage.CroppedOffsetY);
            dupTextureItem.TargetWidth = origTextureItem.TargetWidth;
            dupTextureItem.TargetHeight = origTextureItem.TargetHeight;
            dupTextureItem.BoundingWidth = checked((ushort)duplicateImage.UncroppedWidth);
            dupTextureItem.BoundingHeight = checked((ushort)duplicateImage.UncroppedHeight);
        }
    }

    /// <summary>
    /// Imports all generated texture pages and texture items to the supplied game data.
    /// </summary>
    public void ImportToData(GameMakerData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsurePacked();

        // Import textures
        var embeddedTextureCount = data.EmbeddedTextures.Count;
        foreach (var texture in _pages)
        {
            texture.Name = new GameMakerString($"Texture {embeddedTextureCount++}");
            data.EmbeddedTextures.Add(texture);
        }
        // Import texture items
        var textureItemCount = data.TexturePageItems.Count;
        foreach (var textureItem in _textureItems)
        {
            textureItem.Name = new GameMakerString($"PageItem {textureItemCount++}");
            data.TexturePageItems.Add(textureItem);
        }
    }

    /// <summary>
    /// Imports generated pages into an external texture group and writes their texture-group files.
    /// </summary>
    /// <param name="data">Data file receiving the new pages and texture-page items.</param>
    /// <param name="textureGroup">Existing external texture group receiving the new pages.</param>
    /// <param name="gameDirectory">Directory containing the data file and texture-group folders.</param>
    public void ImportToExternalTextureGroup(GameMakerData data, GameMakerTextureGroupInfo textureGroup, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(textureGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        EnsurePacked();

        if (!data.IsVersionAtLeast(2022, 9))
            throw new InvalidOperationException("External texture groups require GameMaker 2022.9 or newer.");
        if (data.TextureGroupInfo is null || !data.TextureGroupInfo.Contains(textureGroup))
            throw new InvalidOperationException("The texture group must belong to the supplied data file.");
        if (textureGroup.LoadType == GameMakerTextureGroupInfo.TextureGroupLoadType.InFile ||
            string.IsNullOrWhiteSpace(textureGroup.Name?.Content) ||
            string.IsNullOrWhiteSpace(textureGroup.Directory?.Content) ||
            string.IsNullOrWhiteSpace(textureGroup.Extension?.Content))
        {
            throw new InvalidOperationException("The texture group must define an external file location.");
        }

        var embeddedTextureCount = data.EmbeddedTextures.Count;
        var indexInGroup = textureGroup.TexturePages
            .Select(reference => reference?.Resource?.IndexInGroup ?? -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        foreach (var texture in _pages)
        {
            texture.Name = new GameMakerString($"Texture {embeddedTextureCount++}");
            texture.TextureInfo = textureGroup;
            texture.TextureExternal = true;
            texture.TextureLoaded = true;
            texture.TextureWidth = texture.TextureData.Width;
            texture.TextureHeight = texture.TextureData.Height;
            texture.IndexInGroup = indexInGroup++;
            data.EmbeddedTextures.Add(texture);
            textureGroup.TexturePages.Add(new(texture));
            texture.SaveExternalTexture(gameDirectory);
        }

        var textureItemCount = data.TexturePageItems.Count;
        foreach (var textureItem in _textureItems)
        {
            textureItem.Name = new GameMakerString($"PageItem {textureItemCount++}");
            data.TexturePageItems.Add(textureItem);
        }
    }

    private void EnsurePacked()
    {
        if (_pages is null)
            throw new InvalidOperationException("Call PackPages before importing textures.");
    }

    /// <summary>
    /// Rectangle used for packing images on a texture.
    /// </summary>
    private readonly record struct TextureRect(int X, int Y, int Width, int Height);

    /// <summary>
    /// Packs and generates a texture for a single texture page, given all remaining images to be packed.
    /// </summary>
    private GameMakerEmbeddedTexture PackSinglePage()
    {
        // Create embedded texture for page
        GameMakerEmbeddedTexture newTex = new();

        // Try to insert images into free rectangles using greedy heuristics (images are sorted by area already)
        int maxWidth = 16, maxHeight = 16;
        List<TextureRect> freeRectangles = new(128)
        {
            new(0, 0, MaxTextureSize, MaxTextureSize)
        };
        List<PackImage> currentPageImages = new(128);
        for (var i = _images.Count - 1; i >= 0; i--)
        {
            // Attempt inserting current image
            var currImage = _images[i];
            if (TryInsertImage(freeRectangles, (int)currImage.Width + Border * 2, (int)currImage.Height + Border * 2, out var x, out var y))
            {
                // Successfully placed - remove image from list, and add to list for current page
                _images.RemoveAt(i);
                currentPageImages.Add(currImage);

                // Update max width/height of this page
                maxWidth = Math.Max(maxWidth, x + (int)currImage.Width + Border * 2);
                maxHeight = Math.Max(maxHeight, y + (int)currImage.Height + Border * 2);

                // Update texture item for image
                var currTextureItem = currImage.TextureItem;
                currTextureItem.TexturePage = newTex;
                currTextureItem.SourceX = checked((ushort)(x + Border));
                currTextureItem.SourceY = checked((ushort)(y + Border));
                currTextureItem.SourceWidth = checked((ushort)currImage.Width);
                currTextureItem.SourceHeight = checked((ushort)currImage.Height);
                currTextureItem.TargetX = checked((ushort)currImage.CroppedOffsetX);
                currTextureItem.TargetY = checked((ushort)currImage.CroppedOffsetY);
                currTextureItem.TargetWidth = checked((ushort)currImage.Width);
                currTextureItem.TargetHeight = checked((ushort)currImage.Height);
                currTextureItem.BoundingWidth = checked((ushort)currImage.UncroppedWidth);
                currTextureItem.BoundingHeight = checked((ushort)currImage.UncroppedHeight);
            }
        }

        // Crop final page
        uint width = (uint)MaxTextureSize, height = (uint)MaxTextureSize;
        if (PowerOfTwoPages)
        {
            while (width / 2 >= maxWidth)
            {
                width /= 2;
            }
            while (height / 2 >= maxHeight)
            {
                height /= 2;
            }
        }
        else
        {
            width = (uint)maxWidth;
            height = (uint)maxHeight;
        }

        // Generate texture image: every frame is copied into one RGBA buffer, and the page image is built from it once
        int pageWidth = (int)width, pageHeight = (int)height;
        long pageBufferSize = (long)pageWidth * pageHeight * 4L;
        if (pageBufferSize > Array.MaxLength)
        {
            throw new InvalidOperationException($"Texture page size {pageWidth}x{pageHeight} requires a buffer of {pageBufferSize} bytes, exceeding the maximum array length.");
        }
        var pagePixels = new byte[(int)pageBufferSize];
        foreach (var image in currentPageImages)
        {
            CopyImage(pagePixels, pageWidth, pageHeight, image);

            image.Dispose();
        }
        using MagickImage textureImage = new(pagePixels, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGBA));
        newTex.TextureData.Image = GMImage.FromMagickImage(textureImage).ConvertToFormat(Format);

        return newTex;
    }

    // Copies the frame and its border into the page buffer.
    private void CopyImage(byte[] pagePixels, int pageWidth, int pageHeight, PackImage image)
    {
        var borderWidth = Border == 0 || !image.BorderFlags.HasFlag(BorderFlags.Enabled) ? 0 : image.BorderFlags.HasFlag(BorderFlags.ExtraBorder) ? Border : Math.Min(Border, 1);
        using var sourcePixels = image.Image.GetPixels();
        var source = sourcePixels.ToByteArray(PixelMapping.RGBA);
        if (source is null)
        {
            var name = image.TextureItem.Name?.Content ?? "unnamed";
            throw new InvalidDataException($"Frame '{name}' has null source pixel data.");
        }

        CopyFrame(pagePixels, pageWidth, pageHeight, source, (int)image.Image.Width, (int)image.Image.Height,
                  image.TextureItem.SourceX, image.TextureItem.SourceY, borderWidth,
                  image.BorderFlags.HasFlag(BorderFlags.TileHorizontally), image.BorderFlags.HasFlag(BorderFlags.TileVertically),
                  image.TextureItem.Name?.Content);
    }

    // Copies RGBA frame pixels and a border around them into an RGBA page buffer. Everything is cut to the page and to the
    // frame data that exists, so frames that touch an edge, or are larger than the room left on the page, never throw.
    internal static void CopyFrame(byte[] pagePixels, int pageWidth, int pageHeight, byte[] source, int width, int height,
                                   int x, int y, int borderWidth, bool wrapX, bool wrapY, string? itemName = null)
    {
        var itemPrefix = string.IsNullOrEmpty(itemName) ? "Frame" : $"Frame '{itemName}'";
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"{itemPrefix} has invalid dimensions {width}x{height}.");
        if (x < 0 || y < 0 || x >= pageWidth || y >= pageHeight)
            throw new InvalidDataException($"{itemPrefix} placement coordinates ({x}, {y}) are outside the page bounds {pageWidth}x{pageHeight}.");
        long requiredSourceSize = (long)width * height * 4L;
        if (source.Length < requiredSourceSize)
            throw new InvalidDataException($"{itemPrefix} source buffer length {source.Length} is shorter than required {requiredSourceSize} bytes.");
        long requiredPageSize = (long)pageWidth * pageHeight * 4L;
        if (pagePixels.Length < requiredPageSize)
            throw new InvalidDataException($"Page buffer length {pagePixels.Length} is shorter than required {requiredPageSize} bytes.");

        var copyWidth = Math.Min(width, pageWidth - x);
        var lastY = Math.Min(y + height + borderWidth, pageHeight);
        for (var pageY = Math.Max(y - borderWidth, 0); pageY < lastY; pageY++)
        {
            var offsetY = pageY - y;
            var sourceY = wrapY ? Mod(offsetY, height) : Math.Clamp(offsetY, 0, height - 1);
            var sourceRow = source.AsSpan(sourceY * width * 4, width * 4);
            var pageRow = pagePixels.AsSpan(pageY * pageWidth * 4, pageWidth * 4);
            sourceRow[..(copyWidth * 4)].CopyTo(pageRow[(x * 4)..]);
            for (var b = 1; b <= borderWidth; b++)
            {
                var left = x - b;
                if (left >= 0)
                {
                    var leftSource = wrapX ? Mod(-b, width) : 0;
                    sourceRow.Slice(leftSource * 4, 4).CopyTo(pageRow[(left * 4)..]);
                }
                var right = x + width - 1 + b;
                if (right < pageWidth)
                {
                    var rightSource = wrapX ? Mod(width - 1 + b, width) : width - 1;
                    sourceRow.Slice(rightSource * 4, 4).CopyTo(pageRow[(right * 4)..]);
                }
            }
        }
    }
    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;

    private static bool TryInsertImage(List<TextureRect> freeRects, int width, int height, out int x, out int y)
    {
        // Find good location to place rectangle, based on how closely the short and long sides of the rectangle fit
        var bestShortSideFit = int.MaxValue;
        var bestLongSideFit = int.MaxValue;
        TextureRect bestRect = default;
        for (var i = 0; i < freeRects.Count; i++)
        {
            var rect = freeRects[i];
            if (rect.Width >= width && rect.Height >= height)
            {
                var leftoverHoriz = rect.Width - width;
                var leftoverVert = rect.Height - height;
                var shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                var longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                if (shortSideFit < bestShortSideFit || shortSideFit == bestShortSideFit && longSideFit < bestLongSideFit)
                {
                    bestRect = rect;
                    bestShortSideFit = shortSideFit;
                    bestLongSideFit = longSideFit;
                }
            }
        }

        // If no best rectangle is found, insertion failed (not enough space)
        if (bestRect == default)
        {
            x = 0;
            y = 0;
            return false;
        }

        // Split all rectangles we intersect with
        var numRectsToProcess = freeRects.Count;
        for (var i = 0; i < numRectsToProcess; i++)
        {
            var currRect = freeRects[i];

            // If not intersecting, ignore
            if (bestRect.X >= currRect.X + currRect.Width || bestRect.Y >= currRect.Y + currRect.Height)
            {
                continue;
            }
            if (bestRect.X + width <= currRect.X || bestRect.Y + height <= currRect.Y)
            {
                continue;
            }

            // Remove old rect
            freeRects.RemoveAt(i);
            numRectsToProcess--;
            i--;

            if (bestRect.X < currRect.X + currRect.Width && bestRect.X + width > currRect.X)
            {
                // New rectangle at the top side of the used rectangle
                if (bestRect.Y > currRect.Y && bestRect.Y < currRect.Y + currRect.Height)
                {
                    freeRects.Add(new(currRect.X, currRect.Y, currRect.Width, bestRect.Y - currRect.Y));
                }

                // New rectangle at the bottom side of the used rectangle
                if (bestRect.Y + height < currRect.Y + currRect.Height)
                {
                    freeRects.Add(new(currRect.X, bestRect.Y + height, currRect.Width, currRect.Y + currRect.Height - (bestRect.Y + height)));
                }
            }

            if (bestRect.Y < currRect.Y + currRect.Height && bestRect.Y + height > currRect.Y)
            {
                // New rectangle at the left side of the used rectangle
                if (bestRect.X > currRect.X && bestRect.X < currRect.X + currRect.Width)
                {
                    freeRects.Add(new(currRect.X, currRect.Y, bestRect.X - currRect.X, currRect.Height));
                }

                // New rectangle at the right side of the used rectangle
                if (bestRect.X + width < currRect.X + currRect.Width)
                {
                    freeRects.Add(new(bestRect.X + width, currRect.Y, currRect.X + currRect.Width - (bestRect.X + width), currRect.Height));
                }
            }
        }

        // Prune rectangles that are wholly contained within another rectangle
        for (var i = 0; i < freeRects.Count; i++)
        {
            for (var j = i + 1; j < freeRects.Count; j++)
            {
                var a = freeRects[i];
                var b = freeRects[j];

                if (a.X >= b.X && a.Y >= b.Y &&
                    a.X + a.Width <= b.X + b.Width &&
                    a.Y + a.Height <= b.Y + b.Height)
                {
                    freeRects.RemoveAt(i);
                    i--;
                    break;
                }

                if (b.X >= a.X && b.Y >= a.Y &&
                    b.X + b.Width <= a.X + a.Width &&
                    b.Y + b.Height <= a.Y + a.Height)
                {
                    freeRects.RemoveAt(j);
                    j--;
                }
            }
        }

        // Return the X/Y coordinates of the best rectangle determined earlier
        x = bestRect.X;
        y = bestRect.Y;
        return true;
    }

    /// <summary>
    /// Flags used for generating borders for a texture page item.
    /// </summary>
    [Flags]
    public enum BorderFlags
    {
        None = 0,

        /// <summary>
        /// Enables border generation for the pack image.
        /// </summary>
        Enabled = 1 << 0,

        /// <summary>
        /// Whether the pack image should tile (wrap around) horizontally when generating its border.
        /// </summary>
        TileHorizontally = 1 << 1,

        /// <summary>
        /// Whether the pack image should tile (wrap around) vertically when generating its border.
        /// </summary>
        TileVertically = 1 << 2,

        /// <summary>
        /// Whether the border of the pack image should be extended by 2 pixels (in older versions) or 1 pixel (in post-GMS2 versions).
        /// </summary>
        ExtraBorder = 1 << 3
    }

    /// <summary>
    /// Represents a single image to be packed as part of a pack/texture page.
    /// </summary>
    private sealed class PackImage(MagickImage croppedImage, GameMakerTexturePageItem textureItem, int uniqueId, (int X, int Y) croppedOffset, (int Width, int Height) uncroppedSize, BorderFlags borderFlags) : IDisposable
    {
        /// <summary>
        /// Source (cropped) image data.
        /// </summary>
        public MagickImage Image { get; } = croppedImage;

        /// <summary>
        /// Texture item to be populated with data from this image, during final packing.
        /// </summary>
        public GameMakerTexturePageItem TextureItem { get; } = textureItem;

        /// <summary>
        /// Width of the cropped image data.
        /// </summary>
        public uint Width { get; } = croppedImage.Width;

        /// <summary>
        /// Height of the cropped image data.
        /// </summary>
        public uint Height { get; } = croppedImage.Height;

        /// <summary>
        /// X offset into the image at which the crop was performed. That is, this is non-negative.
        /// </summary>
        public int CroppedOffsetX { get; } = croppedOffset.X;

        /// <summary>
        /// Y offset into the image at which the crop was performed. That is, this is non-negative.
        /// </summary>
        public int CroppedOffsetY { get; } = croppedOffset.Y;

        /// <summary>
        /// Full uncropped image width.
        /// </summary>
        public int UncroppedWidth { get; } = uncroppedSize.Width;

        /// <summary>
        /// Full uncropped image height.
        /// </summary>
        public int UncroppedHeight { get; } = uncroppedSize.Height;

        /// <summary>
        /// Unique incrementing ID to be used for deterministic sorting.
        /// </summary>
        public int UniqueID { get; } = uniqueId;

        /// <summary>
        /// Flags defining how the border should be generated for the image on a page.
        /// </summary>
        public BorderFlags BorderFlags { get; } = borderFlags;

        /// <summary>
        /// Gets 7 most-significant bits from the color channels of the given pixel.
        /// </summary>
        /// <remarks>
        /// Dedicates 2 bits for MSB of the red channel, 2 bits for MSB of the green channel, 2 bits for MSB of the blue channel, and 1 bit for the MSB of the alpha channel.
        /// </remarks>
        private static long Get7BitsFromPixel(IPixel<byte> pixel)
        {
            var color = pixel.ToColor();
            return (color.R >> 6) | ((color.G >> 6) << 2) | ((color.B >> 6) << 4) | ((color.A >> 7) << 6);
        }

        /// <summary>
        /// Computes a 64-bit integer hash of the pack image, based on width, height, border flags, and some image contents.
        /// </summary>
        /// <remarks>
        /// This is used as a heuristic for de-duplicating images during texture packing.
        /// </remarks>
        public long ComputeHash()
        {
            long value = 0;

            // Use lower 29 bits for width/height (and also use lower bits to store part of the border flags).
            value |= (long)BorderFlags | (((long)Width & 0b1111_1111_1111_11) << 1) | (((long)Height & 0b1111_1111_1111_11) << 15);

            // Images should be at least 1x1... verify that
            uint width = Image.Width, height = Image.Height;
            if (width < 1 || height < 1 || width > int.MaxValue || height > int.MaxValue)
            {
                throw new InvalidOperationException();
            }

            // Use 7 bits from 5 pixels (corners and center) for most of the rest
            using var pixels = Image.GetPixelsUnsafe();
            value |= Get7BitsFromPixel(pixels.GetPixel(0, 0)) << 29;
            value |= Get7BitsFromPixel(pixels.GetPixel((int)width - 1, 0)) << (29 + 7);
            value |= Get7BitsFromPixel(pixels.GetPixel(0, (int)height - 1)) << (29 + 7 * 2);
            value |= Get7BitsFromPixel(pixels.GetPixel((int)width - 1, (int)height - 1)) << (29 + 7 * 3);
            value |= Get7BitsFromPixel(pixels.GetPixel((int)(width >> 1), (int)(height >> 1))) << (29 + 7 * 4);

            return value;
        }

        /// <summary>
        /// Checks whether the pack image is a duplicate of another pack image.
        /// </summary>
        /// <remarks>
        /// This should only be used after <see cref="ComputeHash"/> has been called on both instances, and the results were equal.
        /// </remarks>
        public bool IsDuplicateOf(PackImage other)
        {
            // Simple checks
            uint widthA = Image.Width, heightA = Image.Height;
            uint widthB = other.Image.Width, heightB = other.Image.Height;
            if (widthA != widthB || heightA != heightB)
            {
                return false;
            }
            if (BorderFlags != other.BorderFlags)
            {
                return false;
            }
            if (CroppedOffsetX != other.CroppedOffsetX || CroppedOffsetY != other.CroppedOffsetY)
            {
                return false;
            }

            // Compare all of the image data
            using var collectionA = Image.GetPixels();
            using var collectionB = other.Image.GetPixels();
            var pixelsA = collectionA.GetReadOnlyArea(0, 0, widthA, heightA);
            var pixelsB = collectionB.GetReadOnlyArea(0, 0, widthB, heightB);
            return pixelsA.SequenceEqual(pixelsB);
        }

        // Disposal for the image
        private bool disposedValue;
        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Image.Dispose();
                }
                disposedValue = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents a duplicate of a <see cref="PackImage"/>, with its own unique texture item.
    /// </summary>
    private sealed class PackImageDuplicate(GameMakerTexturePageItem textureItem, GameMakerTexturePageItem originalTextureItem, int croppedOffsetX, int croppedOffsetY, int uncroppedWidth, int uncroppedHeight)
    {
        /// <summary>
        /// Texture item to be populated with data from this image, during final packing.
        /// </summary>
        public GameMakerTexturePageItem TextureItem { get; } = textureItem;

        /// <summary>
        /// This is the first/single (non-duplicate) texture item, from which its data can be used to partially populate <see cref="TextureItem"/>.
        /// </summary>
        public GameMakerTexturePageItem OriginalTextureItem { get; } = originalTextureItem;

        /// <summary>
        /// X offset into the image at which the crop was performed. That is, this is non-negative.
        /// </summary>
        public int CroppedOffsetX { get; } = croppedOffsetX;

        /// <summary>
        /// Y offset into the image at which the crop was performed. That is, this is non-negative.
        /// </summary>
        public int CroppedOffsetY { get; } = croppedOffsetY;

        /// <summary>
        /// Full uncropped image width.
        /// </summary>
        public int UncroppedWidth { get; } = uncroppedWidth;

        /// <summary>
        /// Full uncropped image height.
        /// </summary>
        public int UncroppedHeight { get; } = uncroppedHeight;
    }
}
