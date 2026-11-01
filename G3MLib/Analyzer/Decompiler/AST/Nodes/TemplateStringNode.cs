/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Decompiler.GameSpecific;

namespace G3MLib.Analyzer.Decompiler.AST;

/// <summary>
/// Represents a template string in the AST.
/// </summary>
public class TemplateStringNode(IGMString format, List<IExpressionNode> fields)
    : IConditionalValueNode
{
    /// <summary>
    /// The underlying format string.
    /// </summary>
    public IGMString Format { get; private set; } = format;

    /// <summary>
    /// The values to be inserted in the format string.
    /// </summary>
    public List<IExpressionNode> Fields { get; private set; } = fields;

    /// <inheritdoc/>
    public bool Duplicated { get; set; }

    /// <inheritdoc/>
    public bool Group { get; set; }

    /// <inheritdoc/>
    public IGMInstruction.DataType StackType { get; set; } = IGMInstruction.DataType.Variable;

    public string ConditionalTypeName => "TemplateString";

    public string ConditionalValue => Format.Content;

    /// <inheritdoc/>
    public IExpressionNode Clean(ASTCleaner cleaner)
    {
        for (var i = 0; i < Fields.Count; i++)
        {
            Fields[i] = Fields[i].Clean(cleaner);
        }
        return this;
    }

    public IEnumerable<IBaseASTNode> EnumerateChildren()
    {
        return Fields;
    }

    public IExpressionNode PostClean(ASTCleaner cleaner)
    {
        for (var i = 0; i < Fields.Count; i++)
        {
            Fields[i] = Fields[i].PostClean(cleaner);
        }
        return this;
    }

    public void Print(ASTPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        printer.Write("$\"");

        var formatText = Format.Content;
        var nextExpectedFieldIndex = 0;
        var maxExpectedFieldIndex = Fields.Count - 1;
        for (var i = 0; i < formatText.Length; i++)
        {
            // Print non-placeholder starting characters normally
            if (formatText[i] != '{')
            {
                printer.Write(StringNode.EscapeChar(formatText[i]));
                continue;
            }

            // Parse the placeholder (may not necessarily be valid)
            var startIndex = i + 1;
            var j = startIndex;
            var invalidCharacter = false;
            while (j < formatText.Length && formatText[j] != '}')
            {
                if (formatText[j] is < '0' or > '9')
                {
                    invalidCharacter = true;
                    break;
                }
                j++;
            }
            if (invalidCharacter || j >= formatText.Length || j == startIndex)
            {
                // Invalid - escape the starting character!
                printer.Write("\\{");
                continue;
            }
            var placeholderNumberText = formatText.AsSpan()[startIndex..j];
            if (!int.TryParse(placeholderNumberText, out var fieldIndex))
            {
                // Invalid - escape the starting character!
                printer.Write("\\{");
                continue;
            }
            if (fieldIndex < 0 || fieldIndex > maxExpectedFieldIndex || fieldIndex != nextExpectedFieldIndex)
            {
                // Totally invalid or unexpected field index... this should have been handled already!
                throw new DecompilerException("Unexpected string template placeholder index");
            }

            // Print the actual field
            printer.Write('{');
            printer.PrintNode(Fields[fieldIndex]);
            printer.Write('}');

            // Move on to next field - should be in linear order for regular interpolated strings
            nextExpectedFieldIndex++;

            // Advance to the end of the placeholder
            i = j;
        }

        // If we didn't use exactly the number of expected indices, that's a problem... this should've been handled already!
        if (nextExpectedFieldIndex != maxExpectedFieldIndex + 1)
        {
            throw new DecompilerException("Unexpected final string template placeholder index");
        }

        printer.Write('"');
    }

    public bool RequiresMultipleLines(ASTPrinter printer, bool isStatementLHS)
    {
        return Fields.Any(field => field.RequiresMultipleLines(printer, false));
    }

    public IExpressionNode? ResolveMacroType(ASTCleaner cleaner, IMacroType type)
    {
        if (type is IMacroTypeConditional conditional)
        {
            return conditional.Resolve(cleaner, this);
        }
        return null;
    }
}
