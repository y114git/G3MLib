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

namespace G3MLib.Analyzer.Compiler.Nodes;

internal abstract class PrePostfixNode(IToken? nearbyToken, IAssignableASTNode expression, bool isIncrement) : IMaybeStatementASTNode
{
    public IAssignableASTNode Expression { get; private set; } = expression;
    public bool IsIncrement { get; } = isIncrement;
    public bool IsStatement { get; set; }
    public IToken? NearbyToken { get; } = nearbyToken;

    protected abstract bool IsPrefix { get; }

    public IASTNode PostProcess(ParseContext context)
    {
        Expression = Expression.PostProcess(context) as IAssignableASTNode ?? throw new InvalidOperationException("Destination no longer assignable");
        return this;
    }

    protected IAssignableASTNode DuplicateExpression(ParseContext context)
    {
        return Expression.Duplicate(context) as IAssignableASTNode ?? throw new InvalidOperationException("Destination no longer assignable");
    }

    public abstract IASTNode Duplicate(ParseContext context);

    public void GenerateCode(BytecodeContext context)
    {
        if (context.CanGenerateArrayOwners && (ArrayOwners.ContainsArrayAccessor(Expression) || ArrayOwners.IsArraySetFunctionOrContainsSubLiteral(Expression)))
        {
            context.CanGenerateArrayOwners = false;
            if (!ArrayOwners.GenerateSetArrayOwner(context, Expression))
            {
                Expression.GenerateCode(context);
                context.Emit(IGMInstruction.Opcode.PopDelete, context.PopDataType());
            }

            Expression.GeneratePrePostAssignCode(context, IsIncrement, IsPrefix, false);
            if (IsStatement)
            {
                context.Emit(IGMInstruction.Opcode.PopDelete, context.PopDataType());
            }
            context.CanGenerateArrayOwners = true;
            return;
        }

        Expression.GeneratePrePostAssignCode(context, IsIncrement, IsPrefix, IsStatement);
    }

    public IEnumerable<IASTNode> EnumerateChildren()
    {
        yield return Expression;
    }
}
