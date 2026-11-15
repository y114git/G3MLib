using System.IO;
using G3MLib.DataFile;

namespace G3MLib.Modding.Patching;

/// <summary>
/// Remembers the data files that one patch or merge operation saved, read back and validated, together with their
/// external audio groups. The operation hands the same file from step to step (validate, copy audio groups, commit,
/// validate again) and each step used to parse the whole file again. Nothing is remembered outside a scope, so a
/// file that changes after the operation ends is always read again.
/// </summary>
internal sealed class ValidatedDataFiles : IDisposable
{
	private readonly record struct Stamp(string Path, long Length, long WriteTicks);

	private static readonly AsyncLocal<ValidatedDataFiles?> s_current = new();
	private static readonly IDisposable s_nested = new Nested();

	private readonly object _gate = new();
	private readonly Dictionary<Stamp, string[]> _files = [];

	/// <summary>Starts a scope for the current operation; a scope that is already open is shared and left to its owner.</summary>
	public static IDisposable Begin()
	{
		if (s_current.Value is not null)
			return s_nested;

		var scope = new ValidatedDataFiles();
		s_current.Value = scope;
		return scope;
	}

	public static void Remember(string path, GameMakerData data)
	{
		if (s_current.Value is not { } scope || StampOf(path) is not { } stamp)
			return;

		var groups = PatchInputService.ExternalAudioGroupPaths(data).ToArray();
		lock (scope._gate)
			scope._files[stamp] = groups;
	}

	/// <summary>True when this exact file (same path, size and write time) was validated earlier in the current operation.</summary>
	public static bool TryGet(string path, out string[] audioGroups)
	{
		audioGroups = [];
		if (s_current.Value is not { } scope || StampOf(path) is not { } stamp)
			return false;

		lock (scope._gate)
		{
			if (!scope._files.TryGetValue(stamp, out var found))
				return false;

			audioGroups = found;
			return true;
		}
	}

	public void Dispose()
	{
		s_current.Value = null;
		lock (_gate)
			_files.Clear();
	}

	private static Stamp? StampOf(string path)
	{
		try
		{
			var info = new FileInfo(Path.GetFullPath(path));
			return info.Exists ? new Stamp(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
		{
			return null;
		}
	}

	private sealed class Nested : IDisposable
	{
		public void Dispose() { }
	}
}
