using System.Globalization;
using System.IO;

namespace G3MLib.Modding.Internal;

internal static class OutputFileCommit
{
	internal static void Commit(IReadOnlyList<(string Source, string Destination)> files)
	{
		if (files.Count == 0) return;
		var backupDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(files[0].Destination)), ".g3mlib-backup-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(backupDirectory);
		var committed = new List<(string Destination, string? Backup)>();
		var keepBackups = false;
		try
		{
			foreach (var (source, destination) in files)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
				var backup = File.Exists(destination) ? Path.Combine(backupDirectory, committed.Count.ToString(CultureInfo.InvariantCulture)) : null;
				if (backup is null) File.Move(source, destination);
				else File.Replace(source, destination, backup);
				committed.Add((destination, backup));
			}
		}
		catch (Exception error)
		{
			var errors = new List<Exception> { error };
			for (var i = committed.Count - 1; i >= 0; i--)
			{
				var (destination, backup) = committed[i];
				try
				{
					if (backup is null) File.Delete(destination);
					else File.Move(backup, destination, overwrite: true);
				}
				catch (Exception rollbackError) { errors.Add(rollbackError); }
			}
			if (errors.Count > 1)
			{
				keepBackups = true;
				throw new AggregateException("Output recovery failed. Original files remain in " + backupDirectory, errors);
			}
			throw;
		}
		finally
		{
			if (!keepBackups)
			{
				try { Directory.Delete(backupDirectory, recursive: true); }
				catch (Exception error) when (error is IOException or UnauthorizedAccessException)
				{ LogService.Warning("Could not remove output backups: " + error.Message); }
			}
		}
	}
}
