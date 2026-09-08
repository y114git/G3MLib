using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Project;

/// <summary>
/// Base interface for managing game file backups and restoration, when installing/uninstalling mods.
/// </summary>
public interface IGameFileBackup
{
    /// <summary>
    /// Attempts to restore modified game files to their original state.
    /// </summary>
    /// <remarks>
    /// Throws a <see cref="GameFileBackupException"/> if a recoverable error occurs (i.e., game files are
    /// not potentially left in a corrupted state). Any other exception could imply game file corruption.
    /// </remarks>
    public void RestoreFiles();

    /// <summary>
    /// Backs up a file at the given absolute path, whether or not it actually exists currently.
    /// </summary>
    /// <param name="path">Absolute path of the file to be backed up.</param>
    /// <remarks>
    /// Throws an exception if the backup fails for any reason; this should disallow all further game modifications.
    /// After all files in a mod are backed up, the manifest will need to be saved via <see cref="SaveManifest()"/> to release its lock.
    /// </remarks>
    public void BackupFile(string path);

    /// <summary>
    /// Backs up a directory's existence (no contents) at the given absolute path, whether it is/was being created or otherwise deleted.
    /// </summary>
    /// <param name="path">Absolute path of the directory to be backed up.</param>
    /// <param name="creating">Whether the directory is/was newly-created (and thus should be removed during restoration); otherwise, will be assumed as deleted.</param>
    public void BackupDirectory(string path, bool creating);

    /// <summary>
    /// Saves the manifest that has been built using <see cref="BackupFile(string)"/>, or does nothing if no manifest has been built.
    /// </summary>
    public void SaveManifest();
}

/// <summary>
/// No-op implementation of game file backups and restoration (nothing is backed up, and nothing is restored).
/// </summary>
public sealed class GameFileNoOpBackup() : IGameFileBackup
{
    public void BackupFile(string path)
    {
    }

    public void BackupDirectory(string path, bool creating)
    {
    }

    public void RestoreFiles()
    {
    }

    public void SaveManifest()
    {
    }
}

/// <summary>
/// Default implementation of game file backups and restoration.
/// </summary>
public sealed class GameFileBackup(string gameDirectory) : IGameFileBackup
{
    /// <summary>
    /// Directory of the game files.
    /// </summary>
    public string GameDirectory { get; set; } = gameDirectory;

    /// <summary>
    /// Filename used for mod backup manifests.
    /// </summary>
    public const string ManifestFilename = "mod-backup-manifest.json";

    /// <summary>
    /// Version number of manifests that this library version is able to read/write. Generally should be backwards-compatible.
    /// </summary>
    public const string CurrentManifestVersion = "v1";

    /// <summary>
    /// All manifest versions that this library version is able to read.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedManifestVersions = ["v1"];

    /// <summary>
    /// Default directory name used for storing mod backup files.
    /// </summary>
    /// <remarks>
    /// If the directory name is taken, an additional number will be appended to the end of the name.
    /// </remarks>
    public const string DefaultBackupDirectoryName = ".modbackup";

    /// <summary>
    /// Backup manifest currently being built as part of mod installation.
    /// </summary>
    private BackupManifest _buildingBackupManifest = null;

    /// <summary>
    /// FileStream used to hold a lock on the backup manifest.
    /// </summary>
    private FileStream _buildingBackupManifestStream = null;

    /// <summary>
    /// Set of relative paths to all files that are in the building backup manifest.
    /// </summary>
    private HashSet<string> _buildingBackupFilePaths = null;

    /// <summary>
    /// Set of relative paths to all directories that are in the building backup manifest.
    /// </summary>
    private HashSet<string> _buildingBackupDirectoryPaths = null;

