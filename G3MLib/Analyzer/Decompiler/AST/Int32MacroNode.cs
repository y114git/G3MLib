/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using G3MLib.Analyzer.Decompiler.GameSpecific;

namespace G3MLib.Analyzer.Decompiler.AST;

public abstract class Int32MacroNode(int value, IGMInstruction.DataType stackType) : IConditionalValueNode
{
    private int IntegerValue { get; } = value;
    public bool Duplicated { get; set; }
    public bool Group { get; set; }
    public IGMInstruction.DataType StackType { get; set; } = stackType;
    public string ConditionalTypeName => "Integer";
    public string ConditionalValue => IntegerValue.ToString();

    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        return this;
    }

    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        return this;
    }

    public void Print(ASTPrinter printer)
    {
        printer.Write(IntegerValue);
    }

    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        return false;
    }

    public IExpressionNode? ResolveMacroType(ASTCleaner cleaner, IMacroType type)
    {
        return type is IMacroTypeInt32 type32 ? type32.Resolve(cleaner, this, IntegerValue) : null;
    }

    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        return [];
    }
}
