using G3MLib.DataFile;

namespace G3MLib.Tests;

public sealed class GameMakerAsyncIoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g3mlib-async-" + Guid.NewGuid().ToString("N"));

    public GameMakerAsyncIoTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class Recorder : IProgress<GameMakerProgress>
    {
        public readonly List<GameMakerProgress> Items = [];
        public Action<GameMakerProgress> OnReport;

        public void Report(GameMakerProgress value)
        {
            Items.Add(value);
            OnReport?.Invoke(value);
        }
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2022.win");

    private static void AssertMonotonicToOne(Recorder recorder)
    {
        Assert.NotEmpty(recorder.Items);
        double previous = 0;
        foreach (var item in recorder.Items)
        {
            Assert.InRange(item.Fraction, previous, 1.0);
            Assert.False(string.IsNullOrEmpty(item.Chunk));
            previous = item.Fraction;
        }
        Assert.Equal(1.0, recorder.Items[^1].Fraction);
    }

    [Fact]
    public async Task ReadAsyncReportsMonotonicProgressUpToOne()
    {
        Recorder recorder = new();
        using var data = await GameMakerIO.ReadAsync(FixturePath, recorder, TestContext.Current.CancellationToken);
        AssertMonotonicToOne(recorder);
        Assert.NotNull(data.GeneralInfo);
    }

    [Fact]
    public async Task WriteAsyncReportsMonotonicProgressUpToOne()
    {
        using var data = await GameMakerIO.ReadAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);
        Recorder recorder = new();
        await GameMakerIO.WriteAsync(Path.Combine(_dir, "out.win"), data, recorder, TestContext.Current.CancellationToken);
        AssertMonotonicToOne(recorder);
    }

    [Fact]
    public async Task AsyncAndSyncOutputsAreByteIdentical()
    {
        var original = File.ReadAllBytes(FixturePath);
        using MemoryStream syncInput = new(original);
        using var syncData = GameMakerIO.Read(syncInput);
        using MemoryStream syncOutput = new();
        GameMakerIO.Write(syncOutput, syncData);

        using var asyncData = await GameMakerIO.ReadAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);
        var target = Path.Combine(_dir, "out.win");
        await GameMakerIO.WriteAsync(target, asyncData, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(syncOutput.ToArray(), File.ReadAllBytes(target));
        using MemoryStream streamOutput = new();
        await GameMakerIO.WriteAsync(streamOutput, asyncData, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(syncOutput.ToArray(), streamOutput.ToArray());
    }

    [Fact]
    public async Task ReadAsyncThrowsWhenAlreadyCancelled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameMakerIO.ReadAsync(FixturePath, null, cts.Token));
    }

    [Fact]
    public async Task ReadAsyncStopsBetweenChunksWhenCancelled()
    {
        using CancellationTokenSource cts = new();
        Recorder recorder = new() { OnReport = _ => cts.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameMakerIO.ReadAsync(FixturePath, recorder, cts.Token));
        Assert.Single(recorder.Items);
    }

    [Fact]
    public async Task CancelledWriteLeavesTargetUntouchedAndNoTempFile()
    {
        using var data = await GameMakerIO.ReadAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);
        var target = Path.Combine(_dir, "out.win");
        byte[] sentinel = [1, 2, 3, 4];
        File.WriteAllBytes(target, sentinel);

        using CancellationTokenSource cts = new();
        Recorder recorder = new() { OnReport = _ => cts.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameMakerIO.WriteAsync(target, data, recorder, cts.Token));

        Assert.Equal(sentinel, File.ReadAllBytes(target));
        Assert.Equal([target], Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task CancelledWriteCreatesNoTargetWhenNoneExisted()
    {
        using var data = await GameMakerIO.ReadAsync(FixturePath, cancellationToken: TestContext.Current.CancellationToken);
        var target = Path.Combine(_dir, "new.win");
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameMakerIO.WriteAsync(target, data, null, cts.Token));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
