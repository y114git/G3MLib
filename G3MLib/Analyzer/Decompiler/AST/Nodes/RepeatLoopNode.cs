/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a repeat loop in the AST.
/// </summary>
public class RepeatLoopNode(IExpressionNode timesToRepeat, BlockNode body) : IStatementNode
{
    /// <summary>
    /// The number of times the loop repeats.
    /// </summary>
    public IExpressionNode TimesToRepeat { get; private set; } = timesToRepeat;

    /// <summary>
    /// The main block of the loop.
    /// </summary>
    public BlockNode Body { get; private set; } = body;

    /// <inheritdoc/>
    public bool SemicolonAfter => false;

    /// <inheritdoc/>
    public bool EmptyLineBefore { get; set; }

    /// <inheritdoc/>
    public bool EmptyLineAfter { get; set; }

    /// <inheritdoc/>
    public IStatementNode Clean(ASTCleaner cleaner)
    {
        TimesToRepeat = TimesToRepeat.Clean(cleaner);
        ElseToContinueCleanup.Clean(cleaner, Body);
        Body.Clean(cleaner);

        EmptyLineAfter = EmptyLineBefore = cleaner.Context.Settings.EmptyLineAroundBranchStatements;

        return this;
    }

    /// <inheritdoc/>
    public IStatementNode PostClean(ASTCleaner cleaner)
    {
        TimesToRepeat = TimesToRepeat.PostClean(cleaner);

        cleaner.TopFragmentContext!.PushLocalScope(cleaner.Context, cleaner.TopFragmentContext!.CurrentPostCleanupBlock!, this);
        Body.PostClean(cleaner);
        cleaner.TopFragmentContext!.PopLocalScope(cleaner.Context);

        return this;
    }

    /// <inheritdoc/>
    public void Print(ASTPrinter printer)
    {
        printer.PrintBlockStatement("repeat", TimesToRepeat, Body);
    }

    /// <inheritdoc/>
    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        return true;
    }

    /// <inheritdoc/>
    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        yield return TimesToRepeat;
        yield return Body;
    }
}
