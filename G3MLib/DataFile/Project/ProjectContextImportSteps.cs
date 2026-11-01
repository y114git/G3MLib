using System.IO;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Project;

public partial class ProjectContext
{

    /// <summary>
    /// Handles non-custom flags defined in the project main options.
    /// </summary>
    private void ProcessFlags()
    {
        foreach (var flag in _mainOptions.Flags)
        {
            // Ignore custom flags
            if (flag.StartsWith('_'))
            {
                continue;
            }

            // Currently, no non-custom flags are supported, so throw an exception.
            throw new ProjectException($"Unsupported project flag \"{flag}\"");
        }
    }

    /// <summary>
    /// Verifies the game version matches, based on known
    /// </summary>
    private void VerifyGameVersion()
    {
        var failed = false;
        if (_mainOptions.VerifyStrings.Count <= 1)
        {
            foreach (var expectedString in _mainOptions.VerifyStrings)
            {
                var found = false;
                foreach (var str in Data.Strings)
                {
                    if (str.Content == expectedString)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    failed = true;
                    break;
                }
            }
        }
        else
        {
            HashSet<string> lookup = new(Data.Strings.Count);
            foreach (var str in Data.Strings)
            {
                lookup.Add(str.Content);
            }
            failed = !_mainOptions.VerifyStrings.All(lookup.Contains);
        }
        if (failed)
        {
            throw new ProjectException("Game data is missing expected strings; cannot import. Wrong game version?");
        }
    }

    /// <summary>
    /// Imports all sub-projects, as defined in the project options.
    /// </summary>
    private void ImportSubProjects()
    {
        if (_mainOptions.SubProjects.Count == 0)
        {
            return;
        }
        _projectJsonPaths ??= [Path.GetFullPath(MainFilePath)];
        foreach (var subProjectRelativePath in _mainOptions.SubProjects)
        {
            var subProjectPath = Paths.JoinVerifyWithinDirectory(MainDirectory, subProjectRelativePath);
            if (!_projectJsonPaths.Add(Path.GetFullPath(subProjectPath)))
            {
                throw new ProjectException("Infinite sub-project recursion detected");
            }
            ProjectContext subContext = new(LoadDirectory, SaveDirectory, subProjectPath)
            {
                _projectJsonPaths = _projectJsonPaths
            };
            subContext.Import(null, FileBackup, MainThreadAction);
        }
    }

    /// <summary>
    /// Imports all external files, as found in locations (file/directory paths) defined in the project options.
    /// </summary>
    private void ImportExternalFiles()
    {
        foreach (var pair in _mainOptions.ExternalFiles)
        {
            // Make sure external file or directory exists
            var sourcePath = Paths.JoinVerifyWithinDirectory(MainDirectory, pair.Source);
            bool isDirectory;
            try
            {
                var sourceAttributes = File.GetAttributes(sourcePath);
                isDirectory = sourceAttributes.HasFlag(FileAttributes.Directory);
            }
            catch (Exception e)
            {
                throw new ProjectException($"Failed to find external file or directory: {e}", e);
            }

            // Get destination path
            var destPath = Paths.JoinVerifyWithinDirectory(SaveDirectory, pair.Destination);

            // Create directories if needed
            var destFullPath = Path.GetFullPath(destPath);
            var destDirectory = Path.GetDirectoryName(destFullPath);
            if (!Directory.Exists(destDirectory))
            {
                FileBackup.BackupDirectory(destDirectory, true);
                Directory.CreateDirectory(destDirectory);
            }

            if (isDirectory)
            {
                // If this is a directory, recursively copy its contents (and create directories).
                CopyDirectory(new DirectoryInfo(sourcePath), destFullPath);
            }
            else
            {
                // If this is a file, copy single file
                FileBackup.BackupFile(destFullPath);
                File.Copy(sourcePath, destPath, true);
            }
        }
    }

    /// <summary>
    /// Applies all file operations (copy/delete), as defined in the project options.
    /// </summary>
    private void ApplyFileOperations()
    {
        // Apply file copies first
        foreach (var operation in _mainOptions.FileCopies)
        {
            // Try finding valid source/destination file paths
            if (!TryFindFileFromPathList(operation.Paths, out var sourcePath, out var destPath))
            {
                // Throw exception only if required
                if (operation.Required)
                {
                    throw new ProjectException("Failed to find required file to be copied");
                }
                continue;
            }

            // Back up destination file
            var destFullPath = Path.GetFullPath(destPath);
            FileBackup.BackupFile(destFullPath);

            // Create directories if needed
            var destDirectory = Path.GetDirectoryName(destFullPath);
            if (!Directory.Exists(destDirectory))
            {
                FileBackup.BackupDirectory(destDirectory, true);
                Directory.CreateDirectory(destDirectory);
            }

            // Perform copy
            File.Copy(sourcePath, destPath, true);
        }

        // Apply file deletes last
        foreach (var operation in _mainOptions.FileDeletes)
        {
            // Try finding valid destination file path
            if (!TryFindFileFromPathList(operation.Paths, out _, out var destPath))
            {
                // Throw exception only if required
                if (operation.Required)
                {
                    throw new ProjectException("Failed to find required file to be deleted");
                }
                continue;
            }

            // Back up destination file
            FileBackup.BackupFile(Path.GetFullPath(destPath));

            // Perform delete (only if file actually exists in destination - it may have been deleted already,
            // but we already verified the file exists at the source path)
            File.Delete(destPath);
        }
    }

    /// <summary>
    /// Applies all file patches, located as defined in the project options.
    /// </summary>
    private void ApplyFilePatches()
    {
        foreach (var patch in _mainOptions.Patches)
        {
            // Get path to patch file
            var fullPatchPath = Paths.JoinVerifyWithinDirectory(MainDirectory, patch.PatchPath);

            // Try finding valid base/output file paths
            if (!TryFindFileFromPathList(patch.DataFilePath, out var baseFilePath, out var outputFilePath))
            {
                throw new ProjectException("Failed to find data file to be patched");
            }
            var baseFileFullPath = Path.GetFullPath(baseFilePath);
            var outputFileFullPath = Path.GetFullPath(outputFilePath);

            // Back up destination file
            FileBackup.BackupFile(outputFileFullPath);

            // Create directories if needed
            var outputDirectory = Path.GetDirectoryName(outputFileFullPath);
            if (!Directory.Exists(outputDirectory))
            {
                FileBackup.BackupDirectory(outputDirectory, true);
                Directory.CreateDirectory(outputDirectory);
            }

            // Apply patch
            const int bufferSize = 131072;
            using FileStream patchStream = new(fullPatchPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
            if (baseFileFullPath == outputFileFullPath)
            {
                var baseFileBytes = File.ReadAllBytes(baseFileFullPath);
                using MemoryStream baseStream = new(baseFileBytes);
                using FileStream outputStream = new(outputFileFullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, bufferSize);
                BPS.ApplyPatch(baseStream, patchStream, outputStream);
            }
            else
            {
                using FileStream baseStream = new(baseFileFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
                using FileStream outputStream = new(outputFileFullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, bufferSize);
                BPS.ApplyPatch(baseStream, patchStream, outputStream);
            }
        }
    }
}
