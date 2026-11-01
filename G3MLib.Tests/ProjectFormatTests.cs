using System.Text;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Project;

namespace G3MLib.Tests;

public sealed class ProjectFormatTests
{
    private static string NewTempDirectory() => Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");

    private static GameMakerData ReadFixture(string fileName)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        return GameMakerIO.Read(input);
    }

    private static IEnumerable<IProjectAsset> ExportableAssets(GameMakerData data)
    {
        IEnumerable<IProjectAsset>[] groups =
        [
            data.AnimationCurves, data.Backgrounds, data.Code, data.Fonts, data.GameObjects, data.ParticleSystems,
            data.Paths, data.Rooms, data.Scripts, data.Sequences, data.Shaders, data.Sounds, data.Sprites
        ];
        return groups.Where(g => g is not null).SelectMany(g => g).Where(a => a?.ProjectExportable ?? false);
    }

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes);

    private static void ExportSelected(GameMakerData data, string directory)
    {
        ProjectContext context = new(data, "input.win", "output.win", Path.Combine(directory, "project.json"), "Test");
        foreach (var asset in ExportableAssets(data))
        {
            context.MarkAssetForExport(asset);
        }
        context.Export(clearMarkedAssets: true);
    }

    public static TheoryData<string> Fixtures() => ["data-2022.win", "data-2023.win", "data-2024.win", "data-2026.win"];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExportTwiceProducesIdenticalBytes(string fileName)
    {
        var first = NewTempDirectory();
        var second = NewTempDirectory();
        try
        {
            using var data = ReadFixture(fileName);
            ExportSelected(data, first);
            ExportSelected(data, second);
            var a = Snapshot(first);
            var b = Snapshot(second);
            Assert.Equal(a.Keys.Order(StringComparer.Ordinal), b.Keys.Order(StringComparer.Ordinal));
            Assert.All(a, pair => Assert.Equal(pair.Value, b[pair.Key]));
        }
        finally
        {
            Directory.Delete(first, true);
            Directory.Delete(second, true);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExportedTextFilesAreUtf8WithoutBomLfAndFinalNewline(string fileName)
    {
        var directory = NewTempDirectory();
        try
        {
            using var data = ReadFixture(fileName);
            ExportSelected(data, directory);
            string[] files = [.. Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".gml", StringComparison.Ordinal))];
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                var bytes = File.ReadAllBytes(file);
                Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), $"BOM in {file}");
                Assert.DoesNotContain((byte)'\r', bytes);
                Assert.True(bytes.Length > 0 && bytes[^1] == (byte)'\n', $"missing final newline in {file}");
                Assert.False(bytes.Length > 1 && bytes[^2] == (byte)'\n', $"extra blank line at end of {file}");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExportAllWritesEveryExportableAssetAndRecordsFullMode(string fileName)
    {
        var directory = NewTempDirectory();
        var mainFile = Path.Combine(directory, "project.json");
        try
        {
            using var data = ReadFixture(fileName);
            ProjectContext context = new(data, "input.win", "output.win", mainFile, "Test");
            context.ExportAll();
            Assert.False(context.HasUnexportedAssets);
            string[] expected = [.. ExportableAssets(data).Select(a => a.ProjectName).Distinct(StringComparer.Ordinal)];
            string[] written = [.. Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".gml", StringComparison.Ordinal))
                .Select(Path.GetFileNameWithoutExtension)];
            Assert.All(expected, name => Assert.Contains(written, w => w == name || w.StartsWith(name, StringComparison.Ordinal)));

            using var manifest = JsonDocument.Parse(File.ReadAllText(mainFile));
            Assert.Equal("full", manifest.RootElement.GetProperty("G3M").GetProperty("Mode").GetString());
            Assert.Equal("Test", manifest.RootElement.GetProperty("Name").GetString());

            var reopened = ProjectContext.CreateWithDirectories(directory, directory, mainFile);
            using var target = GameMakerData.CreateNew2026LTS();
            reopened.Import(target, new GameFileNoOpBackup());
            Assert.Equal(ProjectExportMode.Full, reopened.ExportMode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SelectiveExportKeepsManifestFreeOfFullMode()
    {
        var directory = NewTempDirectory();
        try
        {
            using var data = ReadFixture("data-2022.win");
            ExportSelected(data, directory);
            Assert.DoesNotContain("G3M", File.ReadAllText(Path.Combine(directory, "project.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExportAllTwiceProducesIdenticalBytes(string fileName)
    {
        var first = NewTempDirectory();
        var second = NewTempDirectory();
        try
        {
            using var data = ReadFixture(fileName);
            new ProjectContext(data, "input.win", "output.win", Path.Combine(first, "project.json"), "Test").ExportAll();
            new ProjectContext(data, "input.win", "output.win", Path.Combine(second, "project.json"), "Test").ExportAll();
            var a = Snapshot(first);
            var b = Snapshot(second);
            Assert.Equal(a.Keys.Order(StringComparer.Ordinal), b.Keys.Order(StringComparer.Ordinal));
            Assert.All(a, pair => Assert.Equal(pair.Value, b[pair.Key]));
        }
        finally
        {
            Directory.Delete(first, true);
            Directory.Delete(second, true);
        }
    }

    private static (string Directory, string Base, string Main) ExportWithBase(string fixture, out GameMakerData data)
    {
        var directory = NewTempDirectory();
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "base.win");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), basePath);
        var mainFile = Path.Combine(directory, "project", "project.json");
        data = ReadFixture(fixture);
        new ProjectContext(data, basePath, Path.Combine(directory, "out.win"), mainFile, "Test").ExportAll();
        return (directory, basePath, mainFile);
    }

    [Fact]
    public void ExportAllRecordsBaseFileSizeAndMd5()
    {
        (var directory, var basePath, var mainFile) = ExportWithBase("data-2022.win", out var data);
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(mainFile));
            var g3m = manifest.RootElement.GetProperty("G3M");
            Assert.Equal(new FileInfo(basePath).Length, g3m.GetProperty("BaseFileSize").GetInt64());
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(basePath))), g3m.GetProperty("BaseFileMd5").GetString());
        }
        finally
        {
            data.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ImportWarnsWhenBaseFileDiffersFromRecordedIdentity()
    {
        (var directory, var basePath, var mainFile) = ExportWithBase("data-2022.win", out var data);
        try
        {
            var matching = ProjectContext.CreateWithDataFilePaths(basePath, Path.Combine(directory, "out.win"), mainFile);
            matching.Import(existingFileBackup: new GameFileNoOpBackup());
            Assert.Empty(matching.Warnings);

            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2023.win"), basePath, true);
            List<string> reported = [];
            var other = ProjectContext.CreateWithDataFilePaths(basePath, Path.Combine(directory, "out2.win"), mainFile);
            other.ScriptWarningHandler = reported.Add;
            other.Import(existingFileBackup: new GameFileNoOpBackup());
            var item = Assert.Single(other.Warnings);
            Assert.Equal(other.Warnings, reported);
            Assert.Contains("base data file", item, StringComparison.Ordinal);
        }
        finally
        {
            data.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
