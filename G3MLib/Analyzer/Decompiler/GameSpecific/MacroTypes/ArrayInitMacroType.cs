/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.AST;

namespace G3MLib.Analyzer.Decompiler.GameSpecific;

/// <summary>
/// Macro type that matches a macro type to an inline array initialization.
/// </summary>
public class ArrayInitMacroType(IMacroType type) : IMacroTypeArrayInit
{
    public IMacroType Type { get; } = type;

    /// <summary>
    /// Resolves this macro type for a given array initialization in the AST.
    /// </summary>
    public ArrayInitNode? Resolve(ASTCleaner cleaner, ArrayInitNode arrayInit)
    {
        bool didAnything = false;

        for (int i = 0; i < arrayInit.Elements.Count; i++)
        {
            if (arrayInit.Elements[i] is not IMacroResolvableNode node)
            {
                // Current element is not resolvable as a macro
                continue;
            }

            if (node.ResolveMacroType(cleaner, Type) is not IExpressionNode nodeResolved)
            {
                // Failed to resolve current element's macro type
                continue;
            }

            // Update element
            arrayInit.Elements[i] = nodeResolved;
            didAnything = true;
        }

        return didAnything ? arrayInit : null;
    }
}
