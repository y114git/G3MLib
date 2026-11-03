using ImageMagick;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Util;

/// <summary>
/// Adds or replaces frames in a data file and repacks its in-file texture pages compactly.
/// </summary>
/// <remarks>
/// Existing <see cref="GameMakerTexturePageItem"/> objects are kept and moved, so sprites, fonts and backgrounds that
/// reference them stay valid. Items that shared one rectangle still share one. Each texture group is packed on its own,
/// external texture pages are left untouched. Borders around frames are regenerated (one pixel, clamped).
/// All frames of a group are held in memory while packing.
/// </remarks>
public sealed class TexturePageRepacker
{
    private readonly GameMakerData _data;
    private readonly int _border;
    private readonly int? _maxTextureSize;
    private readonly Dictionary<GameMakerTexturePageItem, MagickImage> _images = new(ReferenceEqualityComparer.Instance);
    private readonly List<(GameMakerTexturePageItem Item, GameMakerTextureGroupInfo Group)> _added = [];

    /// <param name="data">Data file to change.</param>
    /// <param name="maxTextureSize">Largest page size, a power of two. Default: the largest existing page, but at least what the biggest frame needs.</param>
    /// <param name="border">Pixels kept free around every frame.</param>
    public TexturePageRepacker(GameMakerData data, int? maxTextureSize = null, int border = 2)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _maxTextureSize = maxTextureSize;
        _border = border;
    }

    /// <summary>
    /// Queues a new item whose image is a whole frame. The item is appended to the data file by <see cref="Repack"/>.
    /// </summary>
    /// <param name="image">Frame pixels.</param>
    /// <param name="group">Target texture group. Default: the group of the first in-file page.</param>
    public GameMakerTexturePageItem Add(MagickImage image, GameMakerTextureGroupInfo? group = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        group ??= _data.EmbeddedTextures.FirstOrDefault(t => !t.TextureExternal)?.TextureInfo;
        GameMakerTexturePageItem item = new();
        SetFrame(item, image);
        _images[item] = image;
        _added.Add((item, group));
        return item;
    }

    /// <summary>
    /// Queues a new item from raw BGRA pixels. Target offset and bounding box come from the frame; the pixels are copied once.
    /// </summary>
    public GameMakerTexturePageItem Add(PreparedFrame frame, GameMakerTextureGroupInfo? group = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Bgra.Length != (long)frame.Width * frame.Height * 4)
            throw new ArgumentException("The pixel data does not match the frame size.", nameof(frame));
        MagickImage image = new(frame.Bgra.Span, new PixelReadSettings((uint)frame.Width, (uint)frame.Height, StorageType.Char, PixelMapping.BGRA));
        var item = Add(image, group);
        item.TargetX = checked((ushort)frame.TargetX);
        item.TargetY = checked((ushort)frame.TargetY);
        item.BoundingWidth = checked((ushort)(frame.TargetX + frame.Width));
        item.BoundingHeight = checked((ushort)(frame.TargetY + frame.Height));
        return item;
    }

    /// <summary>
    /// Queues new pixels for an existing item. The image is the whole frame: target and bounding box become its size.
    /// </summary>
    public void Replace(GameMakerTexturePageItem item, MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(image);
        if (item.TexturePage is null || item.TexturePage.TextureExternal)
            throw new InvalidOperationException("Only items on in-file texture pages can be replaced.");
        SetFrame(item, image);
        _images[item] = image;
    }

    private static void SetFrame(GameMakerTexturePageItem item, MagickImage image)
    {
        item.SourceWidth = item.TargetWidth = item.BoundingWidth = checked((ushort)image.Width);
        item.SourceHeight = item.TargetHeight = item.BoundingHeight = checked((ushort)image.Height);
        item.TargetX = item.TargetY = 0;
    }

    /// <summary>
    /// Packs every in-file texture group again and updates the data file.
    /// </summary>
    public void Repack() => Apply(Plan(new RepackOptions()));

    /// <summary>
    /// Computes the new pages and placements without changing the data file. Groups are packed in parallel.
    /// </summary>
    public RepackPlan Plan(RepackOptions options, IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Rectangles are read before any item moves, so snapshot the owners per page first.
        List<GameMakerTexturePageItem> existing = [.. _data.TexturePageItems.Where(i => i.TexturePage is { TextureExternal: false })];
        List<IGrouping<GameMakerTextureGroupInfo, GameMakerEmbeddedTexture>> groups = [.. _data.EmbeddedTextures
            .Where(t => !t.TextureExternal && (options.Groups is null || options.Groups.Contains(t.TextureInfo)))
            .GroupBy(t => t.TextureInfo, ReferenceEqualityComparer.Instance as IEqualityComparer<GameMakerTextureGroupInfo>)];
        var plans = new GroupPlan[groups.Count];
        var done = 0;
        Parallel.For(0, groups.Count, new ParallelOptions { CancellationToken = cancellationToken }, index =>
        {
            plans[index] = PlanGroup(groups[index].Key, [.. groups[index]], existing, options);
            progress?.Report(new BatchProgress(Interlocked.Increment(ref done), groups.Count, groups[index].Key?.Name?.Content ?? string.Empty));
        });
        return new RepackPlan([.. plans.Where(p => p is not null)]);
    }

    /// <summary>
    /// Writes a plan into the data file. Single threaded, no cancel point; the plan must come from this repacker and the data file must not have changed since.
    /// </summary>
    public void Apply(RepackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var group in plan.Plans)
        {
            foreach ((var owners, var placed) in group.Placements)
            {
                foreach (var owner in owners)
                {
                    owner.TexturePage = placed.TexturePage;
                    owner.SourceX = placed.SourceX;
                    owner.SourceY = placed.SourceY;
                }
            }
            SwapPages(group.Group, group.OldPages, group.NewPages);
        }

        foreach ((var item, _) in _added)
            _data.TexturePageItems.Add(item);
        _added.Clear();
        _images.Clear();
    }

    private GroupPlan PlanGroup(GameMakerTextureGroupInfo group, List<GameMakerEmbeddedTexture> oldPages, List<GameMakerTexturePageItem> existing, RepackOptions options)
    {
        HashSet<GameMakerEmbeddedTexture> pageSet = new(oldPages, ReferenceEqualityComparer.Instance);
        List<GameMakerTexturePageItem> items = [.. existing.Where(i => pageSet.Contains(i.TexturePage))];
        List<GameMakerTexturePageItem> added = [.. _added.Where(a => ReferenceEquals(a.Group, group)).Select(a => a.Item)];
        if (items.Count == 0 && added.Count == 0)
            return null;

        // One packed image per distinct source rectangle; replaced and added items always get their own.
        Dictionary<(GameMakerEmbeddedTexture, int, int, int, int), List<GameMakerTexturePageItem>> rects = [];
        List<(List<GameMakerTexturePageItem> Owners, MagickImage Image)> frames = [];
        using TextureWorker worker = new();
        foreach (var item in items)
        {
            if (_images.TryGetValue(item, out var replacement))
            {
                frames.Add(([item], new MagickImage(replacement)));
                continue;
            }
            var key = (item.TexturePage, (int)item.SourceX, (int)item.SourceY, (int)item.SourceWidth, (int)item.SourceHeight);
            if (!rects.TryGetValue(key, out var owners))
            {
                rects[key] = owners = [];
                frames.Add((owners, CutFrame(worker, item)));
            }
            owners.Add(item);
        }
        foreach (var item in added)
            frames.Add(([item], new MagickImage(_images[item])));

        var border = options.Border ?? _border;
        bool IsAlone((List<GameMakerTexturePageItem> Owners, MagickImage Image) frame) =>
            options.MaxItemSize is { } limit && Math.Max(frame.Image.Width, frame.Image.Height) > limit
            || options.KeepAlone is { } keep && frame.Owners.Any(keep);
        var shared = frames.Where(f => !IsAlone(f)).ToList();
        var largestFrame = shared.Count == 0 ? 0 : shared.Max(f => (int)Math.Max(f.Image.Width, f.Image.Height)) + border * 2;
        var largestPage = oldPages.Count == 0 ? 0 : oldPages.Max(p => Math.Max(p.TextureData.Width, p.TextureData.Height));
        var maxSize = options.MaxPageSize ?? _maxTextureSize ?? Math.Max(NextPowerOfTwo(largestFrame), NextPowerOfTwo(largestPage));
        var format = oldPages.Count == 0 ? GMImage.ImageFormat.Png : oldPages[0].TextureData.Image.Format;

        // Items with different keys are packed apart; an item kept alone is a bucket of one.
        List<TextureGroupPacker> packers = [];
        Dictionary<object, TextureGroupPacker> keyed = [];
        List<(List<GameMakerTexturePageItem> Owners, GameMakerTexturePageItem Placed)> placements = [];
        foreach (var frame in frames)
        {
            TextureGroupPacker packer;
            if (IsAlone(frame))
            {
                var side = (int)Math.Max(frame.Image.Width, frame.Image.Height) + border * 2;
                packer = new(Math.Max(options.PowerOfTwo ? NextPowerOfTwo(side) : side, 16), border, false, format, options.PowerOfTwo);
                packers.Add(packer);
            }
            else
            {
                var key = options.GroupKey?.Invoke(frame.Owners[0]) ?? string.Empty;
                if (!keyed.TryGetValue(key, out packer))
                {
                    keyed[key] = packer = new(Math.Max(maxSize, 16), border, false, format, options.PowerOfTwo);
                    packers.Add(packer);
                }
            }
            placements.Add((frame.Owners, packer.AddImage(frame.Image, TextureGroupPacker.BorderFlags.Enabled, false)));
        }
        foreach (var packer in packers)
            packer.PackPages();

        List<GameMakerEmbeddedTexture> newPages = [.. placements.Select(p => p.Placed.TexturePage).Distinct()];
        var changed = added.Count > 0 || items.Any(_images.ContainsKey);
        if (!changed && newPages.Sum(p => (long)p.TextureData.Width * p.TextureData.Height) >= oldPages.Sum(p => (long)p.TextureData.Width * p.TextureData.Height))
            return null;

        List<RepackPagePlan> pagePlans = new(newPages.Count);
        for (var i = 0; i < newPages.Count; i++)
        {
            var page = newPages[i];
            int width = page.TextureData.Width, height = page.TextureData.Height;
            var onPage = placements.Where(p => ReferenceEquals(p.Placed.TexturePage, page)).ToList();
            var usedArea = onPage
                .Select(p => ((int)p.Placed.SourceX, (int)p.Placed.SourceY, (int)p.Placed.SourceWidth, (int)p.Placed.SourceHeight))
                .Distinct()
                .Sum(r => (long)r.Item3 * r.Item4);
            var pageArea = (long)width * height;
            var fillRatio = pageArea > 0 ? Math.Clamp((double)usedArea / pageArea, 0.0, 1.0) : 0.0;
            var itemCount = onPage.Sum(p => p.Owners.Count);
            pagePlans.Add(new RepackPagePlan(group, i, width, height, fillRatio, itemCount));
        }

        return new GroupPlan(group, oldPages, newPages, placements, pagePlans);
    }

    internal sealed record GroupPlan(GameMakerTextureGroupInfo Group, List<GameMakerEmbeddedTexture> OldPages, List<GameMakerEmbeddedTexture> NewPages,
        List<(List<GameMakerTexturePageItem> Owners, GameMakerTexturePageItem Placed)> Placements, List<RepackPagePlan> PagePlans);

    private static MagickImage CutFrame(TextureWorker worker, GameMakerTexturePageItem item)
    {
        var page = worker.GetEmbeddedTexture(item.TexturePage);
        lock (page)
        {
            return new MagickImage(page.CloneArea(item.SourceX, item.SourceY, item.SourceWidth, item.SourceHeight));
        }
    }

    private void SwapPages(GameMakerTextureGroupInfo group, List<GameMakerEmbeddedTexture> oldPages, List<GameMakerEmbeddedTexture> newPages)
    {
        var list = _data.EmbeddedTextures;
        var at = oldPages.Count == 0 ? list.Count : list.IndexOf(oldPages[0]);
        foreach (var old in oldPages)
            list.Remove(old);

        var template = oldPages.FirstOrDefault();
        var number = 0;
        foreach (var page in newPages)
        {
            page.TextureInfo = group;
            page.IndexInGroup = number++;
            page.TextureWidth = page.TextureData.Width;
            page.TextureHeight = page.TextureData.Height;
            if (template is not null)
            {
                page.Scaled = template.Scaled;
                page.GeneratedMips = template.GeneratedMips;
            }
            list.Insert(at++, page);
        }

        if (group?.TexturePages is { } references)
        {
            references.Clear();
            foreach (var page in newPages)
                references.Add(new(page));
        }

        for (var i = 0; i < list.Count; i++)
            list[i].Name = new GameMakerString($"Texture {i}");
    }

    private static int NextPowerOfTwo(int value) => value <= 1 ? 1 : (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)value);
}

