/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Compiler;

/// <summary>
/// A range of source text. Offsets are zero-based UTF-16 indices, the end is exclusive.
/// Lines and columns are one-based; the end line and column describe the position of <see cref="EndOffset"/>.
/// </summary>
public readonly record struct SourceSpan(int StartOffset, int EndOffset, int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    /// <summary>Length of the span in characters.</summary>
    public int Length => EndOffset - StartOffset;

    /// <summary>Whether the offset lies inside the span.</summary>
    public bool Contains(int offset) => offset >= StartOffset && offset < EndOffset;
}

/// <summary>
/// Converts text offsets to one-based lines and columns. Lines end at <c>\n</c>.
/// </summary>
public sealed class LineMap
{
    private readonly List<int> _lineStarts = [0];

    /// <summary>Length of the mapped text.</summary>
    public int TextLength { get; }

    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TextLength = text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    /// <summary>Number of lines in the text.</summary>
    public int LineCount => _lineStarts.Count;

    /// <summary>Zero-based offset where the one-based line starts.</summary>
    public int GetLineStart(int line) => _lineStarts[line - 1];

    /// <summary>Returns the one-based line and column of an offset, clamped to the text.</summary>
    public (int Line, int Column) GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, TextLength);
        var index = _lineStarts.BinarySearch(offset);
        if (index < 0)
        {
            index = ~index - 1;
        }
        return (index + 1, offset - _lineStarts[index] + 1);
    }

    /// <summary>Builds a span from two offsets, clamped to the text.</summary>
    public SourceSpan GetSpan(int start, int end)
    {
        start = Math.Clamp(start, 0, TextLength);
        end = Math.Clamp(end, start, TextLength);
        (var startLine, var startColumn) = GetPosition(start);
        (var endLine, var endColumn) = GetPosition(end);
        return new SourceSpan(start, end, startLine, startColumn, endLine, endColumn);
    }
}

/// <summary>
/// Severity of a <see cref="GmlDiagnostic"/>.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A compiler message with a stable code and the source range it refers to.
/// </summary>
/// <param name="Severity">Severity of the message.</param>
/// <param name="Code">Stable identifier of the message category, for example <c>GML2001</c>.</param>
/// <param name="Message">Message text without position information.</param>
/// <param name="Span">Source range in the text of the code entry, or of the macro named by <paramref name="MacroName"/>.</param>
/// <param name="MacroName">Name of the macro whose text the span refers to, or <see langword="null"/> for the code entry itself.</param>
public sealed record GmlDiagnostic(DiagnosticSeverity Severity, string Code, string Message, SourceSpan Span, string? MacroName = null)
{
    /// <summary>Lexer error: the text could not be split into tokens.</summary>
    public const string LexerErrorCode = "GML1001";

    /// <summary>Parser error: the tokens do not form a valid statement or expression.</summary>
    public const string SyntaxErrorCode = "GML2001";

    /// <summary>A name could not be resolved to a function, script or enum value.</summary>
    public const string UnknownSymbolCode = "GML3001";

    /// <summary>Meaning error such as a duplicate declaration or an invalid assignment.</summary>
    public const string SemanticErrorCode = "GML3002";

    /// <summary>The syntax is not available in the GameMaker version of the file.</summary>
    public const string UnsupportedByVersionCode = "GML4001";

    /// <summary>The compiler threw an exception.</summary>
    public const string InternalErrorCode = "GML9001";

    internal static string ClassifyParserMessage(string message)
    {
        if (message.StartsWith("Failed to find function", StringComparison.Ordinal) || message.StartsWith("Failed to find script", StringComparison.Ordinal) || message.StartsWith("Failed to find enum", StringComparison.Ordinal))
        {
            return UnknownSymbolCode;
        }
        if (message.StartsWith("Expected ", StringComparison.Ordinal) || message.StartsWith("Unexpected end of code", StringComparison.Ordinal) || message.StartsWith("Failed to find a valid", StringComparison.Ordinal) || message.StartsWith("Failed to find valid", StringComparison.Ordinal))
        {
            return SyntaxErrorCode;
        }
        if (message.Contains("before GMLv2", StringComparison.Ordinal) || message.Contains("before GMS2", StringComparison.Ordinal) || message.Contains("in this GameMaker version", StringComparison.Ordinal))
        {
            return UnsupportedByVersionCode;
        }
        return SemanticErrorCode;
    }
}
