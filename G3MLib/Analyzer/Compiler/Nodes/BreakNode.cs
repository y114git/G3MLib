/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.Analyzer.Compiler.Lexer;
using G3MLib.Analyzer.Compiler.Parser;
using static G3MLib.Analyzer.Compiler.Nodes.AssignNode;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Compiler.Nodes;

/// <summary>
/// Represents a "break" statement in the AST.
/// </summary>
internal sealed class BreakNode(IToken? token) : IASTNode
{
    /// <inheritdoc/>
    public IToken? NearbyToken { get; } = token;

    /// <inheritdoc/>
    public IASTNode PostProcess(ParseContext context)
    {
        // Throw error if using inside of a try statement's finally block
        if (context.ProcessingFinally)
        {
            context.CompileContext.PushError("Cannot use break inside of finally block", NearbyToken);
        }

        // If in a try statement, and not a switch statement, generate extra code (note: slightly buggy behavior being mimicked!)
        if (context.TryStatementContext is TryStatementContext tryContext && !context.ProcessingSwitch &&
            (!context.CompileContext.GameContext.UsingBetterTryBreakContinue || tryContext.ShouldGenerateBreakContinueCode))
        {
            // Generate block, setting break variable before breaking
            context.CurrentScope.DeclareLocal(tryContext.BreakVariableName);
            BlockNode blockNode = BlockNode.CreateEmpty(NearbyToken, 2);
            blockNode.Children.Add(new AssignNode(AssignKind.Normal, new SimpleVariableNode(tryContext.BreakVariableName, null, InstanceType.Local), new NumberNode(1, NearbyToken)));
            blockNode.Children.Add(this);

            // Let the try statement context know that break/continue variable was used
            tryContext.HasBreakContinueVariable = true;

            return blockNode;
        }

        return this;
    }

    /// <inheritdoc/>
    public IASTNode Duplicate(ParseContext context)
    {
        return this;
    }

    /// <inheritdoc/>
    public void GenerateCode(BytecodeContext context)
    {
        // Need at least one control flow context outside of this statement
        if (!context.AnyControlFlowContexts())
        {
            context.CompileContext.PushError($"Break used outside of any loop or switch statement", NearbyToken);
            return;
        }

        // Use control flow context's break branch
        context.GetTopControlFlowContext().UseBreak(context, context.Emit(Opcode.Branch));
    }

    /// <inheritdoc/>
    public IEnumerable<IASTNode> EnumerateChildren()
    {
        return [];
    }
}
