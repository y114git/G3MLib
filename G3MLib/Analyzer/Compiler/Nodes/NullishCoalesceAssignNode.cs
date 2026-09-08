/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.Analyzer.Compiler.Lexer;
using G3MLib.Analyzer.Compiler.Parser;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Compiler.Nodes;

/// <summary>
/// Represents a nullish coalesce assignment statement in the AST.
/// </summary>
internal sealed class NullishCoalesceAssignNode : IASTNode
{
    /// <summary>
    /// Expression being assigned to.
    /// </summary>
    public IAssignableASTNode Destination { get; private set; }

    /// <summary>
    /// Expression being assigned to, but with less side effects.
    /// </summary>
    public IASTNode? DestinationLessSideEffects { get; private set; }

    /// <summary>
    /// Side effects that were removed from <see cref="DestinationLessSideEffects"/>.
    /// </summary>
    public List<IASTNode>? SideEffects { get; private set; }

    /// <summary>
    /// The expression being evaluated and assigned to the destination.
    /// </summary>
    public IASTNode Expression { get; private set; }

    /// <inheritdoc/>
    public IToken? NearbyToken => Destination.NearbyToken;

    /// <summary>
    /// Creates a nullish coalesce assignment node from the given destination and expression.
    /// </summary>
    public NullishCoalesceAssignNode(IAssignableASTNode destination, IASTNode expression)
    {
        Destination = destination;
        Expression = expression;
    }

    private NullishCoalesceAssignNode(IAssignableASTNode destination, IASTNode? destinationLessSideEffects, List<IASTNode>? sideEffects, IASTNode expression)
    {
        Destination = destination;
        DestinationLessSideEffects = destinationLessSideEffects;
        SideEffects = sideEffects;
        Expression = expression;
    }

    /// <inheritdoc/>
    public IASTNode PostProcess(ParseContext context)
    {
        Destination = Destination.PostProcess(context) as IAssignableASTNode ?? throw new InvalidOperationException("Destination no longer assignable");
        Expression = Expression.PostProcess(context);
        if (context.CompileContext.GameContext.UsingNewNullishAssignSideEffects)
        {
            SideEffects = new(4);
            DestinationLessSideEffects = PrefixPostfixRewriteHelper.DuplicateAndRemoveSideEffects(context, Destination, SideEffects);
        }

        return this;
    }

    /// <inheritdoc/>
    public IASTNode Duplicate(ParseContext context)
    {
        return new NullishCoalesceAssignNode(
            Destination.Duplicate(context) as IAssignableASTNode ?? throw new InvalidOperationException("Destination no longer assignable"),
            DestinationLessSideEffects?.Duplicate(context),
            SideEffects is null ? null : [.. SideEffects.Select(sideEffect => sideEffect.Duplicate(context))],
            Expression.Duplicate(context));
    }

    /// <inheritdoc/>
    public void GenerateCode(BytecodeContext context)
    {
        bool canGenerateArrayOwners = context.CanGenerateArrayOwners;
        if (canGenerateArrayOwners &&
            (ArrayOwners.ContainsArrayAccessor(Destination) || ArrayOwners.ContainsNewArrayLiteral(Expression) ||
             ArrayOwners.IsArraySetFunctionOrContainsSubLiteral(Destination)))
        {
            context.CanGenerateArrayOwners = false;
            ArrayOwners.GenerateSetArrayOwner(context, Destination);
        }

        if (DestinationLessSideEffects is not null)
        {
            DestinationLessSideEffects.GenerateCode(context);
        }
        else
        {
            Destination.GenerateCode(context);
        }
        context.ConvertDataType(DataType.Variable);

        context.Emit(ExtendedOpcode.IsNullishValue);
        SingleForwardBranchPatch skipRightSidePatch = new(context, context.Emit(Opcode.BranchFalse));

        context.Emit(Opcode.PopDelete, DataType.Variable);
        Expression.GenerateCode(context);
        context.ConvertDataType(DataType.Variable);

        context.PushDataType(DataType.Variable);
        Destination.GenerateAssignCode(context);
        SingleForwardBranchPatch skipDestinationPopPatch = new(context, context.Emit(Opcode.Branch));

        skipRightSidePatch.Patch(context);
        context.Emit(Opcode.PopDelete, DataType.Variable);
        if (SideEffects is not null)
        {
            foreach (IASTNode sideEffect in SideEffects)
            {
                sideEffect.GenerateCode(context);
            }
        }
        skipDestinationPopPatch.Patch(context);

        context.CanGenerateArrayOwners = canGenerateArrayOwners;
    }

    /// <inheritdoc/>
    public IEnumerable<IASTNode> EnumerateChildren()
    {
        if (DestinationLessSideEffects is not null)
        {
            yield return DestinationLessSideEffects;
        }
        yield return Destination;
        yield return Expression;
        if (SideEffects is not null)
        {
            foreach (IASTNode sideEffect in SideEffects)
            {
                yield return sideEffect;
            }
        }
    }
}
