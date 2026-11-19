using System.Collections;
using System.Text.RegularExpressions;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.References;

/// <summary>
/// The copy of a resource made by <see cref="ResourceCloneService.Clone"/>, with everything that was added to the data file for it.
/// </summary>
public sealed class ResourceClone
{
    private readonly List<(IList List, object Item)> _added;

    private readonly List<Action> _restore;

    internal ResourceClone(GameMakerNamedResource resource, List<(IList List, object Item)> added, List<Action> restore, IReadOnlyList<GameMakerCode> sharedCode)
    {
        Resource = resource;
        SharedCode = sharedCode;
        _added = added;
        _restore = restore;
        Added = [.. added.Select(entry => entry.Item)];
    }

    /// <summary>The new resource, already added to its list in the data file.</summary>
    public GameMakerNamedResource Resource { get; }

    /// <summary>
    /// The objects the copy added to the data file: the resource itself and the code entries, code locals, audio entries and
    /// particle emitters it owns, and the strings that were new to the file.
    /// </summary>
    public IReadOnlyList<object> Added { get; }

    /// <summary>
    /// Code entries the copy points at but does not own, because their names do not follow the naming of the resource. Replacing their code changes the source too.
    /// </summary>
    public IReadOnlyList<GameMakerCode> SharedCode { get; }

    /// <summary>
    /// Removes everything <see cref="Added"/> from the data file again. Nothing else may refer to the copy yet; use <see cref="ReferenceIndex.ClearUsages"/> first if it does.
    /// Edits made after the clone are kept: the last instance and tile ids are only rewound while they still hold the values the clone set,
    /// and a string is only removed while it is the last one in the file, because a string index may be in use by then. An unused string is harmless. Undo does not take the data gate: call it inside a write scope when a save or script can run at the same time.
    /// </summary>
    public void Undo() => Rewind(_added, _restore);

