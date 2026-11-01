using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Mock;

namespace G3MLib.Tests;

public class GmlDiagnosticsTests
{
    private static CompileContext Parse(string code)
    {
        CompileContext context = new(code, CompileScriptKind.Script, null, new GameContextMock());
        context.Parse();
        return context;
    }

    [Fact]
    public void ParserErrorHasSpanSeverityAndCode()
    {
        var context = Parse("a = 1;\nif (b {\n}");

        var diagnostic = context.Diagnostics[0];
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(GmlDiagnostic.SyntaxErrorCode, diagnostic.Code);
        Assert.StartsWith("Expected ')'", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("line", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(2, diagnostic.Span.StartLine);
        Assert.Equal(7, diagnostic.Span.StartColumn);
        Assert.Equal(7 + 1, diagnostic.Span.EndColumn);
        Assert.Equal(13, diagnostic.Span.StartOffset);
    }

    [Fact]
    public void LexerErrorHasSpan()
    {
        var context = Parse("a = 1;\nb = `;");

        var diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(GmlDiagnostic.LexerErrorCode, diagnostic.Code);
        Assert.Equal(2, diagnostic.Span.StartLine);
    }

    [Fact]
    public void ErrorAtEndOfCodeSpansTheEnd()
    {
        var context = Parse("if (a) {");

        var diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(8, diagnostic.Span.StartOffset);
        Assert.Equal(0, diagnostic.Span.Length);
    }

    [Fact]
    public void LineMapMapsOffsets()
    {
        LineMap map = new("ab\ncd\n");
        Assert.Equal((1, 1), map.GetPosition(0));
        Assert.Equal((1, 3), map.GetPosition(2));
        Assert.Equal((2, 1), map.GetPosition(3));
        Assert.Equal((3, 1), map.GetPosition(6));
        Assert.Equal(3, map.LineCount);
    }
}
