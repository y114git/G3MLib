using System;
using System.IO;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Caching;
using G3MLib.Modding.Diffing;
using Xunit;

namespace G3MLib.Tests;

public class DiffServiceTests
{
    [Fact("DiffServiceTests.cs", 10)]
    public async Task DiffSkipsBinaryAnalysisForGeneralInfoOnlyChange()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string modifiedPath = Path.Combine(directory, "modified.win");
            string reportPath = Path.Combine(directory, "report.md");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                Write(originalPath, original);
            }
            using (GameMakerData modified = Read(originalPath))
            {
                modified.GeneralInfo.DisplayName = modified.Strings.MakeString("changed");
                Write(modifiedPath, modified);
            }
            DiffResult result = await DiffService.CompareAsync(originalPath, modifiedPath, reportPath);
            Assert.True(result.Success, result.Error);
            Assert.Equal(1, result.DifferenceCount);
            Assert.DoesNotContain("## Binary Analysis", await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("DiffServiceTests.cs", 42)]
    public async Task DiffReportsChangedDataFilesAndWritesReusableCache()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string modifiedPath = Path.Combine(directory, "modified.win");
            string reportPath = Path.Combine(directory, "report.md");
            string cachedReportPath = Path.Combine(directory, "cached-report.md");
            string cacheDirectory = Path.Combine(directory, "cache");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                original.Rooms.Add(new GameMakerRoom
                {
                    Name = original.Strings.MakeString("room_diff")
                });
                Write(originalPath, original);
            }
            using (GameMakerData modified = Read(originalPath))
            {
                modified.Rooms.ByName("room_diff").Width = 1234u;
                Write(modifiedPath, modified);
            }
            DiffResult result = await DiffService.CompareAsync(originalPath, modifiedPath, reportPath, DiffReportMode.Standard, G3MCacheOptions.FromDirectory(cacheDirectory));
            Assert.True(result.Success, result.Error);
            Assert.True(result.DifferenceCount > 0);
            Assert.True(File.Exists(reportPath));
            Assert.NotEmpty(Directory.GetFiles(cacheDirectory));
            DiffResult cachedResult = await DiffService.CompareAsync(originalPath, modifiedPath, cachedReportPath, DiffReportMode.Standard, G3MCacheOptions.FromDirectory(cacheDirectory));
            Assert.True(cachedResult.Success, cachedResult.Error);
            Assert.Equal(result.DifferenceCount, cachedResult.DifferenceCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static GameMakerData Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return GameMakerIO.Read(stream);
    }

    private static void Write(string path, GameMakerData data)
    {
        using FileStream stream = File.Create(path);
        GameMakerIO.Write(stream, data);
    }
}
