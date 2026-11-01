using System.IO;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;

namespace G3MLib.DataFile.Scripting;

/// <summary>
/// The globals every <c>.csx</c> script runs against, whatever host starts it. Implements <see cref="IScriptInterface"/>.
/// Without a host the dialogs and prompts are unavailable and throw a <see cref="ScriptException"/>.
/// A host overrides the interactive members and <see cref="ReportProgress"/>, or sets the message handlers.
/// </summary>
public class ScriptGlobals : IScriptInterface
{
    private int _progress;
    private double _progressMaximum = 100;
    private string _progressStatus = string.Empty;

    public ScriptGlobals(GameMakerData? data = null, string filePath = "", string? scriptPath = null)
    {
        Data = data;
        FilePath = filePath ?? string.Empty;
        ScriptPath = scriptPath;
        ScriptRootPath = scriptPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(scriptPath));
    }

    public GameMakerData Data { get; set; }
    public ProjectContext Project { get; set; }
    public string FilePath { get; set; }
    public string ScriptPath { get; set; }
    public object Highlighted { get; private set; }
    public object Selected { get; private set; }
    public bool CanSave { get; set; }
    public bool ScriptExecutionSuccess => ScriptErrorMessage.Length == 0;
    public string ScriptErrorMessage { get; private set; } = string.Empty;
    public string ExePath { get; set; } = AppContext.BaseDirectory;
    public string ScriptErrorType { get; private set; } = string.Empty;
    public bool IsAppClosed { get; set; }
    public Action<Action> MainThreadAction { get; set; } = static action => action();

    /// <summary>Receives <see cref="ScriptMessage"/>, <see cref="SetUMTConsoleText"/> and <see cref="SimpleTextOutput"/> text.</summary>
    public Action<string> MessageHandler { get; set; }

    /// <summary>Receives <see cref="ScriptWarning"/> text.</summary>
    public Action<string> WarningHandler { get; set; }

    /// <summary>Nested scripts started with <see cref="RunUMTScript"/> must stay inside this directory. When unset, the folder of the first script that runs.</summary>
    public string ScriptRootPath { get; set; }

    /// <summary>Path of the data file the host loaded, or an empty string.</summary>
    public string DataFilePath { get; set; } = string.Empty;

    /// <summary>Folder the host writes results to, or an empty string.</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>Folder the host reads inputs from, or an empty string.</summary>
    public string InputDir { get; set; } = string.Empty;

    /// <summary>Whether the host shows detailed output.</summary>
    public virtual bool Verbose => false;

    public void EnsureDataLoaded()
    {
        if (Data is null)
        {
            throw new ScriptException("No data file is currently loaded!");
        }
    }

    public virtual bool MakeNewDataFile()
    {
        if (Data is not null)
        {
            return false;
        }

        Data = GameMakerData.CreateNew2026LTS();
        return true;
    }

    public virtual void ScriptMessage(string message) => MessageHandler?.Invoke(message);
    public virtual void ScriptWarning(string message) => WarningHandler?.Invoke(message);
    public virtual void SetUMTConsoleText(string message) => ScriptMessage(message);
    public virtual bool ScriptQuestion(string message) => false;

    public virtual void ScriptError(string error, string title = "Error", bool SetConsoleText = true)
    {
        ScriptErrorMessage = error;
        ScriptErrorType = title;
        throw new ScriptException(error);
    }

    public virtual void ScriptOpenURL(string url) => throw UiUnavailable();

    public virtual bool RunUMTScript(string path) => ScriptRunner.RunNested(path, this);

    public virtual bool LintUMTScript(string path)
    {
        try
        {
            ScriptRunner.Compile(Path.GetFullPath(path));
            return true;
        }
        catch (Exception e)
        {
            SetScriptError(e);
            return false;
        }
    }

    public virtual void InitializeScriptDialog()
    {
    }

    public string GetDecompiledText(string codeName, GlobalDecompileContext? context = null, IDecompileSettings? settings = null)
    {
        EnsureDataLoaded();
        return GetDecompiledText(Data.Code.ByName(codeName) ?? throw new ScriptException($"Code entry \"{codeName}\" was not found."), context, settings);
    }

    public string GetDecompiledText(GameMakerCode code, GlobalDecompileContext? context = null, IDecompileSettings? settings = null)
    {
        EnsureDataLoaded();
        try
        {
            return new DecompileContext(context ?? new GlobalDecompileContext(Data), code, settings ?? Data.ToolInfo.DecompilerSettings).DecompileToString();
        }
        catch (Exception)
        {
            return "/* DECOMPILER FAILED */";
        }
    }

    public string GetDisassemblyText(string codeName)
    {
        EnsureDataLoaded();
        return GetDisassemblyText(Data.Code.ByName(codeName) ?? throw new ScriptException($"Code entry \"{codeName}\" was not found."));
    }

    public string GetDisassemblyText(GameMakerCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        EnsureDataLoaded();
        if (code.ParentEntry is not null)
        {
            return $"; This code entry belongs to \"{code.ParentEntry.Name.Content}\".";
        }

        try
        {
            return code.Disassemble(Data.Variables, Data.CodeLocals?.For(code), Data.CodeLocals is null);
        }
        catch (Exception)
        {
            return "/* DISASSEMBLY FAILED */";
        }
    }

    public virtual bool AreFilesIdentical(string file1, string file2)
    {
        using FileStream first = new(file1, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream second = new(file2, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (first.Length != second.Length)
        {
            return false;
        }

        var firstBuffer = new byte[81920];
        var secondBuffer = new byte[81920];
        int read;
        while ((read = first.Read(firstBuffer)) > 0)
        {
            second.ReadExactly(secondBuffer, 0, read);
            if (!firstBuffer.AsSpan(0, read).SequenceEqual(secondBuffer.AsSpan(0, read)))
            {
                return false;
            }
        }

        return true;
    }

    public virtual string ScriptInputDialog(string title, string label, string defaultInput, string cancelText, string submitText, bool isMultiline, bool preventClose) => throw UiUnavailable();
    public virtual string SimpleTextInput(string title, string label, string defaultValue, bool allowMultiline, bool showDialog = true) => throw UiUnavailable();
    public virtual void SimpleTextOutput(string title, string label, string message, bool allowMultiline) => ScriptMessage(message);

    public virtual Task ClickableSearchOutput(string title, string query, int resultsCount, IOrderedEnumerable<KeyValuePair<string, List<(int lineNum, string codeLine)>>> resultsDict, bool showInDecompiledView, IOrderedEnumerable<string>? failedList = null) => Task.CompletedTask;
    public virtual Task ClickableSearchOutput(string title, string query, int resultsCount, IDictionary<string, List<(int lineNum, string codeLine)>> resultsDict, bool showInDecompiledView, IEnumerable<string>? failedList = null) => Task.CompletedTask;

    public virtual void SetFinishedMessage(bool isFinishedMessageEnabled)
    {
    }

    public virtual void EnableUI()
    {
    }

    public virtual string PromptChooseDirectory() => throw UiUnavailable();
    public virtual string PromptLoadFile(string defaultExt, string filter) => throw UiUnavailable();
    public virtual string PromptSaveFile(string defaultExt, string filter) => throw UiUnavailable();

    public virtual void ChangeSelection(object newSelection, bool inNewTab = false)
    {
        Selected = newSelection;
        Highlighted = newSelection;
    }

    /// <summary>Called whenever a script changes the progress bar. Hosts override it to show progress.</summary>
    protected virtual void ReportProgress(string label, string status, int value, double maximum)
    {
    }

    public void UpdateProgressBar(string message, string status, double progressValue, double maxValue) => SetProgressBar(message, status, progressValue, maxValue);

    public void SetProgressBar(string message, string status, double progressValue, double maxValue)
    {
        _progressStatus = status ?? string.Empty;
        Volatile.Write(ref _progressMaximum, maxValue);
        Interlocked.Exchange(ref _progress, ToProgress(progressValue));
        ReportProgress(message, _progressStatus, GetProgress(), maxValue);
    }

    public void SetProgressBar() => SetProgressBar(null, string.Empty, 0, 100);

    public void UpdateProgressValue(double progressValue) => SetProgress(ToProgress(progressValue));

    public void UpdateProgressStatus(string status)
    {
        _progressStatus = status ?? string.Empty;
        ReportProgress(null, _progressStatus, GetProgress(), Volatile.Read(ref _progressMaximum));
    }

    public void AddProgress(int amount)
    {
        var value = Interlocked.Add(ref _progress, amount);
        ReportProgress(null, _progressStatus, value, Volatile.Read(ref _progressMaximum));
    }

    public void IncrementProgress() => AddProgress(1);
    public void AddProgressParallel(int amount) => AddProgress(amount);
    public void IncrementProgressParallel() => AddProgress(1);
    public int GetProgress() => Volatile.Read(ref _progress);

    public void SetProgress(int value)
    {
        Interlocked.Exchange(ref _progress, value);
        ReportProgress(null, _progressStatus, value, Volatile.Read(ref _progressMaximum));
    }

    public void HideProgressBar() => Interlocked.Exchange(ref _progress, 0);

    public virtual void StartProgressBarUpdater()
    {
    }

    public virtual Task StopProgressBarUpdater() => Task.CompletedTask;

    // GUI-only UndertaleModTool script calls: accepted and ignored so those scripts run unchanged.
    public static void SyncBinding(string resourceType, bool enable)
    {
    }

    public static void SyncBinding(bool condition, bool enable)
    {
    }

    public static void DisableAllSyncBindings()
    {
    }

    internal void SetScriptError(Exception error)
    {
        ScriptErrorMessage = error.Message;
        ScriptErrorType = error.GetType().Name;
    }

    internal void ClearScriptError()
    {
        ScriptErrorMessage = string.Empty;
        ScriptErrorType = string.Empty;
    }

    private static int ToProgress(double value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private static ScriptException UiUnavailable() => new("This operation requires a UI host implementing IScriptInterface.");
}
