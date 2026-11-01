namespace G3MLib.DataFile.Project;

/// <summary>
/// One resource listed in a project file.
/// </summary>
/// <param name="Name">Resource name.</param>
/// <param name="Path">Project-relative path of the resource folder or file, with forward slashes (for example <c>sprites/spr_player</c>).</param>
/// <param name="Type">Modding resource type (for example <c>Sprites</c>).</param>
/// <param name="Hash">SHA-256 over the resource files as exported.</param>
public sealed record G3MProjectResource(string Name, string Path, string Type, string Hash);

/// <summary>
/// The base data file a project was exported from.
/// </summary>
public sealed record G3MProjectBaseFile(string Name, long Size, string Md5);

/// <summary>
/// An opened project: the contents of the <c>.g3mproj</c> file as a lookup index.
/// Resource folders are not read; use <see cref="GetPath"/> to reach a resource on disk when it is needed.
/// </summary>
public sealed class G3MProject
{
    private readonly Dictionary<string, G3MProjectResource> _byPath;
    private readonly ILookup<string, G3MProjectResource> _byType;

    internal G3MProject(string name, int formatVersion, string mode, string directory, G3MProjectBaseFile baseFile,
        IReadOnlyList<G3MProjectResource> resources, IReadOnlyList<string> folders,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resourceOrder,
        IReadOnlyList<string> audioGroups, IReadOnlyList<string> textureGroups)
    {
        Name = name;
        FormatVersion = formatVersion;
        Mode = mode;
        Directory = directory;
        BaseFile = baseFile;
        Resources = resources;
        Folders = folders;
        ResourceOrder = resourceOrder;
        AudioGroups = audioGroups;
        TextureGroups = textureGroups;
        _byPath = new(resources.Count, StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            _byPath[resource.Path] = resource;
        }
        _byType = resources.ToLookup(r => r.Type, StringComparer.Ordinal);
    }

    /// <summary>Project name.</summary>
    public string Name { get; }

    /// <summary>Format version of the project file.</summary>
    public int FormatVersion { get; }

    /// <summary><c>full</c> or <c>delta</c>.</summary>
    public string Mode { get; }

    /// <summary>Absolute path of the folder that holds the project file and the resource folders.</summary>
    public string Directory { get; }

    /// <summary>Base data file identity, or null when the project was exported without one.</summary>
    public G3MProjectBaseFile BaseFile { get; }

    /// <summary>Every resource, in type then name order.</summary>
    public IReadOnlyList<G3MProjectResource> Resources { get; }

    /// <summary>Virtual explorer folders.</summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>Resource names per type in the order they have in the data file.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOrder { get; }

    /// <summary>Audio group names.</summary>
    public IReadOnlyList<string> AudioGroups { get; }

    /// <summary>Texture group names.</summary>
    public IReadOnlyList<string> TextureGroups { get; }

    /// <summary>Resources of one modding resource type (for example <c>Sprites</c>), or an empty sequence.</summary>
    public IEnumerable<G3MProjectResource> OfType(string type) => _byType[type];

    /// <summary>Finds a resource by its project-relative path, or returns null.</summary>
    public G3MProjectResource Find(string path) => path is not null && _byPath.TryGetValue(path, out var resource) ? resource : null;

    /// <summary>Absolute path of the resource folder or file.</summary>
    public string GetPath(G3MProjectResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return System.IO.Path.Join(Directory, resource.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }
}
