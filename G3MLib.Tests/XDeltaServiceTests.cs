using System;
using System.IO;
using System.Threading.Tasks;
using G3MLib.Modding.XDelta;

namespace G3MLib.Tests;

public sealed class XDeltaServiceTests
{
    [Fact("XDeltaServiceTests.cs", 11)]
    public async Task CreatePatchReportsConfiguredMissingExecutable()
    {
        string missingExecutable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new XDeltaService(missingExecutable, TimeSpan.FromSeconds(1));

        XDeltaResult result = await service.CreatePatchAsync("original.win", "modified.win", "output.xdelta");

        Assert.False(result.Success);
        Assert.Equal($"xdelta executable not found: {missingExecutable}", result.Error);
    }
}
