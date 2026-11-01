using System.Collections;
using System.Text.RegularExpressions;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.References;

/// <summary>Why a rename was refused.</summary>
public enum RenameFailure
{
    /// <summary>The new name is empty, not an identifier, or a GML keyword.</summary>
    InvalidName,
    /// <summary>Another asset, variable, function or code entry already has the new name.</summary>
    NameInUse,
    /// <summary>Nothing with the old name exists.</summary>
    NotFound,
    /// <summary>The name belongs to a GameMaker built-in or an extension, which game code cannot rename.</summary>
    Builtin,
    /// <summary>Two requests of one batch change the same name, so the result would depend on their order.</summary>
    Overlap
}

/// <summary>Thrown when a rename is refused. Nothing has been changed when this is thrown.</summary>
public sealed class RenameException(RenameFailure reason, string message) : ArgumentException(message)
{
    /// <summary>The kind of refusal.</summary>
    public RenameFailure Reason { get; } = reason;
}

/// <summary>One changed string entry.</summary>
/// <param name="String">The string entry whose content changed. Every holder of this entry sees the new text.</param>
/// <param name="OldValue">Content before the rename.</param>
/// <param name="NewValue">Content after the rename.</param>
/// <param name="Location">The main holder, for example <c>Sprite spr_a: Name</c> or <c>Code gml_Object_obj_a_Create_0: Name</c>.</param>
public sealed record RenameChange(GameMakerString String, string OldValue, string NewValue, string Location);

/// <summary>
/// The result of a rename. <see cref="Undo"/> restores the old names, <see cref="Redo"/> applies them again.
/// </summary>
public sealed class RenameChangeSet : IReadOnlyList<RenameChange>
{
    private readonly List<RenameChange> _changes;

    internal RenameChangeSet(List<RenameChange> changes, IReadOnlyList<Reference> uses)
    {
        _changes = changes;
        Uses = uses;
    }

    /// <summary>
    /// Places that hold the renamed string entry, such as code string literals, when a <see cref="ReferenceIndex"/> was passed. Empty otherwise.
    /// </summary>
    public IReadOnlyList<Reference> Uses { get; }

    /// <inheritdoc />
    public int Count => _changes.Count;

    /// <inheritdoc />
    public RenameChange this[int index] => _changes[index];

    /// <summary>Restores the old content of every changed string, last change first.</summary>
    /// <exception cref="InvalidOperationException">A string no longer has the content the rename gave it. Nothing is changed.</exception>
    public void Undo() => Apply(reverse: true);

    /// <summary>Applies the new content again after an <see cref="Undo"/>.</summary>
    /// <exception cref="InvalidOperationException">A string no longer has the content before the rename. Nothing is changed.</exception>
    public void Redo() => Apply(reverse: false);

    private void Apply(bool reverse)
    {
        foreach (var change in _changes)
        {
            var expected = reverse ? change.NewValue : change.OldValue;
            if (change.String.Content != expected)
            {
                throw new InvalidOperationException($"'{change.Location}' is now '{change.String.Content}', expected '{expected}'. It was edited after the rename.");
            }
        }
        foreach (var change in reverse ? _changes.AsEnumerable().Reverse() : _changes)
        {
            change.String.Content = reverse ? change.OldValue : change.NewValue;
        }
    }

    /// <inheritdoc />
    public IEnumerator<RenameChange> GetEnumerator() => _changes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Renames resources, variables and functions and every name that embeds them.
/// </summary>
/// <remarks>
/// Resources are referenced by object, so a rename only changes text: the resource name string entry and the code,
/// function and code locals entries whose names embed the resource name (such as <c>gml_Object_obj_a_Create_0</c>).
/// A string entry is one table entry shared by all its holders, so code string literals equal to the name change
/// with it, and so does any unrelated holder that happens to share the same entry. Pass a <see cref="ReferenceIndex"/> to
/// list the holders in <see cref="RenameChangeSet.Uses"/>. Names used only as part of a longer string, or stored as plain
/// numbers, are not found. A <c>GlobalDecompileContext</c> caches asset names, so create a new one after a rename.
/// All checks run before the first change, so a refused rename changes nothing.
/// </remarks>
public static partial class RenameService
{
    private const string ScriptPrefix = "gml_Script_";

