using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class GlobalDecompileContextTests
{
    [Fact]
    public void GlobalFunctionIndexKeepsTheFirstFunctionWithADuplicateName()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var first = new GameMakerFunction { Name = new("gml_Script_example") };
        data.Functions.Add(first);
        data.Functions.Add(new() { Name = new("gml_Script_example") });
        data.Scripts.Add(new()
        {
            Name = new("example"),
            Code = new() { Name = new("gml_Script_example") }
        });
        GlobalDecompileContext.BuildGlobalFunctionCache(data);
        Assert.True(data.GlobalFunctions.TryGetFunction("example", out var actual));
        Assert.Same(first, actual);
    }
}
