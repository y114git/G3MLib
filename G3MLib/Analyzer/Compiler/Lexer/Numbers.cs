/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Globalization;

namespace G3MLib.Analyzer.Compiler.Lexer;

/// <summary>
/// Helper for number and hex literal lexing operations.
/// </summary>
internal static class Numbers
{
    /// <summary>
    /// Parses a decimal number from the given text position.
    /// </summary>
    public static int ParseDecimal(LexContext context, int startPosition)
    {
        var pos = ContiguousTextReader.ReadWhileNumber(context.Text, startPosition, out var number);

        // Parse number
        if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            // Check if the integer can be represented losslessly as a double
            if ((long)(double)intValue == intValue)
            {
                context.Tokens.Add(new TokenNumber(context, startPosition, number.ToString(), intValue));
            }
            else
            {
                context.Tokens.Add(new TokenInt64(context, startPosition, number.ToString(), intValue));
            }
        }
        else if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            context.Tokens.Add(new TokenNumber(context, startPosition, number.ToString(), floatValue));
        }
        else
        {
            context.CompileContext.PushError($"Invalid number \"{number}\"", context, startPosition);
        }

        return pos;
    }

    /// <summary>
    /// Parses a hex literal from the given text position.
    /// </summary>
    public static int ParseHex(LexContext context, int startPosition, bool dollarSignSyntax)
    {
        var prefix = dollarSignSyntax ? "$" : "0x";
        var pos = ContiguousTextReader.ReadWhileHex(context.Text, startPosition + (dollarSignSyntax ? 1 : 2), out var hex);

        // Parse hex as a number
        if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            if (value is >= int.MinValue and <= int.MaxValue)
            {
                context.Tokens.Add(new TokenNumber(context, startPosition, $"{prefix}{hex}", value));
            }
            else
            {
                context.Tokens.Add(new TokenInt64(context, startPosition, $"{prefix}{hex}", value));
            }
        }
        else
        {
            context.CompileContext.PushError("Invalid hex literal", context, startPosition);
        }

        return pos;
    }
}
