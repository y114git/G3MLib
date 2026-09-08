using System;
using System.IO;
using System.Threading.Tasks;
using G3MLib.Modding.Batching;

namespace G3MLib.Tests;

public sealed class BatchPatchServiceTests
{
    [Fact("BatchPatchServiceTests.cs", 11)]
    public async Task CreateAndApplyRejectMissingOriginalDataFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var options = new BatchOptions
            {
                OriginalPath = Path.Combine(directory, "missing.win"),
                OutDir = Path.Combine(directory, "output")
            };

            await Assert.ThrowsAsync<FileNotFoundException>(() => BatchPatchService.CreateBatchAsync([], options));
            await Assert.ThrowsAsync<FileNotFoundException>(() => BatchPatchService.ApplyBatchAsync([], options));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
