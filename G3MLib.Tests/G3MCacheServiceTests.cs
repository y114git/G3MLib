using System;
using System.IO;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.Modding.Caching;
using Xunit;

namespace G3MLib.Tests;

public sealed class G3MCacheServiceTests
{
    [Fact("G3MCacheServiceTests.cs", 14)]
    public async Task CacheRejectsChangedAndCorruptSourcesWithoutThrowing()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        string sourcePath = Path.Combine(directory, "source.win");
        string cacheDirectory = Path.Combine(directory, "cache");
        Directory.CreateDirectory(directory);
        try
        {
            G3MDataInfoSnapshot snapshot;
            using (GameMakerData data = GameMakerData.CreateNew2026LTS())
            {
                using (FileStream stream = File.Create(sourcePath))
                {
                    GameMakerIO.Write(stream, data);
                }
                snapshot = G3MCacheService.BuildInfoSnapshot(sourcePath, data);
            }

            G3MCacheOptions options = G3MCacheOptions.FromDirectory(cacheDirectory);
            await G3MCacheService.WriteDataInfoCacheAsync(sourcePath, snapshot, options);
            Assert.NotNull(G3MCacheService.TryReadDataInfoSnapshot(sourcePath, options));

            await File.AppendAllTextAsync(sourcePath, "changed", TestContext.Current.CancellationToken);
            Assert.Null(G3MCacheService.TryReadDataInfoSnapshot(sourcePath, options));

            string cachePath = Assert.Single(Directory.GetFiles(cacheDirectory, "*.g3mcache"));
            await File.WriteAllTextAsync(cachePath, "corrupt cache", TestContext.Current.CancellationToken);
            Assert.Null(G3MCacheService.TryReadDataInfoSnapshot(sourcePath, options));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
