using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.References;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class RenameBatchTests
{
    private static GameMakerData Load()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        return GameMakerIO.Read(input);
    }

    private static byte[] Save(GameMakerData data)
    {
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        return output.ToArray();
    }

    private static void EnsureSprites(GameMakerData data, int count)
    {
        while (data.Sprites.Count < count)
        {
            data.Sprites.Add(new GameMakerSprite { Name = data.Strings.MakeString("spr_probe_" + data.Sprites.Count) });
        }
    }

    private static List<GameMakerGameObject> Objects(GameMakerData data, int count)
    {
        while (data.GameObjects.Count < count)
        {
            data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString("obj_probe_" + data.GameObjects.Count) });
        }
        return [.. data.GameObjects.Take(count)];
    }

    [Fact]
    public void TwoNamesCanSwap()
    {
        using var data = Load();
        var objects = Objects(data, 2);
        string a = objects[0].Name.Content, b = objects[1].Name.Content;

        var plan = RenameService.PlanRenames(data, [RenameRequest.ForResource(objects[0], b), RenameRequest.ForResource(objects[1], a)], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(plan.Conflicts);
        var changes = RenameService.Apply(plan);

        Assert.Equal((b, a), (objects[0].Name.Content, objects[1].Name.Content));
        changes.Undo();
        Assert.Equal((a, b), (objects[0].Name.Content, objects[1].Name.Content));
    }

    [Fact]
    public void ThreeNamesCanShiftInACircle()
    {
        using var data = Load();
        var objects = Objects(data, 3);
        string[] names = [.. objects.Select(o => o.Name.Content)];

        var plan = RenameService.PlanRenames(data, [
            RenameRequest.ForResource(objects[0], names[1]),
            RenameRequest.ForResource(objects[1], names[2]),
            RenameRequest.ForResource(objects[2], names[0])], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(plan.Conflicts);
        RenameService.Apply(plan);
        Assert.Equal([names[1], names[2], names[0]], objects.Select(o => o.Name.Content));
    }

    [Fact]
    public void BatchGivesTheBytesOfTheSingleRenamesInOrder()
    {
        using var batch = Load();
        using var single = Load();
        (string Sprite, string Object, string Script) names = ("spr_renamed_a", "obj_renamed_a", "scr_renamed_a");

        RenameService.Apply(RenameService.PlanRenames(batch, [
            RenameRequest.ForResource(batch.Sprites[0], names.Sprite),
            RenameRequest.ForResource(batch.GameObjects[0], names.Object),
            RenameRequest.ForResource(batch.Scripts[0], names.Script)], cancellationToken: TestContext.Current.CancellationToken));
        RenameService.RenameResource(single, single.Sprites[0], names.Sprite);
        RenameService.RenameResource(single, single.GameObjects[0], names.Object);
        RenameService.RenameResource(single, single.Scripts[0], names.Script);

        Assert.Equal(Save(single), Save(batch));
    }

    [Fact]
    public void ConflictsAreListedAndApplyRefusesWithoutChangingAnything()
    {
        using var data = Load();
        EnsureSprites(data, 2);
        var objects = Objects(data, 2);
        var before = Save(data);
        var taken = RenameRequest.ForResource(data.Sprites[0], objects[1].Name.Content);
        var invalid = RenameRequest.ForResource(data.Sprites[1], "1bad");
        var fine = RenameRequest.ForResource(data.Scripts[0], "scr_fine_name");

        var plan = RenameService.PlanRenames(data, [taken, invalid, fine], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([(taken, RenameFailure.NameInUse), (invalid, RenameFailure.InvalidName)], plan.Conflicts.Select(c => (c.Request, c.Reason)));
        Assert.Contains(plan.Changes, change => change.NewValue == "scr_fine_name");
        Assert.Throws<InvalidOperationException>(() => RenameService.Apply(plan));
        Assert.Equal(before, Save(data));
    }

    [Fact]
    public void TwoRequestsForTheSameNameConflict()
    {
        using var data = Load();
        EnsureSprites(data, 2);

        var plan = RenameService.PlanRenames(data, [RenameRequest.ForResource(data.Sprites[0], "same_target"), RenameRequest.ForResource(data.Sprites[1], "same_target")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, plan.Conflicts.Count(c => c.Reason == RenameFailure.NameInUse));
    }

    [Fact]
    public void RenamingOneResourceTwiceIsAnOverlap()
    {
        using var data = Load();

        var plan = RenameService.PlanRenames(data, [RenameRequest.ForResource(data.Sprites[0], "first_try"), RenameRequest.ForResource(data.Sprites[0], "second_try")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(plan.Conflicts, c => c.Reason == RenameFailure.Overlap);
    }

    [Fact]
    public void ProgressAndCancellationAreHonoured()
    {
        using var data = Load();
        EnsureSprites(data, 2);
        List<BatchProgress> reports = [];
        RenameRequest[] requests = [RenameRequest.ForResource(data.Sprites[0], "progress_a"), RenameRequest.ForResource(data.Sprites[1], "progress_b")];
        RenameService.PlanRenames(data, requests, null, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);
        Assert.Equal([1, 2], reports.Select(r => r.Done));

        using CancellationTokenSource source = new();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => RenameService.PlanRenames(data, requests, null, null, source.Token));
    }

    private sealed class SyncProgress(Action<BatchProgress> onReport) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => onReport(value);
    }
}
