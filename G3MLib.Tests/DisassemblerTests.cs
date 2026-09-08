using System.Collections.Generic;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class DisassemblerTests
{
    [Fact("DisassemblerTests.cs", 8)]
    public void EmptyCodeReportsMissingLocalsUnlessIgnored()
    {
        GameMakerCode code = new();
        Assert.Equal("; WARNING: Missing code locals, possibly due to unsupported bytecode version or a brand new code entry.\n\n:[end]", code.Disassemble([], null));
        Assert.Equal(":[end]", code.Disassemble([], null, ignoreMissingCodeLocals: true));
        Assert.Empty(Disassembler.FindBlockAddresses(code));
        code.Instructions.Add(new GameMakerInstruction
        {
            Kind = GameMakerInstruction.Opcode.Ret
        });
        Assert.Equal([0u, 1u], Disassembler.FindBlockAddresses(code).Keys);
    }
}
