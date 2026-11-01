using G3MLib.Analyzer;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;

namespace G3MLib.Tests;

public class DecompilerSourceMapTests
{
    private const string Asm = """
        :[0]
        pushi.e 123
        pop.v.i self.a
        push.v self.b
        conv.v.b
        bf [2]

        :[1]
        push.s "text"
        pop.v.s self.msg

        :[2]
        """;

    private static (DecompiledSource Source, int[] Addresses) Decompile()
    {
        GameContextMock game = new();
        var code = TestUtil.GetCode(Asm, game);
        var addresses = new int[code.InstructionCount];
        for (var i = 1; i < addresses.Length; i++)
        {
            addresses[i] = addresses[i - 1] + IGMInstruction.GetSize(code.GetInstruction(i - 1));
        }
        return (new DecompileContext(game, code).DecompileToSourceMap(), addresses);
    }

    [Fact]
    public void TextMatchesPlainDecompile()
    {
        GameContextMock game = new();
        var plain = new DecompileContext(game, TestUtil.GetCode(Asm, game)).DecompileToString();
        Assert.Equal(plain, Decompile().Source.Text);
    }

    [Fact]
    public void StatementsMapToTheirFirstInstruction()
    {
        (var source, var addresses) = Decompile();
        var lines = source.Text.Split('\n');
        var msgLine = Array.FindIndex(lines, l => l.Contains("msg", StringComparison.Ordinal)) + 1;

        var first = source.Map.FindStatementOnLine(1);
        Assert.Equal(0, first.FirstInstructionOffset);
        Assert.Equal(addresses[1], first.LastInstructionOffset);

        var msg = source.Map.FindStatementOnLine(msgLine);
        Assert.Equal(addresses[5], msg.FirstInstructionOffset);
        Assert.Equal(addresses[6], msg.LastInstructionOffset);
    }

    [Fact]
    public void TextOffsetFindsInnermostNode()
    {
        (var source, _) = Decompile();
        var literal = source.Map.FindAtTextOffset(source.Text.IndexOf("123", StringComparison.Ordinal));
        Assert.False(literal.IsStatement);
        Assert.Equal(0, literal.InstructionOffset);
        Assert.Equal("123", source.Text[literal.Span.StartOffset..literal.Span.EndOffset]);
        Assert.True(source.Map.Entries[literal.Parent].IsStatement);
    }

    [Fact]
    public void InstructionFindsTheStatementThatContainsIt()
    {
        (var source, var addresses) = Decompile();
        var entry = source.Map.FindAtInstruction(addresses[6]);
        Assert.StartsWith("msg =", source.Text[entry.Span.StartOffset..entry.Span.EndOffset], StringComparison.Ordinal);
    }
}
