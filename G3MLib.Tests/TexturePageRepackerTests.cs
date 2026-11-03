using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Tests;

public sealed class TexturePageRepackerTests
{
    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    private static GameMakerData RoundTrip(GameMakerData data)
    {
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        return GameMakerIO.Read(stream);
    }

    private static byte[] Pixels(TextureWorker worker, GameMakerTexturePageItem item)
    {
        using var image = worker.GetTextureFor(item, "frame");
        return image.ToByteArray(MagickFormat.Rgba);
    }

    private static List<byte[]> Snapshot(GameMakerData data)
    {
        using TextureWorker worker = new();
        return [.. data.TexturePageItems.Select(item => Pixels(worker, item))];
    }

    private static MagickImage Solid(int width, int height, MagickColor color) => new(color, (uint)width, (uint)height);

    private static long PageArea(GameMakerData data) => data.EmbeddedTextures.Sum(t => (long)t.TextureData.Width * t.TextureData.Height);

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void RepackKeepsEveryFrameAndNeverGrowsThePages(string fixture)
    {
        using var data = Load(fixture);
        var before = Snapshot(data);
        var areaBefore = PageArea(data);

        new TexturePageRepacker(data).Repack();

        Assert.Equal(before, Snapshot(data));
        Assert.True(PageArea(data) <= areaBefore, $"{fixture}: {areaBefore} -> {PageArea(data)}");
        using var reloaded = RoundTrip(data);
        Assert.Equal(before, Snapshot(reloaded));
    }

    [Fact]
    public void ReplaceChangesOnlyTheReplacedFrame()
    {
        using var data = Load("data-2026.win");
        var before = Snapshot(data);
        var index = data.TexturePageItems.ToList().FindIndex(item => item.SourceWidth > 4 && item.SourceHeight > 4 && item.TexturePage != null);
        var target = data.TexturePageItems[index];
        using var replacement = Solid(target.SourceWidth + 3, target.SourceHeight + 5, new MagickColor(255, 0, 0, 255));

        TexturePageRepacker repacker = new(data);
        repacker.Replace(target, replacement);
        repacker.Repack();

        using var reloaded = RoundTrip(data);
        using TextureWorker worker = new();
        var reloadedTarget = reloaded.TexturePageItems[index];
        using var shown = worker.GetTextureFor(reloadedTarget, "frame");
        Assert.Equal(replacement.Width, shown.Width);
        Assert.Equal(replacement.Height, shown.Height);
        Assert.Equal(replacement.ToByteArray(MagickFormat.Rgba), shown.ToByteArray(MagickFormat.Rgba));

        var after = Snapshot(reloaded);
        for (var i = 0; i < before.Count; i++)
        {
            if (i != index)
                Assert.Equal(before[i], after[i]);
        }
    }

    [Fact]
    public void RepackOfConfiguredDataFilesKeepsEveryFrame()
    {
        var configured = Environment.GetEnvironmentVariable("G3MLIB_REPACK_DATA_FILES");
        if (string.IsNullOrWhiteSpace(configured))
            return;

        foreach (var path in configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var input = File.OpenRead(path);
            using var data = GameMakerIO.Read(input);
            var before = Hashes(data);
            var areaBefore = PageArea(data);
            TexturePageRepacker repacker = new(data);
            using var extra = Solid(40, 30, new MagickColor(1, 2, 3, 255));
            repacker.Add(extra);
            repacker.Repack();

            using var reloaded = RoundTrip(data);
            var after = Hashes(reloaded);
            Assert.Equal(before, [.. Hashes(data).Take(before.Count)]);
            Assert.Equal(before.Count + 1, after.Count);
            // The in-memory round trip has no game folder, so external pages cannot reload there.
            int[] inFile = [.. Enumerable.Range(0, before.Count).Where(i => !data.TexturePageItems[i].TexturePage.TextureExternal)];
            Assert.NotEmpty(inFile);
            Assert.All(inFile, i => Assert.Equal(before[i], after[i]));
            Assert.True(PageArea(reloaded) <= areaBefore + 4096L * 4096, $"{path}: {areaBefore} -> {PageArea(reloaded)}");
        }
    }

