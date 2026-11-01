using System.IO;
using System.Text.Json;

namespace G3MLib.DataFile.Project;

/// <summary>
/// Public bridge for tools that need the built-in project asset serializers
/// without referencing internal Serializable* implementations directly.
/// </summary>
public static class SerializableProjectAssetBridge
{
    public static void Export(ProjectContext projectContext, IProjectAsset asset, string destinationFile)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var serializable = asset.GenerateSerializableProjectAsset(projectContext);
        serializable.Serialize(projectContext, destinationFile);
    }

    public static IProjectAsset Import(ProjectContext projectContext, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(projectContext);
        var serializable = Load(sourceFile);
        projectContext.RegisterAssetPath(serializable, sourceFile);
        serializable.PreImport(projectContext);
        return serializable.Import(projectContext);
    }

    public static IReadOnlyList<IProjectAsset> ImportMany(ProjectContext projectContext, IEnumerable<string> sourceFiles)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(projectContext);
        List<ISerializableProjectAsset> serializableAssets = [];
        foreach (var sourceFile in sourceFiles)
        {
            var asset = Load(sourceFile);
            projectContext.RegisterAssetPath(asset, sourceFile);
            serializableAssets.Add(asset);
        }

        foreach (var asset in serializableAssets)
            asset.PreImport(projectContext);

        List<IProjectAsset> imported = new(serializableAssets.Count);
        foreach (var asset in serializableAssets)
            imported.Add(asset.Import(projectContext));

        return imported;
    }

    private static ISerializableProjectAsset Load(string sourceFile)
    {
        using FileStream fs = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<ISerializableProjectAsset>(fs, ProjectContext.JsonOptions)
            ?? throw new InvalidDataException($"Failed to deserialize project asset \"{sourceFile}\".");
    }
}
