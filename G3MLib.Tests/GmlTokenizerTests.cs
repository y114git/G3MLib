using G3MLib.Analyzer.Compiler;

namespace G3MLib.Tests;

public class GmlTokenizerTests
{
    private static List<(GmlTokenKind Kind, string Text)> Tokens(string code, bool gms2 = true)
        => [.. GmlTokenizer.Tokenize(code, gms2).Select(t => (t.Kind, code[t.Span.StartOffset..t.Span.EndOffset]))];

    [Fact]
    public void ClassifiesCommonTokens()
    {
        Assert.Equal(
        [
            (GmlTokenKind.Keyword, "if"),
            (GmlTokenKind.Separator, "("),
            (GmlTokenKind.Identifier, "a"),
            (GmlTokenKind.Operator, ">="),
            (GmlTokenKind.Number, "0x1F"),
            (GmlTokenKind.Separator, ")"),
            (GmlTokenKind.Identifier, "b"),
            (GmlTokenKind.Operator, "??="),
            (GmlTokenKind.Boolean, "true"),
            (GmlTokenKind.Separator, ";"),
            (GmlTokenKind.Comment, "// note"),
            (GmlTokenKind.Preprocessor, "#macro"),
            (GmlTokenKind.Identifier, "M"),
            (GmlTokenKind.Number, ".5"),
        ], Tokens("if (a >= 0x1F) b ??= true; // note\n#macro M .5"));
    }

    [Fact]
    public void StringsAndComments()
    {
        Assert.Equal(
        [
            (GmlTokenKind.String, "\"a\\\"b\""),
            (GmlTokenKind.String, "@'x\ny'"),
            (GmlTokenKind.TemplateString, "$\"v{\"q\"}\""),
            (GmlTokenKind.Comment, "/* c\n*/"),
            (GmlTokenKind.Unknown, "`"),
        ], Tokens("\"a\\\"b\" @'x\ny' $\"v{\"q\"}\" /* c\n*/ `"));
    }

    [Fact]
    public void UnterminatedInputDoesNotThrow()
    {
        Assert.Equal((GmlTokenKind.String, "\"abc"), Tokens("\"abc\nx")[0]);
        Assert.Equal((GmlTokenKind.Comment, "/* abc"), Tokens("/* abc")[0]);
    }

    [Fact]
    public void SpansCarryLinesAndColumns()
    {
        var token = GmlTokenizer.Tokenize("a\n  bb")[1];
        Assert.Equal(2, token.Span.StartLine);
        Assert.Equal(3, token.Span.StartColumn);
        Assert.Equal(5, token.Span.EndColumn);
        Assert.Equal(4, token.Span.StartOffset);
    }
}