    private const string EventNames = "Create|Destroy|Alarm|Step|Collision|Keyboard|Mouse|Other|Draw|KeyPress|KeyRelease|Gesture|Asynchronous|PreCreate|CleanUp";

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "if", "else", "while", "for", "do", "until", "repeat", "switch", "case", "default", "break", "continue", "return", "exit",
        "with", "var", "globalvar", "function", "constructor", "new", "delete", "static", "enum", "try", "catch", "finally", "throw",
        "begin", "end", "and", "or", "not", "xor", "div", "mod", "true", "false", "undefined", "self", "other", "all", "noone", "global"
    };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    /// <summary>
    /// Renames an asset: sprite, object, room, script, sound, font, path, timeline, shader, sequence, animation curve,
    /// particle system or background (tile set).
    /// </summary>
    /// <param name="data">The data file that holds the resource.</param>
    /// <param name="resource">The resource to rename.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="index">Optional index, used to fill <see cref="RenameChangeSet.Uses"/>.</param>
    /// <exception cref="RenameException">The name is not an identifier or is already used by another asset or code entry.</exception>
    public static RenameChangeSet RenameResource(GameMakerData data, GameMakerNamedResource resource, string newName, ReferenceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return RenameOne(data, RenameRequest.ForResource(resource, newName), index);
    }

    /// <summary>
    /// Renames every variable entry with this name, whatever its instance type, and the matching code locals.
    /// </summary>
    /// <exception cref="RenameException">The variable does not exist, is built in, or the new name is invalid or already a variable.</exception>
    public static RenameChangeSet RenameVariable(GameMakerData data, string oldName, string newName, ReferenceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        return RenameOne(data, RenameRequest.ForVariable(oldName, newName), index);
    }

