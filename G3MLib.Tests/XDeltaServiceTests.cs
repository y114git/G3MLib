using G3MLib.Modding.XDelta;

namespace G3MLib.Tests;

public sealed class XDeltaServiceTests
{
    [Fact]
    public async Task ScopedExecutablesRemainIsolatedAcrossAwait()
    {
        static async Task Check(string name)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + name);
            using (XDeltaService.UseExecutable(path))
            {
                await Task.Yield();
                var result = await new XDeltaService().ExecuteRawAsync([]);
                Assert.Equal("xdelta executable not found: " + path, result.Error);
            }
        }
        await Task.WhenAll(Check("first"), Check("second"));
    }

    [Fact("XDeltaServiceTests.cs", 11)]
    public async Task CreatePatchReportsConfiguredMissingExecutable()
    {
        var missingExecutable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new XDeltaService(missingExecutable, TimeSpan.FromSeconds(1));

        var result = await service.CreatePatchAsync("original.win", "modified.win", "output.xdelta");

        Assert.False(result.Success);
        Assert.Equal($"xdelta executable not found: {missingExecutable}", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchOperationsReportOutputDirectoryFailures(bool apply)
    {
        var directory = Path.Combine(Path.GetTempPath(), "G3MLibTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "original.win");
            var other = Path.Combine(directory, apply ? "patch.xdelta" : "modified.win");
            var blockedDirectory = Path.Combine(directory, "blocked");
            await File.WriteAllTextAsync(original, "original", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(other, "other", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(blockedDirectory, "file", TestContext.Current.CancellationToken);

            var service = new XDeltaService(Environment.ProcessPath, TimeSpan.FromSeconds(1));
            var result = apply
                ? await service.ApplyPatchAsync(original, other, Path.Combine(blockedDirectory, "output.win"))
                : await service.CreatePatchAsync(original, other, Path.Combine(blockedDirectory, "output.xdelta"));

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
