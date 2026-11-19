using System.IO;

namespace G3MLib.Modding.Internal;

/// <summary>Writes a user file through a temporary file in the same folder, so a failed write never leaves a truncated file.</summary>
internal static class AtomicFile
{
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Swept = new();

	/// <summary>Runs <paramref name="write"/> on a temporary file next to <paramref name="path"/>, then replaces <paramref name="path"/> with it. <paramref name="beforeReplace"/> runs after the write and before the swap, for example to release a handle on the target or keep a backup. The temporary file has a name of its own per call and is deleted on failure; temporary files of this target older than a day (left by a crash) are removed at the next write. A symlinked target, a dangling one included, is written through, so the link stays. A missing folder is created only with <paramref name="createDirectory"/>; otherwise the write fails.</summary>
	internal static void Write(string path, Action<FileStream> write, int bufferSize = 4096, FileOptions options = FileOptions.None, Action? beforeReplace = null, bool createDirectory = false)
	{
		path = Resolve(path);
		var temp = Prepare(path, createDirectory);
		try
		{
			using (var stream = Create(temp, bufferSize, options))
			{
				write(stream);
				stream.Flush(flushToDisk: true);
			}
			beforeReplace?.Invoke();
			Replace(temp, path);
		}
		catch
		{
			Discard(temp);
			throw;
		}
	}

	/// <summary>The same as <see cref="Write"/> for a writer that awaits.</summary>
	internal static async Task WriteAsync(string path, Func<FileStream, Task> write, int bufferSize = 4096, FileOptions options = FileOptions.None, bool createDirectory = false)
	{
		path = Resolve(path);
		var temp = Prepare(path, createDirectory);
		try
		{
			await using (var stream = Create(temp, bufferSize, options))
			{
				await write(stream).ConfigureAwait(false);
				stream.Flush(flushToDisk: true);
			}
			Replace(temp, path);
		}
		catch
		{
			Discard(temp);
			throw;
		}
	}

	private static string Resolve(string path)
	{
		path = Path.GetFullPath(path);
		// LinkTarget reads the link itself, so it also sees a link whose target does not exist yet
		return new FileInfo(path).LinkTarget is null ? path : File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
	}

	private static string Prepare(string path, bool createDirectory)
	{
		var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException($"'{path}' has no parent folder to write a temporary file into.", nameof(path));
		if (createDirectory)
		{
			Directory.CreateDirectory(directory);
		}

		// Crash leftovers do not appear while the process runs, so one sweep per target is enough
		if (Swept.TryAdd(path, true))
		{
			SweepStale(directory, Path.GetFileName(path));
		}

		// A name per call keeps concurrent writers of one target from touching each other's file
		return path + "." + Guid.NewGuid().ToString("N") + ".g3mtmp";
	}

	private static void SweepStale(string directory, string fileName)
	{
		var limit = DateTime.UtcNow.AddDays(-1);
		foreach (var stale in Directory.EnumerateFiles(directory, fileName + ".*.g3mtmp"))
		{
			try
			{
				if (File.GetLastWriteTimeUtc(stale) < limit)
				{
					File.Delete(stale);
				}
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		}
	}

	private static FileStream Create(string temp, int bufferSize, FileOptions options) =>
		new(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize, options);

	private static void Replace(string temp, string path)
	{
		if (OperatingSystem.IsWindows() && File.Exists(path))
		{
			// Replace keeps the target's attributes, unlike a move
			File.Replace(temp, path, null);
		}
		else
		{
			if (!OperatingSystem.IsWindows() && File.Exists(path))
			{
				// A rename would give the new file the default mode
				File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
			}

			File.Move(temp, path, overwrite: true);
		}
	}

	private static void Discard(string temp)
	{
		try { File.Delete(temp); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
	}
}
