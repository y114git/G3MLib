using System.Diagnostics;

namespace G3MLib.DataFile;

/// <summary>
/// A reader/writer gate a host and the library share for one <see cref="GameMakerData"/>, so a save does not run while a script or an import changes the data,
/// and two bulk changes do not interleave. Any number of readers can hold it together; a writer holds it alone. Waiting writers go before new readers.
/// </summary>
/// <remarks>
/// The library takes it as a writer in <see cref="Scripting.ScriptRunner.RunAsync"/> and <see cref="Modding.Resources.ResourceImportService"/>, and as a reader in
/// <see cref="GameMakerIO"/> writes, <see cref="Modding.Resources.ResourceExportService.ExportTypes(GameMakerData, string, string, IEnumerable{string})"/> and
/// <see cref="Project.G3MProjectService.Export"/>. Nothing else uses it, so a host that never takes the gate only pays for an uncontended lock.
/// A scope is shared by the code that runs under it, including tasks it starts, so a script that saves the data does not wait on itself.
/// A reader cannot become a writer: asking for write inside a read scope throws <see cref="InvalidOperationException"/> instead of deadlocking.
/// The scope that <see cref="Scripting.ScriptRunner"/> takes is bound to the thread that runs the script, so anything that runs on that thread while the script waits (a UI click handler
/// handled during a dialog) shares the scope and is not held back. A host must not save or import from such a callback while a script runs.
/// Waiting blocks the calling thread; give a timeout or a token when the caller is a UI thread. The async overloads of the library wait on a pool thread.
/// A timeout of <see cref="Timeout.InfiniteTimeSpan"/> waits without a limit; other negative values throw <see cref="ArgumentOutOfRangeException"/>.
/// </remarks>
public sealed class DataAccessGate
{
    private static readonly TimeSpan MaxSlice = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly object _sync = new();
    private readonly AsyncLocal<Scope?> _current = new();
    private int _readers;
    private bool _writing;
    private int _waitingWriters;

    /// <summary>Waits until no writer holds the gate, then holds it as a reader until the scope is disposed.</summary>
    /// <param name="timeout">How long to wait; null waits without a limit.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <exception cref="TimeoutException">The gate was not free in time.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled while waiting.</exception>
    public IDisposable BeginRead(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Begin(write: false, timeout, cancellationToken);

    /// <summary>Waits until no one holds the gate, then holds it alone until the scope is disposed. Arguments and exceptions are the same as for <see cref="BeginRead"/>.</summary>
    /// <exception cref="InvalidOperationException">The caller already holds the gate as a reader.</exception>
    public IDisposable BeginWrite(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Begin(write: true, timeout, cancellationToken);

    private IDisposable Begin(bool write, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var scope = Acquire(write, timeout, cancellationToken);
        if (scope is null)
        {
            return NoScope.Instance;
        }

        scope.Bind();
        return scope;
    }

    /// <summary>Waits for the gate without making the scope ambient; <see cref="Scope.Bind"/> does that on the thread that runs the guarded work. Null when the caller already holds the gate.</summary>
    internal Scope? Acquire(bool write, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        // A task that outlived its scope still sees it in the AsyncLocal; a disposed scope holds nothing
        var held = _current.Value;
        if (held is { Disposed: false })
        {
            if (write && !held.Write)
            {
                throw new InvalidOperationException("The gate is held for reading by this code; release it before asking for write.");
            }

            return null;
        }

        if (timeout is { } given && given != Timeout.InfiniteTimeSpan && given < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must not be negative.");
        }

        TimeSpan? limit = timeout == Timeout.InfiniteTimeSpan ? null : timeout;
        var clock = Stopwatch.StartNew();
        using var wake = cancellationToken.Register(static state => { var self = (DataAccessGate)state!; lock (self._sync) Monitor.PulseAll(self._sync); }, this);
        lock (_sync)
        {
            if (write)
            {
                _waitingWriters++;
            }

            try
            {
                while (Blocked(write))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (limit is null)
                    {
                        Monitor.Wait(_sync);
                        continue;
                    }

                    var left = limit.Value - clock.Elapsed;
                    if (left <= TimeSpan.Zero)
                    {
                        throw new TimeoutException("The data gate was not free in time.");
                    }

                    // Monitor.Wait takes at most int.MaxValue milliseconds
                    Monitor.Wait(_sync, left > MaxSlice ? MaxSlice : left);
                }
            }
            finally
            {
                if (write)
                {
                    _waitingWriters--;
                    // A writer that gives up may leave readers that only waited for it
                    Monitor.PulseAll(_sync);
                }
            }

            if (write)
            {
                _writing = true;
            }
            else
            {
                _readers++;
            }
        }

        return new Scope(this, write);
    }

    private bool Blocked(bool write) => _writing || (write ? _readers > 0 : _waitingWriters > 0);

    private void Release(bool write)
    {
        lock (_sync)
        {
            if (write)
            {
                _writing = false;
            }
            else
            {
                _readers--;
            }

            Monitor.PulseAll(_sync);
        }
    }

    internal sealed class Scope(DataAccessGate gate, bool write) : IDisposable
    {
        private int _disposed;

        internal bool Write { get; } = write;

        internal bool Disposed => Volatile.Read(ref _disposed) == 1;

        /// <summary>Makes this scope the ambient one of the calling flow, for work that runs on another thread than the one that acquired it.</summary>
        internal void Bind() => gate._current.Value = this;

        /// <summary>Clears the ambient scope of the calling flow when it is this one.</summary>
        internal void Unbind()
        {
            if (gate._current.Value == this)
            {
                gate._current.Value = null;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Unbind();
                gate.Release(Write);
            }
        }
    }

    private sealed class NoScope : IDisposable
    {
        internal static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}
