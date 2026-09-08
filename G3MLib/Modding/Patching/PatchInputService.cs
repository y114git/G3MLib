using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Modding.Patching;

public static class PatchInputService
{
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

    internal static void CopyExternalAudioGroups(string sourceDataPath, string materializedDataPath)
    {
        string sourceDir = Path.GetDirectoryName(sourceDataPath);
        string materializedDir = Path.GetDirectoryName(materializedDataPath);
        if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(materializedDir))
        {
            return;
        }
        try
        {
            using FileStream stream = new FileStream(materializedDataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using GameMakerData data = GameMakerIO.Read(stream);
            foreach (string relativePath in (data.AudioGroups?.Select((group, index) => (Group: group, Index: index))
                .Where(item => item.Index > 0)
                .Select(item => string.IsNullOrWhiteSpace(item.Group?.Path?.Content) ? $"audiogroup{item.Index}.dat" : item.Group.Path.Content)
                .Distinct(StringComparer.OrdinalIgnoreCase) ?? []))
            {
                string sourcePath = GetSafeAudioGroupPath(sourceDir, relativePath);
                string destinationPath = GetSafeAudioGroupPath(materializedDir, relativePath);
                if (sourcePath != null && destinationPath != null && File.Exists(sourcePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                }
            }
        }
        catch
        {
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
            return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