    internal static void Rewind(List<(IList List, object Item)> added, List<Action> restore)
    {
        foreach (var undo in restore)
        {
            undo();
        }
        foreach (var (list, item) in Enumerable.Reverse(added))
        {
            if (item is GameMakerString)
            {
                if (list.Count > 0 && ReferenceEquals(list[^1], item))
                {
                    list.RemoveAt(list.Count - 1);
                }
                continue;
            }
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(list[i], item))
                {
                    list.RemoveAt(i);
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Copies a resource inside one data file under a new name, without going through an export folder.
/// </summary>
/// <remarks>
/// The copy owns its own data: events, layers, instances, frames, tracks and masks are duplicated. It keeps pointing at the same
/// shared assets: other resources, texture page items (so the pixels of a copied sprite, font or tile set stay shared until a host
/// replaces them), variables, functions and strings. Code entries and code locals whose names are derived from the resource name
/// (<c>gml_Object_name_Create_0</c>, <c>gml_Script_name</c>, <c>gml_RoomCC_name_1_Create</c>) are copied and renamed with the
/// <see cref="RenameService"/> rules, as are the function entries of those names (so a copied script can be called), embedded sound audio is copied, and the instances of a room get new instance ids.
/// </remarks>
public static class ResourceCloneService
{
    /// <summary>
    /// Copies <paramref name="source"/> under <paramref name="newName"/> and adds the copy to the data file.
    /// </summary>
    /// <param name="data">The data file that holds <paramref name="source"/>.</param>
    /// <param name="source">The resource to copy.</param>
    /// <param name="newName">Name of the copy. Must be a valid identifier that nothing else in the data file uses.</param>
    /// <returns>The copy and the objects added for it. Undo is <see cref="ResourceClone.Undo"/>, plus <see cref="ReferenceIndex.ClearUsages"/> if the copy was already referenced.</returns>
    /// <param name="timeout">How long to wait for the data gate; null waits without a limit. The call holds the gate as a writer, so a host on a UI thread should give a timeout or a token.</param>
    /// <param name="cancellationToken">Stops the wait for the data gate.</param>
    /// <exception cref="TimeoutException">The gate was not free in time.</exception>
    /// <exception cref="RenameException">The name is invalid or in use.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not in <paramref name="data"/>.</exception>
    /// <exception cref="NotSupportedException">The resource type has no list a copy could join, or is not a stand-alone resource: code entries, functions, variables, code locals, audio groups, texture groups, filter effects, embedded audio and embedded textures.</exception>
    public static ResourceClone Clone(GameMakerData data, GameMakerNamedResource source, string newName, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(source);
        if (source is GameMakerCode or GameMakerFunction or GameMakerVariable or GameMakerCodeLocals or GameMakerAudioGroup or GameMakerTextureGroupInfo
            or GameMakerFilterEffect or GameMakerEmbeddedAudio or GameMakerEmbeddedTexture or GameMakerEmbeddedImage)
        {
            throw new NotSupportedException($"{source.GetType().Name} cannot be cloned on its own. Code entries are copied with the resource that owns them; use CodeImportGroup to create one.");
        }
        // The checks run inside the gate: a script that holds it may add the name or remove the source meanwhile
        using var gate = data.Gate.BeginWrite(timeout, cancellationToken);
        IList list;
        try
        {
            list = data[source.GetType()];
        }
        catch (MissingMemberException)
        {
            throw new NotSupportedException($"{source.GetType().Name} has no resource list in the data file and cannot be cloned.");
        }
        var oldName = source.Name?.Content ?? throw new ArgumentException("The resource has no name.", nameof(source));
        if (!RenameService.IsValidName(newName))
        {
            throw new RenameException(RenameFailure.InvalidName, $"'{newName}' is not a valid name.");
        }
        if (RenameService.IsNameInUse(data, newName))
        {
            throw new RenameException(RenameFailure.NameInUse, $"'{newName}' is already in use.");
        }
        if (!list.Cast<object?>().Any(item => ReferenceEquals(item, source)))
        {
            throw new ArgumentException("The resource is not in the data file.", nameof(source));
        }

        return new Run(data, source, oldName, newName, list).Execute();
    }

    private sealed class Run
    {
        private readonly GameMakerData _data;
        private readonly GameMakerNamedResource _source;
        private readonly string _oldName;
        private readonly string _newName;
        private readonly IList _list;
        private readonly Regex[] _patterns;
        private readonly HashSet<GameMakerCode> _ownedCode = new(ReferenceEqualityComparer.Instance);
        private readonly List<(IList List, object Item)> _added = [];
        private readonly List<Action> _restore = [];
        private readonly Dictionary<uint, uint> _newInstanceIds = [];
        private readonly GameMakerString _nameString;
        private readonly ModelCloner _cloner;
        // Built once per run: the data file only offers linear searches, and a room can bring thousands of code entries
        private Dictionary<string, GameMakerString>? _strings;
        private Dictionary<string, GameMakerCodeLocals>? _locals;
        private HashSet<string>? _usedNames;
        private HashSet<string>? _codeNames;
        private readonly List<GameMakerCode> _sharedCode = [];
        private readonly HashSet<GameMakerCode> _sharedSeen = new(ReferenceEqualityComparer.Instance);

        public Run(GameMakerData data, GameMakerNamedResource source, string oldName, string newName, IList list)
        {
            _data = data;
            _source = source;
            _oldName = oldName;
            _newName = newName;
            _list = list;
            _patterns = RenameService.EmbeddedPatterns(source, oldName);
            foreach (var code in data.Code ?? [])
            {
                if (code?.Name?.Content is { } name && _patterns.Any(p => p.IsMatch(name)))
                {
                    AddOwnedWithChildren(code);
                }
            }
            // shortcut: the copy walks the model by reflection (about 0.5 s for a room with 20000 instances and 50000 tiles), compile a delegate per type if rooms get slower
            _cloner = new ModelCloner(ReferenceIndex.CollectRegistry(data), IsOwned, Created, Filled);
            // Last, so a throw above leaves no string behind and the caller has no clone to undo
            _nameString = NewString(newName);
        }

        private void AddOwnedWithChildren(GameMakerCode code)
        {
            if (_ownedCode.Add(code))
            {
                foreach (var child in code.ChildEntries ?? [])
                {
                    if (child is not null)
                    {
                        AddOwnedWithChildren(child);
                    }
                }
            }
        }

        private bool IsOwned(object item) => item switch
        {
            GameMakerCode code => _ownedCode.Contains(code) || NoteShared(code),
            GameMakerEmbeddedAudio audio => _source is GameMakerSound sound && ReferenceEquals(sound.AudioFile, audio) && sound.GroupID <= 0 && _data.EmbeddedAudio.Contains(audio),
            GameMakerParticleSystemEmitter emitter => _source is GameMakerParticleSystem system && system.Emitters.Any(e => ReferenceEquals(e, emitter)),
            _ => false
        };

        private bool NoteShared(GameMakerCode code)
        {
            if (_sharedSeen.Add(code))
            {
                _sharedCode.Add(code);
            }
            return false;
        }

        // The copy joins its data file list before its members are copied, so a parent entry stays ahead of its children.
        private void Created(object source, object copy)
        {
            if (ReferenceEquals(source, _source))
            {
                Add(_list, copy);
            }
            else if (copy is GameMakerCode)
            {
                Add((IList)_data.Code, copy);
            }
            else if (copy is GameMakerEmbeddedAudio)
            {
                Add((IList)_data.EmbeddedAudio, copy);
            }
            else if (copy is GameMakerParticleSystemEmitter)
            {
                Add((IList)_data.ParticleSystemEmitters, copy);
            }
        }

        // A string the file already holds is shared; a new one is recorded so Undo removes it.
        private GameMakerString NewString(string text)
        {
            if (_strings is null)
            {
                _strings = new(StringComparer.Ordinal);
                foreach (var existing in _data.Strings)
                {
                    if (existing?.Content is { } content)
                    {
                        _strings.TryAdd(content, existing);
                    }
                }
            }
            if (!_strings.TryGetValue(text, out var made))
            {
                made = new GameMakerString(text);
                _data.Strings.Add(made);
                _strings[text] = made;
                _added.Add(((IList)_data.Strings, made));
            }
            return made;
        }

        private void Add(IList list, object item)
        {
            list.Add(item);
            _added.Add((list, item));
        }

        private void Filled(object source, object copy)
        {
            if (copy is GameMakerCode code && source is GameMakerCode original)
            {
                code.Name = NewString(UniqueCodeName(original.Name.Content));
                CopyLocals(original, code);
            }
            else if (copy is GameMakerParticleSystemEmitter emitter && source is GameMakerParticleSystemEmitter sourceEmitter)
            {
                emitter.Name = NewString(UniqueEmitterName(sourceEmitter.Name?.Content));
            }
            else if (copy is GameMakerNamedResource { Name: { } name } named && name.Content == _oldName)
            {
                named.Name = _nameString;
            }
        }

        // A child entry whose name has no part of the resource name would repeat the name of the original
        private string UniqueCodeName(string name)
        {
            var renamed = Embedded(name);
            if (renamed != name)
            {
                return renamed;
            }
            _codeNames ??= [.. (_data.Code ?? []).Select(code => code?.Name?.Content).OfType<string>()];
            renamed = name + "_" + _newName;
            for (var n = 2; !_codeNames.Add(renamed); n++)
            {
                renamed = name + "_" + _newName + "_" + n;
            }
            return renamed;
        }

        // The functions RenameService renames with the resource: the ones named like its code entries and, for a script, the script function itself
        private void CopyFunctions()
        {
            if (_data.Functions is not { } functions)
            {
                return;
            }
            foreach (var function in functions.ToList())
            {
                if (function?.Name?.Content is not { } name)
                {
                    continue;
                }
                var renamed = Embedded(name);
                if (renamed == name && !(_source is GameMakerScript && name == _oldName))
                {
                    continue;
                }
                renamed = renamed == name ? _newName : renamed;
                var made = NewString(renamed);
                Add((IList)functions, new GameMakerFunction { Name = made, NameStringID = _data.Strings.IndexOf(made), Classification = function.Classification });
            }
        }

        private string Embedded(string name)
        {
            foreach (var pattern in _patterns)
            {
                name = pattern.Replace(name, _newName);
            }
            return name;
        }

        private string UniqueEmitterName(string? old)
        {
            var baseName = old is not null && old.Contains(_oldName, StringComparison.Ordinal) ? old.Replace(_oldName, _newName, StringComparison.Ordinal) : _newName + "_" + (old ?? "emitter");
            _usedNames ??= new(RenameService.AllNames(_data), StringComparer.OrdinalIgnoreCase);
            var name = baseName;
            for (var n = 2; !_usedNames.Add(name); n++)
            {
                name = baseName + "_" + n;
            }
            return name;
        }

        private Dictionary<string, GameMakerCodeLocals> Locals
        {
            get
            {
                if (_locals is null)
                {
                    _locals = new(StringComparer.Ordinal);
                    foreach (var locals in _data.CodeLocals ?? [])
                    {
                        if (locals?.Name?.Content is { } key)
                        {
                            _locals.TryAdd(key, locals);
                        }
                    }
                }
                return _locals;
            }
        }

        private void CopyLocals(GameMakerCode original, GameMakerCode copy)
        {
            if (original.Name is { } name && Locals.TryGetValue(name.Content, out var locals))
            {
                var copied = (GameMakerCodeLocals)_cloner.CloneUnshared(locals);
                copied.Name = copy.Name;
                Add((IList)_data.CodeLocals, copied);
                Locals[copy.Name.Content] = copied;
            }
        }

        public ResourceClone Execute()
        {
            GameMakerNamedResource copy;
            try
            {
                copy = (GameMakerNamedResource)_cloner.CloneUnshared(_source);
                copy.Name = _nameString;
                switch (copy)
                {
                    case GameMakerSound sound when sound.AudioFile is { } audio && _data.EmbeddedAudio.Contains(audio) && !ReferenceEquals(audio, ((GameMakerSound)_source).AudioFile):
                        sound.AudioID = _data.EmbeddedAudio.IndexOf(audio);
                        break;
                    case GameMakerRoom room:
                        FinishRoom(room);
                        break;
                }
                CopyFunctions();
            }
            catch
            {
                ResourceClone.Rewind(_added, _restore);
                throw;
            }
            return new ResourceClone(copy, _added, _restore, _sharedCode);
        }

        // Instance ids are unique in the whole game, so the copy takes new ones and the creation code names follow.
        private void FinishRoom(GameMakerRoom room)
        {
            var info = _data.GeneralInfo;
            var (lastObj, lastTile) = (info.LastObj, info.LastTile);
            // Null until the room is finished: a failed clone rewinds whatever it had taken
            (uint Obj, uint Tile)? set = null;
            _restore.Add(() =>
            {
                if (set is null || info.LastObj == set.Value.Obj)
                {
                    info.LastObj = lastObj;
                }
                if (set is null || info.LastTile == set.Value.Tile)
                {
                    info.LastTile = lastTile;
                }
            });
            HashSet<GameMakerRoom.GameObject> instances = new(AllInstances(room), ReferenceEqualityComparer.Instance);
            // LastObj can be stale in a modded file; new ids above every existing one cannot meet the old id of a later instance
            foreach (var other in _data.Rooms ?? [])
            {
                foreach (var existing in AllInstances(other))
                {
                    info.LastObj = Math.Max(info.LastObj, existing.InstanceID);
                }
            }
            foreach (var instance in instances)
            {
                var old = instance.InstanceID;
                var next = ++info.LastObj;
                _newInstanceIds[old] = next;
                instance.InstanceID = next;
                RenameInstanceCode(instance.CreationCode, old, next);
                RenameInstanceCode(instance.PreCreateCode, old, next);
            }
            foreach (var tile in room.Tiles ?? [])
            {
                if (tile is not null)
                {
                    tile.InstanceID = ++info.LastTile;
                }
            }
            if (room.InstanceCreationOrderIDs is { } order)
            {
                for (var i = 0; i < order.InstanceIDs.Count; i++)
                {
                    if (_newInstanceIds.TryGetValue(order.InstanceIDs[i], out var mapped))
                    {
                        order.InstanceIDs[i] = mapped;
                    }
                }
            }
            Add((IList)info.RoomOrder, new GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM> { Resource = room });
            set = (info.LastObj, info.LastTile);
        }

        private static IEnumerable<GameMakerRoom.GameObject> AllInstances(GameMakerRoom? room)
        {
            foreach (var instance in room?.GameObjects ?? [])
            {
                if (instance is not null)
                {
                    yield return instance;
                }
            }
            foreach (var layer in room?.Layers ?? [])
            {
                foreach (var instance in layer?.InstancesData?.Instances ?? [])
                {
                    if (instance is not null)
                    {
                        yield return instance;
                    }
                }
            }
        }

        // Only code the copy owns is renamed: its name already holds the new room name.
        private void RenameInstanceCode(GameMakerCode? code, uint old, uint next)
        {
            if (code?.Name is { } name && _ownedCode.Count > 0 && name.Content.Contains($"gml_RoomCC_{_newName}_{old}_", StringComparison.Ordinal))
            {
                var renamed = name.Content.Replace($"gml_RoomCC_{_newName}_{old}_", $"gml_RoomCC_{_newName}_{next}_", StringComparison.Ordinal);
                code.Name = NewString(renamed);
                if (Locals.Remove(name.Content, out var locals))
                {
                    locals.Name = code.Name;
                    Locals[renamed] = locals;
                }
            }
        }
    }
}