/// <summary>Raw BGRA pixels of one frame, with the offset of the frame inside its bounding box.</summary>
public sealed record PreparedFrame(int Width, int Height, int TargetX, int TargetY, ReadOnlyMemory<byte> Bgra);

/// <summary>Settings of one repack run. Null values use the repacker defaults.</summary>
/// <param name="MaxPageSize">Largest page size, a power of two.</param>
/// <param name="Border">Pixels kept free around every frame.</param>
/// <param name="Groups">Only these texture groups are packed. Null packs all in-file groups.</param>
/// <param name="PowerOfTwo">Pages are cut to power of two sizes. Off: pages are cut to the used size.</param>
/// <param name="MaxItemSize">Items with a side larger than this get a page of their own.</param>
/// <param name="KeepAlone">Items for which this returns true get a page of their own. For items that share a rectangle, one match is enough.</param>
/// <param name="GroupKey">Items with different keys never share a page.</param>
public sealed record RepackOptions(int? MaxPageSize = null, int? Border = null, ISet<GameMakerTextureGroupInfo>? Groups = null, bool PowerOfTwo = true, int? MaxItemSize = null, Func<GameMakerTexturePageItem, bool>? KeepAlone = null, Func<GameMakerTexturePageItem, object>? GroupKey = null);

/// <summary>Planned texture page in a <see cref="RepackPlan"/>.</summary>
/// <param name="Group">Texture group the page belongs to, or <see langword="null"/> when the page has no group info.</param>
/// <param name="PageIndex">Zero-based index of the page within its group.</param>
/// <param name="Width">Page width in pixels.</param>
/// <param name="Height">Page height in pixels.</param>
/// <param name="FillRatio">Fraction of the page area covered by packed frame pixels (excluding borders), from 0 to 1.</param>
/// <param name="ItemCount">Number of texture page items placed on the page.</param>
public sealed record RepackPagePlan(GameMakerTextureGroupInfo Group, int PageIndex, int Width, int Height, double FillRatio, int ItemCount);

/// <summary>The result of <see cref="TexturePageRepacker.Plan"/>: new pages and placements that <see cref="TexturePageRepacker.Apply"/> writes into the data file.</summary>
public sealed class RepackPlan
{
    internal RepackPlan(List<TexturePageRepacker.GroupPlan> plans)
    {
        Plans = plans;
        Pages = [.. plans.SelectMany(p => p.PagePlans)];
    }

    internal List<TexturePageRepacker.GroupPlan> Plans { get; }

    /// <summary>Number of texture groups that change.</summary>
    public int GroupCount => Plans.Count;

    /// <summary>Pages of the changed groups before and after.</summary>
    public (int Before, int After) PageCount => (Plans.Sum(p => p.OldPages.Count), Plans.Sum(p => p.NewPages.Count));

    /// <summary>Per-page report for the planned texture pages in the changed groups.</summary>
    public IReadOnlyList<RepackPagePlan> Pages { get; }
}
