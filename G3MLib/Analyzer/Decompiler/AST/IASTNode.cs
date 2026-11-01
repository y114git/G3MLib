/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Base interface for all nodes in the AST.
/// </summary>
public interface IBaseASTNode
{
    /// <summary>
    /// Enumerates all children nodes in the tree.
    /// </summary>
    /// <returns></returns>
    IEnumerable<IBaseASTNode> EnumerateChildren();

    /// <summary>
    /// Prints this node using the provided printer.
    /// </summary>
    void Print(ASTPrinter printer);
}

/// <summary>
/// Generic interface for all nodes in the AST.
/// </summary>
public interface IASTNode<out T> : IBaseASTNode
{
    /// <summary>
    /// Performs a cleanup pass on this node and all of its sub-nodes.
    /// Returns the cleaned version of the node (which is often the same reference).
    /// </summary>
    T Clean(ASTCleaner cleaner);

    /// <summary>
    /// Second cleanup pass on this node and all of its sub-nodes.
    /// Returns the second-pass cleaned version of the node (which is very often the same reference).
    /// </summary>
    T PostClean(ASTCleaner cleaner);

    /// <summary>
    /// Calculates and returns whether the node will require multiple lines when printed.
    /// </summary>
    bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS);
}
