using System;
using System.Collections.Generic;
using System.IO;

namespace G3MLib.Modding.Internal;

internal static class OutputFileCommit
{
    internal static void Commit(IReadOnlyList<(string Source, string Destination)> files)
    {
        if (files.Count == 0) return;
        string backupDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(files[0].Destination))!, ".g3mlib-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        var committed = new List<(string Destination, string? Backup)>();
        bool keepBackups = false;
        try
        {
            foreach (var (source, destination) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                string? backup = File.Exists(destination) ? Path.Combine(backupDirectory, committed.Count.ToString()) : null;
                if (backup is null) File.Move(source, destination);
                else File.Replace(source, destination, backup);
                committed.Add((destination, backup));
            }
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            for (int i = committed.Count - 1; i >= 0; i--)
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
                { G3MLib.Modding.Logging.LogService.Warning("Could not remove output backups: " + error.Message); }
            }
        }
    }
}
