/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a 16-bit signed integer constant in the AST.
/// </summary>
public class Int16Node(short value, bool regularPush) : Int32MacroNode(value, IGMInstruction.DataType.Int16), IConstantNode<short>
{
    /// <inheritdoc/>
    public short Value { get; } = value;

    /// <summary>
    /// If true, this number was pushed with a normal Push instruction opcode,
    /// rather than the usual PushImmediate.
    /// </summary>
    public bool RegularPush { get; } = regularPush;

}
