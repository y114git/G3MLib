using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using G3MLib.DataFile.Scripting;

namespace G3MLib.DataFile.Util;

/// <summary>
/// Standard scripting utility functions.
/// </summary>
public static class ScriptingUtil
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Creates default scripting options for CSX scripts, with all standard imports/references.
    /// </summary>
    public static ScriptOptions CreateDefaultScriptOptions()
    {
        return ScriptOptions.Default
            .AddImports("G3MLib.DataFile", "G3MLib.DataFile.Models", "G3MLib.DataFile.Decompiler",
                        "G3MLib.DataFile.Scripting", "G3MLib.DataFile.Compiler",
                        "System", "System.IO", "System.Collections.Generic",
                        "System.Text.RegularExpressions")
            .AddReferences(typeof(GameMakerObject).GetTypeInfo().Assembly,
                            typeof(System.Text.RegularExpressions.Regex).GetTypeInfo().Assembly,
                            typeof(ImageMagick.MagickImage).GetTypeInfo().Assembly,
                            typeof(G3MLib.Analyzer.Decompiler.DecompileContext).Assembly)
            .WithEmitDebugInformation(true); // When script throws an exception, adds an exception location (line number)
    }

    /// <summary>
    /// Prettifies/stringifies a scripting-caused exception object, dealing with script stack traces and aggregate exceptions.
    /// </summary>
    public static string PrettifyException(in Exception exc)
    {
        // If this is a simple script exception, return its message
        if (exc is ScriptException)
        {
            return exc.Message;
        }

        // Collect all original trace lines that we want to parse
        List<string> traceLines = new();
        void AddTraceLines(Exception exception)
        {
            if (exception.StackTrace is string stackTrace)
                traceLines.AddRange(stackTrace.Split(Environment.NewLine));
        }

        Dictionary<string, int> exTypesDict = null;
        if (exc is AggregateException aggregateException)
        {
            List<string> exTypes = [];

            // Collect trace lines of inner exceptions, and track their exception type names
            foreach (Exception ex in aggregateException.InnerExceptions)
            {
                AddTraceLines(ex);
                exTypes.Add(ex.GetType().FullName);
            }

            // Create a mapping of each exception type to the number of its occurrences
            if (exTypes.Count > 1)
            {
                exTypesDict = exTypes.GroupBy(x => x)
                                     .Select(x => new { Name = x.Key, Count = x.Count() })
                                     .OrderByDescending(x => x.Count)
                                     .ToDictionary(x => x.Name, x => x.Count);
            }
        }
        else if (exc.InnerException is not null)
        {
            // Collect trace lines of single inner exception
            AddTraceLines(exc.InnerException);
        }
        AddTraceLines(exc);

        // Iterate over all lines in the stack trace, finding their line numbers and file names
        List<(string SourceFile, int LineNum)> loadedScriptLineNums = new();
        int expectedNumScriptTraceLines = 0;
        try
        {
            foreach (string traceLine in traceLines)
            {
                // Only handle trace lines that come from a script
                if (traceLine.TrimStart().StartsWith("at Submission", StringComparison.Ordinal))
                {
                    // Add to total count of expected script trace lines
                    expectedNumScriptTraceLines++;

                    // Get full path of the script file, within the line
                    string sourceFile = Regex.Match(traceLine, @"(?<=in ).*\.csx(?=:line \d+)", RegexOptions.CultureInvariant, RegexTimeout).Value;
                    if (!File.Exists(sourceFile))
                        continue;

                    // Try to find line number from the line
                    const string pattern = ":line ";
                    int linePos = traceLine.IndexOf(pattern);
                    if (linePos > 0 && int.TryParse(traceLine[(linePos + pattern.Length)..], out int lineNum))
                    {
                        loadedScriptLineNums.Add((sourceFile, lineNum));
                    }
                }
            }
        }
        catch (Exception e)
        {
            string excString = exc.ToString();

            int endOfPrevStack = excString.IndexOf("--- End of stack trace from previous location ---");
            if (endOfPrevStack != -1)
            {
                // Keep only stack trace of the script
                excString = excString[..endOfPrevStack];
            }

            return $"An error occurred while processing the exception text.\nError message - \"{e.Message}\"\nThe unprocessed text is below.\n\n" + excString;
        }

        // Generate final exception text to show.
        // If we found the expected number of script trace lines, then use them; otherwise, use the regular exception text.
        string excText;
        if (loadedScriptLineNums.Count == expectedNumScriptTraceLines)
        {
            // Read the code for the files to know what the code line associated with the stack trace is
            Dictionary<string, List<string>> scriptsCode = [];
            foreach ((string sourceFile, int _) in loadedScriptLineNums)
            {
                if (!scriptsCode.ContainsKey(sourceFile))
                {
                    string scriptCode = null;
                    try
                    {
                        scriptCode = File.ReadAllText(sourceFile, Encoding.UTF8);
                    }
                    catch (Exception e)
                    {
                        string excString = exc.ToString();

                        return $"An error occurred while processing the exception text.\nError message - \"{e.Message}\"\nThe unprocessed text is below.\n\n" + excString;
                    }
                    scriptsCode.Add(sourceFile, scriptCode.Split('\n').ToList());
                }
            }

            if (loadedScriptLineNums.Any(pair => pair.LineNum is <= 0 || pair.LineNum > scriptsCode[pair.SourceFile].Count))
                return exc.ToString();

            // Generate custom stack trace
            string excLines = string.Join('\n', loadedScriptLineNums.Select(pair =>
            {
                string scriptName = Path.GetFileName(pair.SourceFile);
                string scriptLine = scriptsCode[pair.SourceFile][pair.LineNum - 1]; // - 1 because line numbers start from 1
                return $"Line {pair.LineNum} in script {scriptName}: {scriptLine}";
            }));

            if (exTypesDict is not null)
            {
                string exTypesStr = string.Join(",\n", exTypesDict.Select(x => $"{x.Key}{((x.Value > 1) ? " (x" + x.Value + ")" : string.Empty)}"));
                excText = $"{exc.GetType().FullName}: One or more errors occurred:\n{exTypesStr}\n\nThe current stacktrace:\n{excLines}";
            }
            else
            {
                excText = $"{exc.GetType().FullName}: {exc.Message}\n\nThe current stacktrace:\n{excLines}";
            }
        }
        else
        {
            string excString = exc.ToString();

            int endOfPrevStack = excString.IndexOf("--- End of stack trace from previous location ---");
            if (endOfPrevStack != -1)
            {
                // Keep only stack trace of the script
                excString = excString[..endOfPrevStack];
            }

            excText = excString;
        }

        return excText;
    }
}
