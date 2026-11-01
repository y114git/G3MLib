/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Decompiler.ControlFlow;

namespace G3MLib.Tests;

public class Loop_FindLoops
{
    [Fact]
    public void TestNone()
    {
        var code = TestUtil.GetCode(
            """
            pushi.e 123
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Empty(loops);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleWhile()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.i
            pushi.e 10
            cmp.i.v LT
            bf [end]

            :[1]
            push.v self.i
            push.e 1
            add.i.v
            pop.v.v self.i
            b [0]

            :[end]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WhileLoop>(item);
        var loop0 = (WhileLoop)loops[0];
        Assert.Equal(loop0, fragments[0].Children[0]);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(blocks[1], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[2].Predecessors[0], loop0);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(blocks[1], loop0.Body);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleBreakContinueWhile()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.i
            pushi.e 10
            cmp.i.v LT
            bf [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [6]

            :[3]
            push.v self.b
            conv.v.b
            bf [5]

            :[4]
            b [0]

            :[5]
            push.v self.i
            push.e 1
            add.i.v
            pop.v.v self.i
            b [0]

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WhileLoop>(item);
        var loop0 = (WhileLoop)loops[0];
        Assert.Equal(loop0, fragments[0].Children[0]);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(blocks[5], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[6].Predecessors[0], loop0);
        Assert.Equal([blocks[4]], loop0.Predecessors);
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(blocks[1], loop0.Body);
        Assert.Equal([loop0.After], blocks[2].Successors);
        Assert.Equal([loop0], blocks[4].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestNestedWhile()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 0
            pop.v.i self.i

            :[1]
            push.v self.i
            pushi.e 10
            cmp.i.v LT
            bf [end]

            :[2]
            push.v self.j
            pushi.e 10
            cmp.i.v LT
            bf [4]

            :[3]
            push.v self.j
            push.e 1
            add.i.v
            pop.v.v self.j
            b [2]

            :[4]
            push.v self.i
            push.e 1
            add.i.v
            pop.v.v self.i
            b [1]

            :[end]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<WhileLoop>(loops[0]);
        var typed_2 = Assert.IsType<WhileLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal(loop0, fragments[0].Children[0].Successors[0]);
        Assert.Equal(loop1, loop0.Body);
        Assert.Equal(loop0, loop1.Parent);
        Assert.Equal(blocks[3], loop1.Body);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[2], loop1.Head);
        Assert.Empty(loop1.Predecessors);
        Assert.Equal(blocks[4], loop1.Successors[0]);
        Assert.Empty(blocks[4].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSequentialWhile()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 0
            pop.v.i self.i

            :[1]
            push.v self.i
            pushi.e 10
            cmp.i.v LT
            bf [3]

            :[2]
            push.v self.i
            push.e 1
            add.i.v
            pop.v.v self.i
            b [1]

            :[3]
            push.v self.i
            pushi.e 20
            cmp.i.v LT
            bf [end]

            :[4]
            push.v self.i
            push.e 1
            add.i.v
            pop.v.v self.i
            b [3]

            :[end]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<WhileLoop>(loops[0]);
        var typed_2 = Assert.IsType<WhileLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal(loop0, blocks[0].Successors[0]);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[3], loop1.Head);
        Assert.Equal(blocks[2], loop0.Body);
        Assert.Equal(blocks[4], loop1.Body);
        Assert.Equal(loop1, loop0.Successors[0]);
        Assert.Equal(loop0, loop1.Predecessors[0]);
        Assert.Empty(blocks[3].Predecessors);
        Assert.Equal(blocks[2], loop0.Tail);
        Assert.Equal(blocks[4], loop1.Tail);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Empty(loop1.Tail.Successors);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleDoUntil()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            push.e 1
            add.i.v
            pop.v.v self.a
            push.v self.a
            pushi.e 10
            cmp.i.v GTE
            bf [0]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<DoUntilLoop>(item);
        var loop0 = (DoUntilLoop)loops[0];
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(loop0, fragments[0].Children[0]);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(blocks[0], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal([blocks[1]], loop0.Successors);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleBreakContinueDoUntil()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            conv.v.b
            bf [2]

            :[1]
            b [5]

            :[2]
            push.v self.b
            conv.v.b
            bf [4]

            :[3]
            b [4]

            :[4]
            push.v self.a
            pushi.e 10
            cmp.i.v GTE
            bf [0]

            :[5]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<DoUntilLoop>(item);
        var loop0 = (DoUntilLoop)loops[0];
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(loop0, fragments[0].Children[0]);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(blocks[4], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal([blocks[5]], loop0.Successors);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Equal([loop0.After], blocks[1].Successors);
        Assert.Equal([loop0.Tail], blocks[3].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestNestedDoUntil()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.b
            push.e 1
            add.i.v
            pop.v.v self.b
            push.v self.b
            pushi.e 10
            cmp.i.v GTE
            bf [0]

            :[1]
            push.v self.a
            pushi.e 10
            cmp.i.v GTE
            bf [0]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<DoUntilLoop>(loops[0]);
        var typed_2 = Assert.IsType<DoUntilLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(loop0, loop1.Parent);
        Assert.Equal(blocks[0], loop1.Head);
        Assert.Equal(blocks[0], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Empty(loop1.Predecessors);
        Assert.Equal(blocks[1], loop1.Successors[0]);
        Assert.Empty(blocks[1].Successors);
        Assert.Equal(loop1, loop0.Head);
        Assert.Equal(blocks[1], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal(blocks[2], loop0.Successors[0]);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestNestedDoUntil2()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            push.e 1
            add.i.v
            pop.v.v self.a

            :[1]
            push.v self.b
            push.e 1
            add.i.v
            pop.v.v self.b
            push.v self.b
            pushi.e 10
            cmp.i.v GTE
            bf [1]

            :[2]
            push.v self.a
            pushi.e 10
            cmp.i.v GTE
            bf [0]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<DoUntilLoop>(loops[0]);
        var typed_2 = Assert.IsType<DoUntilLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(loop1, blocks[1].Parent);
        Assert.Equal(blocks[1], loop1.Head);
        Assert.Equal(blocks[1], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Equal([blocks[0]], loop1.Predecessors);
        Assert.Equal(blocks[2], loop1.Successors[0]);
        Assert.Empty(blocks[2].Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(blocks[2], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal(blocks[3], loop0.Successors[0]);
        Assert.Equal(loop0, blocks[0].Parent);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSequentialDoUntil()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            pushi.e 10
            cmp.i.v GTE
            bf [0]

            :[1]
            push.v self.b
            pushi.e 10
            cmp.i.v GTE
            bf [1]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<DoUntilLoop>(loops[0]);
        var typed_2 = Assert.IsType<DoUntilLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Empty(loop0.Predecessors);
        Assert.Equal([loop1], loop0.Successors);
        Assert.Equal([blocks[2]], loop1.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(blocks[0], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[1], loop1.Head);
        Assert.Equal(blocks[1], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Empty(loop1.Head.Predecessors);
        Assert.Empty(loop1.Tail.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestWhileDoUntil()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            pushi.e 10
            cmp.i.v LT
            bf [3]

            :[1]
            push.v self.b
            pushi.e 10
            cmp.i.v GTE
            bf [1]

            :[2]
            b [0]

            :[3]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<WhileLoop>(loops[0]);
        var typed_2 = Assert.IsType<DoUntilLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Equal(loop0, loop1.Parent);
        Assert.Empty(loop1.Predecessors);
        Assert.Equal([blocks[2]], loop1.Successors);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal([blocks[3]], loop0.Successors);
        Assert.Equal(blocks[1], loop1.Head);
        Assert.Equal(blocks[1], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Equal(blocks[0], loop0.Head);
        Assert.Equal(loop1, loop0.Body);
        Assert.Equal(blocks[2], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Empty(loop1.Head.Predecessors);
        Assert.Empty(loop1.Tail.Successors);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Empty(loop0.Body.Predecessors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestDoUntilWhile()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.b
            pushi.e 10
            cmp.i.v LT
            bf [2]

            :[1]
            b [0]

            :[2]
            push.v self.a
            pushi.e 10
            cmp.i.v GTE
            bf [0]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<DoUntilLoop>(loops[0]);
        var typed_2 = Assert.IsType<WhileLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal(loop0, loop1.Parent);
        Assert.Equal(fragments[0], loop0.Parent);
        Assert.Empty(loop1.Predecessors);
        Assert.Equal([blocks[2]], loop1.Successors);
        Assert.Empty(loop0.Predecessors);
        Assert.Equal([blocks[3]], loop0.Successors);
        Assert.Equal(loop1, loop0.Head);
        Assert.Equal(blocks[2], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[0], loop1.Head);
        Assert.Equal(blocks[1], loop1.Body);
        Assert.Equal(blocks[1], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Empty(loop1.Head.Predecessors);
        Assert.Empty(loop1.Body.Predecessors);
        Assert.Empty(loop1.Tail.Successors);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleRepeat()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 100
            dup.i 0
            push.i 0
            cmp.i.i LTE
            bt [2]

            :[1]
            push.i 1
            sub.i.i
            dup.i 0
            conv.i.b
            bt [1]

            :[2]
            popz.i
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<RepeatLoop>(item);
        var loop0 = (RepeatLoop)loops[0];
        Assert.Equal([loop0], blocks[0].Successors);
        Assert.Equal([blocks[2]], loop0.Successors);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[1], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        var item_2 = Assert.Single(blocks[0].Instructions);
        Assert.Equal(IGMInstruction.Opcode.PushImmediate, item_2.Kind);
        Assert.Empty(blocks[1].Instructions);
        Assert.Empty(blocks[2].Instructions);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleBreakContinueRepeat()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 100
            dup.i 0
            push.i 0
            cmp.i.i LTE
            bt [6]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [6]

            :[3]
            push.v self.b
            conv.v.b
            bf [5]

            :[4]
            b [5]

            :[5]
            push.i 1
            sub.i.i
            dup.i 0
            conv.i.b
            bt [1]

            :[6]
            popz.i
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<RepeatLoop>(item);
        var loop0 = (RepeatLoop)loops[0];
        Assert.Equal([loop0], blocks[0].Successors);
        Assert.Equal([blocks[6]], loop0.Successors);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[5], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        var item_2 = Assert.Single(blocks[0].Instructions);
        Assert.Equal(IGMInstruction.Opcode.PushImmediate, item_2.Kind);
        Assert.Empty(blocks[5].Instructions);
        Assert.Empty(blocks[6].Instructions);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Equal([loop0.After], blocks[2].Successors);
        Assert.Equal([loop0.Tail], blocks[4].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestNestedRepeat()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 100
            dup.i 0
            push.i 0
            cmp.i.i LTE
            bt [4]

            :[1]
            pushi.e 200
            dup.i 0
            push.i 0
            cmp.i.i LTE
            bt [3]

            :[2]
            push.i 1
            sub.i.i
            dup.i 0
            conv.i.b
            bt [2]

            :[3]
            popz.i
            push.i 1
            sub.i.i
            dup.i 0
            conv.i.b
            bt [1]

            :[4]
            popz.i
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<RepeatLoop>(loops[0]);
        var typed_2 = Assert.IsType<RepeatLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal([loop1], loop0.Head.Successors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[3], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[2], loop1.Head);
        Assert.Equal(blocks[2], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Equal([blocks[1]], loop1.Predecessors);
        Assert.Equal([blocks[3]], loop1.Successors);
        Assert.Equal([blocks[0]], loop0.Predecessors);
        Assert.Equal([blocks[4]], loop0.Successors);
        var item = Assert.Single(blocks[0].Instructions);
        Assert.Equal(IGMInstruction.Opcode.PushImmediate, item.Kind);
        var item_2 = Assert.Single(blocks[1].Instructions);
        Assert.Equal(IGMInstruction.Opcode.PushImmediate, item_2.Kind);
        Assert.Empty(blocks[2].Instructions);
        Assert.Empty(blocks[3].Instructions);
        Assert.Empty(blocks[4].Instructions);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleEmptyWith()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 123
            pushenv [1]

            :[1]
            popenv [1]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WithLoop>(item);
        var loop0 = (WithLoop)loops[0];
        Assert.Equal(blocks[0], loop0.Predecessors[0]);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[1], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Null(loop0.BreakBlock);
        Assert.Equal(blocks[2], loop0.Successors[0]);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleWith()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 123
            pushenv [2]

            :[1]
            pushi.e 123
            pop.v.i self.a

            :[2]
            popenv [1]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WithLoop>(item);
        var loop0 = (WithLoop)loops[0];
        Assert.Equal(blocks[0], loop0.Predecessors[0]);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[2], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Null(loop0.BreakBlock);
        Assert.Equal(blocks[3], loop0.Successors[0]);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleBreakWith()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 123
            pushenv [3]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [5]

            :[3]
            popenv [1]

            :[4]
            b [6]

            :[5]
            popenv <drop>

            :[6]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WithLoop>(item);
        var loop0 = (WithLoop)loops[0];
        Assert.Equal(blocks[0], loop0.Predecessors[0]);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[3], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[5], loop0.BreakBlock);
        Assert.Equal(blocks[6], loop0.Successors[0]);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Empty(loop0.BreakBlock.Predecessors);
        Assert.Empty(loop0.BreakBlock.Successors);
        Assert.Equal([loop0.After], blocks[2].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestSingleBreakContinueWith()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 123
            pushenv [5]

            :[1]
            push.v self.a
            conv.v.b
            bf [3]

            :[2]
            b [7]

            :[3]
            push.v self.b
            conv.v.b
            bf [5]

            :[4]
            b [5]

            :[5]
            popenv [1]

            :[6]
            b [8]

            :[7]
            popenv <drop>

            :[8]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        var item = Assert.Single(loops);
        Assert.IsType<WithLoop>(item);
        var loop0 = (WithLoop)loops[0];
        Assert.Equal(blocks[0], loop0.Predecessors[0]);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[5], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[7], loop0.BreakBlock);
        Assert.Equal(blocks[8], loop0.Successors[0]);
        Assert.Empty(loop0.Head.Predecessors);
        Assert.Empty(loop0.Tail.Successors);
        Assert.Empty(loop0.BreakBlock.Predecessors);
        Assert.Empty(loop0.BreakBlock.Successors);
        Assert.Equal([loop0.After], blocks[2].Successors);
        Assert.Equal([loop0.Tail], blocks[4].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestNestedWith()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 123
            pushenv [4]

            :[1]
            pushi.e 456
            pushenv [3]

            :[2]
            pushi.e 789
            pop.v.i self.a

            :[3]
            popenv [2]

            :[4]
            popenv [1]

            :[5]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<WithLoop>(loops[0]);
        var typed_2 = Assert.IsType<WithLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal([blocks[0]], loop0.Predecessors);
        Assert.Equal([blocks[5]], loop0.Successors);
        Assert.Equal([blocks[1]], loop1.Predecessors);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[4], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Null(loop0.BreakBlock);
        Assert.Equal(blocks[2], loop1.Head);
        Assert.Equal(blocks[3], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Null(loop1.BreakBlock);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestNestedBreakWith()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            pushi.e 123
            pushenv [7]

            :[1]
            pushi.e 456
            pushenv [3]

            :[2]
            pushi.e 789
            pop.v.i self.a
            b [5]

            :[3]
            popenv [2]

            :[4]
            b [6]

            :[5]
            popenv <drop>

            :[6]
            b [8]

            :[7]
            popenv [1]

            :[8]
            b [10]

            :[9]
            popenv <drop>

            :[10]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(2, loops.Count);
        var typed = Assert.IsType<WithLoop>(loops[0]);
        var typed_2 = Assert.IsType<WithLoop>(loops[1]);
        var loop0 = typed;
        var loop1 = typed_2;
        Assert.Equal([blocks[0]], loop0.Predecessors);
        Assert.Equal([blocks[10]], loop0.Successors);
        Assert.Equal([blocks[1]], loop1.Predecessors);
        Assert.Equal(blocks[1], loop0.Head);
        Assert.Equal(blocks[7], loop0.Tail);
        Assert.IsType<EmptyNode>(loop0.After);
        Assert.Empty(loop0.After.Successors);
        Assert.Equal(blocks[9], loop0.BreakBlock);
        Assert.Empty(loop0.BreakBlock.Predecessors);
        Assert.Empty(loop0.BreakBlock.Successors);
        Assert.Equal(blocks[2], loop1.Head);
        Assert.Equal(blocks[3], loop1.Tail);
        Assert.IsType<EmptyNode>(loop1.After);
        Assert.Empty(loop1.After.Successors);
        Assert.Equal(blocks[5], loop1.BreakBlock);
        Assert.Empty(loop1.BreakBlock.Predecessors);
        Assert.Empty(loop1.BreakBlock.Successors);
        Assert.Equal([loop1.After], blocks[2].Successors);
        Assert.Equal([loop0.After], blocks[6].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }

    [Fact]
    public void TestMultiWithBreak()
    {
        var code = TestUtil.GetCode(
            """
            :[0]
            push.v self.a
            conv.v.b
            bf [10]

            :[1]
            push.v self.b
            pushi.e -9
            pushenv [6]

            :[2]
            push.v self.c
            pushi.e -9
            pushenv [4]

            :[3]
            pushi.e 1
            pop.v.i self.d

            :[4]
            popenv [3]

            :[5]
            pushi.e 1
            pop.v.i self.e
            b [8]

            :[6]
            popenv [2]

            :[7]
            b [9]

            :[8]
            popenv <drop>

            :[9]
            b [12]

            :[10]
            push.v self.f
            pushi.e -9
            pushenv [11]

            :[11]
            popenv [11]

            :[12]
            """
        );
        DecompileContext ctx = new(code);
        var blocks = Block.FindBlocks(ctx);
        var fragments = Fragment.FindFragments(ctx);
        var loops = Loop.FindLoops(ctx);

        Assert.Equal(3, loops.Count);
        Assert.IsType<WithLoop>(loops[0]);
        Assert.IsType<WithLoop>(loops[1]);
        var typed = Assert.IsType<WithLoop>(loops[2]);
        var loop2 = typed;

        Assert.Equal([loop2], blocks[10].Successors);

        TestUtil.VerifyFlowDirections(blocks);
        TestUtil.VerifyFlowDirections(fragments);
        TestUtil.VerifyFlowDirections(loops);
    }
}

