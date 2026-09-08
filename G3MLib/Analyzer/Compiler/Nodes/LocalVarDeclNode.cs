/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.Analyzer.Compiler.Lexer;
using G3MLib.Analyzer.Compiler.Parser;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.Analyzer.Compiler.Nodes;

/// <summary>
/// Represents a (list of) local variable declarations in the AST.
/// </summary>
internal sealed class LocalVarDeclNode : IASTNode
{
    /// <summary>
    /// List of local variables being declared by this node.
    /// </summary>
    public List<string> DeclaredLocals { get; }

    /// <summary>
    /// List of values being assigned by this node, or null for locals with no assignment.
    /// </summary>
    /// <remarks>
    /// Corresponds 1-to-1 with <see cref="DeclaredLocals"/> in order.
    /// </remarks>
    public List<IASTNode?> AssignedValues { get; }

    /// <inheritdoc/>
    public IToken? NearbyToken { get; }

    private LocalVarDeclNode(IToken? token, List<string> declaredLocals, List<IASTNode?> assignedValues)
    {
        DeclaredLocals = declaredLocals;
        AssignedValues = assignedValues;
        NearbyToken = token;
    }

    /// <summary>
    /// Creates a local variable declaration node, parsing from the given context's current position.
    /// </summary>
    public static LocalVarDeclNode? Parse(ParseContext context)
    {
        // Parse "var" keyword
        if (context.EnsureToken(KeywordKind.Var) is not TokenKeyword tokenKeyword)
        {
            return null;
        }

        // Parse list of variables being declared
        List<string> declaredLocals = new(8);
        List<IASTNode?> assignedValues = new(8);
        while (!context.EndOfCode && context.Tokens[context.Position] is TokenVariable tokenVariable)
        {
            context.Position++;

            // Add to declaration's list
            declaredLocals.Add(tokenVariable.Text);

            // Awaiting official Underminers resolution: builtin shadowing differs between compiler versions.
            if (tokenVariable.BuiltinVariable is not null)
            {
                context.CompileContext.PushError($"Declaring local variable over builtin '{tokenVariable.Text}'", tokenVariable);
            }

            // Awaiting official Underminers resolution: duplicate declaration diagnostics differ between compiler versions.
            context.CurrentScope.DeclareLocal(tokenVariable.Text);

            // Check for assignment
            if (context.IsCurrentToken(OperatorKind.Assign) || context.IsCurrentToken(OperatorKind.Assign2))
            {
                context.Position++;
                if (Expressions.ParseExpression(context) is IASTNode value)
                {
                    assignedValues.Add(value);
                }
                else
                {
                    // Failed to parse expression; stop parsing local var declaration
                    assignedValues.Add(null);
                    break;
                }
            }
            else
            {
                // No value getting assigned, just add null to maintain proper alignment
                assignedValues.Add(null);
            }

            // If next token isn't a comma, we're done parsing the list
            if (!context.IsCurrentToken(SeparatorKind.Comma))
            {
                break;
            }

            // Otherwise, move past comma and keep parsing
            context.Position++;
        }

        // Create final local var declaration node
        return new LocalVarDeclNode(tokenKeyword, declaredLocals, assignedValues);
    }

    /// <inheritdoc/>
    public IASTNode PostProcess(ParseContext context)
    {
        for (int i = 0; i < AssignedValues.Count; i++)
        {
            AssignedValues[i] = AssignedValues[i]?.PostProcess(context);
        }
        return this;
    }

    /// <inheritdoc/>
    public IASTNode Duplicate(ParseContext context)
    {
        List<IASTNode?> newAssignedValues = new(AssignedValues);
        for (int i = 0; i < newAssignedValues.Count; i++)
        {
            newAssignedValues[i] = newAssignedValues[i]?.Duplicate(context);
        }
        return new LocalVarDeclNode(NearbyToken, new(DeclaredLocals), newAssignedValues);
    }

    /// <inheritdoc/>
    public void GenerateCode(BytecodeContext context)
    {
        // Generate local variable initial assignments (for ones that have them)
        for (int i = 0; i < AssignedValues.Count; i++)
        {
            if (AssignedValues[i] is IASTNode expression)
            {
                // Handle array copy-on-write
                bool canGenerateArrayOwners = context.CanGenerateArrayOwners;
                if (canGenerateArrayOwners && ArrayOwners.ContainsNewArrayLiteral(expression))
                {
                    context.CanGenerateArrayOwners = false;
                    ArrayOwners.GenerateSetArrayOwner(context, new SimpleVariableNode(DeclaredLocals[i], null));
                }

                // Generate actual assign
                expression.GenerateCode(context);
                VariablePatch varPatch = new(DeclaredLocals[i], InstanceType.Local);
                context.Emit(Opcode.Pop, varPatch, DataType.Variable, context.PopDataType());

                // Restore array owner state
                context.CanGenerateArrayOwners = canGenerateArrayOwners;
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<IASTNode> EnumerateChildren()
    {
        for (int i = 0; i < AssignedValues.Count; i++)
        {
            if (AssignedValues[i] is IASTNode expression)
            {
                yield return expression;
            }
        }
    }
}
