/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.GameSpecific;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a unary expression, such as not (!) and bitwise negation (~).
/// </summary>
public class UnaryNode(IExpressionNode value, IGMInstruction instruction) : IConditionalValueNode
{
    /// <summary>
    /// The expression that this operation is being performed on.
    /// </summary>
    public IExpressionNode Value { get; private set; } = value;

    /// <summary>
    /// The instruction that performs this operation, as in the code.
    /// </summary>
    public IGMInstruction Instruction { get; } = instruction;

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public DataType StackType { get; set; } = instruction.Type1;

    /// <inheritdoc/>
    public string ConditionalTypeName => "Unary";

    /// <inheritdoc/>
    public string ConditionalValue => "";

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        Value = Value.Clean(cleaner);

        // Ensure operation applies to entire node
        if (Value is IMultiExpressionNode)
        {
            Value.Group = true;
        }

        // Ensure this isn't confused for the decrement operator
        if (Instruction.Kind is Opcode.Negate && Value is UnaryNode { Instruction.Kind: Opcode.Negate })
        {
            Value.Group = true;
        }

        return this;
    }

    /// <inheritdoc/>
    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        Value = Value.PostClean(cleaner);
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

        var op = Instruction switch
        {
            { Kind: Opcode.Negate } => '-',
            { Kind: Opcode.Not, Type1: DataType.Boolean } => '!',
            { Kind: Opcode.Not } => '~',
            _ => throw new DecompilerException("Failed to match unary instruction to character")
        };
        printer.Write(op);

        printer.PrintNode(Value);

        if (Group)
        {
            printer.Write(')');
        }
    }

    /// <inheritdoc/>
    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        if (isStatementLHS && (Group || Instruction.Kind is Opcode.Negate))
        {
            return true;
        }
        return Value.RequiresMultipleLines(printer, false);
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
        yield return Value;
    }
}
