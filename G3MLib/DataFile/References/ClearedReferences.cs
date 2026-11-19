using System.Collections;
using System.Reflection;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.References;

/// <summary>
/// The result of <see cref="ReferenceIndex.ClearUsages"/>. <see cref="Undo"/> puts every cleared reference back.
/// </summary>
public sealed class ClearedReferences
{
    private readonly List<(object Holder, PropertyInfo Property, object? Value)> _properties = [];
    private readonly List<(IList List, int Index, object? Item)> _removed = [];

    internal ClearedReferences(IReadOnlyList<Reference> cleared, IReadOnlyList<Reference> skipped)
    {
        Cleared = cleared;
        Skipped = skipped;
    }

    /// <summary>The uses that were cleared.</summary>
    public IReadOnlyList<Reference> Cleared { get; }

    /// <summary>The owners whose references were cleared, once each. Pass them to <see cref="ReferenceIndex.Update"/> after <see cref="Undo"/>, and refresh them in a UI.</summary>
    public IEnumerable<object> ChangedOwners => Cleared.Select(use => use.Owner).Distinct(ReferenceEqualityComparer.Instance);

    /// <summary>The uses that were left alone because they are in code. An instruction cannot lose its operand safely, so change that code first.</summary>
    public IReadOnlyList<Reference> Skipped { get; }

    /// <summary>Puts the cleared properties and list entries back, last change first. It does not take the data gate: call it inside a write scope when a save or script can run at the same time.</summary>
    public void Undo()
    {
        foreach (var (list, index, item) in Enumerable.Reverse(_removed))
        {
            list.Insert(index, item);
        }
        _removed.Clear();
        foreach (var (holder, property, value) in Enumerable.Reverse(_properties))
        {
            property.SetValue(holder, value);
        }
        _properties.Clear();
    }

    internal static ClearedReferences Clear(ReferenceIndex index, object target)
    {
        var uses = index.FindUsages(target);
        var skipped = uses.Where(use => use.InstructionOffset >= 0).ToList();
        var cleared = uses.Where(use => use.InstructionOffset < 0).ToList();
        ClearedReferences result = new(cleared, skipped);
        try
        {
            foreach (var use in cleared)
            {
                result.ClearOne(use, target);
            }
        }
        catch (Exception error)
        {
            result.Undo();
            throw new InvalidOperationException($"A reference could not be cleared: {error.Message}", error);
        }
        index.Update(cleared.Select(use => use.Owner));
        return result;
    }

    private void ClearOne(Reference use, object target)
    {
        var property = use.Holder.GetType().GetProperty(use.Member) ?? throw new InvalidOperationException($"{use.OwnerName}.{use.Member} cannot be cleared.");
        var current = property.GetValue(use.Holder);
        if (current is IList list)
        {
            var at = IndexOf(list, target);
            if (at >= 0)
            {
                _removed.Add((list, at, list[at]));
                list.RemoveAt(at);
            }
            // else an earlier use in the same list took it out already
            return;
        }

        _properties.Add((use.Holder, property, current));
        property.SetValue(use.Holder, null);
    }

    // A list holds the resource itself or a reference wrapper with a Resource property
    private static int IndexOf(IList list, object target)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target) || list[i] is GameMakerResourceRef { Resource: { } inner } && ReferenceEquals(inner, target))
            {
                return i;
            }
        }
        return -1;
    }
}
