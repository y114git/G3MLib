/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using G3MLib.Analyzer.Decompiler.AST;

namespace G3MLib.Analyzer.Decompiler.ControlFlow;

/// <summary>
/// Represents a "repeat" loop in a control flow graph.
/// </summary>
internal sealed class RepeatLoop(int startAddress, int endAddress, int index, IControlFlowNode head, IControlFlowNode tail, IControlFlowNode after)
    : ThreePartLoop(startAddress, endAddress, index, head, tail, after)
{
    public override void UpdateFlowGraph()
    {
        // Get rid of branch (and unneeded logic) from branch into Head
        // The (first) predecessor of Head should always be a Block, as it has logic
        Block headPred = Head.Predecessors[0] as Block ?? throw new DecompilerException("Expected first predecessor to be block");
        headPred.Instructions.RemoveRange(headPred.Instructions.Count - 4, 4);
        IControlFlowNode.DisconnectSuccessor(headPred, 1);

        // Get rid of jumps (and unneeded logic) from Tail
        IControlFlowNode.DisconnectSuccessor(Tail, 1);
        IControlFlowNode.DisconnectSuccessor(Tail, 0);
        Block tailBlock = Tail as Block ?? throw new DecompilerException("Expected Tail to be block");
        if (tailBlock.Instructions is
            [.., { Kind: IGMInstruction.Opcode.Convert }, { Kind: IGMInstruction.Opcode.BranchTrue }])
        {
            // We have a Convert instruction before branching at the end (older GML output)
            tailBlock.Instructions.RemoveRange(tailBlock.Instructions.Count - 5, 5);
        }
        else
        {
            // We don't have any Convert instruction before branching at the end (more recent GML output)
            tailBlock.Instructions.RemoveRange(tailBlock.Instructions.Count - 4, 4);
        }

        // Remove unneeded logic from After (should also always be a Block)
        Block afterBlock = After as Block ?? throw new DecompilerException("Expected After to be block");
        afterBlock.Instructions.RemoveAt(0);

        After = InsertLoopStructure(Head, After);
    }

    public override string ToString()
    {
        return $"{nameof(RepeatLoop)} (start address {StartAddress}, end address {EndAddress}, {Predecessors.Count} predecessors, {Successors.Count} successors)";
    }

    public override void BuildAST(ASTBuilder builder, List<IStatementNode> output)
    {
        // The expression used in the loop header should already be on the stack
        IExpressionNode timesToRepeat = builder.ExpressionStack.Pop();

        // Push this loop context
        Loop? prevLoop = builder.TopFragmentContext!.SurroundingLoop;
        builder.TopFragmentContext.SurroundingLoop = this;

        // Build loop body, and create statement
        BlockNode body = builder.BuildBlock(Head);
        output.Add(new RepeatLoopNode(timesToRepeat, body));

        // Pop this loop context
        builder.TopFragmentContext.SurroundingLoop = prevLoop;
    }
}
