/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Decompiler.ControlFlow;

namespace G3MLib.Tests;

public class BinaryBranch_FindBinaryBranches_Switch
{
    [Fact]
    public void TestSwitchDefaultBreak()
    {
        var code = TestUtil.GetCode(
            """
            # switch (a) { default: break; }

            :[0]
            push.v self.a
            b [2]

            :[1]
            b [3]

            :[2]
            b [3]

            :[3]
            popz.v
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        Assert.Empty(branches);

        Assert.Empty(blocks[2].Instructions);
        var item = Assert.Single(blocks[2].Successors);
        Assert.IsType<BreakNode>(item);
        Assert.Equal([blocks[3]], blocks[2].Successors[0].Successors); // specifically, break goes to the following block, always
        Assert.Contains(blocks[3], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[3], ctx.SwitchContinueBlocks);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestSwitchCaseBreak()
    {
        var code = TestUtil.GetCode(
            """
            # switch (a) { case 1: break; case 2: break; }

            :[0]
            push.v self.a
            dup.v 0
            pushi.e 1
            cmp.i.v EQ
            bt [3]

            :[1]
            dup.v 0
            pushi.e 2
            cmp.i.v EQ
            bt [4]

            :[2]
            b [5]

            :[3]
            b [5]

            :[4]
            b [5]

            :[5]
            popz.v
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        Assert.Empty(branches);

        for (var i = 3; i <= 4; i++)
        {
            Assert.Empty(blocks[i].Instructions);
            var item = Assert.Single(blocks[i].Successors);
            Assert.IsType<BreakNode>(item);
            Assert.Equal([blocks[i + 1]], blocks[i].Successors[0].Successors);
        }
        Assert.Contains(blocks[5], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[5], ctx.SwitchContinueBlocks);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileSwitchDefaultContinue()
    {
        var code = TestUtil.GetCode(
            """
            # while (1) { switch(a) { default: continue; } }

            :[0]
            pushi.e 1
            conv.i.b
            bf [7]

            :[1]
            push.v self.a
            b [3]

            :[2]
            b [6]

            :[3]
            b [5]

            :[4]
            b [6]

            :[5]
            popz.v
            b [0]

            :[6]
            popz.v
            b [0]

            :[7]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WhileLoop>(item);
        var loop = (WhileLoop)loops[0];

        Assert.Empty(branches);

        Assert.Null(loop.ForLoopIncrementor);

        Assert.Contains(blocks[6], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[5], ctx.SwitchEndNodes);
        Assert.Contains(blocks[5], ctx.SwitchContinueBlocks);
        Assert.DoesNotContain(blocks[6], ctx.SwitchContinueBlocks);
        Assert.Contains(blocks[4], ctx.SwitchIgnoreJumpBlocks);
        Assert.DoesNotContain(blocks[5], ctx.SwitchIgnoreJumpBlocks);
        var item_2 = Assert.Single(blocks[3].Successors);
        Assert.IsType<ContinueNode>(item_2);
        Assert.Equal([blocks[4]], blocks[3].Successors[0].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileSwitchCaseContinue()
    {
        var code = TestUtil.GetCode(
            """
            # while (1) { switch(a) { case 1: continue; case 2: continue; } }

            :[0]
            pushi.e 1
            conv.i.b
            bf [9]

            :[1]
            push.v self.a
            dup.v 0
            pushi.e 1
            cmp.i.v EQ
            bt [4]

            :[2]
            dup.v 0
            pushi.e 2
            cmp.i.v EQ
            bt [5]

            :[3]
            b [8]

            :[4]
            b [7]

            :[5]
            b [7]

            :[6]
            b [8]

            :[7]
            popz.v
            b [0]

            :[8]
            popz.v
            b [0]

            :[9]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WhileLoop>(item);
        var loop = (WhileLoop)loops[0];

        Assert.Empty(branches);

        Assert.Null(loop.ForLoopIncrementor);

        Assert.Contains(blocks[8], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[7], ctx.SwitchEndNodes);
        Assert.Contains(blocks[7], ctx.SwitchContinueBlocks);
        Assert.DoesNotContain(blocks[8], ctx.SwitchContinueBlocks);
        Assert.Contains(blocks[3], ctx.SwitchIgnoreJumpBlocks);
        Assert.Contains(blocks[6], ctx.SwitchIgnoreJumpBlocks);
        Assert.DoesNotContain(blocks[7], ctx.SwitchIgnoreJumpBlocks);
        for (var i = 4; i <= 5; i++)
        {
            var item_2 = Assert.Single(blocks[i].Successors);
            Assert.IsType<ContinueNode>(item_2);
            Assert.Equal([blocks[i + 1]], blocks[i].Successors[0].Successors);
        }

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileSwitchCaseIfContinue()
    {
        var code = TestUtil.GetCode(
            """
            # while (1) { switch(a) { case 1: if (a) { continue; } } }

            :[0]
            pushi.e 1
            conv.i.b
            bf [8]

            :[1]
            push.v self.a
            dup.v 0
            pushi.e 1
            cmp.i.v EQ
            bt [3]

            :[2]
            b [7]

            :[3]
            push.v self.a
            conv.v.b
            bf [5]

            :[4]
            b [6]

            :[5]
            b [7]

            :[6]
            popz.v
            b [0]

            :[7]
            popz.v
            b [0]

            :[8]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WhileLoop>(item);
        var loop = (WhileLoop)loops[0];

        Assert.Null(loop.ForLoopIncrementor);

        var item_2 = Assert.Single(branches);
        var b = item_2;
        Assert.Equal(blocks[1], b.Predecessors[0]);
        Assert.Equal([blocks[5]], b.Successors);
        Assert.Equal(blocks[4], b.True);
        Assert.Equal(blocks[5], b.False);
        Assert.Null(b.Else);
        Assert.Empty(b.True.Predecessors);

        Assert.Contains(blocks[7], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[6], ctx.SwitchEndNodes);
        Assert.Contains(blocks[6], ctx.SwitchContinueBlocks);
        Assert.DoesNotContain(blocks[7], ctx.SwitchContinueBlocks);
        Assert.Contains(blocks[2], ctx.SwitchIgnoreJumpBlocks);
        Assert.Contains(blocks[5], ctx.SwitchIgnoreJumpBlocks);
        Assert.DoesNotContain(blocks[6], ctx.SwitchIgnoreJumpBlocks);
        var item_3 = Assert.Single(blocks[4].Successors);
        Assert.IsType<ContinueNode>(item_3);
        Assert.Empty(blocks[4].Successors[0].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileSwitchCaseNestedIfContinue()
    {
        var code = TestUtil.GetCode(
            """
            # while (1) { switch(a) { case 1: if (a) { if (b) { continue; } } break; case 2: break; } }

            :[0]
            pushi.e 1
            conv.i.b
            bf [12]

            :[1]
            push.v self.a
            dup.v 0
            pushi.e 1
            cmp.i.v EQ
            bt [4]

            :[2]
            dup.v 0
            pushi.e 2
            cmp.i.v EQ
            bt [8]

            :[3]
            b [11]

            :[4]
            push.v self.a
            conv.v.b
            bf [7]

            :[5]
            push.v self.b
            conv.v.b
            bf [7]

            :[6]
            b [10]

            :[7]
            b [11]

            :[8]
            b [11]

            :[9]
            b [11]

            :[10]
            popz.v
            b [0]

            :[11]
            popz.v
            b [0]

            :[12]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WhileLoop>(item);
        var loop = (WhileLoop)loops[0];

        Assert.Null(loop.ForLoopIncrementor);

        Assert.Equal(2, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];

        Assert.Equal(blocks[1], b0.Predecessors[0]);
        Assert.Equal([blocks[7]], b0.Successors);
        Assert.Equal(b1, b0.True);
        Assert.Equal(blocks[7], b0.False);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Empty(b1.Predecessors);
        Assert.Empty(b1.Successors);
        Assert.Equal(blocks[6], b1.True);
        Assert.Equal(blocks[7], b1.False);
        Assert.Null(b1.Else);
        Assert.Empty(b1.True.Predecessors);

        Assert.Contains(blocks[11], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[10], ctx.SwitchEndNodes);
        Assert.Contains(blocks[10], ctx.SwitchContinueBlocks);
        Assert.DoesNotContain(blocks[11], ctx.SwitchContinueBlocks);
        Assert.Contains(blocks[3], ctx.SwitchIgnoreJumpBlocks);
        Assert.Contains(blocks[9], ctx.SwitchIgnoreJumpBlocks);
        Assert.DoesNotContain(blocks[10], ctx.SwitchIgnoreJumpBlocks);

        var item_2 = Assert.Single(blocks[6].Successors);
        Assert.IsType<ContinueNode>(item_2);
        Assert.Empty(blocks[6].Successors[0].Successors);

        for (var i = 7; i <= 8; i++)
        {
            var item_3 = Assert.Single(blocks[i].Successors);
            Assert.IsType<BreakNode>(item_3);
            Assert.Equal([blocks[i + 1]], blocks[i].Successors[0].Successors);
        }

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestSwitchExitProblem()
    {
        var code = TestUtil.GetCode(
            """
            # switch (a) { default: return; }

            :[0]
            push.v self.a
            b [2]

            :[1]
            b [3]

            :[2]
            popz.v
            exit.i

            :[3]
            popz.v
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        Assert.Empty(loops);
        Assert.Empty(branches);

        Assert.Contains(blocks[3], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[3], ctx.SwitchContinueBlocks);
        Assert.DoesNotContain(blocks[2], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[2], ctx.SwitchContinueBlocks);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestSwitchExitProblem2()
    {
        var code = TestUtil.GetCode(
            """
            # switch (a)
            # {
            #     default:
            #         switch (b)
            #         {
            #             default:
            #                 break;
            #         }
            #         return;
            # }

            :[0]
            push.v self.a
            b [2]

            :[1]
            b [6]

            :[2]
            push.v self.b
            b [4]

            :[3]
            b [5]

            :[4]
            b [5]

            :[5]
            popz.v
            popz.v
            exit.i

            :[6]
            popz.v
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        Assert.Empty(loops);
        Assert.Empty(branches);

        Assert.Contains(blocks[5], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[5], ctx.SwitchContinueBlocks);
        Assert.Contains(blocks[6], ctx.SwitchEndNodes);
        Assert.DoesNotContain(blocks[6], ctx.SwitchContinueBlocks);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }
}

