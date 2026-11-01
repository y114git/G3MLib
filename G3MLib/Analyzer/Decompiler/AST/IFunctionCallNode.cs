/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Base interface for all nodes that call a function in the AST.
/// </summary>
public interface IFunctionCallNode : IConditionalValueNode, IStatementNode
{
    /// <summary>
    /// Name of the function being called, or <see langword="null"/> if none.
    /// </summary>
    string? FunctionName { get; }

    /// <summary>
    /// List of arguments used to call the function with.
    /// </summary>
    List<IExpressionNode> Arguments { get; }
}

internal static class FunctionCallArguments
{
    public static void Print(ASTPrinter printer, List<IExpressionNode> arguments)
    {
        printer.Write('(');
        for (var i = 0; i < arguments.Count; i++)
        {
            printer.PrintNode(arguments[i]);
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
