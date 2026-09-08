/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using G3MLib.Analyzer.Decompiler.AST;

namespace G3MLib.Analyzer.Decompiler.ControlFlow;

/// <summary>
/// Represents a loop (jump/branch backwards) node in a control flow graph.
/// </summary>
internal abstract class Loop(int startAddress, int endAddress, int index) : IControlFlowNode
{
    public int StartAddress { get; private set; } = startAddress;

    public int EndAddress { get; private set; } = endAddress;

    public List<IControlFlowNode> Predecessors { get; } = [];

    public List<IControlFlowNode> Successors { get; } = [];

    public IControlFlowNode? Parent { get; set; } = null;

    /// <summary>
    /// The child nodes of this loop, those being the constituent parts (such as loop head, tail, and so on).
    /// </summary>
    public abstract List<IControlFlowNode?> Children { get; }

    public bool Unreachable { get; set; } = false;

    /// <summary>
    /// The index of this loop, as assigned in order of when the loop was first discovered (from top to bottom of code).
    /// </summary>
    public int LoopIndex { get; } = index;

    /// <summary>
    /// Called to insert a given loop's node into the control flow graph.
    /// </summary>
    public abstract void UpdateFlowGraph();

    protected IControlFlowNode InsertLoopStructure(IControlFlowNode head, IControlFlowNode after)
    {
        var oldAfter = after;
        var newAfter = new EmptyNode(after.StartAddress);
        IControlFlowNode.InsertPredecessors(after, newAfter, head.EndAddress);
        newAfter.Parent = this;
        IControlFlowNode.InsertStructure(head, oldAfter, this);
        Parent = head.Parent;
        head.Parent = this;
        return newAfter;
    }

    /// <summary>
    /// Finds all loops present in a list of blocks, and updates the control flow graph with special nodes for loops.
    /// </summary>
    public static List<Loop> FindLoops(DecompileContext ctx)
    {
        List<Block> blocks = ctx.Blocks!;

        List<Loop> loops = [];
        HashSet<int> whileLoopsFound = [];
        int loopIndex = 0;

        // Search for different loop types based on instruction patterns
        // Do this in reverse order, because we want to find the ends of loops first
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            ctx.ThrowIfCancellationRequested();
            Block block = blocks[i];

            // If empty, we don't care about the block
            if (block.Instructions.Count == 0)
            {
                continue;
            }

            // Check last instruction (where branches are located)
            IGMInstruction instr = block.Instructions[^1];
            switch (instr.Kind)
            {
                case IGMInstruction.Opcode.Branch:
                    if (instr.BranchOffset < 0)
                    {
                        // While loop detected - only add if this is the first time we see it (in reverse)
                        // If not done only once, then this misfires on "continue" statements
                        int conditionAddr = (block.EndAddress - 4) + instr.BranchOffset;
                        if (whileLoopsFound.Add(conditionAddr))
                        {
                            loops.Add(new WhileLoop(conditionAddr, block.EndAddress, loopIndex++,
                                block.Successors[0], block, blocks[block.BlockIndex + 1]));
                        }
                    }
                    break;
                case IGMInstruction.Opcode.BranchFalse:
                    if (instr.BranchOffset < 0)
                    {
                        // Do...until loop detected
                        loops.Add(new DoUntilLoop(block.Successors[1].StartAddress, block.EndAddress, loopIndex++,
                            block.Successors[1], block, block.Successors[0]));
                    }
                    break;
                case IGMInstruction.Opcode.BranchTrue:
                    if (instr.BranchOffset < 0)
                    {
                        // Repeat loop detected
                        loops.Add(new RepeatLoop(block.Successors[1].StartAddress, block.EndAddress, loopIndex++,
                            block.Successors[1], block, block.Successors[0]));
                    }
                    break;
                case IGMInstruction.Opcode.PushWithContext:
                    {
                        // With loop detected - need to additionally check for break block
                        Block afterBlock = (block.Successors[1].Successors[0] as Block)!;
                        Block? breakBlock = null;
                        if (afterBlock.Instructions is [{ Kind: IGMInstruction.Opcode.Branch }])
                        {
                            Block potentialBreakBlock = blocks[afterBlock.BlockIndex + 1];
                            if (potentialBreakBlock.EndAddress == afterBlock.Successors[0].StartAddress &&
                                potentialBreakBlock.Instructions is
                                    [{ Kind: IGMInstruction.Opcode.PopWithContext, PopWithContextExit: true }])
                            {
                                breakBlock = potentialBreakBlock;
                            }
                        }
                        loops.Add(new WithLoop(block.EndAddress, block.Successors[1].StartAddress, loopIndex++,
                            block, block.Successors[0], block.Successors[1], afterBlock, breakBlock));
                    }
                    break;
            }
        }

        // Update control flow graph to include the new loops
        loops.Sort((a, b) =>
        {
            if (a.StartAddress < b.StartAddress)
                return -1;
            if (a.StartAddress > b.StartAddress)
                return 1;
            if (b.EndAddress < a.EndAddress)
                return -1;
            if (b.EndAddress > a.EndAddress)
                return 1;
            return b.LoopIndex - a.LoopIndex;
        });
        foreach (var loop in loops)
        {
            ctx.ThrowIfCancellationRequested();
            loop.UpdateFlowGraph();
        }

        ctx.LoopNodes = loops;
        return loops;
    }

    public abstract void BuildAST(ASTBuilder builder, List<IStatementNode> output);
}

internal abstract class ThreePartLoop(int startAddress, int endAddress, int index, IControlFlowNode head, IControlFlowNode tail, IControlFlowNode after)
    : Loop(startAddress, endAddress, index)
{
    public override List<IControlFlowNode?> Children { get; } = [head, tail, after];
    public IControlFlowNode Head { get => Children[0]!; protected set => Children[0] = value; }
    public IControlFlowNode Tail { get => Children[1]!; protected set => Children[1] = value; }
    public IControlFlowNode After { get => Children[2]!; protected set => Children[2] = value; }
}
