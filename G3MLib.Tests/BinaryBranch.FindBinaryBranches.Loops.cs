/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Decompiler.ControlFlow;

namespace G3MLib.Tests;

public class BinaryBranch_FindBinaryBranches_Loops
{
    [Fact]
    public void TestWhileIfElse()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [5]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            pushi.e 1
            pop.v.i self.b
            b [4]

            :[3]
            pushi.e 1
            pop.v.i self.c

            :[4]
            b [0]

            :[5]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[5]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[4], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[4]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Successors);
        Assert.Equal(blocks[3], b.False);
        Assert.Equal(blocks[3], b.Else);
        Assert.Empty(blocks[3].Successors);
        Assert.Empty(b.True.Predecessors);
        Assert.Empty(b.Else.Predecessors);

        Assert.Equal(2, blocks[2].Instructions.Count);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileIfEmpty()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [3]

            :[1]
            push.v self.a
            conv.v.b
            bf [2]

            :[2]
            b [0]

            :[3]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[3]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[2], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[2]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.IsType<EmptyNode>(b.True);
        Assert.Empty(b.True.Successors);
        Assert.Equal(blocks[2], b.False);
        Assert.Null(b.Else);
        Assert.Empty(b.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileIfElseEmpty()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [4]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [3]

            :[3]
            b [0]

            :[4]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[4]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[3], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[3]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Successors);
        Assert.Equal(blocks[3], b.False);
        Assert.IsType<EmptyNode>(b.Else);
        Assert.Empty(b.True.Predecessors);
        Assert.Empty(b.Else.Predecessors);

        Assert.Empty(blocks[2].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileIfContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [4]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [0]

            :[3]
            b [0]

            :[4]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.True(loop.MustBeWhileLoop);
        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[4]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[3], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[3]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_3 = Assert.Single(blocks[2].Successors);
        Assert.IsType<ContinueNode>(item_3);
        var c = (ContinueNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Equal(blocks[3], b.False);
        Assert.Null(b.Else);
        Assert.Empty(b.True.Predecessors);

        Assert.Empty(blocks[2].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestNestedWhileIfContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [0]

            :[3]
            pushi.e 1
            conv.i.b
            bf [5]

            :[4]
            b [3]

            :[5]
            b [0]

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        Assert.Equal(2, loops.Count);
        var loop0 = (WhileLoop)loops[0];
        var loop1 = (WhileLoop)loops[1];

        var item = Assert.Single(branches);
        var b = item;

        Assert.True(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[6]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b, loop0.Body);
        Assert.Equal(blocks[5], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([loop1], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_2 = Assert.Single(blocks[2].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Equal(loop1, b.False);
        Assert.Null(b.Else);
        Assert.Empty(b.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileIfElseContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [4]

            :[2]
            b [0]

            :[3]
            b [5]

            :[4]
            b [0]

            :[5]
            b [0]

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.True(loop.MustBeWhileLoop);
        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[6]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[5], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[5]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_3 = Assert.Single(blocks[2].Successors);
        Assert.IsType<ContinueNode>(item_3);
        var c = (ContinueNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([blocks[3]], c.Successors);
        Assert.Equal([], blocks[3].Successors);
        Assert.Equal(blocks[4], b.False);
        Assert.Equal(blocks[4], b.Else);
        var typed = Assert.IsType<ContinueNode>(blocks[4].Successors[0]);
        c = typed;
        Assert.Equal([blocks[4]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Empty(b.Else.Predecessors);
        Assert.Empty(b.True.Predecessors);

        Assert.Empty(blocks[3].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestNestedWhileIfElseContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [8]

            :[1]
            push.v self.a
            conv.v.b
            bf [4]

            :[2]
            b [0]

            :[3]
            b [5]

            :[4]
            b [0]

            :[5]
            pushi.e 1
            conv.i.b
            bf [7]

            :[6]
            b [5]

            :[7]
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

        Assert.Equal(2, loops.Count);
        var loop0 = (WhileLoop)loops[0];
        var loop1 = (WhileLoop)loops[1];

        var item = Assert.Single(branches);
        var b = item;

        Assert.True(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[8]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b, loop0.Body);
        Assert.Equal(blocks[7], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([loop1], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_2 = Assert.Single(blocks[2].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([blocks[3]], c.Successors);
        Assert.Equal([], blocks[3].Successors);
        Assert.Equal(blocks[4], b.False);
        Assert.Equal(blocks[4], b.Else);
        var typed = Assert.IsType<ContinueNode>(blocks[4].Successors[0]);
        c = typed;
        Assert.Equal([blocks[4]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Empty(b.Else.Predecessors);
        Assert.Empty(b.True.Predecessors);

        Assert.Empty(blocks[3].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileIfBreak()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [4]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [4]

            :[3]
            b [0]

            :[4]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[4]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[3], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[3]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_3 = Assert.Single(blocks[2].Successors);
        Assert.IsType<BreakNode>(item_3);
        var c = (BreakNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Equal(blocks[3], b.False);
        Assert.Null(b.Else);
        Assert.Empty(b.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestNestedWhileIfBreak()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [6]

            :[3]
            pushi.e 1
            conv.i.b
            bf [5]

            :[4]
            b [3]

            :[5]
            b [0]

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        Assert.Equal(2, loops.Count);
        var loop0 = (WhileLoop)loops[0];
        var loop1 = (WhileLoop)loops[1];

        var item = Assert.Single(branches);
        var b = item;

        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[6]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b, loop0.Body);
        Assert.Equal(blocks[5], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([loop1], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_2 = Assert.Single(blocks[2].Successors);
        Assert.IsType<BreakNode>(item_2);
        var c = (BreakNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Equal(loop1, b.False);
        Assert.Null(b.Else);
        Assert.Empty(b.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileIfElseBreak()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [4]

            :[2]
            b [6]

            :[3]
            b [5]

            :[4]
            b [6]

            :[5]
            b [0]

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b = item_2;

        Assert.Equal([], loop.Predecessors);
        Assert.Equal([blocks[6]], loop.Successors);
        Assert.Equal(blocks[0], loop.Head);
        Assert.Equal(b, loop.Body);
        Assert.Equal(blocks[5], loop.Tail);
        Assert.IsType<EmptyNode>(loop.After);

        Assert.Equal(loop, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([blocks[5]], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_3 = Assert.Single(blocks[2].Successors);
        Assert.IsType<BreakNode>(item_3);
        var c = (BreakNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([blocks[3]], c.Successors);
        Assert.Equal([], blocks[3].Successors);
        Assert.Equal(blocks[4], b.False);
        Assert.Equal(blocks[4], b.Else);
        var typed = Assert.IsType<BreakNode>(blocks[4].Successors[0]);
        c = typed;
        Assert.Equal([blocks[4]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Empty(b.Else.Predecessors);
        Assert.Empty(b.True.Predecessors);

        Assert.Empty(blocks[3].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestNestedWhileIfElseBreak()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [8]

            :[1]
            push.v self.a
            conv.v.b
            bf [4]

            :[2]
            b [8]

            :[3]
            b [5]

            :[4]
            b [8]

            :[5]
            pushi.e 1
            conv.i.b
            bf [7]

            :[6]
            b [5]

            :[7]
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

        Assert.Equal(2, loops.Count);
        var loop0 = (WhileLoop)loops[0];
        var loop1 = (WhileLoop)loops[1];

        var item = Assert.Single(branches);
        var b = item;

        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[8]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b, loop0.Body);
        Assert.Equal(blocks[7], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b.Parent);
        Assert.Equal([], b.Predecessors);
        Assert.Equal([loop1], b.Successors);
        Assert.Equal(blocks[1], b.Condition);
        Assert.Equal(blocks[2], b.True);
        Assert.Empty(blocks[2].Instructions);
        var item_2 = Assert.Single(blocks[2].Successors);
        Assert.IsType<BreakNode>(item_2);
        var c = (BreakNode)blocks[2].Successors[0];
        Assert.Equal([blocks[2]], c.Predecessors);
        Assert.Equal([blocks[3]], c.Successors);
        Assert.Equal([], blocks[3].Successors);
        Assert.Equal(blocks[4], b.False);
        Assert.Equal(blocks[4], b.Else);
        var typed = Assert.IsType<BreakNode>(blocks[4].Successors[0]);
        c = typed;
        Assert.Equal([blocks[4]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Empty(b.Else.Predecessors);
        Assert.Empty(b.True.Predecessors);

        Assert.Empty(blocks[3].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWhileNestedIfContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 1
            conv.i.b
            bf [5]

            :[1]
            push.v self.a
            conv.v.b
            bf [4]

            :[2]
            push.v self.b
            conv.v.b
            bf [4]

            :[3]
            b [0]

            :[4]
            b [0]

            :[5]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop0 = (WhileLoop)item;

        Assert.Equal(2, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];

        Assert.True(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[5]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b0, loop0.Body);
        Assert.Equal(blocks[4], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b0.Parent);
        Assert.Equal([], b0.Predecessors);
        Assert.Equal([blocks[4]], b0.Successors);
        Assert.Equal(blocks[1], b0.Condition);
        Assert.Equal(b1, b0.True);
        Assert.Equal([], b1.Successors);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Equal(b0, b1.Parent);
        Assert.Equal([], b1.Predecessors);
        Assert.Equal([], b1.Successors);
        Assert.Equal(blocks[2], b1.Condition);
        Assert.Equal(blocks[3], b1.True);
        Assert.Empty(blocks[3].Instructions);
        var item_2 = Assert.Single(blocks[3].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[3].Successors[0];
        Assert.Equal([blocks[3]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Null(b1.Else);
        Assert.Empty(b1.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestForNestedIfContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.l 1
            conv.l.b
            bf [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [4]

            :[2]
            push.v self.b
            conv.v.b
            bf [4]

            :[3]
            b [5]

            :[4]
            pushi.e 1
            pop.v.i self.c

            :[5]
            b [0]

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop0 = (WhileLoop)item;

        Assert.Equal(2, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];

        Assert.Equal(blocks[5], loop0.ForLoopIncrementor);
        Assert.False(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[6]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b0, loop0.Body);
        Assert.Equal(blocks[5], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b0.Parent);
        Assert.Equal([], b0.Predecessors);
        Assert.Equal([blocks[4]], b0.Successors);
        Assert.Equal(blocks[1], b0.Condition);
        Assert.Equal(b1, b0.True);
        Assert.Equal([], b1.Successors);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Equal(b0, b1.Parent);
        Assert.Equal([], b1.Predecessors);
        Assert.Equal([], b1.Successors);
        Assert.Equal(blocks[2], b1.Condition);
        Assert.Equal(blocks[3], b1.True);
        Assert.Empty(blocks[3].Instructions);
        var item_2 = Assert.Single(blocks[3].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[3].Successors[0];
        Assert.Equal([blocks[3]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Null(b1.Else);
        Assert.Empty(b1.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestForNestedIfContinue2()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.l 1
            conv.l.b
            bf [7]

            :[1]
            push.v self.a
            conv.v.b
            bf [5]

            :[2]
            push.v self.b
            conv.v.b
            bf [4]

            :[3]
            b [6]

            :[4]
            pushi.e 1
            pop.v.i self.c

            :[5]
            pushi.e 1
            pop.v.i self.d

            :[6]
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
        var loop0 = (WhileLoop)item;

        Assert.Equal(2, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];

        Assert.Equal(blocks[6], loop0.ForLoopIncrementor);
        Assert.False(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[7]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b0, loop0.Body);
        Assert.Equal(blocks[6], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b0.Parent);
        Assert.Equal([], b0.Predecessors);
        Assert.Equal([blocks[5]], b0.Successors);
        Assert.Equal(blocks[1], b0.Condition);
        Assert.Equal(b1, b0.True);
        Assert.Empty(blocks[4].Successors);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Equal(b0, b1.Parent);
        Assert.Equal([], b1.Predecessors);
        Assert.Equal([blocks[4]], b1.Successors);
        Assert.Equal(blocks[2], b1.Condition);
        Assert.Equal(blocks[3], b1.True);
        Assert.Empty(blocks[3].Instructions);
        var item_2 = Assert.Single(blocks[3].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[3].Successors[0];
        Assert.Equal([blocks[3]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Null(b1.Else);
        Assert.Empty(b1.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestForNestedIfContinue3()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.l 1
            conv.l.b
            bf [8]

            :[1]
            push.v self.a
            conv.v.b
            bf [6]

            :[2]
            push.v self.b
            conv.v.b
            bf [4]

            :[3]
            b [7]

            :[4]
            push.v self.c
            conv.v.b
            bf [6]

            :[5]
            b [7]

            :[6]
            pushi.e 1
            pop.v.i self.d

            :[7]
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
        var loop0 = (WhileLoop)item;

        Assert.Equal(3, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];
        var b2 = branches[2];

        Assert.Equal(blocks[7], loop0.ForLoopIncrementor);
        Assert.False(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[8]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b0, loop0.Body);
        Assert.Equal(blocks[7], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b0.Parent);
        Assert.Equal([], b0.Predecessors);
        Assert.Equal([blocks[6]], b0.Successors);
        Assert.Equal(blocks[1], b0.Condition);
        Assert.Equal(b1, b0.True);
        Assert.Empty(b2.Successors);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Equal(b0, b1.Parent);
        Assert.Equal([], b1.Predecessors);
        Assert.Equal([b2], b1.Successors);
        Assert.Equal(blocks[2], b1.Condition);
        Assert.Equal(blocks[3], b1.True);
        Assert.Empty(blocks[3].Instructions);
        var item_2 = Assert.Single(blocks[3].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[3].Successors[0];
        Assert.Equal([blocks[3]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Null(b1.Else);
        Assert.Empty(b1.True.Predecessors);

        Assert.Equal([b1], b2.Predecessors);
        Assert.Equal(blocks[4], b2.Condition);
        Assert.Equal(blocks[5], b2.True);
        Assert.Empty(blocks[5].Instructions);
        var item_3 = Assert.Single(blocks[5].Successors);
        Assert.IsType<ContinueNode>(item_3);
        c = (ContinueNode)blocks[5].Successors[0];
        Assert.Equal([blocks[5]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Null(b2.Else);
        Assert.Empty(b2.True.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestForNestedIfElseContinue()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.l 1
            conv.l.b
            bf [8]

            :[1]
            push.v self.a
            conv.v.b
            bf [6]

            :[2]
            push.v self.b
            conv.v.b
            bf [5]

            :[3]
            b [7]

            :[4]
            b [6]

            :[5]
            b [7]

            :[6]
            pushi.e 1
            pop.v.i self.c

            :[7]
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
        var loop0 = (WhileLoop)item;

        Assert.Equal(2, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];

        Assert.Equal(blocks[7], loop0.ForLoopIncrementor);
        Assert.False(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[8]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b0, loop0.Body);
        Assert.Equal(blocks[7], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b0.Parent);
        Assert.Equal([], b0.Predecessors);
        Assert.Equal([blocks[6]], b0.Successors);
        Assert.Equal(blocks[1], b0.Condition);
        Assert.Equal(b1, b0.True);
        Assert.Empty(b1.Successors);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Equal(b0, b1.Parent);
        Assert.Equal([], b1.Predecessors);
        Assert.Equal([], b1.Successors);
        Assert.Equal(blocks[2], b1.Condition);
        Assert.Equal(blocks[3], b1.True);
        Assert.Empty(blocks[3].Instructions);
        var item_2 = Assert.Single(blocks[3].Successors);
        Assert.IsType<ContinueNode>(item_2);
        var c = (ContinueNode)blocks[3].Successors[0];
        Assert.Equal([blocks[3]], c.Predecessors);
        Assert.Equal([blocks[4]], c.Successors);
        Assert.Equal(blocks[5], b1.False);
        Assert.Equal(blocks[5], b1.Else);
        Assert.Empty(blocks[5].Instructions);
        var item_3 = Assert.Single(blocks[5].Successors);
        Assert.IsType<ContinueNode>(item_3);
        c = (ContinueNode)blocks[5].Successors[0];
        Assert.Equal([blocks[5]], c.Predecessors);
        Assert.Equal([], c.Successors);
        Assert.Empty(b1.True.Predecessors);
        Assert.Empty(b1.Else.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestWithBreak()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            conv.v.i
            pushenv [2]

            :[1]
            b [4]

            :[2]
            popenv [1]

            :[3]
            b [5]

            :[4]
            popenv <drop>

            :[5]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WithLoop>(item);

        Assert.Empty(branches);

        Assert.Empty(blocks[1].Instructions);
        var item_2 = Assert.Single(blocks[1].Successors);
        Assert.IsType<BreakNode>(item_2);
        Assert.Equal([blocks[2]], blocks[1].Successors[0].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestForNestedIfElseContinue2()
    {
        var code = TestUtil.GetCode(
            """
            # for (;;)
            # {
            #     if (a)
            #     {
            #         if (b)
            #         {
            #         }
            #         else
            #         {
            #             continue
            #         }
            #         c = 1
            #     }
            # }

            :[0]
            push.l 1
            conv.l.b
            bf [7]

            :[1]
            push.v self.a
            conv.v.b
            bf [6]

            :[2]
            push.v self.b
            conv.v.b
            bf [4]

            :[3]
            b [5]

            :[4]
            b [6]

            :[5]
            pushi.e 1
            pop.v.i self.c

            :[6]
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
        var loop0 = (WhileLoop)item;

        Assert.Equal(2, branches.Count);
        var b0 = branches[0];
        var b1 = branches[1];

        Assert.Equal(blocks[6], loop0.ForLoopIncrementor);
        Assert.False(loop0.MustBeWhileLoop);
        Assert.Equal([], loop0.Predecessors);
        Assert.Equal([blocks[7]], loop0.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(b0, loop0.Body);
        Assert.Equal(blocks[6], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);

        Assert.Equal(loop0, b0.Parent);
        Assert.Equal([], b0.Predecessors);
        Assert.Equal([blocks[6]], b0.Successors);
        Assert.Equal(blocks[1], b0.Condition);
        Assert.Equal(b1, b0.True);
        Assert.Null(b0.Else);
        Assert.Empty(b0.True.Predecessors);

        Assert.Empty(b1.Predecessors);
        Assert.Equal([blocks[5]], b1.Successors);
        Assert.Equal(blocks[2], b1.Condition);
        Assert.Equal(blocks[3], b1.True);
        Assert.Equal(blocks[4], b1.Else);
        Assert.Equal(blocks[4], b1.False);
        Assert.Empty(b1.True.Predecessors);
        Assert.Empty(b1.Else.Predecessors);
        Assert.Empty(blocks[3].Instructions);

        Assert.Empty(blocks[4].Instructions);
        var item_2 = Assert.Single(blocks[4].Successors);
        Assert.IsType<ContinueNode>(item_2);
        Assert.Empty(blocks[4].Successors[0].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }

    [Fact]
    public void TestIfWhile()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            conv.v.b
            bf [3]

            :[1]
            push.v self.b
            conv.v.b
            bf [3]

            :[2]
            b [1]

            :[3]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);
        Switch.FindSwitchStatements(ctx);
        var branches = BinaryBranch.FindBinaryBranches(ctx);

        var item = Assert.Single(loops);
        var loop0 = (WhileLoop)item;

        var item_2 = Assert.Single(branches);
        var b0 = item_2;

        Assert.Equal(loop0, b0.True);
        Assert.Null(b0.Else);
        Assert.Equal(blocks[3], b0.False);
        Assert.Empty(loop0.Predecessors);
        Assert.Empty(loop0.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
        TestUtil.VerifyFlowDirections(branches);
        TestUtil.EnsureNoRemainingJumps(ctx);
    }
}

