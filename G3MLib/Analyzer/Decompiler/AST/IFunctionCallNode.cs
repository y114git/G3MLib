/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using System.Linq;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Base interface for all nodes that call a function in the AST.
/// </summary>
public interface IFunctionCallNode : IConditionalValueNode, IStatementNode
{
    /// <summary>
    /// Name of the function being called, or <see langword="null"/> if none.
    /// </summary>
    public string? FunctionName { get; }

    /// <summary>
    /// List of arguments used to call the function with.
    /// </summary>
    public List<IExpressionNode> Arguments { get; }
}

internal static class FunctionCallArguments
{
    public static void Print(ASTPrinter printer, List<IExpressionNode> arguments)
    {
        printer.Write('(');
        for (int i = 0; i < arguments.Count; i++)
        {
            arguments[i].Print(printer);
            if (i != arguments.Count - 1)
            {
                printer.Write(", ");
            }
        }
        printer.Write(')');
    }

    public static bool RequiresMultipleLines(ASTPrinter printer, List<IExpressionNode> arguments)
    {
        return arguments.Any(argument => argument.RequiresMultipleLines(printer, false));
    }
}
