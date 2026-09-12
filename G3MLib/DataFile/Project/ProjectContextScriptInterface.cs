using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace G3MLib.DataFile.Project;

partial class ProjectContext : IScriptInterface
{
    private object _highlighted;
    private object _selected;
    private int _progress;
    private string _scriptErrorMessage = string.Empty;
    private string _scriptErrorType = string.Empty;

    public Action<string> ScriptMessageHandler { get; set; }
    public Action<string> ScriptWarningHandler { get; set; }

    public ProjectContext Project => this;
    public string FilePath => LoadDataPath;
    public string ScriptPath => _scriptPath;
    public object Highlighted => _highlighted;
    public object Selected => _selected;
    public bool CanSave => Data is not null && SaveDataPath is not null;
    public bool ScriptExecutionSuccess => _scriptErrorMessage.Length == 0;
    public string ScriptErrorMessage => _scriptErrorMessage;
    public string ExePath => AppContext.BaseDirectory;
    public string ScriptErrorType => _scriptErrorType;
    public bool IsAppClosed => false;

    GameMakerData IScriptInterface.Data => Data;

    public void AddProgress(int amount) => Interlocked.Add(ref _progress, amount);
    public void AddProgressParallel(int amount) => AddProgress(amount);

    public void ChangeSelection(object newSelection, bool inNewTab = false)
    {
        _selected = newSelection;
        _highlighted = newSelection;
    }

    public Task ClickableSearchOutput(string title, string query, int resultsCount, IOrderedEnumerable<KeyValuePair<string, List<(int lineNum, string codeLine)>>> resultsDict, bool showInDecompiledView, IOrderedEnumerable<string> failedList = null) => Task.CompletedTask;

    public Task ClickableSearchOutput(string title, string query, int resultsCount, IDictionary<string, List<(int lineNum, string codeLine)>> resultsDict, bool showInDecompiledView, IEnumerable<string> failedList = null) => Task.CompletedTask;

    public void EnableUI()
    {
    }

    public string GetDecompiledText(string codeName, GlobalDecompileContext context = null, IDecompileSettings settings = null)
    {
        EnsureDataLoaded();
        return GetDecompiledText(Data.Code.ByName(codeName) ?? throw new ScriptException($"Code entry \"{codeName}\" was not found."), context, settings);
    }

    public string GetDecompiledText(GameMakerCode code, GlobalDecompileContext context = null, IDecompileSettings settings = null)
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
            CSharpScript.Create(File.ReadAllText(path, Encoding.UTF8), _scriptOptions.WithFilePath(path).WithFileEncoding(Encoding.UTF8), typeof(IScriptInterface)).Compile();
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
            string fullPath = Path.GetFullPath(path);
            string scriptText = $"#line 1 \"{fullPath}\"\n" + File.ReadAllText(fullPath, Encoding.UTF8);
            string previousScriptPath = _scriptPath;
            _scriptPath = fullPath;
            try
            {
                MainThreadAction(() => CSharpScript.EvaluateAsync(scriptText, _scriptOptions.WithFilePath(fullPath).WithFileEncoding(Encoding.UTF8), this, typeof(IScriptInterface)).GetAwaiter().GetResult());
            }
            finally
            {
                _scriptPath = previousScriptPath;
            }
            _scriptErrorMessage = string.Empty;
            _scriptErrorType = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            SetScriptError(e);
            return false;
        }
    }

    public void ScriptError(string error, string title = "Error", bool SetConsoleText = true)
    {
        _scriptErrorMessage = error;
        _scriptErrorType = title;
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
        _scriptErrorMessage = error.Message;
        _scriptErrorType = error.GetType().Name;
    }
}
