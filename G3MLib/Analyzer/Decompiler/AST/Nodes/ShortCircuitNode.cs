/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.ControlFlow;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents short circuit &amp;&amp; and || in the AST.
/// </summary>
public class ShortCircuitNode(List<IExpressionNode> conditions, ShortCircuitType logicType) : IMultiExpressionNode
{
    /// <summary>
    /// List of conditions in this short circuit chain.
    /// </summary>
    public List<IExpressionNode> Conditions { get; private set; } = conditions;

    /// <summary>
    /// Type of logic (And or Or) being used.
    /// </summary>
    public ShortCircuitType LogicType { get; } = logicType;

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public IGMInstruction.DataType StackType { get; set; } = IGMInstruction.DataType.Boolean;

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        for (var i = 0; i < Conditions.Count; i++)
        {
            Conditions[i] = Conditions[i].Clean(cleaner);

            // Group inner short circuits, so they don't clash order of operations
            if (Conditions[i] is ShortCircuitNode sc)
            {
                sc.Group = true;
            }
        }

        return this;
    }

    /// <inheritdoc/>
    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        for (var i = 0; i < Conditions.Count; i++)
        {
            Conditions[i] = Conditions[i].PostClean(cleaner);
        }
        return this;
    }

    /// <inheritdoc/>
    public void Print(ASTPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (Group)
        {
            printer.Write('(');
        }

        var op = (LogicType == ShortCircuitType.And) ? " && " : " || ";
        for (var i = 0; i < Conditions.Count; i++)
        {
            printer.PrintNode(Conditions[i]);
            if (i != Conditions.Count - 1)
            {
                printer.Write(op);
            }
        }

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
        foreach (var condition in Conditions)
        {
            if (condition.RequiresMultipleLines(printer, isStatementLHS))
            {
                return true;
            }
            isStatementLHS = false;
        }
        return false;
    }

    /// <inheritdoc/>
    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        return Conditions;
    }
}
