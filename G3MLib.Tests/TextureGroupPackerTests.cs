using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Tests;

public sealed class TextureGroupPackerTests
{
    // Every pixel gets its own color, so a wrongly placed pixel is always seen.
    private static byte[] Frame(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = (byte)(i + 1);
            rgba[i * 4 + 1] = (byte)width;
            rgba[i * 4 + 2] = (byte)(height);
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    private static uint Pixel(byte[] rgba, int width, int x, int y) => BitConverter.ToUInt32(rgba, (y * width + x) * 4);

    [Fact]
    public void FrameAtTheOriginWithABorderDoesNotThrow()
    {
        var page = new byte[16 * 16 * 4];
        var frame = Frame(4, 4);

        TextureGroupPacker.CopyFrame(page, 16, 16, frame, 4, 4, 0, 0, 2, false, false);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
                Assert.Equal(Pixel(frame, 4, x, y), Pixel(page, 16, x, y));
        }
        // The border right and below the frame repeats its edge, the part left of and above the page is dropped.
        Assert.Equal(Pixel(frame, 4, 3, 0), Pixel(page, 16, 5, 0));
        Assert.Equal(Pixel(frame, 4, 0, 3), Pixel(page, 16, 0, 5));
        Assert.Equal(Pixel(frame, 4, 3, 3), Pixel(page, 16, 5, 5));
        Assert.Equal(0u, Pixel(page, 16, 6, 0));
    }

