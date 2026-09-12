using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Patching;

public static class PatchInputService
{
    /// <summary>Copies a data file and its external audio groups, restoring replaced files if committing an output fails.</summary>
    public static void CopyDataFile(string sourcePath, string outputPath)
        => WriteOutput(outputPath, staged =>
        {
            File.Copy(sourcePath, staged);
            CopyExternalAudioGroups(sourcePath, staged);
        });

    /// <summary>Writes and validates a data file, retaining existing output audio groups and copying missing groups from the source.</summary>
    public static void WriteDataFile(GameMakerData data, string outputPath, string? sourcePath = null)
        => WriteOutput(outputPath, staged =>
        {
            using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                GameMakerIO.Write(stream, data);
                stream.Flush(flushToDisk: true);
            }
            CopyExternalAudioGroups(outputPath, staged, data);
            if (sourcePath is not null) CopyExternalAudioGroups(sourcePath, staged, data);
        });

    private static void WriteOutput(string outputPath, Action<string> write)
    {
        string destination = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string stagingDirectory = Path.Combine(directory, ".g3mlib-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            string staged = Path.Combine(stagingDirectory, Path.GetFileName(destination));
            write(staged);
            CommitDataFile(staged, destination);
        }
        finally { Directory.Delete(stagingDirectory, recursive: true); }
    }

    internal static void CommitDataFile(string stagedPath, string outputPath)
    {
        string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(stagedPath))!;
        string destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        using var stream = File.OpenRead(stagedPath);
        using var data = GameMakerIO.Read(stream);
        var files = new List<(string Source, string Destination)>();
        foreach (string relativePath in ExternalAudioGroupPaths(data))
        {
            string source = GetSafeAudioGroupPath(sourceDirectory, relativePath) ?? throw new InvalidDataException("Audio group path escapes the data directory.");
            string destination = GetSafeAudioGroupPath(destinationDirectory, relativePath) ?? throw new InvalidDataException("Audio group path escapes the output directory.");
            if (File.Exists(source)) files.Add((source, destination));
        }
        stream.Dispose();
        files.Add((stagedPath, outputPath));
        G3MLib.Modding.Internal.OutputFileCommit.Commit(files);
    }

    private static IEnumerable<string> ExternalAudioGroupPaths(GameMakerData data) =>
        data.AudioGroups?.Select((group, index) => (Group: group, Index: index))
            .Where(item => item.Index > 0)
            .Select(item => string.IsNullOrWhiteSpace(item.Group?.Path?.Content) ? $"audiogroup{item.Index}.dat" : item.Group.Path.Content)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal) ?? [];

    public static bool IsXDelta(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".xdelta", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vcdiff", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> MaterializeDataAsync(string originalPath, string inputPath, string tempDir)
    {
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original data file not found", originalPath);
        }
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Patch input not found", inputPath);
        }
        Directory.CreateDirectory(tempDir);
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();
        string output = Path.Combine(tempDir, $"materialized_{Guid.NewGuid():N}{Path.GetExtension(originalPath)}");
        if (DataFileExtensionUtil.IsDataFile(inputPath))
        {
            ValidateDataFile(inputPath, inputPath);
            return inputPath;
        }
        if (IsXDelta(inputPath))
        {
            XDeltaResult result = await new XDeltaService().ApplyPatchAsync(originalPath, inputPath, output);
            if (!result.Success)
            {
                throw new InvalidOperationException("Failed to apply xdelta '" + Path.GetFileName(inputPath) + "': " + result.Error);
            }
            CopyExternalAudioGroups(originalPath, output);
        }
        else
        {
            if (ext == ".csx")
            {
                throw new NotSupportedException("CSX inputs require a scripting host; materialize the data file before invoking G3MLib.");
            }
            if (ext is not ".g3mpatch" and not ".zip")
            {
                throw new NotSupportedException($"Unsupported patch input '{Path.GetFileName(inputPath)}' ({ext}). Supported: .g3mpatch, .xdelta, .vcdiff, .csx and GameMaker data files.");
            }
            PatchApplyResult patchResult = await PatchService.ApplyPatchAsync(originalPath, inputPath, output);
            if (!patchResult.Success)
            {
                throw new InvalidOperationException("Failed to apply patch '" + Path.GetFileName(inputPath) + "': " + patchResult.Error);
            }
            CopyExternalAudioGroups(originalPath, output);
        }
        ValidateDataFile(output, inputPath);
        return output;
    }

    private static void ValidateDataFile(string dataPath, string inputPath)
    {
        try
        {
            using FileStream stream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using (GameMakerIO.Read(stream))
            {
            }
        }
        catch (Exception ex)
        {
            if (!string.Equals(dataPath, inputPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(dataPath);
                }
                catch
                {
                }
            }
            throw new InvalidDataException("Validation failed for materialized input '" + Path.GetFileName(inputPath) + "': " + ex.Message, ex);
        }
    }

    /// <summary>Copies missing external audio groups beside a materialized data file, preserving groups already written there.</summary>
    public static void CopyExternalAudioGroups(string sourceDataPath, string materializedDataPath)
    {
        using var stream = File.OpenRead(materializedDataPath);
        using var data = GameMakerIO.Read(stream);
        CopyExternalAudioGroups(sourceDataPath, materializedDataPath, data);
    }

    private static void CopyExternalAudioGroups(string sourceDataPath, string materializedDataPath, GameMakerData data)
    {
        string sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourceDataPath));
        string materializedDir = Path.GetDirectoryName(Path.GetFullPath(materializedDataPath));
        if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(materializedDir))
        {
            return;
        }
        foreach (string relativePath in ExternalAudioGroupPaths(data))
        {
            string sourcePath = GetSafeAudioGroupPath(sourceDir, relativePath) ?? throw new InvalidDataException("Audio group path escapes the data directory.");
            string destinationPath = GetSafeAudioGroupPath(materializedDir, relativePath) ?? throw new InvalidDataException("Audio group path escapes the output directory.");
            if (File.Exists(sourcePath) && !File.Exists(destinationPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourcePath, destinationPath);
            }
        }
    }

    private static string? GetSafeAudioGroupPath(string baseDirectory, string relativePath)
    {
        try
        {
            string root = Path.GetFullPath(baseDirectory).TrimEnd(new char[2]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            });
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            return G3MLib.DataFile.Util.Paths.IsWithinDirectory(root, path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
