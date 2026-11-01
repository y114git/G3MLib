using System.IO;

namespace G3MLib.Modding.Logging;

/// <summary>Carries the cancellation token of the running modding operation to the loops and stages that cannot take it as a parameter.</summary>
internal static class ModdingCancellation
{
	private static readonly AsyncLocal<CancellationToken> Current = new();

	internal static CancellationToken Token => Current.Value;

	/// <summary>Makes <paramref name="token"/> the ambient token until disposed. A token that cannot be cancelled keeps the enclosing one.</summary>
	internal static IDisposable Begin(CancellationToken token)
	{
		if (!token.CanBeCanceled) return NoScope.Instance;
		var previous = Current.Value;
		Current.Value = token;
		return new Scope(previous);
	}

	/// <summary>Ignores cancellation until disposed, for the final steps that commit outputs and must not stop half way.</summary>
	internal static IDisposable Uncancellable()
	{
		var previous = Current.Value;
		Current.Value = default;
		return new Scope(previous);
	}

	internal static void Throw() => Current.Value.ThrowIfCancellationRequested();

	/// <summary>True for a cancellation, including one a parallel loop wrapped in an <see cref="AggregateException"/>.</summary>
	internal static bool IsCancellation(Exception exception) =>
		exception is OperationCanceledException || exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(IsCancellation);

	/// <summary>Rethrows a cancellation as a plain <see cref="OperationCanceledException"/> so callers see one exception type.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	internal static void Rethrow(Exception exception, CancellationToken token)
	{
		if (exception is OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
		throw new OperationCanceledException(token);
	}

	/// <summary>
	/// Runs <paramref name="body"/> under <paramref name="token"/>. On cancellation, deletes what the body added to <paramref name="outputDirectory"/>
	/// and throws <see cref="OperationCanceledException"/>. Files that already existed and were overwritten cannot be restored.
	/// </summary>
	internal static void Run(string outputDirectory, CancellationToken token, Action body)
	{
		using var scope = Begin(token);
		token.ThrowIfCancellationRequested();
		HashSet<string>? existing = null;
		if (token.CanBeCanceled && Directory.Exists(outputDirectory))
			existing = new HashSet<string>(Directory.EnumerateFileSystemEntries(outputDirectory, "*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
		try
		{
			body();
			token.ThrowIfCancellationRequested();
		}
		catch (Exception ex) when (token.CanBeCanceled && IsCancellation(ex))
		{
			Rollback(outputDirectory, existing);
			Rethrow(ex, token);
		}
	}

	private static void Rollback(string outputDirectory, HashSet<string>? existing)
	{
		try
		{
			if (!Directory.Exists(outputDirectory)) return;
			if (existing is null)
			{
				Directory.Delete(outputDirectory, recursive: true);
				return;
			}
			foreach (var path in Directory.EnumerateFileSystemEntries(outputDirectory, "*", SearchOption.AllDirectories).Where(path => !existing.Contains(path)).OrderByDescending(path => path.Length).ToList())
			{
				if (File.Exists(path)) File.Delete(path);
				else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LogService.Warning("Could not remove partial output: " + ex.Message);
		}
	}

	private sealed class Scope(CancellationToken previous) : IDisposable
	{
		public void Dispose() => Current.Value = previous;
	}

	private sealed class NoScope : IDisposable
	{
		internal static readonly NoScope Instance = new();
		public void Dispose() { }
	}
}

/// <summary>Parallel loops that stop when the ambient operation is cancelled.</summary>
internal static class CancellableParallel
{
	private static ParallelOptions Options(ParallelOptions? options)
	{
		var result = options ?? new ParallelOptions();
		result.CancellationToken = ModdingCancellation.Token;
		return result;
	}

	internal static void For(int fromInclusive, int toExclusive, Action<int> body) =>
		Parallel.For(fromInclusive, toExclusive, Options(null), BatchRun.WrapRange(fromInclusive, toExclusive, body));

	internal static void ForEach<T>(IEnumerable<T> source, Action<T> body) =>
		Parallel.ForEach(source, Options(null), BatchRun.Wrap(source, body));

	internal static void ForEach<T>(IEnumerable<T> source, ParallelOptions options, Action<T> body) =>
		Parallel.ForEach(source, Options(options), BatchRun.Wrap(source, body));
}
