using System.Collections.Immutable;
using System.IO;
using System.Text;
using G3MLib.DataFile.Legacy;
using G3MLib.DataFile.Util;
using G3MLib.Modding.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace G3MLib.DataFile.Scripting;

/// <summary>The outcome of running a script. <see cref="Error"/> is a message ready to show to a user.</summary>
public sealed record ScriptRunResult(bool Success, string? Error = null)
{
    /// <summary>True when the run stopped because its token was cancelled or its timeout passed. <see cref="Success"/> is false then.</summary>
    public bool Cancelled { get; init; }
}

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

    /// <summary>
    /// Runs a script and reports failures as a result instead of throwing. Cancellation is cooperative: the script sees the token as <see cref="ScriptGlobals.CancellationToken"/>,
    /// and <see cref="ScriptGlobals"/> members, progress calls and the library's bulk operations stop with an <see cref="OperationCanceledException"/> once it is cancelled,
    /// so a script that never checks it still ends at its next library call. The token also ends when <paramref name="timeout"/> passes, when a nested run's parent is cancelled,
    /// or when the operation that started the script (such as a project import) is cancelled. The timeout also counts the time spent waiting for the data gate; <see cref="Timeout.InfiniteTimeSpan"/> or null means no limit. A cancelled run (a nested one included) returns <see cref="ScriptRunResult.Cancelled"/>.
    /// </summary>
    /// <remarks>
    /// The run holds the write gate of the <see cref="ScriptGlobals.Data"/> that is set when it starts. With no data (a script may call <c>MakeNewDataFile</c>) nothing is held, and a data
    /// object a script creates or assigns itself is not gated. The gate scope is bound to the thread that <see cref="ScriptGlobals.MainThreadAction"/> runs the script on and stays bound
    /// until the script ends, so code that runs on that thread meanwhile (a click handler handled while the script shows a dialog) is inside the scope and passes the gate.
    /// A host must not save, import or start another script from such a callback while a script runs.
    /// </remarks>
    public static Task<ScriptRunResult> RunAsync(string scriptPath, ScriptGlobals globals, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(globals);
        TimeSpan? limit = timeout == Timeout.InfiniteTimeSpan ? null : timeout;
        if (limit is { } checkedLimit && checkedLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be positive, or Timeout.InfiniteTimeSpan for no limit.");
        }

        var fullPath = Path.GetFullPath(scriptPath);
        var previousPath = globals.ScriptPath;
        var previousToken = globals.CancellationToken;
        using var timeoutSource = new CancellationTokenSource();
        if (limit is { } after)
        {
            timeoutSource.CancelAfter(after);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ModdingCancellation.Token, previousToken, timeoutSource.Token);
        var token = linked.Token;
        try
        {
            globals.CancellationToken = token;
            globals.ScriptPath = fullPath;
            globals.ClearScriptError();
            globals.ScriptRootPath ??= Path.GetDirectoryName(fullPath);
            var script = Compile(fullPath);
            // The gate is taken here, so a busy gate never blocks the host's main thread; the scope is bound to the thread that runs the script
            using var gate = globals.Data?.Gate.Acquire(write: true, cancellationToken: token);
            // The ambient token is set inside the action because a host may run it on another thread
            globals.MainThreadAction(() =>
            {
                using var scope = ModdingCancellation.Begin(token);
                gate?.Bind();
                try
                {
                    script.RunAsync(globals, token).GetAwaiter().GetResult();
                }
                finally
                {
                    gate?.Unbind();
                }
            });
            return Task.FromResult(new ScriptRunResult(true));
        }
        catch (CompilationErrorException e)
        {
            globals.SetScriptError(e);
            return Task.FromResult(new ScriptRunResult(false, "Compilation error:" + Environment.NewLine + string.Join(Environment.NewLine, e.Diagnostics)));
        }
        catch (Exception e) when (token.IsCancellationRequested && ModdingCancellation.IsCancellation(e))
        {
            var timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            return Task.FromResult(new ScriptRunResult(false, timedOut ? $"Script timed out after {timeout}." : "Script was cancelled.") { Cancelled = true });
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
            globals.CancellationToken = previousToken;
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
        if (result.Cancelled)
        {
            // A ScriptException would make the outer run report a failure instead of a cancellation
            throw new OperationCanceledException(globals.CancellationToken);
        }

        if (!result.Success)
        {
            throw new ScriptException(result.Error);
        }

        return true;
    }

    private static Script<object> Create(string scriptPath) =>
        CSharpScript.Create(UTMTScriptTranslator.Translate(File.ReadAllText(scriptPath, Encoding.UTF8)), CreateOptions(scriptPath), typeof(ScriptGlobals));
}
