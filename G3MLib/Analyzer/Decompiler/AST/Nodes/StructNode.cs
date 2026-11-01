/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.GameSpecific;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// A struct declaration/instantiation within the AST.
/// </summary>
public class StructNode(BlockNode body, ASTFragmentContext fragmentContext) : IFragmentNode, IConditionalValueNode
{
    /// <summary>
    /// The body of the struct (typically a block with assignments).
    /// </summary>
    public BlockNode Body { get; private set; } = body;

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public IGMInstruction.DataType StackType { get; set; } = IGMInstruction.DataType.Variable;

    /// <inheritdoc/>
    public bool SemicolonAfter => Group;

    /// <inheritdoc/>
    public bool EmptyLineBefore { get => false; set => _ = value; }

    /// <inheritdoc/>
    public bool EmptyLineAfter { get => false; set => _ = value; }

    /// <inheritdoc/>
    public ASTFragmentContext FragmentContext { get; } = fragmentContext;

    /// <inheritdoc/>
    public string ConditionalTypeName => "Struct";

    /// <inheritdoc/>
    public string ConditionalValue => "";

    /// <summary>
    /// Performs extra cleanup before the struct body cleanup is performed.
    /// </summary>
    private void PreCleanBody(ASTCleaner cleaner)
    {
        for (var i = 0; i < Body.Children.Count; i++)
        {
            // Replace impossible fields that use "variable_struct_set" and make them use quoted fields instead.
            if (Body.Children[i] is FunctionCallNode
                {
                    Function.Name.Content: VMConstants.StructSetFunction,
                    Arguments.Count: 3
                }
                callNode)
            {
                Body.Children[i] = new AssignNode(callNode.Arguments[1], callNode.Arguments[2]);
            }

            // If a negative constant integer is found, rewrite it as its (likely) corresponding constant name to be more accurate, if possible.
            if (Body.Children[i] is AssignNode { Value: IExpressionNode rhs } assign && rhs is Int16Node i16Node && i16Node.Value < 0 &&
                cleaner.Context.GameContext.LookupCommonNegativeConstant(i16Node.Value, out var constantName))
            {
                Body.Children[i] = new AssignNode(assign.Variable, new MacroValueNode(constantName));
            }
        }

    }

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        ArgumentNullException.ThrowIfNull(cleaner);
        PreCleanBody(cleaner);
        Body.Clean(cleaner);
        return this;
    }

    /// <inheritdoc/>
    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        Body.PostCleanStruct(cleaner);
        return this;
    }

    /// <inheritdoc/>
    IStatementNode IASTNode<IStatementNode>.Clean(ASTCleaner cleaner)
    {
        PreCleanBody(cleaner);
        Body.Clean(cleaner);

        // When a standalone statement, make sure this is grouped
        Group = true;

        return this;
    }

    /// <inheritdoc/>
    IStatementNode IASTNode<IStatementNode>.PostClean(ASTCleaner cleaner)
    {
        Body.PostCleanStruct(cleaner);
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
        if (Body.Children.Count == 0)
        {
            // Don't print a normal block in this case; condense down
            printer.Write("{}");
        }
        else
        {
            printer.PrintNode(Body);
        }
        if (Group)
        {
            if (Body.Children.Count > 0 && !printer.Context.Settings.OpenBlockBraceOnSameLine)
            {
                printer.EndLine();
                printer.StartLine();
            }
            printer.Write(')');
        }
    }

    /// <inheritdoc/>
    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        return isStatementLHS && Group || Body.Children.Count != 0;
    }

    /// <inheritdoc/>
    public IExpressionNode? ResolveMacroType(ASTCleaner cleaner, IMacroType type)
    {
        if (type is IMacroTypeConditional conditional)
        {
            return conditional.Resolve(cleaner, this);
        }
        return null;
    }

    /// <inheritdoc/>
    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        yield return Body;
    }
}
