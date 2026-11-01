using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;

namespace G3MLib.Tests;

public sealed class GmlProjectCompileTests
{
    // Compiles every .gml file of the GameMaker project in G3MLIB_GML_PROJECT as its own script, so one failure does not hide the rest.
    [Fact]
    public void EveryGmlSourceOfAConfiguredProjectCompiles()
    {
        var project = Environment.GetEnvironmentVariable("G3MLIB_GML_PROJECT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(project) && Directory.Exists(project), "G3MLIB_GML_PROJECT is not set or names no folder");

        using var data = GameMakerData.CreateNew2026LTS();
        List<string> failures = [];
        var count = 0;
        foreach (var file in Directory.GetFiles(project, "*.gml", SearchOption.AllDirectories))
        {
            CodeImportGroup group = new(data) { AutoCreateAssets = false };
            group.QueueReplace($"gml_Script_probe{count++}", File.ReadAllText(file));
            try
            {
                group.Import();
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetRelativePath(project, file)}: {e.Message.ReplaceLineEndings(" ")}");
            }
        }
        Assert.True(count > 0);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
