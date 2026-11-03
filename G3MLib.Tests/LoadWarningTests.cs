using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class LoadWarningTests
{
    private static byte[] Write(GameMakerData data)
    {
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        return output.ToArray();
    }

    private static byte[] WithUnknownChunk()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.FORM.AddChunk(new GameMakerRawChunk("ZZZZ") { RawData = new byte[16] });
        return Write(data);
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2023.win")]
    [InlineData("data-2024.win")]
    [InlineData("data-2026.win")]
    public void NormalFileHasNoWarnings(string fixture)
    {
        List<string> handled = [];
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        using var data = GameMakerIO.Read(input, (warning, _) => handled.Add(warning));

        Assert.Empty(data.LoadWarnings);
        Assert.Empty(handled);
        Assert.False(data.MayLoseDataOnSave);
    }

    [Fact]
    public void NewDataFileAndItsRoundTripHaveNoWarnings()
    {
        using var created = GameMakerData.CreateNew2026LTS();
        Assert.Empty(created.LoadWarnings);
        Assert.False(created.MayLoseDataOnSave);

        using var reread = GameMakerIO.Read(new MemoryStream(Write(created)));
        Assert.Empty(reread.LoadWarnings);
        Assert.False(reread.MayLoseDataOnSave);
    }

    [Fact]
    public void UnknownChunkIsListedAndMarksPossibleDataLoss()
    {
        List<(string Warning, bool Important)> handled = [];
        using var data = GameMakerIO.Read(new MemoryStream(WithUnknownChunk()), (warning, important) => handled.Add((warning, important)));

        var warning = Assert.Single(data.LoadWarnings);
        Assert.Equal("ZZZZ", warning.Chunk);
        Assert.Contains("ZZZZ", warning.Message, StringComparison.Ordinal);
        Assert.False(warning.IsImportant);
        Assert.Equal(data.FORM.Chunks["ZZZZ"].Offset + 8, warning.Offset);
        Assert.True(data.MayLoseDataOnSave);

        var forwarded = Assert.Single(handled);
        Assert.Contains("ZZZZ", forwarded.Warning, StringComparison.Ordinal);
        Assert.False(forwarded.Important);
    }

    [Fact]
    public void UnknownChunkReadsWithoutAHandlerAndStaysInTheList()
    {
        using var data = GameMakerIO.Read(new MemoryStream(WithUnknownChunk()));

        Assert.Single(data.LoadWarnings);
        Assert.True(data.MayLoseDataOnSave);
    }

    [Fact]
    public async Task ReadAsyncFillsTheWarningList()
    {
        using var data = await GameMakerIO.ReadAsync(new MemoryStream(WithUnknownChunk()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(data.LoadWarnings);
        Assert.True(data.MayLoseDataOnSave);
    }

    [Fact]
    public void UnsupportedBytecodeVersionIsListed()
    {
        using var created = GameMakerData.CreateNew2026LTS();
        created.GeneralInfo.BytecodeVersion = 12;
        using var data = GameMakerIO.Read(new MemoryStream(Write(created)));

        var warning = Assert.Single(data.LoadWarnings);
        Assert.Equal("GEN8", warning.Chunk);
        Assert.Contains("12", warning.Message, StringComparison.Ordinal);
        Assert.True(data.MayLoseDataOnSave);
    }

    [Fact]
    public void ImportantWarningsAreRecordedAndStillReachTheHandler()
    {
        List<(string Warning, bool Important)> handled = [];
        using MemoryStream stream = new();
        using GameMakerReader reader = new(stream, (warning, important) => handled.Add((warning, important)));
        reader.gameMakerData = new GameMakerData();

        reader.SubmitWarning("minor", false);
        Assert.False(reader.gameMakerData.MayLoseDataOnSave);
        reader.SubmitWarning("serious");

        Assert.Equal(["minor", "serious"], reader.gameMakerData.LoadWarnings.Select(w => w.Message));
        Assert.Equal([false, true], reader.gameMakerData.LoadWarnings.Select(w => w.IsImportant));
        Assert.True(reader.gameMakerData.MayLoseDataOnSave);
        Assert.Equal([false, true], handled.Select(h => h.Important));
        Assert.All(handled, h => Assert.Contains("\nat ", h.Warning, StringComparison.Ordinal));
    }

    [Fact]
    public void ImportantWarningWithoutAHandlerStillThrows()
    {
        using MemoryStream stream = new();
        using GameMakerReader reader = new(stream);
        reader.gameMakerData = new GameMakerData();

        Assert.Throws<IOException>(() => reader.SubmitWarning("serious"));
        reader.SubmitWarning("minor", false);

        Assert.Equal(["serious", "minor"], reader.gameMakerData.LoadWarnings.Select(w => w.Message));
    }

    [Fact]
    public void SkippedRoomLayerInstancesRecordDataLossWarning()
    {
        using var created = GameMakerData.CreateNew2026LTS();
        GameMakerGameObject obj = new() { Name = created.Strings.MakeString("obj_test") };
        created.GameObjects.Add(obj);
        GameMakerRoom room = new() { Name = created.Strings.MakeString("rm_warn") };
        room.GameObjects.Add(new GameMakerRoom.GameObject { InstanceID = 100000u, ObjectDefinition = obj });
        GameMakerRoom.Layer layer = new()
        {
            LayerName = created.Strings.MakeString("Instances"),
            LayerType = GameMakerRoom.LayerType.Instances,
            LayerId = 1,
            Data = new GameMakerRoom.Layer.LayerInstancesData
            {
                Instances = [new GameMakerRoom.GameObject { InstanceID = 100050u, ObjectDefinition = obj }]
            }
        };
        room.Layers.Add(layer);
        created.Rooms.Add(room);

        using var reread = GameMakerIO.Read(new MemoryStream(Write(created)));

        var warning = Assert.Single(reread.LoadWarnings);
        Assert.Equal("ROOM", warning.Chunk);
        Assert.Contains("100050", warning.Message, StringComparison.Ordinal);
        Assert.True(reread.MayLoseDataOnSave);
    }

    [Fact]
    public void LoadWarningsReturnsSnapshotCopy()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var snapshot1 = data.LoadWarnings;
        Assert.Empty(snapshot1);

        data.AddLoadWarning(new LoadWarning("TEST", "Warning 1", false, 0), false);
        var snapshot2 = data.LoadWarnings;
        Assert.Empty(snapshot1);
        Assert.Single(snapshot2);

        data.AddLoadWarning(new LoadWarning("TEST", "Warning 2", false, 0), false);
        Assert.Empty(snapshot1);
        Assert.Single(snapshot2);
        Assert.Equal(2, data.LoadWarnings.Count);
    }

    [Fact]
    public void AddLoadWarningIsThreadSafeUnderConcurrentCalls()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        const int iterations = 1000;
        Parallel.For(0, iterations, i =>
        {
            data.AddLoadWarning(new LoadWarning("CHUNK", $"Warning {i}", false, i), i % 2 == 0);
            _ = data.LoadWarnings;
        });

        Assert.Equal(iterations, data.LoadWarnings.Count);
        Assert.True(data.MayLoseDataOnSave);
    }
}
