/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Compiler.Lexer;

namespace G3MLib.Analyzer.Compiler.Errors;

/// <summary>
/// Represents an error that occurred when parsing code into an AST.
/// </summary>
public sealed class ParserError : ICompileError
{
    /// <inheritdoc/>
    public string BaseMessage { get; }

    private readonly IToken? _nearbyToken;
    private readonly string _codeText;

    /// <summary>
    /// Internal constructor for parser errors. Takes a nearby token, or null if there is none.
    /// </summary>
    internal ParserError(string baseMessage, string codeText, IToken? nearbyToken = null)
    {
        BaseMessage = baseMessage;
        _codeText = codeText;
        _nearbyToken = nearbyToken;
    }

    /// <inheritdoc/>
    public GmlDiagnostic Diagnostic
    {
        get
        {
            var code = GmlDiagnostic.ClassifyParserMessage(BaseMessage);
            if (_nearbyToken is IToken token)
            {
                var text = token.Context.Text;
                var start = Math.Min(token.TextPosition, text.Length);
                var span = new LineMap(text).GetSpan(start, start + Math.Max(1, token.ToString().Length));
                return new GmlDiagnostic(DiagnosticSeverity.Error, code, BaseMessage, span, token.Context.MacroName);
            }
            // No token: the error is about the end of the code
            return new GmlDiagnostic(DiagnosticSeverity.Error, code, BaseMessage, new LineMap(_codeText).GetSpan(_codeText.Length, _codeText.Length));
        }
    }

    /// <inheritdoc/>
    public string GenerateMessage()
    {
        if (_nearbyToken is IToken token)
        {
            (var line, var column) = token.Context.GetLineAndColumnFromPos(token.TextPosition);
            if (token.Context.MacroName is string macroName)
            {
                return $"{BaseMessage} around line {line}, column {column} of macro \"{macroName}\"";
            }
            return $"{BaseMessage} around line {line}, column {column}";
        }
        return BaseMessage;
    }
}
