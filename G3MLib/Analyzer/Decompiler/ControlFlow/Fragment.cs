/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.AST;

namespace G3MLib.Analyzer.Decompiler.ControlFlow;

/// <summary>
/// Represents a single VM code fragment, used for single function contexts.
/// </summary>
internal sealed class Fragment(int startAddr, int endAddr, IGMCode codeEntry, bool rootScope, List<IControlFlowNode> blocks) : IControlFlowNode
{
    public int StartAddress { get; private set; } = startAddr;

    public int EndAddress { get; private set; } = endAddr;

    public List<IControlFlowNode> Predecessors { get; } = [];

    public List<IControlFlowNode> Successors { get; } = [];

    public IControlFlowNode? Parent { get; set; }

    public List<IControlFlowNode?> Children { get; } = blocks;

    public bool Unreachable { get; set; }

    /// <summary>
    /// Code entry that this fragment belongs to.
    /// </summary>
    public IGMCode CodeEntry { get; } = codeEntry;

    /// <summary>
    /// Whether this fragment is a root-scope fragment (that is, it may be a global function when inside of a global script).
    /// </summary>
    public bool RootScope { get; } = rootScope;

    /// <summary>
    /// The base blocks that this fragment is composed of.
    /// </summary>
    public List<Block> Blocks { get; } = [];

    /// <summary>
    /// Finds code fragments from a decompile context.
    /// Note that this will modify the control flow and instructions of the existing blocks.
    /// </summary>
    public static List<Fragment> FindFragments(DecompileContext ctx)
    {
        var fragments = FindFragments(ctx.Code, ctx.Blocks, ctx.CancellationToken);
        ctx.FragmentNodes = fragments;
        return fragments;
    }

    /// <summary>
    /// Finds code fragments from a code entry and its list of blocks.
    /// Note that this will modify the control flow and instructions of the existing blocks.
    /// </summary>
    public static List<Fragment> FindFragments(IGMCode code, List<Block> blocks, CancellationToken cancellationToken = default)
    {
        if (code.Parent is not null)
            throw new ArgumentException("Expected code entry to be root level.", nameof(code));

        // Map code entry addresses to code entries
        Dictionary<int, IGMCode> codeEntries = new(code.ChildCount);
        for (var i = 0; i < code.ChildCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = code.GetChild(i);
            codeEntries.Add(child.StartOffset, child);
        }

        // Build fragments, using a stack to track hierarchy
        List<Fragment> fragments = new(code.ChildCount);
        Stack<Fragment> stack = new();
        Fragment current = new(code.StartOffset, code.Length, code, false, []);
        fragments.Add(current);
        for (var i = 0; i < blocks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = blocks[i];

            // Check if our current fragment is ending at this block
            if (block.StartAddress == current.EndAddress)
            {
                if (stack.Count > 0)
                {
                    // We're an inner fragment - mark first block as no longer unreachable, if it is
                    // (normally always unreachable, unless there's a loop header at the first block)
                    if (current.Children[0].Unreachable)
                    {
                        current.Children[0].Unreachable = false;
                        IControlFlowNode.DisconnectPredecessor(current.Children[0], 0);
                    }

                    // We're an inner fragment - remove "exit" instruction
                    var lastBlockInstructions = current.Blocks[^1].Instructions;
                    if (lastBlockInstructions is not [.., { Kind: IGMInstruction.Opcode.Exit }])
                    {
                        throw new DecompilerException("Expected exit at end of fragment.");
                    }
                    lastBlockInstructions.RemoveAt(lastBlockInstructions.Count - 1);

                    // Go to the fragment the next level up
                    current = stack.Pop();
                }
                else
                {
                    // We're done processing now. Add last block and exit loop.
                    current.Children.Add(block);
                    current.Blocks.Add(block);

                    if (block.StartAddress != code.Length)
                    {
                        throw new DecompilerException("Code length mismatches final block address.");
                    }

                    break;
                }
            }

            // Check for new fragment starting at this block
            if (codeEntries.TryGetValue(block.StartAddress, out var newCode))
            {
                // Our "current" is now the next level up
                stack.Push(current);

                // Compute the end address of this fragment, by looking at previous block
                var previous = blocks[i - 1];
                if (previous.Instructions[^1].Kind != IGMInstruction.Opcode.Branch)
                {
                    throw new DecompilerException("Expected branch before fragment start.");
                }
                var endAddr = previous.Successors[0].StartAddress;

                // Remove previous block's branch instruction
                previous.Instructions.RemoveAt(previous.Instructions.Count - 1);

                // Make our new "current" be this new fragment
                current = new Fragment(block.StartAddress, endAddr, newCode, stack.Count == 1, []);
                fragments.Add(current);

                // Rewire previous block to jump to this fragment, and this fragment
                // to jump to the successor of the previous block.
                IControlFlowNode.InsertSuccessor(previous, 0, current);
            }

            // If we're at the start of the fragment, track parent node on the block
            if (current.Children.Count == 0)
            {
                block.Parent = current;
            }

            // Add this block to our current fragment
            current.Children.Add(block);
            current.Blocks.Add(block);
        }

        if (stack.Count > 0)
        {
            throw new DecompilerException("Failed to close all fragments.");
        }

        return fragments;
    }

    public override string ToString()
    {
        return $"{nameof(Fragment)} (start address {StartAddress}, end address {EndAddress}, {Predecessors.Count} predecessors, {Successors.Count} successors)";
    }

    public void BuildAST(ASTBuilder builder, List<IStatementNode> output)
    {
        var node = IFragmentNode.Create(builder, this);
        if (node is IExpressionNode exprNode)
        {
            builder.ExpressionStack.Push(exprNode);
        }
        else
        {
            output.Add(node);
        }
    }
}
