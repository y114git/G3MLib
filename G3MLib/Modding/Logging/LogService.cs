using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace G3MLib.Modding.Logging;

public enum ModdingLogLevel { Trace, Information, Warning, Error }

public sealed class ModdingLogMessage
{
    public required ModdingLogLevel Level { get; init; }
    public required string Message { get; init; }
}

public sealed class ModdingProgress
{
    public required string Operation { get; init; }
    public required int Current { get; init; }
    public required int Total { get; init; }
    public bool IsComplete { get; init; }
}

public static class LogService
{
    private static string s_currentOperation = "";

    public static event Action<ModdingLogMessage>? MessageLogged;
    public static event Action<ModdingProgress>? ProgressReported;
    public static bool Verbose { get; set; }
    public static bool Suppress { get; set; }
    public static bool FileLoggingEnabled => MessageLogged is not null;

    public static void SetOperation(string operation) => s_currentOperation = operation;
    public static void Log(string message) => Write(ModdingLogLevel.Trace, message);

    public static void Log(ref LogInterpolatedStringHandler handler)
    {
        string message = handler.ToString();
        if (!string.IsNullOrEmpty(message)) Write(ModdingLogLevel.Trace, message);
    }

    public static void Info(string message) => Write(ModdingLogLevel.Information, message);
    public static void Error(string message) => Write(ModdingLogLevel.Error, message);
    public static void Warning(string message) => Write(ModdingLogLevel.Warning, message);

    public static void Progress(int current, int total)
    {
        if (total > 0 && !Suppress)
            ProgressReported?.Invoke(new ModdingProgress { Operation = s_currentOperation, Current = current, Total = total });
    }

    public static void ProgressRange(int current, int total, int rangeStart, int rangeEnd)
    {
        if (total <= 0 || Suppress) return;
        int start = Math.Clamp(rangeStart, 0, 100);
        Progress(start + (int)Math.Round((double)(current * (Math.Clamp(rangeEnd, start, 100) - start)) / total), 100);
    }

    public static void ProgressComplete()
    {
        if (!Suppress)
            ProgressReported?.Invoke(new ModdingProgress { Operation = s_currentOperation, Current = 0, Total = 0, IsComplete = true });
    }

    private static void Write(ModdingLogLevel level, string message)
    {
        if (level != ModdingLogLevel.Trace || Verbose || MessageLogged is not null)
            MessageLogged?.Invoke(new ModdingLogMessage { Level = level, Message = message });
    }
}

[InterpolatedStringHandler]
public ref struct LogInterpolatedStringHandler
{
    private DefaultInterpolatedStringHandler _inner;
    private readonly bool _enabled = LogService.Verbose || LogService.FileLoggingEnabled;

    public LogInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
    {
        shouldAppend = _enabled;
        _inner = _enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
    }

    public void AppendLiteral(string value) { if (_enabled) _inner.AppendLiteral(value); }
    public void AppendFormatted<T>(T value) { if (_enabled) _inner.AppendFormatted(value); }
    public void AppendFormatted<T>(T value, string? format) { if (_enabled) _inner.AppendFormatted(value, format); }
    public void AppendFormatted<T>(T value, int alignment) { if (_enabled) _inner.AppendFormatted(value, alignment); }
    public void AppendFormatted<T>(T value, int alignment, string? format) { if (_enabled) _inner.AppendFormatted(value, alignment, format); }
    public void AppendFormatted(ReadOnlySpan<char> value) { if (_enabled) _inner.AppendFormatted(value); }
    public override string ToString() => _enabled ? _inner.ToString() : "";
}
