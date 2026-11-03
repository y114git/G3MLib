using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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
	private static readonly AsyncLocal<string?> CurrentOperation = new();
	private static readonly AsyncLocal<Scope?> CurrentScope = new();
	private static readonly AsyncLocal<WarningCollector?> CurrentWarningCollector = new();

	public static event Action<ModdingLogMessage>? MessageLogged;
	public static event Action<ModdingProgress>? ProgressReported;
	private static volatile bool s_verbose;
	private static volatile bool s_suppress;
	public static bool Verbose { get => CurrentScope.Value?.Verbose ?? s_verbose; set => s_verbose = value; }
	public static bool Suppress { get => CurrentScope.Value?.Suppress ?? s_suppress; set => s_suppress = value; }
	public static bool FileLoggingEnabled => CurrentScope.Value is { } scope ? scope.Message is not null : MessageLogged is not null;

	/// <summary>Routes log and progress messages in this async operation to the supplied host callbacks.</summary>
	public static IDisposable BeginScope(Action<ModdingLogMessage>? message, Action<ModdingProgress>? progress, bool? verbose = null, bool? suppress = null)
	{
		var scope = new Scope(CurrentScope.Value, message, progress, verbose ?? Verbose, suppress ?? Suppress);
		CurrentScope.Value = scope;
		return scope;
	}

	private sealed class Scope(Scope? previous, Action<ModdingLogMessage>? message, Action<ModdingProgress>? progress, bool verbose, bool suppress) : IDisposable
	{
		public Action<ModdingLogMessage>? Message { get; } = message;
		public Action<ModdingProgress>? Progress { get; } = progress;
		public bool Verbose { get; } = verbose;
		public bool Suppress { get; } = suppress;
		public void Dispose()
		{
			if (ReferenceEquals(CurrentScope.Value, this)) CurrentScope.Value = previous;
		}
	}

	internal static IDisposable CollectWarnings(out ConcurrentQueue<string> warnings)
	{
		var queue = new ConcurrentQueue<string>();
		warnings = queue;
		var collector = new WarningCollector(CurrentWarningCollector.Value, queue);
		CurrentWarningCollector.Value = collector;
		return collector;
	}

	private sealed class WarningCollector(WarningCollector? previous, ConcurrentQueue<string> warnings) : IDisposable
	{
		public WarningCollector? Previous { get; } = previous;
		public ConcurrentQueue<string> Warnings { get; } = warnings;
		public void Dispose()
		{
			if (ReferenceEquals(CurrentWarningCollector.Value, this)) CurrentWarningCollector.Value = Previous;
		}
	}

	public static void SetOperation(string operation) => CurrentOperation.Value = operation;
	public static void Log(string message) => Write(ModdingLogLevel.Trace, message);

	public static void Log(ref LogInterpolatedStringHandler handler)
	{
		var message = handler.ToString();
		if (!string.IsNullOrEmpty(message)) Write(ModdingLogLevel.Trace, message);
	}

	public static void Info(string message) => Write(ModdingLogLevel.Information, message);
	public static void Error(string message) => Write(ModdingLogLevel.Error, message);
	public static void Warning(string message) => Write(ModdingLogLevel.Warning, message);

	public static void Progress(int current, int total)
	{
		if (total > 0 && !Suppress)
			ReportProgress(new ModdingProgress { Operation = CurrentOperation.Value ?? "", Current = current, Total = total });
		ModdingCancellation.Throw();
	}

	public static void ProgressRange(int current, int total, int rangeStart, int rangeEnd)
	{
		if (total <= 0 || Suppress) return;
		var start = Math.Clamp(rangeStart, 0, 100);
		Progress(start + (int)Math.Round((double)current * (Math.Clamp(rangeEnd, start, 100) - start) / total), 100);
	}

	public static void ProgressComplete()
	{
		if (!Suppress)
			ReportProgress(new ModdingProgress { Operation = CurrentOperation.Value ?? "", Current = 0, Total = 0, IsComplete = true });
	}

	private static void Write(ModdingLogLevel level, string message)
	{
		if (level == ModdingLogLevel.Warning)
		{
			for (var collector = CurrentWarningCollector.Value; collector is not null; collector = collector.Previous)
				collector.Warnings.Enqueue(message);
		}
		var sink = CurrentScope.Value is { } scope ? scope.Message : MessageLogged;
		if (level != ModdingLogLevel.Trace || Verbose || sink is not null)
			sink?.Invoke(new ModdingLogMessage { Level = level, Message = message });
	}

	private static void ReportProgress(ModdingProgress progress)
	{
		var sink = CurrentScope.Value is { } scope ? scope.Progress : ProgressReported;
		sink?.Invoke(progress);
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
