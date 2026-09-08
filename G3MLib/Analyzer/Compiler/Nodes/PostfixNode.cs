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
/// Represents a post (++/-- on right side) expression in the AST.
/// </summary>
internal sealed class PostfixNode(IToken? token, IAssignableASTNode expression, bool isIncrement) : PrePostfixNode(token, expression, isIncrement)
{
    protected override bool IsPrefix => false;

    /// <inheritdoc/>
    public override IASTNode Duplicate(ParseContext context)
    {
        return new PostfixNode(
            NearbyToken,
            DuplicateExpression(context),
            IsIncrement)
        {
            IsStatement = IsStatement
        };
    }

}
