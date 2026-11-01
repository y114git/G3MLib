/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.GameSpecific;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a binary expression, such as basic two-operand arithmetic operations.
/// </summary>
public class BinaryNode : IMultiExpressionNode, IConditionalValueNode
{
    /// <summary>
    /// Left side of the binary operation.
    /// </summary>
    public IExpressionNode Left { get; private set; }

    /// <summary>
    /// Right side of the binary operation.
    /// </summary>
    public IExpressionNode Right { get; private set; }

    /// <summary>
    /// The instruction that performs this operation, as in the code.
    /// </summary>
    public IGMInstruction Instruction { get; }

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public DataType StackType { get; set; }

    /// <inheritdoc/>
    public string ConditionalTypeName => "Binary";

    /// <inheritdoc/>
    public string ConditionalValue => "";

    public BinaryNode(IExpressionNode left, IExpressionNode right, IGMInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        Left = left;
        Right = right;
        Instruction = instruction;

        // Get resulting stack type depending on instruction's opcode and types for Left and Right
        StackType = instruction.Type1.BinaryResultWith(instruction.Kind, instruction.Type2);
    }

    /// <summary>
    /// Checks whether the given expression node needs to be grouped, and
    /// sets <see cref="IExpressionNode.Group"/> accordingly if required.
    /// </summary>
    private void CheckGroup(IExpressionNode node)
    {
        if (node is BinaryNode binary)
        {
            if (binary.Instruction.Kind != Instruction.Kind)
            {
                binary.Group = true;
            }
            if (binary.Instruction.Kind == Opcode.Compare)
            {
                binary.Group = true;
            }
        }
        else if (node is IMultiExpressionNode)
        {
            node.Group = true;
        }
    }

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        Left = Left.Clean(cleaner);
        Right = Right.Clean(cleaner);

        // Resolve macro types carrying between left and right nodes
        if (Left is IMacroTypeNode leftTypeNode && Right is IMacroResolvableNode rightResolvableNode &&
            leftTypeNode.GetExpressionMacroType(cleaner) is IMacroType leftMacroType &&
            rightResolvableNode.ResolveMacroType(cleaner, leftMacroType) is IExpressionNode rightResolved)
        {
            Right = rightResolved;
        }
        else if (Right is IMacroTypeNode rightTypeNode && Left is IMacroResolvableNode leftResolvableNode &&
                 rightTypeNode.GetExpressionMacroType(cleaner) is IMacroType rightMacroType &&
                 leftResolvableNode.ResolveMacroType(cleaner, rightMacroType) is IExpressionNode leftResolved)
        {
            Left = leftResolved;
        }

        // Check whether left/right sides need to have parentheses added (grouped)
        CheckGroup(Left);
        CheckGroup(Right);

        // If the right side of the binary is another BinaryNode, that implies it was evaluated earlier
        // than this one, meaning it should have parentheses around it.
        if (Right is BinaryNode)
        {
            Right.Group = true;
        }

        // Check for a special case of grouping on Left, with division of an integer type (caused by boolean conversions)
        if (Instruction is { Kind: Opcode.Divide, Type1: DataType.Double, Type2: DataType.Int32 } &&
            Left is BinaryNode { Instruction.Kind: Opcode.Multiply })
        {
            Left.Group = false;
        }

        return this;
    }

    /// <inheritdoc/>
    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        Left = Left.PostClean(cleaner);
        Right = Right.PostClean(cleaner);
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

        printer.PrintNode(Left);

        var op = Instruction switch
        {
            { Kind: Opcode.Add } => " + ",
            { Kind: Opcode.Subtract } => " - ",
            { Kind: Opcode.Multiply } => " * ",
            { Kind: Opcode.Divide } => " / ",
            { Kind: Opcode.GMLDivRemainder } => " div ",
            { Kind: Opcode.GMLModulo } => " % ",
            { Kind: Opcode.And, Type1: DataType.Boolean, Type2: DataType.Boolean } => " && ",
            { Kind: Opcode.And } => " & ",
            { Kind: Opcode.Or, Type1: DataType.Boolean, Type2: DataType.Boolean } => " || ",
            { Kind: Opcode.Or } => " | ",
            { Kind: Opcode.Xor, Type1: DataType.Boolean, Type2: DataType.Boolean } => " ^^ ",
            { Kind: Opcode.Xor } => " ^ ",
            { Kind: Opcode.ShiftLeft } => " << ",
            { Kind: Opcode.ShiftRight } => " >> ",
            { Kind: Opcode.Compare, ComparisonKind: ComparisonType.LesserThan } => " < ",
            { Kind: Opcode.Compare, ComparisonKind: ComparisonType.LesserEqualThan } => " <= ",
            { Kind: Opcode.Compare, ComparisonKind: ComparisonType.EqualTo } => " == ",
            { Kind: Opcode.Compare, ComparisonKind: ComparisonType.NotEqualTo } => " != ",
            { Kind: Opcode.Compare, ComparisonKind: ComparisonType.GreaterEqualThan } => " >= ",
            { Kind: Opcode.Compare, ComparisonKind: ComparisonType.GreaterThan } => " > ",
            _ => throw new DecompilerException("Failed to match binary instruction to string")
        };
        printer.Write(op);

        printer.PrintNode(Right);

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
        return Left.RequiresMultipleLines(printer, isStatementLHS) || Right.RequiresMultipleLines(printer, false);
    }

    /// <inheritdoc/>
    public IExpressionNode? ResolveMacroType(ASTCleaner cleaner, IMacroType type)
    {
        var didAnything = false;

        if (Left is IMacroResolvableNode leftResolvable &&
            leftResolvable.ResolveMacroType(cleaner, type) is IExpressionNode leftResolved)
        {
            Left = leftResolved;
            didAnything = true;
        }
        if (Right is IMacroResolvableNode rightResolvable &&
            rightResolvable.ResolveMacroType(cleaner, type) is IExpressionNode rightResolved)
        {
            Right = rightResolved;
            didAnything = true;
        }

        return didAnything ? this : null;
    }

    /// <inheritdoc/>
    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        yield return Left;
        yield return Right;
    }
}
