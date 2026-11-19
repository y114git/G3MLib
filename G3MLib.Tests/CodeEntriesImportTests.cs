using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class CodeEntriesImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string WriteEntry(string name, string gml)
    {
        var dir = Path.Combine(_root, "CodeEntries", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".gml"), gml);
        return Path.Combine(_root, "CodeEntries");
    }

    [Fact]
    public void ImportsGmlFilesAndReportsEveryEntry()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var typeDir = WriteEntry("gml_Script_good", "var a = 1;");
        WriteEntry("gml_Script_bad", "var b = (;");
        Directory.CreateDirectory(Path.Combine(typeDir, "gml_Script_asm_only"));
        File.WriteAllText(Path.Combine(typeDir, "gml_Script_asm_only", "gml_Script_asm_only.asm"), "pop.v.v self.x");

        var results = ResourceImportService.Import("CodeEntries", data, _root, null, progress: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ResultKind.Written, results.Single(r => r.Name == "gml_Script_good").Kind);
        Assert.Equal(ResultKind.Failed, results.Single(r => r.Name == "gml_Script_bad").Kind);
        Assert.Contains("assemble", results.Single(r => r.Name == "gml_Script_asm_only").Message);
        Assert.NotNull(data.Code.ByName("gml_Script_good"));
    }

    [Fact]
    public void OnlyFilterImportsTheNamedEntry()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        WriteEntry("gml_Script_one", "var a = 1;");
        WriteEntry("gml_Script_two", "var a = 2;");

        var results = ResourceImportService.Import("CodeEntries", data, _root, ["gml_Script_two"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["gml_Script_two"], results.Select(r => r.Name));
        Assert.Null(data.Code.ByName("gml_Script_one"));
    }

    [Fact]
    public void OutsideABatchACompileErrorThrows()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var typeDir = WriteEntry("gml_Script_bad", "var b = (;");

        var error = Assert.Throws<InvalidOperationException>(() => ResourceImportService.Import("CodeEntries", data, typeDir));
        Assert.Contains("gml_Script_bad", error.Message);
    }

    private sealed class Reports : IProgress<BatchProgress>
    {
        public List<BatchProgress> Items { get; } = [];

        public void Report(BatchProgress value) => Items.Add(value);
    }

    [Fact]
    public void OnlyFilterExpectsOnlyTheMatchedFolders()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        WriteEntry("gml_Script_one", "var a = 1;");
        WriteEntry("gml_Script_two", "var a = 2;");
        Reports reports = new();

        ResourceImportService.Import("CodeEntries", data, _root, ["gml_Script_two"], reports, TestContext.Current.CancellationToken);

        Assert.All(reports.Items, report => Assert.Equal(1, report.Total));
    }
}
