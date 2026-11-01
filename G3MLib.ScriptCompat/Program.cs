using System.Globalization;
using System.Text;
using G3MLib.DataFile;
using G3MLib.DataFile.Scripting;
using Microsoft.CodeAnalysis.Scripting;

// Compiles every .csx script under a folder against the G3MLib script host and runs the ones that need no dialog on a copy of a data file.
// Usage: G3MLib.ScriptCompat <scripts folder> <data file> <work folder> <report.md>
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: G3MLib.ScriptCompat <scripts folder> <data file> <work folder> <report.md>");
    return 2;
}

var scriptsSource = Path.GetFullPath(args[0]);
var fixture = Path.GetFullPath(args[1]);
var work = Path.GetFullPath(args[2]);
var reportPath = Path.GetFullPath(args[3]);
var timeout = TimeSpan.FromSeconds(60);

if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
var scriptsCopy = Path.Combine(work, "Scripts");
CopyDirectory(scriptsSource, scriptsCopy);
Directory.SetCurrentDirectory(work);

List<Row> rows = [];
foreach (var script in Directory.EnumerateFiles(scriptsCopy, "*.csx", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    var category = Path.GetFileName(Path.GetDirectoryName(script));
    var name = Path.GetFileName(script);
    Row row = new(name, category);
    rows.Add(row);

    try
    {
        ScriptRunner.Compile(script);
        row.Compiles = true;
    }
    catch (CompilationErrorException exception)
    {
        row.Reason = "compile: " + Shorten(string.Join("; ", exception.Diagnostics.Take(2).Select(d => d.Id + " " + d.GetMessage(CultureInfo.InvariantCulture) + " (line " + (d.Location.GetLineSpan().StartLinePosition.Line + 1) + ")")));
        continue;
    }
    catch (Exception exception)
    {
        row.Reason = "compile: " + Shorten(exception.Message);
        continue;
    }

    var runDirectory = Path.Combine(work, "runs", rows.Count.ToString("000", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(runDirectory);
    var dataPath = Path.Combine(runDirectory, Path.GetFileName(fixture));
    File.Copy(fixture, dataPath);
    using var data = GameMakerIO.Read(File.OpenRead(dataPath));
    CompatGlobals globals = new(data, dataPath, script) { ExePath = work, ScriptRootPath = work, CanSave = true };
    Directory.SetCurrentDirectory(runDirectory);
    var run = Task.Run(() => ScriptRunner.RunAsync(script, globals));
    if (!run.Wait(timeout))
    {
        row.Reason = "run: no result after " + timeout.TotalSeconds + " s";
        continue;
    }

    var result = run.Result;
    if (globals.DialogCalled is not null)
    {
        row.Runs = "needs dialog";
        row.Reason = globals.DialogCalled;
    }
    else if (result.Success)
    {
        row.Runs = "yes";
    }
    else
    {
        row.Runs = "no";
        row.Reason = "run: " + Explain(result.Error);
    }
}

Directory.SetCurrentDirectory(work);
WriteReport(reportPath, rows);
Console.WriteLine($"scripts {rows.Count}, compile {rows.Count(r => r.Compiles)}, run {rows.Count(r => r.Runs == "yes")}, dialog {rows.Count(r => r.Runs == "needs dialog")}, run failed {rows.Count(r => r.Runs == "no")}");
Environment.Exit(0);
return 0;

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
}

string Explain(string error)
{
    const string trace = "The current stacktrace:";
    var split = error.IndexOf(trace, StringComparison.Ordinal);
    if (split < 0) return Shorten(error);
    var type = error["Script execution failed: ".Length..error.IndexOf(':', "Script execution failed: ".Length)];
    return Shorten(type + " at " + error[(split + trace.Length)..]);
}

string Shorten(string text)
{
    var line = text.Replace('\r', ' ').Replace('\n', ' ').Replace("|", "/", StringComparison.Ordinal).Trim();
    return line.Length <= 200 ? line : line[..197] + "...";
}

static void WriteReport(string path, List<Row> rows)
{
    StringBuilder report = new();
    report.AppendLine("| Script | Category | Compiles | Runs | Reason |");
    report.AppendLine("| --- | --- | --- | --- | --- |");
    foreach (var row in rows)
        report.AppendLine(CultureInfo.InvariantCulture, $"| {row.Name} | {row.Category} | {(row.Compiles ? "yes" : "no")} | {row.Runs} | {row.Reason} |");
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    File.WriteAllText(path, report.ToString());
}

internal sealed class Row(string name, string category)
{
    public string Name { get; } = name;
    public string Category { get; } = category;
    public bool Compiles { get; set; }
    public string Runs { get; set; } = "-";
    public string Reason { get; set; } = "";
}

internal sealed class CompatGlobals(GameMakerData data, string filePath, string scriptPath) : ScriptGlobals(data, filePath, scriptPath)
{
    public string DialogCalled { get; private set; }

    private ScriptCancelledException Dialog(string name)
    {
        DialogCalled ??= name;
        return new ScriptCancelledException("Dialog needed: " + name);
    }

    public override bool ScriptQuestion(string message) => throw Dialog(nameof(ScriptQuestion));
    public override string ScriptInputDialog(string title, string label, string defaultInput, string cancelText, string submitText, bool isMultiline, bool preventClose) => throw Dialog(nameof(ScriptInputDialog));
    public override string SimpleTextInput(string title, string label, string defaultValue, bool allowMultiline, bool showDialog = true) => throw Dialog(nameof(SimpleTextInput));
    public override string PromptChooseDirectory() => throw Dialog(nameof(PromptChooseDirectory));
    public override string PromptLoadFile(string defaultExt, string filter) => throw Dialog(nameof(PromptLoadFile));
    public override string PromptSaveFile(string defaultExt, string filter) => throw Dialog(nameof(PromptSaveFile));
    public override void ScriptOpenURL(string url) => throw Dialog(nameof(ScriptOpenURL));
}
