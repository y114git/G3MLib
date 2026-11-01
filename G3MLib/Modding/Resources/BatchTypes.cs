using System.Collections.Concurrent;

namespace G3MLib.Modding.Resources;

public enum ResultKind
{
	Written,
	Skipped,
	Warning,
	Failed
}

/// <summary>The outcome of one item in a batch export or import. <see cref="Message"/> is set for every kind except a plain Written.</summary>
public sealed record ItemResult(string Name, ResultKind Kind, string? Message);

public sealed record BatchProgress(int Done, int Total, string Item);

/// <summary>
/// Collects item results and progress for one running export or import. The run travels in an async local like the cancellation token,
/// so the per type methods keep their signatures and report nothing when no run is active.
/// </summary>
internal sealed class BatchRun
{
	private static readonly AsyncLocal<BatchRun?> Current = new();

	[ThreadStatic]
	private static bool s_reported;

	private readonly IProgress<BatchProgress>? _progress;
	private readonly ConcurrentQueue<ItemResult> _results = new();
	private int _done;
	private int _total;

	private BatchRun(IProgress<BatchProgress>? progress)
	{
		_progress = progress;
	}

	/// <summary>True while a batch run collects results, so a failed item can be reported and skipped instead of stopping the run.</summary>
	internal static bool Active => Current.Value != null;

	internal static IReadOnlyList<ItemResult> Run(string outputDirectory, IProgress<BatchProgress>? progress, CancellationToken cancellationToken, Action body) =>
		Execute(progress, () => ModdingCancellation.Run(outputDirectory, cancellationToken, body));

	/// <summary>Like <see cref="Run"/> for an import: cancelling deletes nothing, because the input folder must never change.</summary>
	internal static IReadOnlyList<ItemResult> RunImport(IProgress<BatchProgress>? progress, CancellationToken cancellationToken, Action body) =>
		Execute(progress, () =>
		{
			using var scope = ModdingCancellation.Begin(cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				body();
				cancellationToken.ThrowIfCancellationRequested();
			}
			catch (Exception ex) when (ModdingCancellation.IsCancellation(ex))
			{
				ModdingCancellation.Rethrow(ex, cancellationToken);
			}
		});

	private static IReadOnlyList<ItemResult> Execute(IProgress<BatchProgress>? progress, Action scoped)
	{
		var run = new BatchRun(progress);
		var previous = Current.Value;
		Current.Value = run;
		try
		{
			scoped();
		}
		finally
		{
			Current.Value = previous;
		}
		run.Finish();
		return [.. run._results.OrderBy(result => result.Name, StringComparer.Ordinal).ThenBy(result => result.Kind)];
	}

	internal static void Expect(int count)
	{
		var run = Current.Value;
		if (run != null) Interlocked.Add(ref run._total, count);
	}

	/// <summary>Records one finished item. Cancellation is checked here, so every item is a cancel point.</summary>
	internal static void Item(string name, ResultKind kind = ResultKind.Written, string? message = null)
	{
		ModdingCancellation.Throw();
		var run = Current.Value;
		if (run == null) return;
		s_reported = true;
		run._results.Enqueue(new ItemResult(name, kind, message));
		var done = Interlocked.Increment(ref run._done);
		run._progress?.Report(new BatchProgress(done, Volatile.Read(ref run._total), name));
	}

	internal static void Failed(string name, Exception exception) => Item(name, ResultKind.Failed, exception.Message);

	/// <summary>Runs one item that has no error handling of its own. Inside a batch run a failure becomes a Failed result; outside it still throws.</summary>
	internal static void Guard(string name, Action body)
	{
		if (!Active)
		{
			body();
			return;
		}
		try
		{
			body();
			Item(name);
		}
		catch (Exception ex) when (!ModdingCancellation.IsCancellation(ex))
		{
			Failed(name, ex);
		}
	}

	/// <summary>Wraps a parallel loop body: counts the candidates up front, and counts a candidate the body skipped (filtered out, unnamed) as done without a result.</summary>
	internal static Action<T> Wrap<T>(IEnumerable<T> source, Action<T> body)
	{
		var run = Current.Value;
		if (run == null) return body;
		if (source.TryGetNonEnumeratedCount(out var count)) Expect(count);
		return item =>
		{
			s_reported = false;
			body(item);
			if (!s_reported) Interlocked.Increment(ref run._done);
		};
	}

	internal static Action<int> WrapRange(int fromInclusive, int toExclusive, Action<int> body) =>
		Wrap(Enumerable.Range(fromInclusive, toExclusive - fromInclusive), body);

	private void Finish()
	{
		var total = Math.Max(Volatile.Read(ref _total), Volatile.Read(ref _done));
		if (_results.Count != total) _progress?.Report(new BatchProgress(total, total, string.Empty));
	}
}
