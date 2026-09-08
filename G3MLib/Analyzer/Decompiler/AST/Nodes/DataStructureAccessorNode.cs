/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a non-array GameMaker accessor.
/// </summary>
public sealed class DataStructureAccessorNode(IExpressionNode target, DataStructureAccessorNode.AccessorKind kind,
                                              List<IExpressionNode> indices) : IExpressionNode
{
    public enum AccessorKind
    {
        List,
        Map,
        Grid,
        Struct
    }

    public IExpressionNode Target { get; private set; } = target;
    public AccessorKind Kind { get; } = kind;
    public List<IExpressionNode> Indices { get; } = indices;

    public bool Duplicated { get; set; }
    public bool Group { get; set; }
    public IGMInstruction.DataType StackType { get; set; } = IGMInstruction.DataType.Variable;

    internal static IExpressionNode? TryCreateGet(FunctionCallNode call, DecompileContext context)
    {
        if (!TryGetKind(call.FunctionName, context, out AccessorKind kind, out int indexCount) || call.Arguments.Count != indexCount + 1)
        {
            return null;
        }

        return new DataStructureAccessorNode(call.Arguments[0], kind, [.. call.Arguments.GetRange(1, indexCount)])
        {
            Duplicated = call.Duplicated,
            Group = call.Group,
            StackType = call.StackType
        };
    }

    internal static IStatementNode? TryCreateSet(FunctionCallNode call, DecompileContext context)
    {
        if (!TryGetKind(call.FunctionName, context, out AccessorKind kind, out int indexCount) || call.Arguments.Count != indexCount + 2)
        {
            return null;
        }

        DataStructureAccessorNode accessor = new(call.Arguments[0], kind, [.. call.Arguments.GetRange(1, indexCount)])
        {
            StackType = call.StackType
        };
        return new AssignNode(accessor, call.Arguments[^1]);
    }

    private static bool TryGetKind(string functionName, DecompileContext context, out AccessorKind kind, out int indexCount)
    {
        kind = default;
        indexCount = 0;

        if (context.Settings.CleanupDataStructureAccessors)
        {
            switch (functionName)
            {
                case "ds_list_find_value" or "ds_list_set":
                    kind = AccessorKind.List;
                    indexCount = 1;
                    return true;
                case "ds_map_find_value" or "ds_map_set":
                    kind = AccessorKind.Map;
                    indexCount = 1;
                    return true;
                case "ds_grid_get" or "ds_grid_set":
                    kind = AccessorKind.Grid;
                    indexCount = 2;
                    return true;
            }
        }

        if (context.Settings.CleanupStructVariableAccessors &&
            (functionName is "variable_struct_get" or "variable_struct_set"))
        {
            kind = AccessorKind.Struct;
            indexCount = 1;
            return true;
        }

        return false;
    }

    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        Target = Target.Clean(cleaner);
        for (int i = 0; i < Indices.Count; i++)
        {
            Indices[i] = Indices[i].Clean(cleaner);
        }
        return this;
    }

    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        Target = Target.PostClean(cleaner);
        ExpressionNodeList.PostClean(cleaner, Indices);
        return this;
    }

    public void Print(ASTPrinter printer)
    {
        if (Group)
        {
            printer.Write('(');
        }

        Target.Print(printer);
        printer.Write(Kind switch
        {
            AccessorKind.List => "[| ",
            AccessorKind.Map => "[? ",
            AccessorKind.Grid => "[# ",
            _ => "[$ "
        });
        Indices[0].Print(printer);
        if (Indices.Count == 2)
        {
            printer.Write(", ");
            Indices[1].Print(printer);
        }
        printer.Write(']');

        if (Group)
        {
            printer.Write(')');
        }
    }

    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        return (isStatementLHS && Group) || Target.RequiresMultipleLines(printer, isStatementLHS) ||
               Indices.Exists(index => index.RequiresMultipleLines(printer, false));
    }

    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        yield return Target;
        foreach (IExpressionNode index in Indices)
        {
            yield return index;
        }
    }
}
