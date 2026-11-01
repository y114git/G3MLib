using System.IO;
using System.Text;
using System.Text.Json;

namespace G3MLib.DataFile.Project;

/// <summary>
/// How much of the game a project folder holds.
/// </summary>
public enum ProjectExportMode
{
    /// <summary>Only changed or marked assets, applied over a base data file.</summary>
    Delta,

    /// <summary>Every asset ("export all"), so the folder plus the original data file rebuild the game.</summary>
    Full
}

public partial class ProjectContext
{
    /// <summary>
    /// Export mode recorded in the project manifest. Projects without a record are <see cref="ProjectExportMode.Delta"/>.
    /// </summary>
    public ProjectExportMode ExportMode =>
        string.Equals(_mainOptions?.G3M?.Mode, "full", StringComparison.Ordinal) ? ProjectExportMode.Full : ProjectExportMode.Delta;

    /// <summary>
    /// Exports every exportable asset of the loaded data and records full mode in the manifest.
    /// </summary>
    /// <exception cref="ProjectException">When a project-specific exception occurs</exception>
    public void ExportAll()
    {
        if (_mainOptions is null)
        {
            throw new InvalidOperationException("Project has not yet been imported");
        }
        foreach (var asset in EnumerateAllExportableAssets())
        {
            MarkAssetForExport(asset);
        }
        Export(clearMarkedAssets: true);
        _mainOptions.G3M ??= new();
        _mainOptions.G3M.Mode = "full";
        if (LoadDataPath is not null && File.Exists(LoadDataPath))
        {
            _mainOptions.G3M.BaseFileSize = new FileInfo(LoadDataPath).Length;
            _mainOptions.G3M.BaseFileMd5 = HashBaseFile(LoadDataPath);
        }
        SaveMainOptions();
    }

    private readonly List<string> _warnings = [];

    // The layout matches the UTMT 0.9 project format, which has no fields for these; they are lost when the project is imported again.
    private const string LayoutLimitsWarning =
        "The UTMT project layout cannot hold some data: object event action fields (each event is saved as one code action, so the library, kind, " +
        "apply-to, relative and argument settings of other actions are lost), room layer effect flags, and sprite texture page rectangles " +
        "(textures are repacked when the project is imported). Use the g3mproj project format to keep them.";

    /// <summary>
    /// Non-fatal problems found while importing, such as a base data file that differs from the one the project was exported from.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    private void AddWarning(string message)
    {
        _warnings.Add(message);
        ScriptWarningHandler?.Invoke(message);
    }

    private static string HashBaseFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(stream));
    }

    /// <summary>
    /// Compares the data file at <see cref="LoadDataPath"/> with the identity recorded in the manifest.
    /// Returns <see langword="true"/> only when a record exists and matches; warns when it exists and does not.
    /// </summary>
    private bool BaseFileMatchesRecord()
    {
        var record = _mainOptions?.G3M;
        if (record?.BaseFileMd5 is null || LoadDataPath is null || !File.Exists(LoadDataPath))
        {
            return false;
        }
        if (new FileInfo(LoadDataPath).Length == record.BaseFileSize && HashBaseFile(LoadDataPath) == record.BaseFileMd5)
        {
            return true;
        }
        AddWarning($"The base data file \"{Path.GetFileName(LoadDataPath)}\" differs from the one this project was exported from; " +
                   "assets are imported over it instead of being rebuilt from the project.");
        return false;
    }

    private IEnumerable<IProjectAsset> EnumerateAllExportableAssets()
    {
        IEnumerable<IProjectAsset>[] groups =
        [
            Data.AnimationCurves, Data.Backgrounds, Data.Code, Data.Fonts, Data.GameObjects, Data.ParticleSystems,
            Data.Paths, Data.Rooms, Data.Scripts, Data.Sequences, Data.Shaders, Data.Sounds, Data.Sprites
        ];
        return groups.Where(g => g is not null).SelectMany(g => g).Where(a => a is { ProjectExportable: true });
    }

    private void SaveMainOptions()
    {
        File.WriteAllText(MainFilePath, JsonSerializer.Serialize(_mainOptions, JsonOptions));
        NormalizeTextFile(MainFilePath);
    }

    // Folders (relative to the project root) whose text files are normalized after an export.
    private IEnumerable<string> TextFileRoots()
    {
        foreach (var type in Enum.GetValues<SerializableAssetType>())
        {
            yield return Path.Join(MainDirectory, type.ToFilesystemNamePlural());
        }
    }

    /// <summary>
    /// Assets marked for export, in a stable order (type, then name), so output does not depend on hash set order.
    /// </summary>
    private List<IProjectAsset> SortedMarkedAssets()
    {
        return [.. _assetsMarkedForExport
            .OrderBy(a => a.ProjectAssetType)
            .ThenBy(a => a.ProjectName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Rewrites exported JSON and GML files as UTF-8 without BOM, LF line endings and exactly one final newline.
    /// Files already in that form are not touched.
    /// </summary>
    private void NormalizeProjectTextFiles()
    {
        List<string> files = [MainFilePath, Path.Join(MainDirectory, "general-info-options.json")];
        foreach (var root in TextFileRoots().Where(Directory.Exists))
        {
            files.AddRange(Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories));
            files.AddRange(Directory.EnumerateFiles(root, "*.gml", SearchOption.AllDirectories));
        }
        foreach (var file in files.Where(File.Exists))
        {
            NormalizeTextFile(file);
        }
    }

    /// <summary>
    /// Normalizes one file in place and returns its final bytes.
    /// </summary>
    internal static byte[] NormalizeTextFile(string path)
    {
        var original = File.ReadAllBytes(path);
        var normalized = NormalizeTextBytes(original);
        if (!ReferenceEquals(normalized, original))
        {
            File.WriteAllBytes(path, normalized);
        }
        return normalized;
    }

    /// <summary>
    /// Returns <paramref name="original"/> as UTF-8 without BOM, LF line endings and exactly one final newline;
    /// the same array when it already is.
    /// </summary>
    internal static byte[] NormalizeTextBytes(byte[] original)
    {
        ReadOnlySpan<byte> span = original;
        if (span.StartsWith(Encoding.UTF8.Preamble))
        {
            span = span[Encoding.UTF8.Preamble.Length..];
        }
        var buffer = new byte[span.Length + 1];
        var length = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == (byte)'\r' && i + 1 < span.Length && span[i + 1] == (byte)'\n')
            {
                continue;
            }
            buffer[length++] = span[i];
        }
        while (length > 0 && buffer[length - 1] == (byte)'\n')
        {
            length--;
        }
        buffer[length++] = (byte)'\n';
        return buffer.AsSpan(0, length).SequenceEqual(original) ? original : buffer.AsSpan(0, length).ToArray();
    }
}
