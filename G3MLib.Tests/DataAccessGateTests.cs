using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.References;
using G3MLib.DataFile.Scripting;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class DataAccessGateTests
{
    // A task started normally shares the caller's scope by design, so contention is tested from a flow that does not inherit it
    private static Task<T> OnOtherFlow<T>(Func<T> work)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(work);
    }

    [Fact]
    public async Task WriterExcludesReadersAndReadersShare()
    {
        var gate = new DataAccessGate();
        using (gate.BeginRead())
        {
            using var second = await OnOtherFlow(() => gate.BeginRead(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<TimeoutException>(() => OnOtherFlow(() => gate.BeginWrite(TimeSpan.FromMilliseconds(50))));
        }

        using (gate.BeginWrite())
        {
            await Assert.ThrowsAsync<TimeoutException>(() => OnOtherFlow(() => gate.BeginRead(TimeSpan.FromMilliseconds(50))));
        }

        using var free = gate.BeginWrite(TimeSpan.Zero);
    }

    [Fact]
    public async Task WaiterContinuesWhenTheHolderReleases()
    {
        var gate = new DataAccessGate();
        var holder = gate.BeginWrite();
        var waiter = OnOtherFlow(() => gate.BeginRead(TimeSpan.FromSeconds(10)));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waiter.IsCompleted);
        holder.Dispose();
        (await waiter).Dispose();
    }

    [Fact]
    public async Task CancellationStopsTheWait()
    {
        var gate = new DataAccessGate();
        using var holder = gate.BeginWrite();
        using var source = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OnOtherFlow(() => gate.BeginRead(null, source.Token)));
    }

    [Fact]
    public void NestedScopesOfTheSameCodeDoNotWaitOnThemselves()
    {
        var gate = new DataAccessGate();
        using (gate.BeginWrite())
        {
            using var again = gate.BeginWrite(TimeSpan.Zero);
            using var read = gate.BeginRead(TimeSpan.Zero);
        }

        using (gate.BeginRead())
        {
            using var read = gate.BeginRead(TimeSpan.Zero);
            Assert.Throws<InvalidOperationException>(() => gate.BeginWrite(TimeSpan.Zero));
        }

        using var free = gate.BeginWrite(TimeSpan.Zero);
    }

    [Fact]
    public async Task ScopeDisposedBeforeATaskAsksIsNotHeldByThatTask()
    {
        var gate = new DataAccessGate();
        var proceed = new TaskCompletionSource();
        var scope = gate.BeginWrite();
        var late = Task.Run(async () =>
        {
            await proceed.Task;
            return gate.BeginWrite(TimeSpan.Zero);
        }, TestContext.Current.CancellationToken);
        scope.Dispose();
        using var other = gate.BeginRead(TimeSpan.Zero);
        proceed.SetResult();
        // The task inherited the disposed scope; it must ask the gate for real and so find it busy
        await Assert.ThrowsAsync<TimeoutException>(() => late);
    }

    [Fact]
    public async Task InfiniteTimeoutWaitsAndOtherNegativeTimeoutsThrow()
    {
        var gate = new DataAccessGate();
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.BeginRead(TimeSpan.FromMilliseconds(-5)));
        var holder = gate.BeginWrite();
        var waiter = OnOtherFlow(() => gate.BeginRead(Timeout.InfiniteTimeSpan));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiter.IsCompleted);
        holder.Dispose();
        (await waiter).Dispose();

        using var big = gate.BeginWrite(TimeSpan.MaxValue);
    }

    [Fact]
    public async Task ScriptThatSavesTheDataDoesNotWaitOnItsOwnGate()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var path = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}.csx");
        await File.WriteAllTextAsync(path, "G3MLib.DataFile.GameMakerIO.Write(new System.IO.MemoryStream(), Data);", TestContext.Current.CancellationToken);
        try
        {
            var result = await ScriptRunner.RunAsync(path, new ScriptGlobals(data), timeout: TimeSpan.FromSeconds(60));
            Assert.True(result.Success, result.Error);
            using var free = data.Gate.BeginWrite(TimeSpan.Zero);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PublicMutatorsWaitForAHeldReader()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var index = ReferenceIndex.Build(data);
        var empty = Directory.CreateTempSubdirectory("g3mlib-gate-").FullName;
        try
        {
            var holder = data.Gate.BeginRead();
            var order = OnOtherFlow(() => { ResourceImportService.ImportAssetOrder(data, empty); return true; });
            var clear = OnOtherFlow(() => index.ClearUsages(new GameMakerSprite()));
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.False(order.IsCompleted);
            Assert.False(clear.IsCompleted);

            holder.Dispose();
            await order.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await clear.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(empty, recursive: true); }
    }
}
