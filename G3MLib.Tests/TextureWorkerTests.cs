using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Tests;

public sealed class TextureWorkerTests
{
    private static byte[] Encode(IMagickImage<byte> image)
    {
        using MemoryStream stream = new();
        image.Write(stream, MagickFormat.Png32);
        return stream.ToArray();
    }

    // Reference crop through the cached page image, the way frames were cut before the raw page cache.
    private static byte[] Reference(TextureWorker worker, GameMakerTexturePageItem item)
    {
        using var image = worker.GetEmbeddedTexture(item.TexturePage).CloneArea(item.SourceX, item.SourceY, item.SourceWidth, item.SourceHeight);
        if (item.SourceWidth != item.TargetWidth || item.SourceHeight != item.TargetHeight)
        {
            image.InterpolativeResize(item.TargetWidth, item.TargetHeight, PixelInterpolateMethod.Bilinear);
        }
        image.Strip();
        return Encode(image);
    }

    [Fact]
    public void ItemWithoutATexturePageFailsWithAnImageName()
    {
        using var worker = new TextureWorker();
        var item = new GameMakerTexturePageItem { SourceWidth = 4, SourceHeight = 4, TargetWidth = 4, TargetHeight = 4, BoundingWidth = 4, BoundingHeight = 4 };
        Assert.Contains("spr_lost", Assert.Throws<InvalidDataException>(() => worker.GetTextureFor(item, "spr_lost")).Message);
        Assert.Throws<InvalidDataException>(() => worker.CutBgra(item, false, out _, out _));
        Assert.Throws<InvalidDataException>(() => worker.ExportAsPNG(item, Path.Combine(Path.GetTempPath(), "never.png"), "spr_lost"));
        Assert.Throws<ArgumentNullException>(() => worker.GetEmbeddedTexture(null));
        Assert.Throws<InvalidOperationException>(() => item.ReplaceTexture(new MagickImage(MagickColors.Red, 4, 4)));
    }

    [Fact]
    public void PreparedReplaceChangesNothingUntilCommitAndCommitsOnce()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(input);
        var item = data.Sprites.Where(s => s != null).SelectMany(s => s.Textures).First(t => t?.Texture?.TexturePage != null).Texture;
        var before = item.TexturePage.TextureData.Image;
        var targetBefore = (item.TargetWidth, item.TargetHeight);
        using var replacement = new MagickImage(MagickColors.Red, 3, 5);

        var prepared = item.PrepareReplace(replacement);
        Assert.Same(before, item.TexturePage.TextureData.Image);
        Assert.Equal(targetBefore, (item.TargetWidth, item.TargetHeight));

        prepared.Commit();
        Assert.NotSame(before, item.TexturePage.TextureData.Image);
        Assert.Equal((3, 5), (item.TargetWidth, item.TargetHeight));
        Assert.Throws<InvalidOperationException>(prepared.Commit);
    }

    [Fact]
    public void CommitFailsWhenThePageImageChangedSincePrepare()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(input);
        var item = data.Sprites.Where(s => s != null).SelectMany(s => s.Textures).First(t => t?.Texture?.TexturePage != null).Texture;
        using var first = new MagickImage(MagickColors.Red, 3, 5);
        using var second = new MagickImage(MagickColors.Blue, 3, 5);
        var a = item.PrepareReplace(first);
        var b = item.PrepareReplace(second);

        a.Commit();

        Assert.Throws<InvalidOperationException>(b.Commit);
    }

    [Fact]
    public void CommitFailsWhenTheItemWasRepointedOrClearedSincePrepare()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(input);
        var item = data.Sprites.Where(s => s != null).SelectMany(s => s.Textures).First(t => t?.Texture?.TexturePage != null).Texture;
        var page = item.TexturePage;
        var other = data.EmbeddedTextures.First(t => !ReferenceEquals(t, page));
        using var replacement = new MagickImage(MagickColors.Red, 3, 5);

        var repointed = item.PrepareReplace(replacement);
        item.TexturePage = other;
        Assert.Throws<InvalidOperationException>(repointed.Commit);

        item.TexturePage = page;
        var cleared = item.PrepareReplace(replacement);
        item.TexturePage = null;
        Assert.Throws<InvalidOperationException>(cleared.Commit);
    }

    [Fact]
    public void ExportedPngMatchesDirectEncodeAndRepeatedFramesAreIdentical()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(input);
        var item = data.Sprites.Where(s => s != null).SelectMany(s => s.Textures).First(t => t?.Texture?.TexturePage != null).Texture;
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using TextureWorker worker = new();
            var first = Path.Combine(directory, "a.png");
            var second = Path.Combine(directory, "b.png");
            worker.ExportAsPNG(item, first);
            worker.ExportAsPNG(item, second);
            using var frame = worker.GetTextureFor(item, "frame");
            Assert.Equal(Encode(frame), File.ReadAllBytes(first));
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void FramesMatchTheCachedPageCropWhenCutInParallel(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        using var data = GameMakerIO.Read(input);
        GameMakerTexturePageItem[] items = [.. data.Sprites.Where(s => s != null).SelectMany(s => s.Textures).Where(t => t?.Texture?.TexturePage != null).Select(t => t.Texture)];
        Assert.NotEmpty(items);
        using TextureWorker worker = new();
        Parallel.ForEach(items, item =>
        {
            using var frame = worker.GetTextureFor(item, "frame");
            Assert.Equal(Reference(worker, item), Encode(frame));
        });
    }
}
