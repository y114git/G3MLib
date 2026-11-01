using System.Collections.Immutable;
using System.IO;
using System.Text;
using G3MLib.DataFile.Legacy;
using G3MLib.DataFile.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace G3MLib.DataFile.Scripting;

/// <summary>The outcome of running a script. <see cref="Error"/> is a message ready to show to a user.</summary>
public sealed record ScriptRunResult(bool Success, string? Error = null);

/// <summary>
/// Compiles and runs <c>.csx</c> scripts against <see cref="ScriptGlobals"/>. Every host (CLI, GUI, project imports) goes through here,
/// so scripts written for UndertaleModTool are translated and see the same options everywhere.
/// </summary>
public static class ScriptRunner
{
    /// <summary>Script options for <paramref name="scriptPath"/>: the standard imports and references, with <c>#load</c> and <c>#r</c> resolved next to the script.</summary>
    public static ScriptOptions CreateOptions(string scriptPath)
    {
        var directory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;
        return ScriptingUtil.CreateDefaultScriptOptions()
            .WithFilePath(scriptPath)
            .WithFileEncoding(Encoding.UTF8)
            .WithSourceResolver(ScriptSourceResolver.Default.WithBaseDirectory(directory))
            .WithMetadataResolver(ScriptMetadataResolver.Default.WithBaseDirectory(directory));
    }

    /// <summary>Compiles a script without running it. Throws <see cref="CompilationErrorException"/> when it does not compile.</summary>
    public static Script<object> Compile(string scriptPath)
    {
        var script = Create(scriptPath);
        ImmutableArray<Diagnostic> errors = [.. script.Compile().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        if (!errors.IsEmpty)
        {
            throw new CompilationErrorException(string.Join(Environment.NewLine, errors), errors);
        }

        return script;
    }

    /// <summary>Runs a script and reports failures as a result instead of throwing.</summary>
    public static Task<ScriptRunResult> RunAsync(string scriptPath, ScriptGlobals globals)
    {
        ArgumentNullException.ThrowIfNull(globals);
        var fullPath = Path.GetFullPath(scriptPath);
        var previousPath = globals.ScriptPath;
        try
        {
            globals.ScriptPath = fullPath;
            globals.ClearScriptError();
            globals.ScriptRootPath ??= Path.GetDirectoryName(fullPath);
            var script = Compile(fullPath);
            globals.MainThreadAction(() => script.RunAsync(globals).GetAwaiter().GetResult());
            return Task.FromResult(new ScriptRunResult(true));
        }
        catch (CompilationErrorException e)
        {
            globals.SetScriptError(e);
            return Task.FromResult(new ScriptRunResult(false, "Compilation error:" + Environment.NewLine + string.Join(Environment.NewLine, e.Diagnostics)));
        }
        catch (Exception e)
        {
            if (e is not ScriptException || globals.ScriptErrorMessage.Length == 0)
            {
                globals.SetScriptError(e);
            }
            var inner = e is AggregateException { InnerExceptions: [Exception single] } ? single : e;
            return Task.FromResult(new ScriptRunResult(false, inner is ScriptException ? inner.Message : "Script execution failed: " + ScriptingUtil.PrettifyException(in inner)));
        }
        finally
        {
            globals.ScriptPath = previousPath;
        }
    }

    /// <summary>Runs a script started by another script. The path is relative to the calling script and must stay inside the globals' root directory.</summary>
    internal static bool RunNested(string path, ScriptGlobals globals)
    {
        var root = globals.ScriptRootPath ?? Environment.CurrentDirectory;
        var directory = Path.GetDirectoryName(globals.ScriptPath) ?? root;
        var fullPath = Path.GetFullPath(path, directory);
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ScriptException("Nested script path escapes the script directory.");
        }

        if (!File.Exists(fullPath))
        {
            throw new ScriptException("Script was not found: " + fullPath);
        }

        var result = RunAsync(fullPath, globals).GetAwaiter().GetResult();
        if (!result.Success)
        {
            throw new ScriptException(result.Error);
        }

        return true;
    }

    private static Script<object> Create(string scriptPath) =>
        CSharpScript.Create(UTMTScriptTranslator.Translate(File.ReadAllText(scriptPath, Encoding.UTF8)), CreateOptions(scriptPath), typeof(ScriptGlobals));
}
