using System.Collections.Concurrent;
using System.Reflection;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.References;

// Copies the object graph below one resource field by field. Objects that live in a data file list (strings, code entries, other
// resources, texture page items) are shared by the copy, unless the owner says the copy needs its own version of them.
internal sealed class ModelCloner(HashSet<object> shared, Func<object, bool> isOwned, Action<object, object> created, Action<object, object> filled)
{
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldCache = new();
    private static readonly ConcurrentDictionary<Type, bool> HoldsCache = new();
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly Dictionary<object, object> _map = new(ReferenceEqualityComparer.Instance);

    // Maps a source object to the copy that stands in for it, so references to it inside the graph point at the copy.
    public void Map(object source, object copy) => _map[source] = copy;

    // Copies an object that lives in a data file list although the graph would share it.
    public object CloneUnshared(object source) => CloneObject(source, source.GetType());

    public object? CloneValue(object? value)
    {
        if (value is null)
        {
            return null;
        }
        var type = value.GetType();
        if (type.IsValueType)
        {
            // A struct is copied by value, but a model reference inside it still points at the source
            return HoldsModel(type) ? CloneStruct(value, type) : value;
        }
        if (value is string || value is Type || value is Delegate)
        {
            return value;
        }
        if (_map.TryGetValue(value, out var done))
        {
            return done;
        }
        if (type.IsArray)
        {
            return CloneArray((Array)value, type);
        }
        if (shared.Contains(value) ? !isOwned(value) : !IsCopyable(type))
        {
            return value;
        }
        return CloneObject(value, type);
    }

    private Array CloneArray(Array source, Type type)
    {
        var elementType = type.GetElementType()!;
        var copy = (Array)source.Clone();
        _map[source] = copy;
        if (elementType == typeof(string) || (elementType.IsValueType && !HoldsModel(elementType)))
        {
            return copy;
        }
        if (copy.Rank != 1 || copy.GetLowerBound(0) != 0)
        {
            throw new NotSupportedException($"{type.Name} cannot be copied: only one-dimensional arrays that start at index 0 are supported.");
        }
        for (var i = 0; i < copy.Length; i++)
        {
            copy.SetValue(CloneValue(copy.GetValue(i)), i);
        }
        return copy;
    }

    private object CloneStruct(object boxed, Type type)
    {
        // The box is already a private copy of the struct
        foreach (var field in FieldsOf(type))
        {
            var value = field.GetValue(boxed);
            if (value is not null && !ReferenceEquals(CloneValue(value), value))
            {
                field.SetValue(boxed, CloneValue(value));
            }
        }
        return boxed;
    }

    private object CloneObject(object source, Type type)
    {
        // The copies of the keys would not match the hash codes stored in the table
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var open && (open == typeof(Dictionary<,>) || open == typeof(HashSet<>))
            && !type.GetGenericArguments()[0].IsValueType && IsCopyable(type.GetGenericArguments()[0]))
        {
            throw new NotSupportedException($"{type.Name} with model objects as keys cannot be copied.");
        }
        var copy = MemberwiseCloneMethod.Invoke(source, null)!;
        _map[source] = copy;
        created(source, copy);
        foreach (var field in FieldsOf(type))
        {
            var value = field.GetValue(copy);
            if (value is null)
            {
                continue;
            }
            if (value is Delegate)
            {
                // Observers of the original must not hear about the copy.
                field.SetValue(copy, null);
                continue;
            }
            var next = CloneValue(value);
            if (!ReferenceEquals(next, value))
            {
                field.SetValue(copy, next);
            }
        }
        filled(source, copy);
        return copy;
    }

    // Model types and the framework collections they are built from are copied; everything else (images, streams, readers) is shared.
    private static bool IsCopyable(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        if (ns is "System.Collections.Generic" or "System.Collections.ObjectModel")
        {
            return true;
        }
        if (!ns.StartsWith("G3MLib.DataFile", StringComparison.Ordinal) || ns.StartsWith("G3MLib.DataFile.Util", StringComparison.Ordinal))
        {
            return false;
        }
        return !typeof(GameMakerData).IsAssignableFrom(type) && !typeof(GameMakerChunk).IsAssignableFrom(type)
            && !typeof(GameMakerReader).IsAssignableFrom(type) && !typeof(GameMakerWriter).IsAssignableFrom(type)
            && type != typeof(DataAccessGate);
    }

    // True for a struct that has a field the cloner would copy: a model object, a framework collection, or another such struct
    private static bool HoldsModel(Type type) => type.IsValueType && !type.IsPrimitive && !type.IsEnum && HoldsCache.GetOrAdd(type, static t =>
    {
        for (var current = t; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var fieldType = field.FieldType;
                var element = fieldType.IsArray ? fieldType.GetElementType()! : fieldType;
                if (element.IsValueType ? HoldsModel(element) : element != typeof(string) && (fieldType.IsArray || IsCopyable(element)))
                {
                    return true;
                }
            }
        }
        return false;
    });

    private static FieldInfo[] FieldsOf(Type type) => FieldCache.GetOrAdd(type, static t =>
    {
        List<FieldInfo> fields = [];
        for (var current = t; current is not null && current != typeof(object); current = current.BaseType)
        {
            fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string) && (!f.FieldType.IsValueType || HoldsModel(f.FieldType))));
        }
        return [.. fields];
    });
}
