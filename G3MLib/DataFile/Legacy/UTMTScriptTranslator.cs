using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Linq;
using System.Text;

namespace G3MLib.DataFile.Legacy;

/// <summary>
/// Translates source code written against the UndertaleModTool public API into the current API.
/// </summary>
public static class UTMTScriptTranslator
{
    /// <summary>
    /// Rewrites legacy API identifiers while preserving comments, literals, and formatting.
    /// </summary>
    public static string Translate(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.Contains("Undertale", StringComparison.Ordinal) &&
            !source.Contains("Underanalyzer", StringComparison.Ordinal) &&
            !source.Contains("undertaleData", StringComparison.Ordinal))
        {
            return source;
        }

        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var replacements = root.DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken) && TryTranslate(token.ValueText, out _))
            .Select(token =>
            {
                TryTranslate(token.ValueText, out string replacement);
                return (token.SpanStart, token.Span.Length, replacement);
            })
            .OrderByDescending(replacement => replacement.SpanStart);

        var translated = new StringBuilder(source);
        foreach ((int start, int length, string replacement) in replacements)
            translated.Remove(start, length).Insert(start, replacement);

        return translated.ToString();
    }

    private static bool TryTranslate(string identifier, out string replacement)
    {
        replacement = identifier switch
        {
            "UndertaleModLib" => "G3MLib.DataFile",
            "Underanalyzer" => "G3MLib.Analyzer",
            "undertaleData" => "gameMakerData",
            _ when identifier.StartsWith("Undertale", StringComparison.Ordinal) => "GameMaker" + identifier["Undertale".Length..],
            _ => string.Empty
        };

        return replacement.Length != 0;
    }
}
