/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Linq;
using G3MLib.Analyzer.Compiler.Lexer;
using G3MLib.Analyzer.Compiler.Nodes;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Compiler.Bytecode;

internal static class ArrayOwners
{
    /// <summary>
    /// Returns whether the given node has a new array literal inside of it (as long as it doesn't cross function barriers),
    /// mimicking official compiler behavior.
    /// </summary>
    public static bool ContainsNewArrayLiteral(IASTNode node)
    {
        if (node is SimpleFunctionCallNode { FunctionName: VMConstants.NewArrayFunction })
        {
            return true;
        }
        if (node is not (SimpleFunctionCallNode or FunctionCallNode or FunctionDeclNode))
        {
            return node.EnumerateChildren().Any(ContainsNewArrayLiteral);
        }
        return false;
    }

    /// <summary>
    /// Returns whether the given node has an array accessor inside of it (as long as it doesn't cross function barriers),
    /// mimicking official compiler behavior.
    /// </summary>
    public static bool ContainsArrayAccessor(IASTNode node)
    {
        if (node is AccessorNode { Kind: AccessorNode.AccessorKind.Array })
        {
            return true;
        }
        if (node is not (SimpleFunctionCallNode or FunctionCallNode or FunctionDeclNode))
        {
            return node.EnumerateChildren().Any(ContainsArrayAccessor);
        }
        return false;
    }

    /// <summary>
    /// Returns whether the given node is an array setter function (as long as it doesn't cross function barriers),
    /// mimicking official compiler behavior.
    /// </summary>
    public static bool IsArraySetFunction(IASTNode node)
    {
        if (node is SimpleFunctionCallNode
            {
                FunctionName: "array_set" or "array_set_pre" or "array_set_post" or
                              "array_set_2D" or "array_set_2D_pre" or "array_set_2D_post" or
                              "array_create" or VMConstants.NewArrayFunction
            })
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns whether the given node is an array setter function, or has a new array literal inside of it (as
    /// long as it doesn't cross function barriers), mimicking official compiler behavior.
    /// </summary>
    public static bool IsArraySetFunctionOrContainsSubLiteral(IASTNode node)
    {
        if (IsArraySetFunction(node))
        {
            return true;
        }
        return node.EnumerateChildren().Any(ContainsNewArrayLiteral);
    }

    /// <summary>
    /// Finds a variable node for an array (doesn't go too far, but goes through first level accessors).
    /// </summary>
    private static IVariableASTNode? FindArrayVariable(IASTNode expression)
    {
        if (expression is IVariableASTNode variableNode)
        {
            return variableNode;
        }
        if (expression is AccessorNode accessorNode)
        {
            return FindArrayVariable(accessorNode.Expression);
        }
        return null;
    }

    /// <summary>
    /// Generates a <see cref="IGMInstruction.ExtendedOpcode.SetArrayOwner"/> call, as long as it's necessary.
    /// </summary>
    /// <returns><see langword="true"/> if a call was generated; <see langword="false"/> otherwise.</returns>
    public static bool GenerateSetArrayOwner(BytecodeContext context, IASTNode expression)
    {
        // Generate ID
        long id;
        if (FindArrayVariable(expression) is IVariableASTNode { VariableName: string variableName } variableNode)
        {
            // Use variable information
            bool isDot = variableNode is DotVariableNode || (variableNode is SimpleVariableNode { CollapsedFromDot: true });
            id = context.GenerateArrayOwnerID(variableName, context.CurrentScope.ArrayOwnerID, isDot);
        }
        else
        {
            // No variable information to use
            id = context.GenerateArrayOwnerID(null, context.CurrentScope.ArrayOwnerID, false);
        }

        // If we have a new ID, generate code!
        if (id != context.LastArrayOwnerID)
        {
            context.LastArrayOwnerID = id;
            NumberNode.GenerateCode(context, id);
            context.ConvertDataType(DataType.Int32);
            context.Emit(ExtendedOpcode.SetArrayOwner);
            return true;
        }
        return false;
    }
}