    /// <inheritdoc/>
    public void RestoreFiles()
    {
        // Don't allow a restore while producing backup files...
        if (_buildingBackupManifest is not null)
        {
            throw new InvalidOperationException();
        }

        // Load manifest JSON
        string manifestPath = Path.Join(GameDirectory, ManifestFilename);
        if (!File.Exists(manifestPath))
        {
            // Nothing to restore
            return;
        }
        BackupManifest manifest;
        try
        {
            using FileStream fs = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            manifest = JsonSerializer.Deserialize<BackupManifest>(fs);
        }
        catch (Exception ex)
        {
            throw new GameFileBackupException($"Failed to load backup manifest: {ex}", ex);
        }

        // Verify we know how to process the backup manifest's version
        if (!SupportedManifestVersions.Contains(manifest.ManifestVersion))
        {
            throw new GameFileBackupException($"Unsupported backup manifest version: {manifest.ManifestVersion}");
        }

        // Verify backup directory exists
        string backupDirPath = Paths.JoinVerifyWithinDirectory(GameDirectory, manifest.Path);
        if (!Directory.Exists(backupDirPath))
        {
            throw new GameFileBackupException($"Mod backup directory is missing (as defined in {ManifestFilename})");
        }

        // For all backup files that exist, verify integrity
        foreach (BackupFileEntry file in manifest.Files)
        {
            // Ignore files with no backup
            if (string.IsNullOrWhiteSpace(file.BackupPath))
            {
                continue;
            }

            // Ensure backup file exists
            string backupFilePath = Paths.JoinVerifyWithinDirectory(backupDirPath, file.BackupPath);
            if (!File.Exists(backupFilePath))
            {
                throw new GameFileBackupException($"Missing backup file for \"{file.Path}\" (missing \"{file.BackupPath}\")");
            }

            // Verify file hash, if stored
            string expectedHash = file.HashSHA256;
            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                expectedHash = file.HashSHA1;
            }
            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                continue;
            }
            using FileStream fs = new(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hashString = HashBytesToHex(string.IsNullOrWhiteSpace(file.HashSHA256) ? GetLegacySha1Hash(fs) : SHA256.HashData(fs));
            if (!string.Equals(hashString, expectedHash, StringComparison.Ordinal))
            {
                throw new GameFileBackupException($"Backup file corrupted: checksum mismatch for file \"{file.Path}\" (stored at \"{file.BackupPath}\")");
            }
        }

        // Perform sanity checks before beginning full restoration
        foreach (BackupFileEntry file in manifest.Files)
        {
            // Sanity check that the corresponding file can be written to or deleted (for basic cases - this is not secure and has race conditions)
            string gameDataLocation = Paths.JoinVerifyWithinDirectory(GameDirectory, file.Path);
            if (!File.Exists(gameDataLocation))
            {
                continue;
            }
            try
            {
                using FileStream stream = new(gameDataLocation, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception e)
            {
                throw new GameFileBackupException($"Backup restoration file failed to open at \"{file.Path}\": {e}", e);
            }
        }

        // Restore deleted directories
        foreach (BackupDirectoryEntry dir in manifest.Directories)
        {
            // Only process if the directory was not created by a mod
            if (dir.WasCreated)
            {
                continue;
            }

            // Try to create directories
            string gameDataLocation = Paths.JoinVerifyWithinDirectory(GameDirectory, dir.Path);
            Directory.CreateDirectory(gameDataLocation);
        }

        // Restore all files
        foreach (BackupFileEntry file in manifest.Files)
        {
            // If a backup file exists, move it to original location. Otherwise, delete file at given location.
            string gameDataLocation = Paths.JoinVerifyWithinDirectory(GameDirectory, file.Path);
            if (string.IsNullOrWhiteSpace(file.BackupPath))
            {
                // Delete mod-specific file, as long as it still exists
                if (File.Exists(gameDataLocation))
                {
                    File.Delete(gameDataLocation);
                }
            }
            else
            {
                // Move backup file and potentially overwrite original file.
                // Make directories along the path, just in case the user or modder did nefarious things with the structure.
                string backupFilePath = Paths.JoinVerifyWithinDirectory(backupDirPath, file.BackupPath);
                Directory.CreateDirectory(Path.GetDirectoryName(gameDataLocation));
                File.Move(backupFilePath, gameDataLocation, true);
            }
        }

        // Remove mod-created directories, if possible.
        // First, sort directories by path length as a simple heuristic to remove inner directories first...
        List<string> directoriesToDelete = [];
        foreach (BackupDirectoryEntry dir in manifest.Directories)
        {
            // Only process if the directory was created by a mod
            if (!dir.WasCreated)
            {
                continue;
            }
            string gameDataLocation = Paths.JoinVerifyWithinDirectory(GameDirectory, dir.Path);
            directoriesToDelete.Add(gameDataLocation);
        }
        directoriesToDelete.Sort((a, b) => b.Length - a.Length);
        foreach (string directory in directoriesToDelete)
        {
            // Try to remove directory (does NOT work recursively - ignore exceptions for this reason, besides being mostly harmless).
            // This also loosely compensates for duplicate directories
            try
            {
                Directory.Delete(directory, false);
            }
            catch
            {
                // Non-empty duplicate directories are retained for the final emptiness check.
            }
        }

        // Ensure backup directory is empty - if not, throw an exception to warn the user to never add files there
        DirectoryInfo info = new(backupDirPath);
        if (info.EnumerateFileSystemInfos().Any())
        {
            throw new GameFileBackupException($"Backup directory was not empty after restoring. Please clear the directory and remove {ManifestFilename}. (Backup directory located at {backupDirPath})");
        }

        // Delete manifest and backup directory itself
        Directory.Delete(backupDirPath, false);
        File.Delete(manifestPath);
    }

