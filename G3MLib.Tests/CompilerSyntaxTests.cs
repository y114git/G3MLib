using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;

namespace G3MLib.Tests;

public sealed class CompilerSyntaxTests
{
    private static void AssertCompiles(string source)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        group.QueueReplace("gml_Script_probe", source);
        group.Import();
        Assert.NotNull(data.Code.ByName("gml_Script_probe"));
    }

    [Fact]
    public void ReturnWithoutValueBeforeBlockClose()
    {
        AssertCompiles("function f(a)\n{\n\tif (a)\n\t{\n\t\treturn\n\t}\n\ta = 1;\n}");
    }

    [Fact]
    public void ReturnWithoutValueBeforeEnd()
    {
        AssertCompiles("if (argument0) begin return end");
    }

    [Fact]
    public void ArrayAccessOnFunctionCallResult()
    {
        AssertCompiles("var a = array_create(2)[0];\nvar b = array_create(2)[1];");
    }

    [Fact]
    public void ArrayAccessOnFunctionCallResultAsArgument()
    {
        AssertCompiles("show_debug_message(array_create(4, 0)[2]);");
    }

    [Fact]
    public void ChainedAccessOnFunctionCallResult()
    {
        AssertCompiles("var a = array_create(2)[0][1];");
    }

    [Fact]
    public void TemplateStringCompilesOnLts2026()
    {
        AssertCompiles("var n = 3;\nvar s = $\"WAVE {n}\";");
    }

    [Fact]
    public void TrailingCommaInArrayAndStructLiterals()
    {
        AssertCompiles("var a = [1, 2,];\nvar b = { x : 1, y : [3,], };");
    }
}
