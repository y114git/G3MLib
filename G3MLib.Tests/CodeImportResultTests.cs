using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class CodeImportResultTests
{
    private sealed class SyncProgress(Action<BatchProgress> onReport) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => onReport(value);
    }

    private static GameMakerData WithScript(string name, string source)
    {
        var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        group.QueueReplace(name, source);
        group.Import();
        return data;
    }

    [Fact]
    public void ResultListsEveryOperationInOrder()
    {
        using var data = WithScript("gml_Script_a", "var first_value = 1;");
        CodeImportGroup group = new(data);
        group.QueueReplace("gml_Script_a", "var second_value = 2;");
        group.QueueFindReplace("gml_Script_a", "no_such_text", "zz");
        group.QueueAppend("gml_Script_b", "var third_value = 3;");

        var result = group.Import(CodeImportMode.Apply, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Replace", "FindReplace", "Append"], result.Operations.Select(o => o.Operation));
        Assert.Equal([CodeOperationStatus.Applied, CodeOperationStatus.NoMatch, CodeOperationStatus.Applied], result.Operations.Select(o => o.Status));
        Assert.True(result.Committed);
    }

    [Fact]
    public void CompileErrorGivesFailedWithLineAndColumn()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        group.QueueReplace("gml_Script_ok", "var a = 1;");
        group.QueueReplace("gml_Script_bad", "var a = 1;\nvar b = (;");

        var result = group.Import(CodeImportMode.Apply, cancellationToken: TestContext.Current.CancellationToken);

        var bad = result.Operations.Single(o => o.CodeEntry == "gml_Script_bad");
        Assert.Equal(CodeOperationStatus.Failed, bad.Status);
        Assert.Equal(2, bad.Line);
        Assert.True(bad.Column > 0);
        Assert.False(string.IsNullOrEmpty(bad.Message));
        Assert.Equal(CodeOperationStatus.Applied, result.Operations.Single(o => o.CodeEntry == "gml_Script_ok").Status);
        Assert.False(result.Compile.Successful);
    }

    [Fact]
    public void DryRunChangesNothing()
    {
        using var data = WithScript("gml_Script_a", "var first_value = 1;");
        var code = data.Code.ByName("gml_Script_a");
        var length = code.Length;
        var functions = data.Functions.Count;
        var variables = data.Variables.Count;
        var strings = data.Strings.Count;
        CodeImportGroup group = new(data) { AutoCreateAssets = false };
        group.QueueReplace(code, "function g() { return some_new_name + 12345; }\nvar local_one = \"fresh text\";");

        var result = group.Import(CodeImportMode.DryRun, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Compile.Successful);
        Assert.False(result.Committed);
        Assert.Equal(CodeOperationStatus.Applied, Assert.Single(result.Operations).Status);
        Assert.Equal((length, functions, variables, strings), (code.Length, data.Functions.Count, data.Variables.Count, data.Strings.Count));
    }

    [Fact]
    public void StageAppliesNothingWhenOneOperationFails()
    {
        using var data = WithScript("gml_Script_a", "var first_value = 1;");
        var code = data.Code.ByName("gml_Script_a");
        var length = code.Length;
        CodeImportGroup group = new(data);
        group.QueueReplace(code, "var long_replacement = 1;\nvar another = 2;\nvar third = 3;");
        group.QueueReplace("gml_Script_bad", "var b = (;");

        var result = group.Import(CodeImportMode.Stage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Committed);
        Assert.Equal(length, code.Length);
        Assert.Contains(result.Operations, o => o.Status == CodeOperationStatus.Failed);
    }

    [Fact]
    public void StageCommitsWhenEverythingCompiles()
    {
        using var data = WithScript("gml_Script_a", "var first_value = 1;");
        var code = data.Code.ByName("gml_Script_a");
        var length = code.Length;
        CodeImportGroup group = new(data);
        group.QueueReplace(code, "var long_replacement = 1;\nvar another = 2;\nvar third = 3;");

        var result = group.Import(CodeImportMode.Stage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Committed);
        Assert.NotEqual(length, code.Length);
    }

    [Fact]
    public void ImportReportsProgressAndHonoursCancellation()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        List<BatchProgress> reports = [];
        CodeImportGroup group = new(data);
        group.QueueReplace("gml_Script_a", "var a = 1;");
        group.QueueReplace("gml_Script_b", "var b = 1;");
        group.Import(CodeImportMode.Apply, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);
        Assert.Equal(2, reports.Max(r => r.Done));
        Assert.Equal(2, reports.Max(r => r.Total));

        using CancellationTokenSource source = new();
        source.Cancel();
        group.QueueReplace("gml_Script_c", "var c = 1;");
        Assert.ThrowsAny<OperationCanceledException>(() => group.Import(CodeImportMode.Apply, null, source.Token));
    }

    [Fact]
    public void OldImportStillThrowsOnCompileErrors()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        group.QueueReplace("gml_Script_bad", "var b = (;");

        Assert.Throws<InvalidOperationException>(() => group.Import(true));
    }
}
