/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace G3MLib.Analyzer.Compiler.Lexer;

/// <summary>
/// Helper class to read text faster than regular string building operations, when text is contiguous.
/// </summary>
internal static class ContiguousTextReader
{
    /// <summary>
    /// Reads text until reaching a whitespace, outputting a string containing the text, and returning its end position.
    /// </summary>
    public static int ReadUntilWhitespace(ReadOnlySpan<char> text, int startPosition, out ReadOnlySpan<char> output)
    {
        // Read until no longer identifier characters
        var pos = startPosition;
        while (pos < text.Length)
        {
            if (!char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }
            else
            {
                // No longer identifier characters, so stop
                break;
            }
        }

        output = text[startPosition..pos];
        return pos;
    }

    /// <summary>
    /// Reads text until reaching a specific character, outputting a string containing the text, and returning its end position.
    /// </summary>
    public static int ReadUntilChar(ReadOnlySpan<char> text, int startPosition, char c, out ReadOnlySpan<char> output)
    {
        // Read until no longer identifier characters
        var pos = startPosition;
        while (pos < text.Length)
        {
            if (text[pos] != c)
            {
                pos++;
            }
            else
            {
                // No longer identifier characters, so stop
                break;
            }
        }

        output = text[startPosition..pos];
        return pos;
    }

    /// <summary>
    /// Reads identifier text until reaching its end, outputting a string containing the text, and returning its end position.
    /// </summary>
    public static int ReadWhileIdentifier(ReadOnlySpan<char> text, int startPosition, out ReadOnlySpan<char> output)
    {
        // Read until no longer identifier characters
        var pos = startPosition;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
            {
                pos++;
            }
            else
            {
                // No longer identifier characters, so stop
                break;
            }
        }

        output = text[startPosition..pos];
        return pos;
    }

    /// <summary>
    /// Reads internal identifier text until reaching its end, outputting a string containing the text, and returning its end position.
    /// </summary>
    /// <remarks>
    /// An "internal" identifier is one starting with '@' and containing at least one character that is not '@'.
    /// </remarks>
    public static int ReadWhileInternalIdentifier(ReadOnlySpan<char> text, int startPosition, out ReadOnlySpan<char> output, out bool anyNormalChars)
    {
        // Read until no longer internal identifier characters
        anyNormalChars = false;
        var pos = startPosition;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '@')
            {
                pos++;
                if (c != '@')
                {
                    // Mark that we found a "normal" identifier character
                    anyNormalChars = true;
                }
            }
            else
            {
                // No longer internal identifier characters, so stop
                break;
            }
        }

        output = text[startPosition..pos];
        return pos;
    }

    /// <summary>
    /// Reads number (decimal) text until reaching its end, outputting a string containing the text, and returning its end position.
    /// </summary>
    public static int ReadWhileNumber(ReadOnlySpan<char> text, int startPosition, out ReadOnlySpan<char> output)
    {
        // Read until no longer number characters
        var pos = startPosition;
        var usedDot = false;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsDigit(c) || !usedDot && c == '.')
            {
                usedDot |= c == '.';
                pos++;
            }
            else
            {
                // No longer number characters, so stop
                break;
            }
        }

        output = text[startPosition..pos];
        return pos;
    }

    /// <summary>
    /// Reads hex digit text until reaching its end, outputting a string containing the text, and returning its end position.
    /// </summary>
    public static int ReadWhileHex(ReadOnlySpan<char> text, int startPosition, out ReadOnlySpan<char> output)
    {
        // Read until no longer hex digit characters
        var pos = startPosition;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f')
            {
                pos++;
            }
            else
            {
                // No longer hex digit characters, so stop
                break;
            }
        }

        output = text[startPosition..pos];
        return pos;
    }
}
