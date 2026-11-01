/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Compiler;

/// <summary>
/// Kinds of tokens returned by <see cref="GmlTokenizer"/>.
/// </summary>
public enum GmlTokenKind
{
    Keyword,
    Boolean,
    Identifier,
    Number,
    String,
    TemplateString,
    Comment,
    Operator,
    Separator,
    Preprocessor,
    Unknown
}

/// <summary>A token with its kind and source range.</summary>
public readonly record struct GmlToken(GmlTokenKind Kind, SourceSpan Span);

/// <summary>
/// Splits GML text into classified tokens without a game context and without compiling.
/// It never fails: unrecognized characters become <see cref="GmlTokenKind.Unknown"/> and unterminated
/// strings and comments run to the end of the line or text. Whitespace is skipped; comments are returned.
/// </summary>
public static class GmlTokenizer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "then", "else", "switch", "case", "default", "begin", "end", "break", "continue", "exit", "return",
        "while", "for", "repeat", "do", "until", "with", "var", "globalvar", "not", "div", "mod", "and", "or", "xor",
        "enum", "try", "catch", "finally", "throw", "new", "delete", "function", "static"
    };

    // Longer entries come first, so greedy matching works.
    private static readonly string[] Operators =
    [
        "??=", "<<", ">>", ">=", "<=", "==", "!=", "<>", "&&", "||", "^^", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=",
        "??", "++", "--", ":=", "=", "+", "-", "*", "/", "%", "!", "?", "&", "|", "^", "~", "<", ">"
    ];

    private static readonly string[] Separators =
    [
        "[|", "[?", "[#", "[@", "[$", "{", "}", "(", ")", ".", ",", ";", ":", "[", "]"
    ];

    /// <summary>
    /// Tokenizes GML text.
    /// </summary>
    /// <param name="text">GML source.</param>
    /// <param name="gms2OrLater">Whether to use GameMaker Studio 2 string rules (escapes, <c>@"</c> and <c>$"</c> strings).</param>
    public static IReadOnlyList<GmlToken> Tokenize(string text, bool gms2OrLater = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        LineMap map = new(text);
        List<GmlToken> tokens = new(Math.Max(16, text.Length / 4));
        var pos = 0;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
                continue;
            }

            var next = pos + 1 < text.Length ? text[pos + 1] : '\0';
            GmlTokenKind kind;
            int end;
            if (c == '/' && next == '/')
            {
                end = text.IndexOf('\n', pos);
                end = end < 0 ? text.Length : end;
                kind = GmlTokenKind.Comment;
            }
            else if (c == '/' && next == '*')
            {
                end = text.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                kind = GmlTokenKind.Comment;
            }
            else if (IsIdentStart(c))
            {
                end = ScanWhile(text, pos, IsIdentPart);
                var word = text[pos..end];
                kind = word is "true" or "false" ? GmlTokenKind.Boolean
                    : Keywords.Contains(word) ? GmlTokenKind.Keyword
                    : GmlTokenKind.Identifier;
            }
            else if (c == '#' && IsIdentStart(next))
            {
                end = ScanWhile(text, pos + 1, IsIdentPart);
                kind = GmlTokenKind.Preprocessor;
            }
            else if (c == '$' && next == '"' && gms2OrLater)
            {
                end = ScanTemplate(text, pos + 2);
                kind = GmlTokenKind.TemplateString;
            }
            else if (c == '@' && (next == '"' || next == '\'') && gms2OrLater)
            {
                end = ScanVerbatim(text, pos + 2, next);
                kind = GmlTokenKind.String;
            }
            else if (c == '"' && gms2OrLater)
            {
                end = ScanEscaped(text, pos + 1);
                kind = GmlTokenKind.String;
            }
            else if (c == '"' || c == '\'' && !gms2OrLater)
            {
                end = ScanVerbatim(text, pos + 1, c);
                kind = GmlTokenKind.String;
            }
            else if (char.IsAsciiDigit(c) || c == '.' && char.IsAsciiDigit(next))
            {
                end = ScanNumber(text, pos);
                kind = GmlTokenKind.Number;
            }
            else if (c == '$' && char.IsAsciiHexDigit(next))
            {
                end = ScanWhile(text, pos + 1, char.IsAsciiHexDigit);
                kind = GmlTokenKind.Number;
            }
            else if (Match(text, pos, Separators) is > 0 and int sepLen)
            {
                end = pos + sepLen;
                kind = GmlTokenKind.Separator;
            }
            else if (Match(text, pos, Operators) is > 0 and int opLen)
            {
                end = pos + opLen;
                kind = GmlTokenKind.Operator;
            }
            else
            {
                end = pos + 1;
                kind = GmlTokenKind.Unknown;
            }

            tokens.Add(new GmlToken(kind, map.GetSpan(pos, end)));
            pos = end;
        }
        return tokens;
    }

    private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private static int ScanWhile(string text, int pos, Func<char, bool> predicate)
    {
        while (pos < text.Length && predicate(text[pos]))
        {
            pos++;
        }
        return pos;
    }

    private static int Match(string text, int pos, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.CompareOrdinal(text, pos, candidate, 0, candidate.Length) == 0)
            {
                return candidate.Length;
            }
        }
        return 0;
    }

    private static int ScanNumber(string text, int pos)
    {
        if (text[pos] == '0' && pos + 1 < text.Length && text[pos + 1] is 'x' or 'X')
        {
            return ScanWhile(text, pos + 2, char.IsAsciiHexDigit);
        }
        pos = ScanWhile(text, pos, char.IsAsciiDigit);
        if (pos < text.Length && text[pos] == '.')
        {
            pos = ScanWhile(text, pos + 1, char.IsAsciiDigit);
        }
        return pos;
    }

    // Regular string: backslash escapes, ends at the closing quote or at a newline.
    private static int ScanEscaped(string text, int pos)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\\' && pos + 1 < text.Length && text[pos + 1] != '\n')
            {
                pos += 2;
                continue;
            }
            if (c == '\n')
            {
                return pos;
            }
            pos++;
            if (c == '"')
            {
                return pos;
            }
        }
        return pos;
    }

    // Verbatim string: no escapes, may span lines.
    private static int ScanVerbatim(string text, int pos, char quote)
    {
        var close = text.IndexOf(quote, pos);
        return close < 0 ? text.Length : close + 1;
    }

    // Template string: fields in braces may hold quotes, so quotes only end the string at brace depth 0.
    private static int ScanTemplate(string text, int pos)
    {
        var depth = 0;
        while (pos < text.Length)
        {
            var c = text[pos++];
            if (c == '\\' && depth == 0 && pos < text.Length)
            {
                pos++;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
            }
            else if (c == '"' && depth == 0)
            {
                return pos;
            }
            else if (c == '\n' && depth == 0)
            {
                return pos - 1;
            }
        }
        return pos;
    }
}
