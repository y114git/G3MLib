using System.Text;
using System.Text.RegularExpressions;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Compiler;

/// <summary>
/// Interface for different types of code import operations.
/// </summary>
internal interface ICodeImportOperation
{
    /// <summary>
    /// Code entry to be imported to, for this operation.
    /// </summary>
    GameMakerCode CodeEntry { get; }

    /// <summary>
    /// Name of the operation kind, as reported in import results.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Perform this import operation.
    /// </summary>
    /// <returns><see langword="false"/> when the operation found nothing to change and queued nothing.</returns>
    bool Import(CodeImportGroup group);
}

/// <summary>
/// Represents a code replace operation.
/// </summary>
internal readonly record struct CodeReplaceOperation(GameMakerCode CodeEntry, string Code) : ICodeImportOperation
{
    /// <inheritdoc/>
    public string Kind => "Replace";

    /// <inheritdoc/>
    public bool Import(CodeImportGroup group)
    {
        group.CompileGroup.QueueCodeReplace(CodeEntry, Code);
        return true;
    }
}

/// <summary>
/// Represents a code append operation (by decompiling existing code, and appending the code string to the end).
/// </summary>
internal readonly record struct CodeAppendOperation(GameMakerCode CodeEntry, string Code) : ICodeImportOperation
{
    /// <inheritdoc/>
    public string Kind => "Append";

    /// <inheritdoc/>
    public bool Import(CodeImportGroup group)
    {
        var finalCodeToCompile = string.Concat(group.DecompileExistingCode(CodeEntry), "\n", Code);
        group.CompileGroup.QueueCodeReplace(CodeEntry, finalCodeToCompile);
        return true;
    }
}

/// <summary>
/// Represents a code prepend operation (by decompiling existing code, and prepending the code string to the start).
/// </summary>
internal readonly record struct CodePrependOperation(GameMakerCode CodeEntry, string Code) : ICodeImportOperation
{
    /// <inheritdoc/>
    public string Kind => "Prepend";

    /// <inheritdoc/>
    public bool Import(CodeImportGroup group)
    {
        var finalCodeToCompile = string.Concat(Code, "\n", group.DecompileExistingCode(CodeEntry));
        group.CompileGroup.QueueCodeReplace(CodeEntry, finalCodeToCompile);
        return true;
    }
}

/// <summary>
/// Represents a code find and replace operation (by decompiling existing code, and performing find/replace on that string).
/// </summary>
internal readonly record struct CodeFindReplaceOperation
    (GameMakerCode CodeEntry, string Search, string Replacement, bool IsRegex, bool CaseSensitive, bool TrimmedLines) : ICodeImportOperation
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private readonly record struct TrimmedLine(int WhitespaceStartIndex, int StartIndex, int EndIndex, int WhitespaceEndIndex);

    /// <summary>
    /// Returns a list of ranges, representing trimmed lines from the given string.
    /// </summary>
    private static List<TrimmedLine> SplitByTrimmedLines(ReadOnlySpan<char> str)
    {
        List<TrimmedLine> lines = new(16);
        var lineWhitespaceStartIndex = 0;
        var lineStartIndex = 0;
        var lineEndIndex = 0;
        var beginningOfLine = true;
        for (var i = 0; i < str.Length; i++)
        {
            // End of line
            if (str[i] == '\n')
            {
                lines.Add(new(lineWhitespaceStartIndex, lineStartIndex, lineEndIndex, i + 1));
                lineWhitespaceStartIndex = i + 1;
                lineStartIndex = i + 1;
                lineEndIndex = i + 1;
                beginningOfLine = true;
                continue;
            }

            // Find the beginning of a line
            if (beginningOfLine && !char.IsWhiteSpace(str[i]))
            {
                lineStartIndex = i;
                beginningOfLine = false;
            }

            // Find the end of a line
            if (!beginningOfLine && !char.IsWhiteSpace(str[i]))
            {
                lineEndIndex = i + 1;
            }
        }

        // Handle final line
        lines.Add(new(lineWhitespaceStartIndex, lineStartIndex, lineEndIndex, str.Length));

        return lines;
    }

    /// <summary>
    /// Replaces Search with Replacement on the given string, processing on a line-by-line basis, trimming each line's whitespace.
    /// </summary>
    private string ReplaceTrimmedLines(ReadOnlySpan<char> code, StringComparison comparison)
    {
        ReadOnlySpan<char> search = Search;
        StringBuilder sb = new(code.Length);

        // Split search and code into each line being matched
        var codeLines = SplitByTrimmedLines(code);
        var searchLines = SplitByTrimmedLines(search);

        // Enumerate over code lines
        for (var i = 0; i < codeLines.Count; i++)
        {
            // Check for match starting at this line
            var match = true;
            if (i + searchLines.Count > codeLines.Count)
            {
                match = false;
            }
            else
            {
                // Compare all lines until any difference is found
                for (var j = 0; j < searchLines.Count; j++)
                {
                    var codeLine = codeLines[i + j];
                    var searchLine = searchLines[j];
                    var codeLineChars = code[codeLine.StartIndex..codeLine.EndIndex];
                    var searchLineChars = search[searchLine.StartIndex..searchLine.EndIndex];
                    if (!codeLineChars.Equals(searchLineChars, comparison))
                    {
                        match = false;
                        break;
                    }
                }
            }

            // If a match was found, insert replacement; otherwise, append current code line
            if (match)
            {
                sb.Append(Replacement);
                i += searchLines.Count - 1;
            }
            else
            {
                var codeLine = codeLines[i];
                var fullCodeLineChars = code[codeLine.WhitespaceStartIndex..codeLine.WhitespaceEndIndex];
                sb.Append(fullCodeLineChars);
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string Kind => IsRegex ? "RegexFindReplace" : TrimmedLines ? "TrimmedLinesFindReplace" : "FindReplace";

    /// <inheritdoc/>
    public bool Import(CodeImportGroup group)
    {
        // Decompile code
        var decompilation = group.DecompileExistingCode(CodeEntry);

        // Perform find & replace
        string finalCodeToCompile;
        if (IsRegex)
        {
            // Regex replace
            var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
            if (!CaseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }
            finalCodeToCompile = Regex.Replace(decompilation, Search, Replacement, options, RegexTimeout);
        }
        else
        {
            // Regular replace
            var comparison = StringComparison.Ordinal;
            if (!CaseSensitive)
            {
                comparison = StringComparison.OrdinalIgnoreCase;
            }
            if (TrimmedLines)
            {
                finalCodeToCompile = ReplaceTrimmedLines(decompilation, comparison);
            }
            else
            {
                finalCodeToCompile = decompilation.Replace(Search, Replacement, comparison);
            }
        }

        // Queue lower level replace operation, if code has changed
        if (decompilation != finalCodeToCompile)
        {
            group.CompileGroup.QueueCodeReplace(CodeEntry, finalCodeToCompile);
            return true;
        }
        if (group.ThrowOnNoOpFindReplace)
        {
            throw new InvalidOperationException($"No-op find and replace performed on {CodeEntry.Name?.Content}");
        }
        return false;
    }
}
