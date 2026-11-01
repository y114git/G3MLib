using G3MLib.DataFile;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Scripting;

namespace G3MLib.Tests;

public sealed class ScriptGlobalsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");

    public ScriptGlobalsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteScript(string name, string source)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, source);
        return path;
    }

    [Fact]
    public async Task ScriptSeesTheInterfaceMembersAndHostHandlers()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        List<string> messages = [];
        ScriptGlobals globals = new(data, "data.win") { MessageHandler = messages.Add };
        var script = WriteScript("a.csx", "EnsureDataLoaded(); ScriptMessage(Data.Rooms.Count.ToString() + FilePath); SetProgressBar(\"x\", \"y\", 5, 10); IncrementProgress();");

        var result = await ScriptRunner.RunAsync(script, globals);

        Assert.True(result.Success, result.Error);
        Assert.Equal([data.Rooms.Count + "data.win"], messages);
        Assert.Equal(6, globals.GetProgress());
        Assert.True(globals.ScriptExecutionSuccess);
    }

    [Fact]
    public async Task ScriptsWrittenForUndertaleModToolAreTranslated()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        ScriptGlobals globals = new(data);
        var script = WriteScript("utmt.csx", "using UndertaleModLib.Models;\nUndertaleData d = Data;\nif (d.Rooms.Count < 0) ScriptError(\"no\");\nScriptMessage(typeof(UndertaleRoom).Name);");
        List<string> messages = [];
        globals.MessageHandler = messages.Add;

        var result = await ScriptRunner.RunAsync(script, globals);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["GameMakerRoom"], messages);
    }

    [Fact]
    public async Task ScriptErrorAndMissingDataAreReported()
    {
        ScriptGlobals globals = new();

        var noData = await ScriptRunner.RunAsync(WriteScript("nodata.csx", "EnsureDataLoaded();"), globals);
        var error = await ScriptRunner.RunAsync(WriteScript("error.csx", "ScriptError(\"stop\", \"Title\");"), globals);
        Assert.Equal("No data file is currently loaded!", noData.Error);
        Assert.Equal("stop", error.Error);
        Assert.False(globals.ScriptExecutionSuccess);
        Assert.Equal("Title", globals.ScriptErrorType);
        var broken = await ScriptRunner.RunAsync(WriteScript("broken.csx", "int x = ;"), globals);
        Assert.StartsWith("Compilation error:", broken.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DialogsNeedAHost()
    {
        ScriptGlobals globals = new();

        var result = await ScriptRunner.RunAsync(WriteScript("dialog.csx", "PromptChooseDirectory();"), globals);

        Assert.False(result.Success);
        Assert.Contains("UI host", result.Error, StringComparison.Ordinal);
        Assert.False(globals.ScriptQuestion("ok?"));
    }

    [Fact]
    public async Task NestedScriptsRunInsideTheRootOnly()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "mod"));
        File.WriteAllText(Path.Combine(_dir, "mod", "child.csx"), "ScriptMessage(\"child\");");
        File.WriteAllText(Path.Combine(_dir, "outside.csx"), "ScriptMessage(\"outside\");");
        List<string> messages = [];
        ScriptGlobals globals = new() { MessageHandler = messages.Add };

        var ok = await ScriptRunner.RunAsync(WriteScript(Path.Combine("mod", "parent.csx"), "RunUMTScript(\"child.csx\");"), globals);
        var escape = await ScriptRunner.RunAsync(WriteScript(Path.Combine("mod", "escape.csx"), "RunUMTScript(\"../outside.csx\");"), globals);

        Assert.True(ok.Success, ok.Error);
        Assert.Equal(["child"], messages);
        Assert.Equal("Nested script path escapes the script directory.", escape.Error);
    }

    [Fact]
    public void FilesAreComparedByContent()
    {
        var a = WriteScript("a.bin", "same");
        var b = WriteScript("b.bin", "same");
        var c = WriteScript("c.bin", "diff");

        ScriptGlobals globals = new();

        Assert.True(globals.AreFilesIdentical(a, b));
        Assert.False(globals.AreFilesIdentical(a, c));
    }

    [Fact]
    public void ProjectContextRunsTranslatedScriptsThroughTheSameHost()
    {
        var script = WriteScript("project.csx", "UndertaleData d = null;\nScriptMessage(\"ran\" + (d is null));");
        var context = ProjectContext.CreateWithDataFilePaths("input.win", "output.win", Path.Combine(_dir, "project.json"));
        List<string> messages = [];
        context.ScriptMessageHandler = messages.Add;

        Assert.True(context.RunUMTScript(script), context.ScriptErrorMessage);
        Assert.Equal(["ranTrue"], messages);
        Assert.True(context.LintUMTScript(script));
        Assert.False(context.LintUMTScript(WriteScript("broken.csx", "int x = ;")));
        Assert.Equal("CompilationErrorException", context.ScriptErrorType);
    }
}
