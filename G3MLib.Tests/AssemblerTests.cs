using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class AssemblerTests
{
    [Fact("AssemblerTests.cs", 9)]
    public void AssembleBuildsInstructionsWithAndWithoutLookupCaches()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerFunction function = new()
        {
            Name = data.Strings.MakeString("test_function")
        };
        GameMakerVariable variable = new()
        {
            Name = data.Strings.MakeString("test_variable"),
            InstanceType = GameMakerInstruction.InstanceType.Self
        };
        data.Functions.Add(function);
        data.Variables.Add(variable);
        GameMakerInstruction direct = Assembler.AssembleOne("ret.d", data);
        Assert.Equal(GameMakerInstruction.Opcode.Ret, direct.Kind);
        Assert.Equal("ret.d", direct.ToString());
        Assert.Equal(0, Assembler.AssembleOne("push.i room0", data).ValueInt);
        Assert.Same(function, Assembler.AssembleOne("call.v test_function(argc=0)", data).ValueFunction);
        Assert.Equal("test string", Assembler.AssembleOne("push.s \"test string\"", data).ValueString.Resource.Content);
        Assert.Same(variable, Assembler.AssembleOne("push.v self.test_variable", data).ValueVariable);
        Assembler.SetLookupCaches(data);
        try
        {
            Assert.Single(Assembler.Assemble(":[entry]\nret.d", data));
            Assert.Equal(0, Assembler.AssembleOne("push.i room0", data).ValueInt);
            Assert.Same(function, Assembler.AssembleOne("call.v test_function(argc=0)", data).ValueFunction);
            Assert.Equal("test string", Assembler.AssembleOne("push.s \"test string\"", data).ValueString.Resource.Content);
            Assert.Same(variable, Assembler.AssembleOne("push.v self.test_variable", data).ValueVariable);
            using GameMakerData otherData = GameMakerData.CreateNew2026LTS();
            Assert.Equal(0, Assembler.AssembleOne("push.i room0", otherData).ValueInt);
        }
        finally
        {
            Assembler.ClearLookupCaches();
        }
    }

    [Fact("AssemblerTests.cs", 49)]
    public void AssembleResolvesLabelsAndSpecialBranchForms()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        List<GameMakerInstruction> instructions = Assembler.Assemble("""
			:[start]
			push.i 1
			bf [end]
			popenv <drop>
			:[end]
			chkindex
			ret.d
			""", data);

        Assert.Collection(instructions,
            instruction => Assert.Equal(GameMakerInstruction.Opcode.Push, instruction.Kind),
            instruction =>
            {
                Assert.Equal(GameMakerInstruction.Opcode.Bf, instruction.Kind);
                Assert.NotEqual(0, instruction.JumpOffset);
            },
            instruction => Assert.True(instruction.JumpOffsetPopenvExitMagic),
            instruction =>
            {
                Assert.Equal(GameMakerInstruction.Opcode.Break, instruction.Kind);
                Assert.Equal("chkindex.d", instruction.ToString());
            },
            instruction => Assert.Equal(GameMakerInstruction.Opcode.Ret, instruction.Kind));
    }
}