    private static List<string> Hashes(GameMakerData data)
    {
        using TextureWorker worker = new();
        return [.. data.TexturePageItems.Select(item => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pixels(worker, item))))];
    }

    [Fact]
    public void ManyAddedFramesPackIntoFewPagesWithTheGivenPixels()
    {
        using var data = Load("data-2026.win");
        var count = data.TexturePageItems.Count;
        TexturePageRepacker repacker = new(data);
        List<MagickImage> images = [.. Enumerable.Range(0, 60).Select(i => Solid(9 + i % 5, 6 + i % 3, new MagickColor((byte)(i * 4), (byte)i, 0, 255)))];
        foreach (var image in images)
            repacker.Add(image);
        repacker.Repack();

        using var reloaded = RoundTrip(data);
        using TextureWorker worker = new();
        for (var i = 0; i < images.Count; i++)
            Assert.Equal(images[i].ToByteArray(MagickFormat.Rgba), Pixels(worker, reloaded.TexturePageItems[count + i]));
        Assert.True(PageArea(reloaded) <= 4 * 256 * 256);
    }

    [Fact]
    public void AddAppendsAnItemWithTheGivenPixels()
    {
        using var data = Load("data-2026.win");
        var count = data.TexturePageItems.Count;
        using var image = Solid(13, 7, new MagickColor(0, 255, 0, 255));

        TexturePageRepacker repacker = new(data);
        var added = repacker.Add(image);
        repacker.Repack();

        Assert.Equal(count + 1, data.TexturePageItems.Count);
        Assert.Same(added, data.TexturePageItems[count]);
        using var reloaded = RoundTrip(data);
        using TextureWorker worker = new();
        using var shown = worker.GetTextureFor(reloaded.TexturePageItems[count], "frame");
        Assert.Equal(image.ToByteArray(MagickFormat.Rgba), shown.ToByteArray(MagickFormat.Rgba));
        Assert.Equal(13, reloaded.TexturePageItems[count].BoundingWidth);
        Assert.Equal(7, reloaded.TexturePageItems[count].BoundingHeight);
    }

    [Fact]
    public void ItemsSharingARectangleStillShareItAfterRepack()
    {
        using var data = Load("data-2026.win");
        var shared = data.TexturePageItems
            .GroupBy(i => (i.TexturePage, i.SourceX, i.SourceY, i.SourceWidth, i.SourceHeight))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .FirstOrDefault();
        if (shared is null)
            return;

        new TexturePageRepacker(data).Repack();

        Assert.All(shared, item => Assert.Equal((shared[0].TexturePage, shared[0].SourceX, shared[0].SourceY), (item.TexturePage, item.SourceX, item.SourceY)));
    }

    [Fact]
    public void PlanChangesNothingAndApplyEqualsRepack()
    {
        using var planned = Load("data-2026.win");
        using var direct = Load("data-2026.win");
        using var frame = Solid(11, 5, new MagickColor(9, 8, 7, 255));
        var before = Snapshot(planned);

        TexturePageRepacker repacker = new(planned);
        repacker.Add(frame);
        var plan = repacker.Plan(new RepackOptions(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(before, Snapshot(planned));
        Assert.Equal(before.Count, planned.TexturePageItems.Count);
        Assert.Equal(1, plan.GroupCount);
        repacker.Apply(plan);

        TexturePageRepacker other = new(direct);
        other.Add(frame);
        other.Repack();
        Assert.Equal(Hashes(direct), Hashes(planned));
    }

    [Fact]
    public void PlanReportsPerPageDimensionsFillRatioAndItemCountsBeforeApply()
    {
        using var data = Load("data-2026.win");
        var expectedGroup = data.EmbeddedTextures.First(t => !t.TextureExternal).TextureInfo;
        var existingItems = data.TexturePageItems.Count(i => i.TexturePage is { TextureExternal: false } && ReferenceEquals(i.TexturePage.TextureInfo, expectedGroup));
        using var first = Solid(20, 16, new MagickColor(10, 20, 30, 255));
        using var second = Solid(24, 18, new MagickColor(40, 50, 60, 255));

        TexturePageRepacker repacker = new(data);
        var isolatedItem = repacker.Add(first);
        repacker.Add(second);
        var plan = repacker.Plan(new RepackOptions(KeepAlone: item => ReferenceEquals(item, isolatedItem)), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(plan.PageCount.After, plan.Pages.Count);
        Assert.True(plan.Pages.Count >= 2);
        Assert.Equal(Enumerable.Range(0, plan.Pages.Count), plan.Pages.Select(p => p.PageIndex));
        Assert.Equal(existingItems + 2, plan.Pages.Sum(p => p.ItemCount));
        Assert.All(plan.Pages, page =>
        {
            Assert.Same(expectedGroup, page.Group);
            Assert.True(page.Width >= 16);
            Assert.True(page.Height >= 16);
            Assert.InRange(page.FillRatio, 0.0001, 1.0);
            Assert.True(page.ItemCount >= 1);
        });
        var isolatedPage = Assert.Single(plan.Pages, p => p.ItemCount == 1);
        Assert.Equal((double)(20 * 16) / (isolatedPage.Width * isolatedPage.Height), isolatedPage.FillRatio, 6);

        repacker.Apply(plan);
        var committedPages = data.EmbeddedTextures.Where(t => !t.TextureExternal && ReferenceEquals(t.TextureInfo, expectedGroup)).ToList();
        Assert.Equal(plan.Pages.Count, committedPages.Count);
        for (var i = 0; i < plan.Pages.Count; i++)
        {
            var plannedPage = plan.Pages[i];
            var committedPage = committedPages[i];
            Assert.Equal(plannedPage.PageIndex, committedPage.IndexInGroup);
            Assert.Equal((plannedPage.Width, plannedPage.Height), (committedPage.TextureData.Width, committedPage.TextureData.Height));
            Assert.Equal(plannedPage.ItemCount, data.TexturePageItems.Count(item => ReferenceEquals(item.TexturePage, committedPage)));
        }
    }

    [Fact]
    public void PlanHonoursCancellation()
    {
        using var data = Load("data-2026.win");
        using CancellationTokenSource source = new();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new TexturePageRepacker(data).Plan(new RepackOptions(), null, source.Token));
    }

    [Fact]
    public void PreparedFrameAddsTheGivenPixels()
    {
        using var data = Load("data-2026.win");
        var count = data.TexturePageItems.Count;
        byte[] bgra = [.. Enumerable.Range(0, 6 * 4 * 4).Select(i => (byte)(i * 7))];
        for (var i = 3; i < bgra.Length; i += 4) bgra[i] = 255;

        TexturePageRepacker repacker = new(data);
        var item = repacker.Add(new PreparedFrame(6, 4, 2, 3, bgra));
        repacker.Repack();

        using var reloaded = RoundTrip(data);
        using TextureWorker worker = new();
        var shown = reloaded.TexturePageItems[count];
        Assert.Equal((2, 3, 8, 7), (shown.TargetX, shown.TargetY, shown.BoundingWidth, shown.BoundingHeight));
        Assert.Equal(bgra, worker.CutBgra(shown, false, out _, out _).ToArray());
        Assert.Same(item, data.TexturePageItems[count]);
    }

    [Fact]
    public void FramesLargerThanTheSizeFieldsAreRejected()
    {
        using var data = Load("data-2026.win");
        using var wide = Solid(70000, 1, new MagickColor(1, 1, 1, 255));
        Assert.Throws<OverflowException>(() => new TexturePageRepacker(data).Add(wide));
    }

    [Fact]
    public void PagePlanItemCountIncludesAllSharedOwnersMatchingCommittedItemCount()
    {
        using var data = Load("data-2026.win");
        var existingItem = data.TexturePageItems.First(i => i.TexturePage is { TextureExternal: false });
        var duplicateItem = new GameMakerTexturePageItem
        {
            Name = data.Strings.MakeString("duplicate_item"),
            TexturePage = existingItem.TexturePage,
            SourceX = existingItem.SourceX,
            SourceY = existingItem.SourceY,
            SourceWidth = existingItem.SourceWidth,
            SourceHeight = existingItem.SourceHeight,
            TargetX = existingItem.TargetX,
            TargetY = existingItem.TargetY,
            TargetWidth = existingItem.TargetWidth,
            TargetHeight = existingItem.TargetHeight,
            BoundingWidth = existingItem.BoundingWidth,
            BoundingHeight = existingItem.BoundingHeight
        };
        data.TexturePageItems.Add(duplicateItem);

        TexturePageRepacker repacker = new(data);
        var plan = repacker.Plan(new RepackOptions(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(plan);

        repacker.Apply(plan);
        var committedPages = data.EmbeddedTextures.Where(t => !t.TextureExternal).ToList();
        for (var i = 0; i < plan.Pages.Count; i++)
        {
            var plannedPage = plan.Pages[i];
            var committedPage = committedPages.FirstOrDefault(p => p.IndexInGroup == plannedPage.PageIndex && ReferenceEquals(p.TextureInfo, plannedPage.Group));
            if (committedPage != null)
            {
                Assert.Equal(plannedPage.ItemCount, data.TexturePageItems.Count(item => ReferenceEquals(item.TexturePage, committedPage)));
            }
        }
    }

    [Fact]
    public void PageEditorReplacesOnlyTheBlittedFrame()
    {
        using var data = Load("data-2026.win");
        var before = Snapshot(data);
        var index = data.TexturePageItems.ToList().FindIndex(item => item.SourceWidth > 2 && item.SourceHeight > 2 && item.TexturePage != null && !item.TexturePage.TextureExternal);
        var target = data.TexturePageItems[index];
        int w = target.SourceWidth, h = target.SourceHeight;
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < bgra.Length; i += 4) { bgra[i] = 10; bgra[i + 1] = 20; bgra[i + 2] = 30; bgra[i + 3] = 255; }

        var editor = PageEditor.Open(target.TexturePage);
        Assert.Throws<ArgumentException>(() => editor.Blit(target, bgra, w + 1, h));
        editor.Blit(target, bgra, w, h);
        editor.Commit();

        using var reloaded = RoundTrip(data);
        using TextureWorker worker = new();
        Assert.Equal(bgra, worker.CutBgra(reloaded.TexturePageItems[index], false, out _, out _).ToArray());
        var after = Snapshot(reloaded);
        for (var i = 0; i < before.Count; i++)
        {
            var samePage = data.TexturePageItems[i].TexturePage == target.TexturePage;
            if (i != index && !samePage)
                Assert.Equal(before[i], after[i]);
        }
    }
}
