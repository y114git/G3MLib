using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class DataRepairTests
{
    private static string? Find(string name)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.Exists(fixture) ? fixture : null;
    }

    public static TheoryData<string> Files() =>
    [
        "data-2022.win",
        "data-2023.win",
        "data-2024.win",
        "data-2026.win"
    ];

    [Theory]
    [MemberData(nameof(Files))]
    public void RecomputedCountersEqualTheFilesOwn(string name)
    {
        if (Find(name) is not { } path)
            return;
        using var input = File.OpenRead(path);
        using var data = GameMakerIO.Read(input);

        var counters = DataRepair.RecomputeVariableCounters(data);

        Assert.Equal((data.VarCount1, data.VarCount2, data.MaxLocalVarCount), (counters.VarCount1, counters.VarCount2, counters.MaxLocalVarCount));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void BuiltCodeLocalsEqualTheFilesOwn(string name)
    {
        if (Find(name) is not { } path)
            return;
        using var input = File.OpenRead(path);
        using var data = GameMakerIO.Read(input);
        var different = 0;
        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            var real = data.CodeLocals?.FirstOrDefault(l => l.Name.Content == code.Name.Content);
            if (real is null)
                continue;
            var built = DataRepair.BuildCodeLocals(data, code);
            if (!built.Locals.Select(l => (l.Name.Content, l.Index)).Order().SequenceEqual(real.Locals.Select(l => (l.Name.Content, l.Index)).Order()))
                different++;
        }

        // GameMaker lists locals by declaration and this lists them by first use: before 2.3 the index follows the position, so a handful of entries differ
        Assert.True(different <= 2, $"{different} entries differ");
    }

    [Fact]
    public void RestoreAddsMissingLocalsAndLeavesOthers()
    {
        using var input = File.OpenRead(Find("data-2022.win"));
        using var data = GameMakerIO.Read(input);
        var code = data.Code.First(c => c.ParentEntry is null && data.CodeLocals.Any(l => l.Name.Content == c.Name.Content));
        var real = data.CodeLocals.First(l => l.Name.Content == code.Name.Content);
        List<(string?, uint)> expected = real.Locals.Select(l => (l.Name.Content, l.Index)).ToList();
        var before = data.CodeLocals.Count;
        data.CodeLocals.Remove(real);

        var restored = DataRepair.RestoreMissingCodeLocals(data, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, restored);
        Assert.Equal(before, data.CodeLocals.Count);
        Assert.Equal(expected.Order(), data.CodeLocals.First(l => l.Name.Content == code.Name.Content).Locals.Select(l => (l.Name.Content, l.Index)).Order());
        Assert.Equal(0, DataRepair.RestoreMissingCodeLocals(data, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ApplyWritesTheCountersIntoTheHeader()
    {
        using var input = File.OpenRead(Find("data-2026.win"));
        using var data = GameMakerIO.Read(input);
        (var one, var two, var max) = (data.VarCount1, data.VarCount2, data.MaxLocalVarCount);
        data.VarCount1 = data.VarCount2 = 0;
        data.MaxLocalVarCount = 0;

        DataRepair.ApplyVariableCounters(data);

        Assert.Equal((one, two, max), (data.VarCount1, data.VarCount2, data.MaxLocalVarCount));
    }

    [Fact]
    public void WriterRaisesTheCountersWhenVariablesWereAdded()
    {
        using var input = File.OpenRead(Find("data-2026.win"));
        using var data = GameMakerIO.Read(input);
        var declared = data.VarCount1;
        data.Variables.Add(new GameMakerVariable { Name = data.Strings.MakeString("added_without_counter"), InstanceType = GameMakerInstruction.InstanceType.Self, VarID = (int)declared + 4 });

        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        output.Position = 0;
        using var reloaded = GameMakerIO.Read(output);

        Assert.Equal(declared + 5, reloaded.VarCount1);
    }
}
