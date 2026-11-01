/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.GameSpecific;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents an array literal in the AST.
/// </summary>
public class ArrayInitNode(List<IExpressionNode> elements) : IConditionalValueNode
{
    /// <summary>
    /// List of elements in this array literal.
    /// </summary>
    public List<IExpressionNode> Elements { get; } = elements;

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public IGMInstruction.DataType StackType { get; set; } = IGMInstruction.DataType.Variable;

    /// <inheritdoc/>
    public string ConditionalTypeName => "ArrayInit";

    /// <inheritdoc/>
    public string ConditionalValue => "";

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        for (var i = 0; i < Elements.Count; i++)
        {
            Elements[i] = Elements[i].Clean(cleaner);
        }
        return this;
    }

    /// <inheritdoc/>
    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        for (var i = 0; i < Elements.Count; i++)
        {
            Elements[i] = Elements[i].PostClean(cleaner);
        }
        return this;
    }

    /// <inheritdoc/>
    public void Print(ASTPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        printer.Write('[');
        for (var i = 0; i < Elements.Count; i++)
        {
            printer.PrintNode(Elements[i]);
            if (i != Elements.Count - 1)
            {
                printer.Write(", ");
            }
        }
        printer.Write(']');
    }

    /// <inheritdoc/>
    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        for (var i = 0; i < Elements.Count; i++)
        {
            if (Elements[i].RequiresMultipleLines(printer, false))
            {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public IExpressionNode? ResolveMacroType(ASTCleaner cleaner, IMacroType type)
    {
        if (type is IMacroTypeArrayInit typeArrayInit)
        {
            return typeArrayInit.Resolve(cleaner, this);
        }
        return null;
    }

    /// <inheritdoc/>
    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        return Elements;
    }
}
