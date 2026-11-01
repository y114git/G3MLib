using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class DataLookupTests
{
    private static GameMakerSprite AddSprite(GameMakerData data, string name)
    {
        GameMakerSprite sprite = new() { Name = data.Strings.MakeString(name) };
        data.Sprites.Add(sprite);
        return sprite;
    }

    [Fact]
    public void FindStringHitAndMiss()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var existing = data.Strings.MakeString("probe");
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        Assert.Same(existing, lookup.FindString("probe"));
        Assert.Null(lookup.FindString("missing"));
    }

    [Fact]
    public void GetOrAddStringAppendsOnceAndKeepsIndex()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        var before = data.Strings.Count;
        var first = lookup.GetOrAddString("fresh", out var index);
        var second = lookup.GetOrAddString("fresh");
        Assert.Same(first, second);
        Assert.Equal(before + 1, data.Strings.Count);
        Assert.Equal(before, index);
        Assert.Same(first, data.Strings[index]);
        Assert.Same(first, lookup.FindString("fresh"));
    }

    [Fact]
    public void TryGetFindsByNameWithIndexAndIsCaseSensitive()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        AddSprite(data, "sprA");
        var b = AddSprite(data, "sprB");
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        Assert.True(lookup.TryGet("sprB", out GameMakerSprite found, out var index));
        Assert.Same(b, found);
        Assert.Equal(data.Sprites.IndexOf(b), index);
        Assert.False(lookup.TryGet("SPRB", out GameMakerSprite _, out var missIndex));
        Assert.Equal(-1, missIndex);
    }

    [Fact]
    public void AddThenTryGet()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        Assert.False(lookup.TryGet("late", out GameMakerSprite _, out var _));
        GameMakerSprite sprite = new() { Name = lookup.GetOrAddString("late") };
        lookup.Add(sprite);
        Assert.True(lookup.TryGet("late", out GameMakerSprite found, out var index));
        Assert.Same(sprite, found);
        Assert.Same(sprite, data.Sprites[index]);
    }

    [Fact]
    public void ListChangesOutsideTheLookupRebuildTheIndex()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var a = AddSprite(data, "sprA");
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        Assert.True(lookup.TryGet("sprA", out GameMakerSprite _, out var _));

        var b = AddSprite(data, "sprB");
        Assert.True(lookup.TryGet("sprB", out GameMakerSprite foundB, out var indexB));
        Assert.Same(b, foundB);
        Assert.Equal(data.Sprites.IndexOf(b), indexB);

        data.Sprites.Remove(a);
        Assert.False(lookup.TryGet("sprA", out GameMakerSprite _, out var _));
        Assert.True(lookup.TryGet("sprB", out GameMakerSprite _, out var shifted));
        Assert.Equal(0, shifted);

        data.Strings.Add(new GameMakerString("outside"));
        Assert.NotNull(lookup.FindString("outside"));
    }

    [Fact]
    public void InPlaceRenameIsRejectedAndRebuilt()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var a = AddSprite(data, "old");
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        Assert.True(lookup.TryGet("old", out GameMakerSprite _, out var _));
        a.Name.Content = "new";
        Assert.False(lookup.TryGet("old", out GameMakerSprite _, out var _));
        Assert.True(lookup.TryGet("new", out GameMakerSprite found, out var _));
        Assert.Same(a, found);
    }

    [Fact]
    public void BuildHandlesTenThousandStrings()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        for (var i = 0; i < 10000; i++)
        {
            data.Strings.Add(new GameMakerString("s" + i));
        }
        var lookup = DataLookup.Build(data, TestContext.Current.CancellationToken);
        for (var i = 0; i < 10000; i++)
        {
            var found = lookup.FindString("s" + i);
            Assert.NotNull(found);
            Assert.Equal("s" + i, found.Content);
        }
        Assert.Null(lookup.FindString("s10000"));
    }
}
