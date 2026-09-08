/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.Analyzer.Compiler.Lexer;
using G3MLib.Analyzer.Compiler.Parser;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Compiler.Nodes;

/// <summary>
/// Represents a prefix (++/-- on left side) expression or statement in the AST.
/// </summary>
internal sealed class PrefixNode(IToken? token, IAssignableASTNode expression, bool isIncrement) : PrePostfixNode(token, expression, isIncrement)
{
    protected override bool IsPrefix => true;

    /// <summary>
    /// Creates a prefix node, parsing from the given context's current position,
    /// and given whether or not the prefix is an increment.
    /// </summary>
    public static PrefixNode? Parse(ParseContext context, TokenOperator token, bool isIncrement)
    {
        if (Expressions.ParseChainExpression(context) is not IASTNode expression)
        {
            return null;
        }

        // Ensure expression is assignable
        if (expression is not IAssignableASTNode assignableExpression)
        {
            return null;
        }

        // Create final node
        return new PrefixNode(token, assignableExpression, isIncrement);
    }

    /// <inheritdoc/>
    public override IASTNode Duplicate(ParseContext context)
    {
        return new PrefixNode(
            NearbyToken,
            DuplicateExpression(context),
            IsIncrement)
        {
            IsStatement = IsStatement
        };
    }

}
