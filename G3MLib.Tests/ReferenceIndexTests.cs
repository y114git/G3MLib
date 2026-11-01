using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.References;

namespace G3MLib.Tests;

public class ReferenceIndexTests
{
    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void FindsEveryVariableAndFunctionUseInCode(string fixture)
    {
        using var data = Load(fixture);
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);

        var variableUses = data.Code.Sum(c => c.Instructions.Count(i => i.ValueVariable is not null));
        var found = data.Variables.Sum(v => index.FindUsages(v).Count(r => r.Member == "Variable"));
        Assert.True(variableUses > 0);
        Assert.Equal(variableUses, found);

        var use = data.Variables.SelectMany(index.FindUsages).First(r => r.Member == "Variable");
        Assert.IsType<GameMakerCode>(use.Owner);
        Assert.IsType<GameMakerInstruction>(use.Holder);
        Assert.True(use.InstructionOffset >= 0);
    }

    [Fact]
    public void FindsResourceReferencesAndMovesThemOnUpdate()
    {
        using var data = Load("data-2026.win");
        var owner = data.GameObjects[0];
        var sprite = data.Sprites[0];
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(index.FindUsages(sprite), r => ReferenceEquals(r.Owner, owner));

        owner.Sprite = sprite;
        index.Update([owner], TestContext.Current.CancellationToken);
        Assert.Contains(index.FindUsages(sprite), r => ReferenceEquals(r.Owner, owner) && r.Member == "Sprite");

        owner.Sprite = null;
        index.Update([owner], TestContext.Current.CancellationToken);
        Assert.DoesNotContain(index.FindUsages(sprite), r => ReferenceEquals(r.Owner, owner));
    }

    [Fact]
    public void UpdateMatchesAFullRebuild()
    {
        using var data = Load("data-2026.win");
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);
        var owner = data.GameObjects[0];
        owner.Sprite = data.Sprites[0];
        index.Update([owner], TestContext.Current.CancellationToken);

        Assert.Equal(ReferenceIndex.Build(data, TestContext.Current.CancellationToken).ReferenceCount, index.ReferenceCount);
    }

    [Fact]
    public void RoomInstancesCountAsObjectUses()
    {
        using var data = Load("data-2026.win");
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);

        foreach (var room in data.Rooms)
        {
            foreach (var instance in room.GameObjects.Where(i => i.ObjectDefinition is not null))
            {
                Assert.Contains(index.FindUsages(instance.ObjectDefinition), r => ReferenceEquals(r.Owner, room));
            }
        }
    }

    [Fact]
    public void UnreferencedExcludesUsedAssets()
    {
        using var data = Load("data-2026.win");
        var owner = data.GameObjects[0];
        GameMakerSprite sprite = new() { Name = new GameMakerString("spr_fresh") };
        data.Sprites.Add(sprite);
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);
        Assert.Contains(sprite, index.FindUnreferenced(typeof(GameMakerSprite)));

        owner.Sprite = sprite;
        index.Update([owner], TestContext.Current.CancellationToken);
        Assert.DoesNotContain(sprite, index.FindUnreferenced(typeof(GameMakerSprite)));
        Assert.All(index.FindUnreferenced(), u => Assert.DoesNotContain(index.FindUsages(u), r => !ReferenceEquals(r.Owner, u)));
    }

    private sealed class SyncProgress(Action<Modding.Resources.BatchProgress> onReport) : IProgress<Modding.Resources.BatchProgress>
    {
        public void Report(Modding.Resources.BatchProgress value) => onReport(value);
    }

    [Fact]
    public void BuildReportsProgressUpToTheOwnerCount()
    {
        using var data = Load("data-2026.win");
        List<Modding.Resources.BatchProgress> reports = [];

        ReferenceIndex.Build(data, new SyncProgress(r => { lock (reports) reports.Add(r); }), TestContext.Current.CancellationToken);

        Assert.NotEmpty(reports);
        Assert.Equal(reports[0].Total, reports.Max(r => r.Done));
    }

    [Fact]
    public void UpdateSeesOwnersAddedAndRemovedSinceBuild()
    {
        using var data = Load("data-2026.win");
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);
        var sprite = data.Sprites[0];
        GameMakerGameObject added = new() { Name = new GameMakerString("obj_added"), Sprite = sprite };
        data.GameObjects.Add(added);

        index.Update([added], TestContext.Current.CancellationToken);
        Assert.Contains(index.FindUsages(sprite), r => ReferenceEquals(r.Owner, added));

        data.GameObjects.Remove(added);
        index.Update([added], TestContext.Current.CancellationToken);
        Assert.DoesNotContain(index.FindUsages(sprite), r => ReferenceEquals(r.Owner, added));
        Assert.Equal(ReferenceIndex.Build(data, TestContext.Current.CancellationToken).ReferenceCount, index.ReferenceCount);
    }

    [Fact]
    public void FindUsagesFilterMatchesTheFullList()
    {
        using var data = Load("data-2026.win");
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);
        var variable = data.Variables.First(v => index.FindUsages(v).Count > 1);

        Assert.Equal(
            index.FindUsages(variable).Where(r => r.Owner is GameMakerCode && r.Member == "Variable").ToList(),
            [.. index.FindUsages(variable, typeof(GameMakerCode), "Variable")]);
        Assert.Empty(index.FindUsages(variable, typeof(GameMakerRoom)));
    }

    [Fact]
    public void FindsUnreferencedStringsVariablesAndFunctions()
    {
        using var data = Load("data-2026.win");
        var text = data.Strings.MakeString("unreferenced_probe_text");
        GameMakerVariable variable = new() { Name = data.Strings.MakeString("unreferenced_probe_variable") };
        GameMakerFunction function = new() { Name = data.Strings.MakeString("unreferenced_probe_function") };
        data.Variables.Add(variable);
        data.Functions.Add(function);
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);

        var found = index.FindUnreferenced(UnreferencedKind.Variables | UnreferencedKind.Functions);
        Assert.Contains(variable, found);
        Assert.Contains(function, found);
        Assert.All(found, item => Assert.DoesNotContain(index.FindUsages(item), r => !ReferenceEquals(r.Owner, item)));
        Assert.DoesNotContain(data.Variables.First(v => index.FindUsages(v).Count > 0), found);

        // The probe text is used by nothing except holders that this index does not own
        Assert.Contains(text, index.FindUnreferenced(UnreferencedKind.Strings));
    }

    [Fact]
    public void TransitiveSearchFindsWhatOnlyUnusedAssetsUse()
    {
        using var data = Load("data-2026.win");
        GameMakerSprite sprite = new() { Name = data.Strings.MakeString("spr_chain_probe") };
        GameMakerGameObject owner = new() { Name = data.Strings.MakeString("obj_chain_probe"), Sprite = sprite };
        data.Sprites.Add(sprite);
        data.GameObjects.Add(owner);
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);

        var direct = index.FindUnreferenced(UnreferencedKind.Assets);
        var chained = index.FindUnreferenced(UnreferencedKind.Assets | UnreferencedKind.Strings, true);

        Assert.Contains(owner, direct);
        Assert.DoesNotContain(sprite, direct);
        Assert.Contains(sprite, chained);
        Assert.Contains(sprite.Name, chained);
        Assert.Contains(owner.Name, chained);
    }

    [Fact]
    public void FindsEmptyPagesAndUnusedTextureItems()
    {
        using var data = Load("data-2026.win");
        GameMakerEmbeddedTexture page = new();
        GameMakerTexturePageItem item = new() { TexturePage = data.EmbeddedTextures[0] };
        data.EmbeddedTextures.Add(page);
        data.TexturePageItems.Add(item);
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);

        Assert.Contains(page, index.FindUnreferenced(UnreferencedKind.EmptyPages));
        Assert.DoesNotContain(data.EmbeddedTextures[0], index.FindUnreferenced(UnreferencedKind.EmptyPages));
        Assert.Contains(item, index.FindUnreferenced(UnreferencedKind.TexturePageItems));
    }

    [Fact]
    public void CodeScanListsWhatTheEntryUses()
    {
        using var data = Load("data-2026.win");
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);
        var code = data.Code.First(c => c.Instructions.Any(i => i.ValueVariable is not null) && c.Instructions.Any(i => i.ValueFunction is not null));

        var scan = CodeScan.Scan(data, code);

        Assert.Equal(code.Instructions.Where(i => i.ValueVariable is not null).Select(i => i.ValueVariable).Distinct().Count(), scan.Variables.Count);
        Assert.All(scan.Functions, f => Assert.Contains(index.FindUsages(f), r => ReferenceEquals(r.Owner, code)));
    }

    [Fact]
    public void AudioIndexFindsIdsNoSoundUses()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("a"), GroupID = 1, AudioID = 0 });
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("b"), GroupID = 1, AudioID = 3 });
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("c"), GroupID = 2, AudioID = 1 });

        Assert.Equal([1, 2], AudioIndex.Unreferenced(data, 1, 4));
    }
}
