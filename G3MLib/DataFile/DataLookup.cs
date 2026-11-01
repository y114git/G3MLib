namespace G3MLib.DataFile;

/// <summary>
/// Hash indexes over the string table and the named resource lists of a <see cref="GameMakerData"/>.
/// </summary>
/// <remarks>
/// One writer at a time; any number of readers while no writer runs. Each index records the
/// <see cref="GameMakerObservableList{T}.Version"/> of its list and rebuilds itself when the list was changed
/// behind its back, so a stale index is repaired on the next lookup (the repair counts as a write).
/// Renaming a resource by editing <see cref="GameMakerString.Content"/> in place does not change a list:
/// call <see cref="Build"/> again after renames. <see cref="TryGet{T}"/> rejects a stale hit and rebuilds
/// that one index once, but cannot find a resource under its new name before that.
/// When several entries share a name or content, the first one wins, as in a linear scan.
/// </remarks>
public sealed class DataLookup
{
    private readonly GameMakerData _data;
    private readonly IList<GameMakerString> _strings;
    private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, object> _nameIndexes = [];
    private int _stringVersion;

    private DataLookup(GameMakerData data)
    {
        _data = data;
        _strings = data.Strings ?? throw new InvalidOperationException("The data file has no string table.");
    }

    /// <summary>
    /// Indexes the string table. Name indexes of resource lists are built on first use.
    /// </summary>
    public static DataLookup Build(GameMakerData data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        DataLookup lookup = new(data);
        lookup.RebuildStrings(ct);
        return lookup;
    }

    /// <summary>
    /// Returns the string with exactly this content, or <see langword="null"/>.
    /// </summary>
    public GameMakerString? FindString(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return TryFindString(s, out var index) ? _strings[index] : null;
    }

    private bool TryFindString(string s, out int index)
    {
        SyncStrings();
        // A hit whose content no longer matches means an in-place rename: rebuild once and look again.
        if (_stringIndex.TryGetValue(s, out index) && _strings[index]?.Content != s)
        {
            RebuildStrings(CancellationToken.None);
        }
        return _stringIndex.TryGetValue(s, out index);
    }

    /// <summary>
    /// Returns the string with this content, appending a new entry at the end of the table if there is none.
    /// </summary>
    public GameMakerString GetOrAddString(string s) => GetOrAddString(s, out _);

    /// <inheritdoc cref="GetOrAddString(string)"/>
    /// <param name="s">The string content.</param>
    /// <param name="index">The position of the string in the table.</param>
    public GameMakerString GetOrAddString(string s, out int index)
    {
        ArgumentNullException.ThrowIfNull(s);

        if (TryFindString(s, out index))
        {
            return _strings[index];
        }

        // String indices are referenced throughout the data file, so entries are only ever appended.
        GameMakerString created = new(s);
        index = _strings.Count;
        _strings.Add(created);
        _stringIndex[s] = index;
        _stringVersion = StringVersion();
        return created;
    }

    /// <summary>
    /// Finds a resource by exact (case sensitive) name in the data file's list of <typeparamref name="T"/>.
    /// </summary>
    public bool TryGet<T>(string name, out T resource, out int index) where T : GameMakerNamedResource
    {
        ArgumentNullException.ThrowIfNull(name);

        var names = Names<T>();
        names.Sync();
        // A hit whose name no longer matches means an in-place rename: rebuild once and look again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!names.Map.TryGetValue(name, out index))
            {
                break;
            }
            if (names.List[index]?.Name?.Content == name)
            {
                resource = names.List[index];
                return true;
            }
            names.Rebuild();
        }

        resource = default;
        index = -1;
        return false;
    }

    /// <summary>
    /// Appends a resource to the data file's list of <typeparamref name="T"/> and indexes it.
    /// </summary>
    public void Add<T>(T item) where T : GameMakerNamedResource
    {
        ArgumentNullException.ThrowIfNull(item);

        var names = Names<T>();
        names.Sync();
        names.List.Add(item);
        if (item.Name?.Content is string content)
        {
            names.Map.TryAdd(content, names.List.Count - 1);
        }
        names.Version = names.CurrentVersion();
    }

    private NameIndex<T> Names<T>() where T : GameMakerNamedResource
    {
        if (_nameIndexes.TryGetValue(typeof(T), out var existing))
        {
            return (NameIndex<T>)existing;
        }

        // Limit: the first IList<T> property wins, so the two GameMakerGlobalInit lists are not told apart.
        var property = typeof(GameMakerData).GetProperties()
            .FirstOrDefault(p => p.PropertyType == typeof(IList<T>))
            ?? throw new InvalidOperationException($"The data file has no list of {typeof(T).Name}.");
        var list = (IList<T>)property.GetValue(_data)
            ?? throw new InvalidOperationException($"The data file has no {property.Name} list.");
        NameIndex<T> created = new(list);
        created.Rebuild();
        _nameIndexes[typeof(T)] = created;
        return created;
    }

    private int StringVersion() => (_strings as GameMakerObservableList<GameMakerString>)?.Version ?? 0;

    private void SyncStrings()
    {
        if (_stringVersion != StringVersion())
        {
            RebuildStrings(CancellationToken.None);
        }
    }

    private void RebuildStrings(CancellationToken ct)
    {
        _stringIndex.Clear();
        for (var i = 0; i < _strings.Count; i++)
        {
            if ((i & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            if (_strings[i]?.Content is string content)
            {
                _stringIndex.TryAdd(content, i);
            }
        }
        _stringVersion = StringVersion();
    }

    private sealed class NameIndex<T> where T : GameMakerNamedResource
    {
        public readonly IList<T> List;
        public readonly Dictionary<string, int> Map = new(StringComparer.Ordinal);
        public int Version;

        public NameIndex(IList<T> list) => List = list;

        public int CurrentVersion() => (List as GameMakerObservableList<T>)?.Version ?? 0;

        public void Sync()
        {
            if (Version != CurrentVersion())
            {
                Rebuild();
            }
        }

        public void Rebuild()
        {
            Map.Clear();
            for (var i = 0; i < List.Count; i++)
            {
                if (List[i]?.Name?.Content is string content)
                {
                    Map.TryAdd(content, i);
                }
            }
            Version = CurrentVersion();
        }
    }
}
