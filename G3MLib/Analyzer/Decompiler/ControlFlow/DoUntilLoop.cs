/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using G3MLib.Analyzer.Decompiler.AST;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Decompiler.ControlFlow;

/// <summary>
/// Represents a "do...until" loop node in a control flow graph.
/// </summary>
internal sealed class DoUntilLoop(int startAddress, int endAddress, int index, IControlFlowNode head, IControlFlowNode tail, IControlFlowNode after)
    : ThreePartLoop(startAddress, endAddress, index, head, tail, after)
{
    public override void UpdateFlowGraph()
    {
        // Get rid of jumps from tail
        IControlFlowNode.DisconnectSuccessor(Tail, 1);
        IControlFlowNode.DisconnectSuccessor(Tail, 0);
        Block tailBlock = (Tail as Block)!;
        tailBlock.Instructions.RemoveAt(tailBlock.Instructions.Count - 1);

        After = InsertLoopStructure(Head, After);
    }

    public override string ToString()
    {
        return $"{nameof(DoUntilLoop)} (start address {StartAddress}, end address {EndAddress}, {Predecessors.Count} predecessors, {Successors.Count} successors)";
    }

    public override void BuildAST(ASTBuilder builder, List<IStatementNode> output)
    {
        // Push this loop context
        Loop? prevLoop = builder.TopFragmentContext!.SurroundingLoop;
        builder.TopFragmentContext.SurroundingLoop = this;

        // Build body
        int stackCountBefore = builder.ExpressionStack.Count;
        BlockNode body = builder.BuildBlock(Head);
        int stackCountAfter = builder.ExpressionStack.Count;

        // Verify that we created a loop condition while simulating the body
        if (stackCountAfter != stackCountBefore + 1)
        {
            throw new DecompilerException(
                $"Expected condition after do..until loop. Stack count: {stackCountBefore} -> {stackCountAfter}");
        }

        // Use newly-created condition from stack, and create statement
        IExpressionNode condition = builder.ExpressionStack.Pop();

        // If condition was an int16 that was directly used without conversion, that means it was a boolean
        if (condition is Int16Node { Value: 0 or 1, StackType: not DataType.Boolean } i16)
        {
            condition = new BooleanNode(i16.Value == 1)
            {
                StackType = i16.StackType
            };
        }

        // Add loop node to output
        output.Add(new DoUntilLoopNode(body, condition));

        // Pop this loop context
        builder.TopFragmentContext.SurroundingLoop = prevLoop;
    }
}
