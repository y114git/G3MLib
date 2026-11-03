using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Interop;

namespace G3MLib.Tests;

public sealed class YyScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3m-yyscript-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    private static GameMakerData RoundTrip(GameMakerData data)
    {
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        return GameMakerIO.Read(stream);
    }

    private string WriteSource()
    {
        var folder = Path.Combine(_root, "scripts", "yy_script");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "yy_script.gml"), "function yy_script() {\n    yy_script_made_up_global = 1;\n}\n");
        File.WriteAllText(Path.Combine(folder, "yy_script.yy"), /*lang=json*/ """{"$GMScript":"v1","%Name":"yy_script","isCompatibility":false,"isDnD":false,"name":"yy_script","resourceType":"GMScript",}""");
        return Path.Combine(folder, "yy_script.yy");
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void ImportsCompilesAndWritesBack(string fixture)
    {
        using var data = Load(fixture);
        CodeImportGroup group = new(data);
        var name = YyScript.Read(WriteSource(), group);
        group.Import();

        // Functions in 2.3+ scripts are registered under their code entry name.
        Assert.NotNull(data.Scripts.ByName("gml_Script_" + name));
        Assert.Contains(data.Functions.ToList(), f => f.Name.Content == "gml_Script_" + name);
        Assert.Contains(data.Variables.ToList(), v => v.Name.Content == "yy_script_made_up_global");

        using var reloaded = RoundTrip(data);
        Assert.NotNull(reloaded.Scripts.ByName("gml_Script_" + name));
        var outFolder = Path.Combine(_root, "reloaded", fixture);
        YyScript.Write(name, reloaded, new GlobalDecompileContext(reloaded), outFolder, YyFormat.Lts2026);
        Assert.Contains("yy_script_made_up_global = 1;", File.ReadAllText(Path.Combine(outFolder, name + ".gml")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("data-2022.win", "gml_GlobalScript_f")]
    [InlineData("data-2022.win", "gml_Script_f")]
    [InlineData("data-2026.win", "gml_GlobalScript_f")]
    [InlineData("data-2026.win", "gml_Script_f")]
    public void CompiledFunctionScriptDecompilesAfterRoundTrip(string fixture, string entryName)
    {
        using var data = Load(fixture);
        CodeImportGroup group = new(data);
        group.QueueReplace(entryName, "function f(){ g = 1; }");
        Assert.True(group.Import().Successful);

        using var reloaded = RoundTrip(data);
        GlobalDecompileContext context = new(reloaded);
        var decompiled = new DecompileContext(context, reloaded.Code.ByName(entryName), reloaded.ToolInfo.DecompilerSettings).DecompileToString();
        Assert.Contains("function f()", decompiled, StringComparison.Ordinal);
        Assert.Contains("g = 1;", decompiled, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gml_GlobalScript_f")]
    [InlineData("gml_Script_f")]
    public void CompiledFunctionScriptOnFreshDataFileDecompilesAfterRoundTrip(string entryName)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        group.QueueReplace(entryName, "function f(){ g = 1; }");
        Assert.True(group.Import().Successful);

        using var reloaded = RoundTrip(data);
        GlobalDecompileContext context = new(reloaded);
        var decompiled = new DecompileContext(context, reloaded.Code.ByName(entryName), reloaded.ToolInfo.DecompilerSettings).DecompileToString();
        Assert.Contains("function f()", decompiled, StringComparison.Ordinal);
        Assert.Contains("g = 1;", decompiled, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void WritesAnExistingScript(string fixture)
    {
        using var data = Load(fixture);
        foreach (var format in Enum.GetValues<YyFormat>())
        {
            var folder = Path.Combine(_root, "out", format.ToString());
            YyScript.Write("Script1", data, new GlobalDecompileContext(data), folder, format);
            Assert.Contains("function Script1", File.ReadAllText(Path.Combine(folder, "Script1.gml")), StringComparison.Ordinal);
            Assert.Contains("\"resourceType\": \"GMScript\"", File.ReadAllText(Path.Combine(folder, "Script1.yy")), StringComparison.Ordinal);
        }
    }
}