    [Fact]
    public void FrameAtTheFarEdgeWithABorderDoesNotThrow()
    {
        var page = new byte[16 * 16 * 4];
        var frame = Frame(4, 4);

        TextureGroupPacker.CopyFrame(page, 16, 16, frame, 4, 4, 12, 12, 2, false, false);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
                Assert.Equal(Pixel(frame, 4, x, y), Pixel(page, 16, 12 + x, 12 + y));
        }
        Assert.Equal(Pixel(frame, 4, 0, 0), Pixel(page, 16, 10, 10));
        Assert.Equal(Pixel(frame, 4, 0, 3), Pixel(page, 16, 10, 15));
        Assert.Equal(Pixel(frame, 4, 3, 0), Pixel(page, 16, 15, 10));
        Assert.Equal(0u, Pixel(page, 16, 9, 9));
    }

    [Fact]
    public void FrameTouchingTheRightAndBottomEdgeIsKeptWhole()
    {
        var page = new byte[16 * 16 * 4];
        var frame = Frame(6, 5);

        TextureGroupPacker.CopyFrame(page, 16, 16, frame, 6, 5, 10, 11, 1, true, true);

        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 6; x++)
                Assert.Equal(Pixel(frame, 6, x, y), Pixel(page, 16, 10 + x, 11 + y));
        }
        // Tiling wraps to the opposite side of the frame.
        Assert.Equal(Pixel(frame, 6, 5, 4), Pixel(page, 16, 9, 10));
        Assert.Equal(Pixel(frame, 6, 0, 4), Pixel(page, 16, 10, 10));
        Assert.Equal(Pixel(frame, 6, 5, 0), Pixel(page, 16, 9, 11));
    }

    [Fact]
    public void FrameLargerThanTheRoomOnThePageIsCutToThePage()
    {
        var page = new byte[16 * 16 * 4];
        var frame = Frame(8, 8);

        TextureGroupPacker.CopyFrame(page, 16, 16, frame, 8, 8, 12, 13, 2, false, false);

        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 4; x++)
                Assert.Equal(Pixel(frame, 8, x, y), Pixel(page, 16, 12 + x, 13 + y));
        }
        Assert.Equal(Pixel(frame, 8, 0, 0), Pixel(page, 16, 11, 13));
    }

    [Fact]
    public void CopyFrameThrowsInvalidDataExceptionForInvalidFrames()
    {
        var page = new byte[16 * 16 * 4];

        var ex1 = Assert.Throws<InvalidDataException>(() =>
            TextureGroupPacker.CopyFrame(page, 16, 16, new byte[12], 4, 4, 2, 2, 2, false, false, "item_short"));
        Assert.Contains("item_short", ex1.Message, StringComparison.Ordinal);
        Assert.Contains("shorter than required", ex1.Message, StringComparison.Ordinal);

        var ex2 = Assert.Throws<InvalidDataException>(() =>
            TextureGroupPacker.CopyFrame(page, 16, 16, Frame(4, 4), 4, 4, 40, 2, 2, false, false, "item_x_out"));
        Assert.Contains("item_x_out", ex2.Message, StringComparison.Ordinal);
        Assert.Contains("outside the page bounds", ex2.Message, StringComparison.Ordinal);

        var ex3 = Assert.Throws<InvalidDataException>(() =>
            TextureGroupPacker.CopyFrame(page, 16, 16, Frame(4, 4), 4, 4, 2, 40, 2, false, false, "item_y_out"));
        Assert.Contains("item_y_out", ex3.Message, StringComparison.Ordinal);
        Assert.Contains("outside the page bounds", ex3.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            TextureGroupPacker.CopyFrame(page, 16, 16, Frame(4, 4), 4, 4, -1, 2, 2, false, false));

        Assert.Throws<InvalidDataException>(() =>
            TextureGroupPacker.CopyFrame(page, 16, 16, Frame(4, 4), 0, 4, 2, 2, 2, false, false));
    }

    [Fact]
    public void BorderPixelsRepeatTheEdgeOfTheFrame()
    {
        var page = new byte[16 * 16 * 4];
        var frame = Frame(4, 3);

        TextureGroupPacker.CopyFrame(page, 16, 16, frame, 4, 3, 5, 5, 2, false, false);

        Assert.Equal(Pixel(frame, 4, 0, 0), Pixel(page, 16, 3, 3));
        Assert.Equal(Pixel(frame, 4, 0, 1), Pixel(page, 16, 4, 6));
        Assert.Equal(Pixel(frame, 4, 3, 2), Pixel(page, 16, 10, 9));
        Assert.Equal(Pixel(frame, 4, 2, 0), Pixel(page, 16, 7, 4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PackedFramesFillingAWholePageKeepTheirPixels(int border)
    {
        var size = 16 - border * 2;
        var frame = Frame(size, size);
        using MagickImage image = new(frame, new PixelReadSettings((uint)size, (uint)size, StorageType.Char, PixelMapping.RGBA));
        TextureGroupPacker packer = new(16, border, false, GMImage.ImageFormat.RawBgra, false);
        var item = packer.AddImage(image, TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.ExtraBorder, false);

        packer.PackPages();
        using var data = GameMakerData.CreateNew2026LTS();
        packer.ImportToData(data);

        var texture = data.EmbeddedTextures[^1];
        Assert.Equal((16, 16), (texture.TextureData.Width, texture.TextureData.Height));
        using var page = texture.TextureData.Image.GetMagickImage();
        var rgba = page.ToByteArray(MagickFormat.Rgba);
        Assert.Equal((border, border), (item.SourceX, item.SourceY));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                Assert.Equal(Pixel(frame, size, x, y), Pixel(rgba, 16, border + x, border + y));
        }
    }

    [Fact]
    public void ManyPackedFramesKeepTheirPixelsAndBorders()
    {
        TextureGroupPacker packer = new(256, 2, false, GMImage.ImageFormat.RawBgra, true);
        List<(GameMakerTexturePageItem Item, byte[] Rgba, int Width, int Height)> frames = [];
        for (var i = 0; i < 30; i++)
        {
            int width = 3 + i % 9, height = 2 + i % 7;
            var rgba = Frame(width, height);
            rgba[0] = (byte)(i * 8);
            MagickImage image = new(rgba, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            frames.Add((packer.AddImage(image, TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.ExtraBorder, false), rgba, width, height));
        }

        packer.PackPages();
        using var data = GameMakerData.CreateNew2026LTS();
        packer.ImportToData(data);

        foreach (var (item, rgba, width, height) in frames)
        {
            using var page = item.TexturePage.TextureData.Image.GetMagickImage();
            var pageRgba = page.ToByteArray(MagickFormat.Rgba);
            int pageWidth = (int)page.Width, x = item.SourceX, y = item.SourceY;
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                    Assert.Equal(Pixel(rgba, width, column, row), Pixel(pageRgba, pageWidth, x + column, y + row));
                Assert.Equal(Pixel(rgba, width, 0, row), Pixel(pageRgba, pageWidth, x - 2, y + row));
                Assert.Equal(Pixel(rgba, width, width - 1, row), Pixel(pageRgba, pageWidth, x + width + 1, y + row));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CroppedAndTiledFramesPackCleanly(bool powerOfTwo)
    {
        TextureGroupPacker.BorderFlags[] flags =
        [
            TextureGroupPacker.BorderFlags.Enabled,
            TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.ExtraBorder,
            TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.TileHorizontally,
            TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.TileVertically,
            TextureGroupPacker.BorderFlags.Enabled | TextureGroupPacker.BorderFlags.TileHorizontally | TextureGroupPacker.BorderFlags.TileVertically,
            TextureGroupPacker.BorderFlags.None
        ];

        TextureGroupPacker packer = new(32, 2, true, GMImage.ImageFormat.RawBgra, powerOfTwo);
        List<GameMakerTexturePageItem> items = [];
        for (var i = 0; i < flags.Length; i++)
        {
            var rgba = new byte[8 * 6 * 4];
            for (var y = 1; y < 5; y++)
            {
                for (var x = 2; x < 7; x++)
                {
                    var offset = (y * 8 + x) * 4;
                    rgba[offset] = (byte)(i * 25 + x);
                    rgba[offset + 1] = (byte)(y * 40);
                    rgba[offset + 2] = 180;
                    rgba[offset + 3] = 255;
                }
            }

            MagickImage image = new(rgba, new PixelReadSettings(8, 6, StorageType.Char, PixelMapping.RGBA));
            items.Add(packer.AddImage(image, flags[i], allowCrop: true));
        }

        packer.PackPages();
        using var data = GameMakerData.CreateNew2026LTS();
        packer.ImportToData(data);

        Assert.All(items, item =>
        {
            Assert.NotNull(item.TexturePage);
            Assert.Equal((ushort)2, item.TargetX);
            Assert.Equal((ushort)1, item.TargetY);
            Assert.Equal((ushort)5, item.TargetWidth);
            Assert.Equal((ushort)4, item.TargetHeight);
            Assert.Equal((ushort)8, item.BoundingWidth);
            Assert.Equal((ushort)6, item.BoundingHeight);
        });
    }
}
