using System.Linq;
using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;
using Xunit;

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
        string result = new DecompileContext(gameContext, code).DecompileToString();
        Assert.Equal(source, result.Trim());
    }
}
