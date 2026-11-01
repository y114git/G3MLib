/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents the "throw" keyword being used to throw an object/exception in the AST.
/// </summary>
public class ThrowNode(IExpressionNode value) : IExpressionNode, IStatementNode, IBlockCleanupNode
{
    /// <summary>
    /// The value being thrown.
    /// </summary>
    public IExpressionNode Value { get; private set; } = value;

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public IGMInstruction.DataType StackType { get; set; } = IGMInstruction.DataType.Variable;

    /// <inheritdoc/>
    public bool SemicolonAfter => true;

    /// <inheritdoc/>
    public bool EmptyLineBefore { get => false; set => _ = value; }

    /// <inheritdoc/>
    public bool EmptyLineAfter { get => false; set => _ = value; }

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        Value = Value.Clean(cleaner);
        return this;
    }

    /// <inheritdoc/>
    IStatementNode IASTNode<IStatementNode>.Clean(ASTCleaner cleaner)
    {
        Value = Value.Clean(cleaner);
        return this;
    }

    /// <inheritdoc/>
    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        Value = Value.PostClean(cleaner);
        return this;
    }

    /// <inheritdoc/>
    IStatementNode IASTNode<IStatementNode>.PostClean(ASTCleaner cleaner)
    {
        Value = Value.PostClean(cleaner);
        return this;
    }

    /// <inheritdoc/>
    public int BlockClean(ASTCleaner cleaner, BlockNode block, int i)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(cleaner);
        // Remove duplicated finally statements
        if (cleaner.TopFragmentContext.FinallyStatementCount.Count > 0 &&
            cleaner.Context.GameContext.UsingFinallyBeforeThrow)
        {
            var count = cleaner.TopFragmentContext.FinallyStatementCount.Peek();
            if (i - count >= 0)
            {
                block.Children.RemoveRange(i - count, count);
                return i - count;
            }
        }

        return i;
    }

    /// <inheritdoc/>
    public void Print(ASTPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (Group)
        {
            printer.Write('(');
        }

        printer.Write("throw ");
        printer.PrintNode(Value);

        if (Group)
        {
            printer.Write(')');
        }
    }

    /// <inheritdoc/>
    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        if (isStatementLHS && Group)
        {
            return true;
        }
        return Value.RequiresMultipleLines(printer, false);
    }

    /// <inheritdoc/>
    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        yield return Value;
    }
}
