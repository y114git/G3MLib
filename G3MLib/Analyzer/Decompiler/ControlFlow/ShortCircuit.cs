/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.AST;

namespace G3MLib.Analyzer.Decompiler.ControlFlow;

public enum ShortCircuitType
{
    And,
    Or
}

internal sealed class ShortCircuit(int startAddress, int endAddress, ShortCircuitType logicKind, List<IControlFlowNode> children)
    : IControlFlowNode
{
    public int StartAddress { get; private set; } = startAddress;

    public int EndAddress { get; private set; } = endAddress;

    public List<IControlFlowNode> Predecessors { get; } = [];

    public List<IControlFlowNode> Successors { get; } = [];

    public IControlFlowNode? Parent { get; set; }

    public List<IControlFlowNode?> Children { get; } = children;

    public bool Unreachable { get; set; }

    public ShortCircuitType LogicKind { get; } = logicKind;

    /// <summary>
    /// Locates all blocks where a short circuit "ends", storing them on the context for later processing.
    /// </summary>
    public static void FindShortCircuits(DecompileContext ctx)
    {
        var blocks = ctx.Blocks;
        var oldBytecodeVersion = ctx.OlderThanBytecode15;

        ctx.ShortCircuitBlocks = [];

        // Identify and restructure short circuits
        foreach (var block in blocks)
        {
            ctx.ThrowIfCancellationRequested();
            // Match push.e (or on old versions, pushi.e) instruction, standalone in a block
            if (oldBytecodeVersion &&
                    block is
                    {
                        Instructions: [
                            {
                                Kind: IGMInstruction.Opcode.Push or IGMInstruction.Opcode.PushImmediate,
                                Type1: IGMInstruction.DataType.Int16
                            }]
                    }
                    ||
                !oldBytecodeVersion &&
                    block is
                    {
                        Instructions: [
                            {
                                Kind: IGMInstruction.Opcode.Push,
                                Type1: IGMInstruction.DataType.Int16
                            }]
                    })
            {
                ctx.ShortCircuitBlocks.Add(block);
            }
        }
    }

    /// <summary>
    /// Inserts all short-circuit operations contained within a list of blocks, updating the control flow graph accordingly.
    /// </summary>
    public static List<ShortCircuit> InsertShortCircuits(DecompileContext ctx)
    {
        List<ShortCircuit> shortCircuits = [];

        // Identify and restructure short circuits
        foreach (var block in ctx.ShortCircuitBlocks)
        {
            ctx.ThrowIfCancellationRequested();
            // Add child nodes
            List<IControlFlowNode> children = [block.Predecessors[0]];
            foreach (var predecessor in block.Predecessors)
            {
                // Connect to the next condition (the non-branch path from the previous condition)
                children.Add(predecessor.Successors[0]);
            }

            var logicKind = block.Instructions[0].ValueShort == 0 ? ShortCircuitType.And : ShortCircuitType.Or;
            ShortCircuit sc = new(children[0].StartAddress, block.EndAddress, logicKind, children);
            shortCircuits.Add(sc);

            // Remove branches and connections from previous blocks (not necessarily children!)
            for (var i = block.Predecessors.Count - 1; i >= 0; i--)
            {
                var pred = block.Predecessors[i] as Block ?? throw new DecompilerException("Expected predecessor to be block");
                pred.Instructions.RemoveAt(pred.Instructions.Count - 1);
                IControlFlowNode.DisconnectSuccessor(pred, 1);
                IControlFlowNode.DisconnectSuccessor(pred, 0);
            }
            var finalBlock = ctx.Blocks[block.BlockIndex - 1];
            finalBlock.Instructions.RemoveAt(finalBlock.Instructions.Count - 1);
            IControlFlowNode.DisconnectSuccessor(finalBlock, 0);

            block.Instructions.RemoveAt(0);

            IControlFlowNode.InsertStructure(children[0], block.Successors[0], sc);

            // Update parent status of the first child, as well as this loop, for later operation
            sc.Parent = children[0].Parent;
            children[0].Parent = sc;

            // Update parent status of remaining children, so they can be later updated if necessary
            for (var i = 1; i < children.Count; i++)
                children[i].Parent = sc;
        }

        ctx.ShortCircuitNodes = shortCircuits;
        return shortCircuits;
    }

    public override string ToString()
    {
        return $"{nameof(ShortCircuit)} (start address {StartAddress}, end address {EndAddress}, {Predecessors.Count} predecessors, {Successors.Count} successors)";
    }

    public void BuildAST(ASTBuilder builder, List<IStatementNode> output)
    {
        List<IExpressionNode> conditions = new(Children.Count);

        // Build first condition (it may already be on expression stack, though)
        builder.BuildArbitrary(Children[0], output, -1);
        conditions.Add(builder.ExpressionStack.Pop());

        for (var i = 1; i < Children.Count; i++)
        {
            conditions.Add(builder.BuildExpression(Children[i]));
        }

        // If any condition was an int16 that was directly used without conversion, that means it was a boolean
        foreach (ref var condition in conditions.Items())
        {
            if (condition is Int16Node { Value: 0 or 1, StackType: not IGMInstruction.DataType.Boolean } i16)
            {
                condition = new BooleanNode(i16.Value == 1)
                {
                    StackType = i16.StackType
                };
            }
        }

        builder.ExpressionStack.Push(new ShortCircuitNode(conditions, LogicKind));
    }
}