    /// <summary>
    /// Creates a manifest if one hasn't already been created
    /// </summary>
    private void InitializeManifest()
    {
        if (_buildingBackupManifest is not null)
        {
            return;
        }

        // Make sure there's no existing backup manifest, and create a lock on a new one
        string manifestPath = Path.Join(GameDirectory, ManifestFilename);
        if (File.Exists(manifestPath))
        {
            throw new GameFileBackupException("Mod backup manifest still exists when trying to make a new backup");
        }
        _buildingBackupManifestStream = new(manifestPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        try
        {
            // Choose and create a directory for backup, choosing alternatives just in case
            string backupDirName = DefaultBackupDirectoryName;
            string backupDirPath = Path.Join(GameDirectory, backupDirName);
            int loopCounter = 0;
            while (Directory.Exists(backupDirName))
            {
                if (loopCounter >= 50)
                {
                    throw new GameFileBackupException("Too many directory name conflicts for mod backup directory");
                }
                backupDirName = $"{DefaultBackupDirectoryName}{++loopCounter}";
                backupDirPath = Path.Join(GameDirectory, backupDirName);
            }
            Directory.CreateDirectory(backupDirPath);

            _buildingBackupManifest = new()
            {
                ManifestVersion = CurrentManifestVersion,
                Path = backupDirName,
                Files = [],
                Directories = []
            };
            _buildingBackupFilePaths = [];
            _buildingBackupDirectoryPaths = [];
        }
        catch
        {
            _buildingBackupManifestStream.Dispose();
            _buildingBackupManifestStream = null;
            _buildingBackupManifest = null;
            _buildingBackupFilePaths = null;
            _buildingBackupDirectoryPaths = null;
            throw;
        }
    }

    /// <inheritdoc/>
    public void BackupFile(string path)
    {
        InitializeManifest();

        // Ensure we haven't added this path more than once, to avoid errors.
        if (!_buildingBackupFilePaths.Add(Path.GetFullPath(path)))
        {
            return;
        }

        // If the file exists, hash and make a copy of it. Otherwise, just declare it in the manifest (so it gets deleted when restoring, if necessary).
        string relativePath = Path.GetRelativePath(GameDirectory, path);
        if (File.Exists(path))
        {
            // Hash file
            string hashString;
            using (FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hashString = HashBytesToHex(SHA256.HashData(fs));
            }

            // Determine an unused filename based on hash and relative path
            string backupFileNameBase = $"backup_{MurmurHash.Hash(relativePath)}_{hashString}";
            string backupFileName = backupFileNameBase;
            string backupDirPath = Paths.JoinVerifyWithinDirectory(GameDirectory, _buildingBackupManifest.Path);
            string backupFilePath = Path.Join(backupDirPath, backupFileName);
            int loopCounter = 0;
            while (File.Exists(backupFilePath))
            {
                if (loopCounter >= 50)
                {
                    throw new GameFileBackupException($"Too many filename conflicts for mod file backup (path \"{relativePath}\")");
                }
                backupFileName = $"{backupFileNameBase}_{++loopCounter}";
                backupFilePath = Path.Join(backupDirPath, backupFileName);
            }

            // Copy file (don't allow overwrites, though - we're trying to *avoid* that...)
            File.Copy(path, backupFilePath, false);

            // Add to manifest
            _buildingBackupManifest.Files.Add(new BackupFileEntry()
            {
                Path = relativePath,
                BackupPath = Path.GetRelativePath(backupDirPath, backupFilePath),
                HashSHA256 = hashString
            });
        }
        else
        {
            // Declare (presumably) newly-added file in manifest only
            _buildingBackupManifest.Files.Add(new BackupFileEntry()
            {
                Path = relativePath,
                BackupPath = null,
                HashSHA1 = null
            });
        }
    }

    /// <inheritdoc/>
    public void BackupDirectory(string path, bool creating)
    {
        InitializeManifest();

        // Ensure we haven't added this path more than once, to avoid errors.
        if (!_buildingBackupDirectoryPaths.Add(Path.GetFullPath(path)))
        {
            return;
        }

        // Add entry based on relative path
        string relativePath = Path.GetRelativePath(GameDirectory, path);
        _buildingBackupManifest.Directories.Add(new BackupDirectoryEntry()
        {
            Path = relativePath,
            WasCreated = creating
        });
    }

    /// <summary>
    /// Saves the manifest that has been built using <see cref="BackupFile(string)"/>, or does nothing if no manifest has been built.
    /// </summary>
    public void SaveManifest()
    {
        if (_buildingBackupManifest is null)
        {
            return;
        }
        JsonSerializer.Serialize(_buildingBackupManifestStream, _buildingBackupManifest);
        _buildingBackupManifestStream.Dispose();
        _buildingBackupManifestStream = null;
        _buildingBackupManifest = null;
        _buildingBackupFilePaths = null;
    }

    private static string HashBytesToHex(byte[] hash) => Convert.ToHexStringLower(hash);

    [SuppressMessage("Security", "S4790:Use a stronger hashing algorithm.", Justification = "Legacy backup manifests store SHA-1 checksums and remain readable.")]
    private static byte[] GetLegacySha1Hash(Stream stream) => SHA1.HashData(stream);

    /// <summary>
    /// Used for serializing manifest information for backup files and directories.
    /// </summary>
    internal sealed class BackupManifest
    {
        /// <summary>
        /// Manifest version number, for managing upgrades (and detecting incompatibility) correctly.
        /// </summary>
        public string ManifestVersion { get; set; }

        /// <summary>
        /// Relative path from the game data to the directory containing backup files.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// List of backup files.
        /// </summary>
        public List<BackupFileEntry> Files { get; set; }

        /// <summary>
        /// List of backup directories.
        /// </summary>
        public List<BackupDirectoryEntry> Directories { get; set; }
    }

    /// <summary>
    /// A single backup file entry, as stored in a backup manifest.
    /// </summary>
    internal sealed class BackupFileEntry
    {
        /// <summary>
        /// Path of the file in regular game data.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Path of the backed-up file, or <see langword="null"/> if no backup exists.
        /// </summary>
        /// <remarks>
        /// No backup can naturally occur when a file is newly-added as part of a mod (and should just be deleted during a restore).
        /// </remarks>
        public string BackupPath { get; set; }

        /// <summary>
        /// Legacy SHA-1 hash of the backed-up file, or <see langword="null"/> if this hash should not be verified.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string HashSHA1 { get; set; }

        /// <summary>
        /// SHA-256 hash of the backed-up file, or <see langword="null"/> if this hash should not be verified.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string HashSHA256 { get; set; }
    }

    /// <summary>
    /// A single backup directory entry, as stored in a backup manifest.
    /// </summary>
    internal sealed class BackupDirectoryEntry
    {
        /// <summary>
        /// Path of the directory in regular game data.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Whether the directory was newly-created as part of mod installation (and should be deleted upon restoration, if possible).
        /// Otherwise, the directory is assumed to have been deleted and will be re-created upon restoration.
        /// </summary>
        public bool WasCreated { get; set; }
    }
}
