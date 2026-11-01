namespace G3MLib.DataFile;

/// <summary>
/// A reader-writer lock for hosts that run background jobs on a <see cref="GameMakerData"/> while it is edited.
/// </summary>
/// <remarks>
/// <see cref="GameMakerData"/> does not lock. The host keeps one <see cref="ModelLock"/> next to the model:
/// background jobs (decompile, export, search, diff, patch) hold <see cref="Read"/> for as long as they read the model,
/// and every edit holds <see cref="Write"/>. Any number of readers run together; a writer runs alone.
/// The lock is not reentrant: taking it again on the same thread throws <see cref="LockRecursionException"/>.
/// A job that must not block edits works on a snapshot (a second <see cref="GameMakerData"/> read from the same file) instead.
/// </remarks>
public sealed class ModelLock : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    /// <summary>Waits for all writers to finish, then holds a shared read lock until the result is disposed.</summary>
    public IDisposable Read()
    {
        _lock.EnterReadLock();
        return new Releaser(_lock, write: false);
    }

    /// <summary>Waits for all readers and writers to finish, then holds the exclusive write lock until the result is disposed.</summary>
    public IDisposable Write()
    {
        _lock.EnterWriteLock();
        return new Releaser(_lock, write: true);
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private sealed class Releaser(ReaderWriterLockSlim target, bool write) : IDisposable
    {
        private ReaderWriterLockSlim? _target = target;

        public void Dispose()
        {
            var held = Interlocked.Exchange(ref _target, null);
            if (held is null)
            {
                return;
            }
            if (write)
            {
                held.ExitWriteLock();
            }
            else
            {
                held.ExitReadLock();
            }
        }
    }
}
