/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a 32-bit signed integer constant in the AST.
/// </summary>
public class Int32Node(int value) : Int32MacroNode(value, IGMInstruction.DataType.Int32), IConstantNode<int>
{
    /// <inheritdoc/>
    public int Value { get; } = value;

}
