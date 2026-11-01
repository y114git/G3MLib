using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;

namespace G3MLib.Tests;

public class DecompilerSourceMapDataFileTests
{
    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void OffsetsAgreeWithTheDisassembly(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        using var data = GameMakerIO.Read(input);
        GlobalDecompileContext global = new(data);
        var decompiled = 0;
        foreach (var code in data.Code)
        {
            if (code.ParentEntry is not null || code.Instructions.Count == 0)
            {
                continue;
            }
            var source = new DecompileContext(global, code).DecompileToSourceMap();
            code.Disassemble(data.Variables, data.CodeLocals?.For(code), true, out var lines);
            HashSet<int> offsets = [.. lines.Select(l => l.InstructionOffset)];
            foreach (var entry in source.Map.Entries)
            {
                Assert.InRange(entry.Span.EndOffset, 1, source.Text.Length);
                if (entry.InstructionOffset >= 0)
                {
                    Assert.Contains(entry.InstructionOffset, offsets);
                }
            }
            decompiled++;
        }
        Assert.True(decompiled > 0);
    }
}