    /// <summary>
    /// Renames a function defined in game code (<c>function name() {}</c>), the code entries named after it and its code locals.
    /// A name that belongs to a script renames the script instead.
    /// </summary>
    /// <exception cref="RenameException">The function does not exist, is built in or from an extension, or the new name is invalid or taken.</exception>
    public static RenameChangeSet RenameFunction(GameMakerData data, string oldName, string newName, ReferenceIndex? index = null)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        return RenameOne(data, RenameRequest.ForFunction(oldName, newName), index);
    }

    private static RenameChangeSet RenameOne(GameMakerData data, RenameRequest request, ReferenceIndex? index)
    {
        var plan = PlanRenames(data, [request], index);
        if (plan.Conflicts.Count > 0)
        {
            throw new RenameException(plan.Conflicts[0].Reason, plan.Conflicts[0].Message);
        }
        return Apply(plan);
    }

    /// <summary>
    /// Checks a batch of renames against each other and against the data file, and lists what would change. Nothing is changed.
    /// Names may swap or shift: only the names after the whole batch have to be free.
    /// </summary>
    /// <remarks>
    /// A name string that two requests would change (for example a collision event code entry that embeds both object names) is a conflict
    /// of kind <see cref="RenameFailure.Overlap"/>; rename those in separate batches.
    /// </remarks>
    public static RenamePlan PlanRenames(GameMakerData data, IEnumerable<RenameRequest> requests, ReferenceIndex? index = null, IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(requests);
        List<RenameRequest> list = [.. requests];
        Plan plan = new();
        List<RenameConflict> conflicts = [];
        List<Candidate> candidates = [];
        List<Reference> uses = [];
        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = list[i];
            try
            {
                var candidate = PlanRequest(data, request, i, plan);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                    uses.AddRange(candidate.Uses(index));
                }
            }
            catch (RenameException ex)
            {
                conflicts.Add(new RenameConflict(request, ex.Reason, ex.Message));
            }
            progress?.Report(new BatchProgress(i + 1, list.Count, request.OldName ?? request.Resource?.Name?.Content ?? string.Empty));
        }

        CheckFinalNames(data, plan, candidates, conflicts);
        conflicts.AddRange(plan.Overlaps.Select(o => new RenameConflict(list[o.Request], RenameFailure.Overlap, o.Message)));
        return new RenamePlan(plan.Changes(), [.. conflicts.OrderBy(c => list.IndexOf(c.Request))], uses);
    }

    /// <summary>
    /// Writes a plan into the data file. Single threaded, no cancel point.
    /// </summary>
    /// <exception cref="InvalidOperationException">The plan has conflicts. Nothing is changed.</exception>
    public static RenameChangeSet Apply(RenamePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Conflicts.Count > 0)
        {
            throw new InvalidOperationException($"The plan has {plan.Conflicts.Count} conflict(s): {plan.Conflicts[0].Message}");
        }
        foreach (var change in plan.Changes)
        {
            change.String.Content = change.NewValue;
        }
        return new RenameChangeSet([.. plan.Changes], plan.Uses);
    }

    // One accepted request with what the final name check needs.
    private sealed record Candidate(RenameRequest Request, int Number, GameMakerNamedResource? Resource, List<GameMakerVariable> Variables, List<GameMakerFunction> Functions, string NewEntry, Func<ReferenceIndex, IReadOnlyList<Reference>> UsesOf)
    {
        public IReadOnlyList<Reference> Uses(ReferenceIndex? index) => index is null ? [] : UsesOf(index);
    }

    private static Candidate? PlanRequest(GameMakerData data, RenameRequest request, int number, Plan plan)
    {
        switch (request.Kind)
        {
            case RenameKind.Resource:
                return PlanResource(data, request, number, plan, request.Resource ?? throw new RenameException(RenameFailure.NotFound, "The request has no resource."));
            case RenameKind.Variable:
                return PlanVariable(data, request, number, plan);
            default:
                if (data.Scripts?.FirstOrDefault(s => s?.Name?.Content == request.OldName) is { } script)
                {
                    return PlanResource(data, request, number, plan, script);
                }
                return PlanFunction(data, request, number, plan);
        }
    }

    private static Candidate? PlanResource(GameMakerData data, RenameRequest request, int number, Plan plan, GameMakerNamedResource resource)
    {
        var newName = request.NewName;
        var oldName = resource.Name?.Content ?? throw new RenameException(RenameFailure.NotFound, "The resource has no name.");
        RequireIdentifier(newName);
        if (newName == oldName)
        {
            return null;
        }

        plan.Add(resource.Name, newName, $"{TypeName(resource)} {oldName}: Name", number);
        var patterns = EmbeddedPatterns(resource, oldName);
        foreach (var code in data.Code ?? [])
        {
            Embed(plan, code?.Name, patterns, newName, "Code", number);
        }
        foreach (var locals in data.CodeLocals ?? [])
        {
            Embed(plan, locals?.Name, patterns, newName, "Code locals", number);
        }
        foreach (var function in data.Functions ?? [])
        {
            Embed(plan, function?.Name, patterns, newName, "Function", number);
        }
        if (resource is GameMakerScript)
        {
            foreach (var function in (data.Functions ?? []).Where(f => f?.Name?.Content == oldName))
            {
                plan.Add(function.Name, newName, $"Function {oldName}: Name", number);
            }
        }
        return new Candidate(request, number, resource, [], [], string.Empty, index => index.FindUsages(resource.Name));
    }

    private static Candidate? PlanVariable(GameMakerData data, RenameRequest request, int number, Plan plan)
    {
        var oldName = request.OldName;
        var newName = request.NewName;
        RequireIdentifier(newName);
        List<GameMakerVariable> entries = [.. (data.Variables ?? []).Where(v => v?.Name?.Content == oldName)];
        if (entries.Count == 0)
        {
            throw new RenameException(RenameFailure.NotFound, $"No variable named '{oldName}'.");
        }
        if (newName == oldName)
        {
            return null;
        }
        BuiltinList builtins = new(data);
        if (builtins.InstanceVars.ContainsKey(oldName) || builtins.GlobalVars.ContainsKey(oldName) || builtins.GlobalArrayVars.ContainsKey(oldName) || builtins.InstanceLimitedVars.ContainsKey(oldName))
        {
            throw new RenameException(RenameFailure.Builtin, $"'{oldName}' is a built-in variable.");
        }

        foreach (var entry in entries)
        {
            plan.Add(entry.Name, newName, $"Variable {oldName}: Name", number);
        }
        foreach (var local in (data.CodeLocals ?? []).SelectMany(l => l?.Locals ?? []).Where(l => l?.Name?.Content == oldName))
        {
            plan.Add(local.Name, newName, $"Code local {oldName}: Name", number);
        }
        return new Candidate(request, number, null, entries, [], string.Empty, index => index.FindVariableUsages(oldName));
    }

    private static Candidate? PlanFunction(GameMakerData data, RenameRequest request, int number, Plan plan)
    {
        var oldName = request.OldName;
        var newName = request.NewName;
        RequireIdentifier(newName);
        var oldEntry = ScriptPrefix + oldName;
        var newEntry = ScriptPrefix + newName;
        List<GameMakerFunction> entries = [.. (data.Functions ?? []).Where(f => f?.Name?.Content == oldEntry)];
        if (entries.Count == 0)
        {
            throw (data.Functions ?? []).Any(f => f?.Name?.Content == oldName)
                ? new RenameException(RenameFailure.Builtin, $"'{oldName}' is a built-in or extension function.")
                : new RenameException(RenameFailure.NotFound, $"No function named '{oldName}'.");
        }
        if (newName == oldName)
        {
            return null;
        }

        foreach (var entry in entries)
        {
            plan.Add(entry.Name, newEntry, $"Function {oldName}: Name", number);
        }
        foreach (var code in (data.Code ?? []).Where(c => c?.Name?.Content == oldEntry))
        {
            plan.Add(code.Name, newEntry, $"Code {oldEntry}: Name", number);
        }
        foreach (var locals in (data.CodeLocals ?? []).Where(l => l?.Name?.Content == oldEntry))
        {
            plan.Add(locals.Name, newEntry, $"Code locals {oldEntry}: Name", number);
        }
        return new Candidate(request, number, null, [], entries, newEntry, index => index.FindFunctionUsages(oldEntry));
    }

    // Name clashes are judged on the names after the whole batch, so a swap or a shift is allowed.
    private static void CheckFinalNames(GameMakerData data, Plan plan, List<Candidate> candidates, List<RenameConflict> conflicts)
    {
        HashSet<int> failed = [];
        void Fail(Candidate candidate, string message, RenameFailure reason = RenameFailure.NameInUse)
        {
            if (failed.Add(candidate.Number))
            {
                conflicts.Add(new RenameConflict(candidate.Request, reason, message));
            }
        }

        List<GameMakerNamedResource> assets = [.. AssetLists(data).SelectMany(l => l.OfType<GameMakerNamedResource>())];
        foreach (var candidate in candidates)
        {
            var newName = candidate.Request.NewName;
            if (candidate.Resource is not null)
            {
                var other = assets.FirstOrDefault(a => a.Name is not null && !ReferenceEquals(a, candidate.Resource) && plan.Final(a.Name) == newName);
                if (other is not null)
                {
                    Fail(candidate, $"'{newName}' is already the name of {TypeName(other)} '{newName}'.");
                }
            }
            else if (candidate.Variables.Count > 0)
            {
                if ((data.Variables ?? []).Any(v => v?.Name is not null && !candidate.Variables.Contains(v) && plan.Final(v.Name) == newName))
                {
                    Fail(candidate, $"A variable named '{newName}' already exists.");
                }
            }
            else if ((data.Functions ?? []).Any(f => f?.Name is not null && !candidate.Functions.Contains(f) && (plan.Final(f.Name) == newName || plan.Final(f.Name) == candidate.NewEntry))
                || assets.Any(a => a.Name is not null && plan.Final(a.Name) == newName))
            {
                Fail(candidate, $"A function or asset named '{newName}' already exists.");
            }
        }

        // A renamed code entry must not land on the name of another code entry
        foreach (var group in (data.Code ?? []).Where(c => c?.Name?.Content is not null).GroupBy(c => plan.Final(c.Name), StringComparer.Ordinal))
        {
            if (group.Count() < 2 || !group.Any(c => plan.Contains(c.Name)))
            {
                continue;
            }
            var culprit = candidates.FirstOrDefault(c => group.Any(code => plan.OriginOf(code.Name) == c.Number));
            if (culprit is not null)
            {
                Fail(culprit, $"A code entry named '{group.Key}' already exists.");
            }
        }
    }

    private static void RequireIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !IdentifierPattern().IsMatch(name))
        {
            throw new RenameException(RenameFailure.InvalidName, $"'{name}' is not a valid name: use letters, digits and underscores, and do not start with a digit.");
        }
        if (Reserved.Contains(name))
        {
            throw new RenameException(RenameFailure.InvalidName, $"'{name}' is a GML keyword.");
        }
    }

    private static string TypeName(object resource) => resource.GetType().Name.Replace("GameMaker", "", StringComparison.Ordinal);

    private static IEnumerable<IList> AssetLists(GameMakerData data)
    {
        foreach (var list in new IList?[] { (IList?)data.GameObjects, (IList?)data.Sprites, (IList?)data.Sounds, (IList?)data.Rooms, (IList?)data.Backgrounds,
            (IList?)data.Paths, (IList?)data.Scripts, (IList?)data.Fonts, (IList?)data.Timelines, (IList?)data.Shaders, (IList?)data.Sequences,
            (IList?)data.AnimationCurves, (IList?)data.ParticleSystems })
        {
            if (list is not null)
            {
                yield return list;
            }
        }
    }

    // Names GameMaker derives from a resource name: lookbehind and lookahead keep a longer name (obj_a_b) from matching obj_a
    private static Regex[] EmbeddedPatterns(GameMakerNamedResource resource, string name)
    {
        var old = Regex.Escape(name);
        return resource switch
        {
            GameMakerGameObject =>
            [
                new($"(?<=gml_Object_){old}(?=_(?:{EventNames})_)"),
                new($"(?<=gml_Object_[A-Za-z0-9_]*_Collision_){old}(?=$|_[0-9])")
            ],
            GameMakerScript => [new($"(?<=gml_(?:Script|GlobalScript)_){old}(?=$|_[0-9])")],
            GameMakerRoom =>
            [
                new($"(?<=gml_Room_){old}(?=_(?:Create|PreCreate)(?:$|_))"),
                new($"(?<=gml_RoomCC_){old}(?=_[0-9]+_(?:Create|PreCreate))")
            ],
            GameMakerTimeline => [new($"(?<=gml_Timeline_){old}(?=_[0-9])")],
            _ => []
        };
    }

    private static void Embed(Plan plan, GameMakerString? text, Regex[] patterns, string newName, string kind, int number)
    {
        if (text?.Content is not string content || patterns.Length == 0)
        {
            return;
        }
        var rewritten = content;
        foreach (var pattern in patterns)
        {
            rewritten = pattern.Replace(rewritten, newName);
        }
        if (rewritten != content)
        {
            plan.Add(text, rewritten, $"{kind} {content}: Name", number);
        }
    }

    private sealed class Plan
    {
        private readonly Dictionary<GameMakerString, (string NewValue, string Location, int Request)> _items = new(ReferenceEqualityComparer.Instance);
        private readonly List<GameMakerString> _order = [];

        public List<(int Request, string Message)> Overlaps { get; } = [];

        public void Add(GameMakerString text, string newValue, string location, int request)
        {
            if (text?.Content is null)
            {
                return;
            }
            if (_items.TryGetValue(text, out var existing))
            {
                if (existing.Request != request && existing.NewValue != newValue)
                {
                    Overlaps.Add((request, $"'{text.Content}' is changed by two renames of the batch."));
                }
                return;
            }
            _items[text] = (newValue, location, request);
            _order.Add(text);
        }

        public bool Contains(GameMakerString text) => _items.ContainsKey(text);

        public string Final(GameMakerString text) => _items.TryGetValue(text, out var item) ? item.NewValue : text.Content;

        public int OriginOf(GameMakerString text) => _items.TryGetValue(text, out var item) ? item.Request : -1;

        public List<RenameChange> Changes() => [.. _order.Select(t => new RenameChange(t, t.Content, _items[t].NewValue, _items[t].Location))];
    }
}

