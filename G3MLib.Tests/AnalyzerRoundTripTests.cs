using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;

namespace G3MLib.Tests;

public sealed class AnalyzerRoundTripTests
{
    [Theory("AnalyzerRoundTripTests.cs", 10)]
    [InlineData(["return 123;"])]
    [InlineData(["return 1.5;"])]
    [InlineData(["return \"text\";"])]
    public void CompiledCodeDecompilesToTheOriginalStatement(string source)
    {
        GameContextMock gameContext = new();
        CompileContext compileContext = new(source, CompileScriptKind.ObjectEvent, null, gameContext);
        compileContext.Compile();
        Assert.False(compileContext.HasErrors);
        compileContext.Link();
        GMCode code = new("root", [.. compileContext.OutputInstructions.Cast<GMInstruction>()])
        {
            Length = compileContext.OutputLength
        };
        var result = new DecompileContext(gameContext, code).DecompileToString();
        Assert.Equal(source, result.Trim());
    }

    [Fact]
    public void UseSemicolonFalseOmitsTrailingSemicolonsOnSimpleStatements()
    {
        const string source = "var x = 1;\nx += 2;\nreturn x;";
        GameContextMock gameContext = new();
        CompileContext compileContext = new(source, CompileScriptKind.ObjectEvent, null, gameContext);
        compileContext.Compile();
        Assert.False(compileContext.HasErrors);
        compileContext.Link();
        GMCode code = new("root", [.. compileContext.OutputInstructions.Cast<GMInstruction>()])
        {
            Length = compileContext.OutputLength
        };

        var withSemicolons = new DecompileContext(gameContext, code, new DecompileSettings { UseSemicolon = true }).DecompileToString();
        var withoutSemicolons = new DecompileContext(gameContext, code, new DecompileSettings { UseSemicolon = false }).DecompileToString();

        Assert.Equal("var x = 1;\nx += 2;\nreturn x;", withSemicolons.Trim().ReplaceLineEndings("\n"));
        Assert.Equal("var x = 1\nx += 2\nreturn x", withoutSemicolons.Trim().ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("return 1;")]
    [InlineData("{\n    return 42;\n}")]
    [InlineData("if (1)\n    return 10;")]
    [InlineData("if (1)\n    return 10;\nelse\n    return 20;")]
    [InlineData("switch (1)\n{\n    case 1:\n        return 99;\n}")]
    public void ReturnStatementPrintsSemicolonWithUseSemicolonTrue(string source)
    {
        GameContextMock gameContext = new();
        CompileContext compileContext = new(source, CompileScriptKind.ObjectEvent, null, gameContext);
        compileContext.Compile();
        Assert.False(compileContext.HasErrors);
        compileContext.Link();
        GMCode code = new("test", [.. compileContext.OutputInstructions.Cast<GMInstruction>()])
        {
            Length = compileContext.OutputLength
        };

        var withSemicolons = new DecompileContext(gameContext, code, new DecompileSettings { UseSemicolon = true }).DecompileToString();
        Assert.Contains("return", withSemicolons, StringComparison.Ordinal);
        var lines = withSemicolons.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("return ", StringComparison.Ordinal))
            {
                Assert.EndsWith(";", trimmed);
            }
        }
    }

    [Theory]
    [InlineData("return 1;", "return 1")]
    [InlineData("{\n    return 42;\n}", "return 42")]
    public void ReturnStatementOmitsSemicolonWithUseSemicolonFalse(string source, string expectedReturn)
    {
        GameContextMock gameContext = new();
        CompileContext compileContext = new(source, CompileScriptKind.ObjectEvent, null, gameContext);
        compileContext.Compile();
        Assert.False(compileContext.HasErrors);
        compileContext.Link();
        GMCode code = new("test", [.. compileContext.OutputInstructions.Cast<GMInstruction>()])
        {
            Length = compileContext.OutputLength
        };

        var withoutSemicolons = new DecompileContext(gameContext, code, new DecompileSettings { UseSemicolon = false }).DecompileToString();
        Assert.Contains(expectedReturn, withoutSemicolons, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedReturn + ";", withoutSemicolons, StringComparison.Ordinal);
    }
}
