using System.IO;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Scripting;

namespace G3MLib.DataFile.Project;

public partial class ProjectContext : IScriptInterface
{
    private int _progress;

    public Action<string> ScriptMessageHandler { get; set; }
    public Action<string> ScriptWarningHandler { get; set; }

    public ProjectContext Project => this;
    public string FilePath => LoadDataPath;
    public string ScriptPath { get; private set; }
    public object Highlighted { get; private set; }
    public object Selected { get; private set; }
    public bool CanSave => Data is not null && SaveDataPath is not null;
    public bool ScriptExecutionSuccess => ScriptErrorMessage.Length == 0;
    public string ScriptErrorMessage { get; private set; } = string.Empty;
    public string ExePath => AppContext.BaseDirectory;
    public string ScriptErrorType { get; private set; } = string.Empty;
    public bool IsAppClosed => false;

    GameMakerData IScriptInterface.Data => Data;

    public void AddProgress(int amount) => Interlocked.Add(ref _progress, amount);
    public void AddProgressParallel(int amount) => AddProgress(amount);

    public void ChangeSelection(object newSelection, bool inNewTab = false)
    {
        Selected = newSelection;
        Highlighted = newSelection;
    }

    public Task ClickableSearchOutput(string title, string query, int resultsCount, IOrderedEnumerable<KeyValuePair<string, List<(int lineNum, string codeLine)>>> resultsDict, bool showInDecompiledView, IOrderedEnumerable<string>? failedList = null) => Task.CompletedTask;

    public Task ClickableSearchOutput(string title, string query, int resultsCount, IDictionary<string, List<(int lineNum, string codeLine)>> resultsDict, bool showInDecompiledView, IEnumerable<string>? failedList = null) => Task.CompletedTask;

    public void EnableUI()
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

    public int GetProgress() => Volatile.Read(ref _progress);
    public void HideProgressBar() => Interlocked.Exchange(ref _progress, 0);
    public void IncrementProgress() => AddProgress(1);
    public void IncrementProgressParallel() => AddProgress(1);
    public void InitializeScriptDialog()
    {
    }

    public bool LintUMTScript(string path)
    {
        try
        {
            InitializeScripting();
            ScriptRunner.Compile(Path.GetFullPath(path));
            return true;
        }
        catch (Exception e)
        {
            SetScriptError(e);
            return false;
        }
    }

    public bool MakeNewDataFile()
    {
        if (Data is not null)
        {
            return false;
        }

        Data = GameMakerData.CreateNew2026LTS();
        return true;
    }

    public string PromptChooseDirectory() => throw UiUnavailable();
    public string PromptLoadFile(string defaultExt, string filter) => throw UiUnavailable();
    public string PromptSaveFile(string defaultExt, string filter) => throw UiUnavailable();

    public bool RunUMTScript(string path)
    {
        try
        {
            InitializeScripting();
            var previousScriptPath = ScriptPath;
            var globals = CreateScriptGlobals();
            ScriptRunResult result;
            try
            {
                ScriptPath = Path.GetFullPath(path);
                result = ScriptRunner.RunAsync(path, globals).GetAwaiter().GetResult();
            }
            finally
            {
                ScriptPath = previousScriptPath;
            }
            ScriptErrorMessage = globals.ScriptErrorMessage;
            ScriptErrorType = globals.ScriptErrorType;
            return result.Success;
        }
        catch (Exception e)
        {
            SetScriptError(e);
            return false;
        }
    }

    public void ScriptError(string error, string title = "Error", bool SetConsoleText = true)
    {
        ScriptErrorMessage = error;
        ScriptErrorType = title;
        throw new ScriptException(error);
    }

    public string ScriptInputDialog(string title, string label, string defaultInput, string cancelText, string submitText, bool isMultiline, bool preventClose) => throw UiUnavailable();
    public void ScriptMessage(string message) => ScriptMessageHandler?.Invoke(message);
    public void ScriptOpenURL(string url) => throw UiUnavailable();
    public bool ScriptQuestion(string message) => false;
    public void ScriptWarning(string message) => ScriptWarningHandler?.Invoke(message);
    public void SetFinishedMessage(bool isFinishedMessageEnabled)
    {
    }
    public void SetProgress(int value) => Interlocked.Exchange(ref _progress, value);
    public void SetProgressBar(string message, string status, double progressValue, double maxValue) => SetProgress((int)Math.Clamp(progressValue, int.MinValue, int.MaxValue));
    public void SetProgressBar() => SetProgress(0);
    public void SetUMTConsoleText(string message) => ScriptMessage(message);
    public string SimpleTextInput(string title, string label, string defaultValue, bool allowMultiline, bool showDialog = true) => throw UiUnavailable();
    public void SimpleTextOutput(string title, string label, string message, bool allowMultiline) => ScriptMessage(message);
    public void StartProgressBarUpdater()
    {
    }
    public Task StopProgressBarUpdater() => Task.CompletedTask;
    public void UpdateProgressBar(string message, string status, double progressValue, double maxValue) => SetProgressBar(message, status, progressValue, maxValue);
    public void UpdateProgressStatus(string status)
    {
    }
    public void UpdateProgressValue(double progressValue) => SetProgress((int)Math.Clamp(progressValue, int.MinValue, int.MaxValue));

    private static ScriptException UiUnavailable() => new("This operation requires a UI host implementing IScriptInterface.");

    private void EnsureDataLoaded()
    {
        if (Data is null)
        {
            throw new ScriptException("No data file is currently loaded!");
        }
    }

    private void SetScriptError(Exception error)
    {
        ScriptErrorMessage = error.Message;
        ScriptErrorType = error.GetType().Name;
    }
}