/// <summary>What a <see cref="RenameRequest"/> renames.</summary>
public enum RenameKind
{
    Resource,
    Variable,
    Function
}

/// <summary>One rename of a batch. Create it with <see cref="ForResource"/>, <see cref="ForVariable"/> or <see cref="ForFunction"/>.</summary>
public sealed record RenameRequest(RenameKind Kind, string OldName, string NewName, GameMakerNamedResource? Resource = null)
{
    public static RenameRequest ForResource(GameMakerNamedResource resource, string newName)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new(RenameKind.Resource, resource.Name?.Content ?? string.Empty, newName, resource);
    }

    public static RenameRequest ForVariable(string oldName, string newName) => new(RenameKind.Variable, oldName, newName);

    public static RenameRequest ForFunction(string oldName, string newName) => new(RenameKind.Function, oldName, newName);
}

/// <summary>A request that cannot be done, and why.</summary>
public sealed record RenameConflict(RenameRequest Request, RenameFailure Reason, string Message);

/// <summary>The result of <see cref="RenameService.PlanRenames"/>: what would change, and the requests that cannot be done.</summary>
public sealed class RenamePlan
{
    internal RenamePlan(IReadOnlyList<RenameChange> changes, IReadOnlyList<RenameConflict> conflicts, IReadOnlyList<Reference> uses)
    {
        Changes = changes;
        Conflicts = conflicts;
        Uses = uses;
    }

    /// <summary>The string entries that change. Check <see cref="Conflicts"/> before relying on them.</summary>
    public IReadOnlyList<RenameChange> Changes { get; }

    /// <summary>Requests that cannot be done. <see cref="RenameService.Apply"/> refuses a plan that has any.</summary>
    public IReadOnlyList<RenameConflict> Conflicts { get; }

    /// <summary>Places that hold the renamed string entries, when a <see cref="ReferenceIndex"/> was passed.</summary>
    public IReadOnlyList<Reference> Uses { get; }
}
