using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A string entry a data file can have.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerString : GameMakerResource, ISearchable, IDisposable, G3MLib.Analyzer.IGMString
{
    /// <summary>
    /// The contents of the string.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Initializes a new empty instance of type <see cref="GameMakerString"/>.
    /// </summary>
    public GameMakerString()
    {
    }

    /// <summary>
    /// Initializes a new instance of type <see cref="GameMakerString"/> with a specified content.
    /// </summary>
    /// <param name="content">The content for the string.</param>
    public GameMakerString(string? content)
    {
        this.Content = content;
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGMString(Content);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Content = reader.ReadGMString();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return ToString(true);
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <param name="isGMS2">Whether the string is from a GameMaker Studio 2 data file or not.</param>
    /// <returns>A string that represents the current object.</returns>
    public string ToString(bool isGMS2)
    {
        if (Content == null)
            return "\"null\""; // NPE Fix.

        if (isGMS2)
            return "\"" + Content.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"") + "\"";

        // Handle GM:S 1's lack of escaping
        // Yes, in GM:S 1 you cannot escape quotation marks. You are expected to concatenate
        // single-quote strings with double-quote strings.
        string res = Content;
        bool front, back;
        if (res.StartsWith('"'))
        {
            front = true;
            res = res.Remove(0, 1);
            if (res.Length == 0)
                return "'\"'";
        }
        else
            front = false;
        if (res.EndsWith('"'))
        {
            res = res.Remove(res.Length - 1);
            back = true;
        }
        else
            back = false;
        res = res.Replace("\"", "\" + '\"' + \"");
        if (front)
            res = "'\"' + \"" + res;
        else
            res = "\"" + res;
        if (back)
            res += "\" + '\"'";
        else
            res += "\"";
        return res;
    }

    /// <inheritdoc />
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        Content = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public bool SearchMatches(string? filter)
    {
        return filter is not null && Content?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Unescapes text for the assembler.
    /// </summary>
    /// <param name="text">The text to unescape.</param>
    /// <returns>A string which features the <b>text</b> <c>\n</c>, <c>\r</c>, <c>"</c> and <c>\</c> being properly unescaped.</returns>
    public static string UnescapeText(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch != '\\' || i + 1 >= text.Length)
            {
                result.Append(ch);
                i++;
                continue;
            }

            char next = text[i + 1];
            i += 2;
            switch (next)
            {
                case 'r':
                    result.Append('\r');
                    break;
                case 'n':
                    result.Append('\n');
                    break;
                case '"':
                    result.Append('"');
                    break;
                case '\\':
                    result.Append('\\');
                    break;
                default:
                    result.Append('\\');
                    result.Append(next);
                    break;
            }
        }
        return result.ToString();
    }
}
