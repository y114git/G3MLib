using System.Collections.ObjectModel;
using System.IO;

namespace G3MLib.DataFile;

/// <summary>A list whose <see cref="Version"/> changes with every change to its contents.</summary>
public interface IVersionedList
{
    /// <summary>Increases with every change to the contents of the list.</summary>
    int Version { get; }
}

/// <summary>
/// Basic observable list type, usable for data models to be presented in graphical interfaces.
/// </summary>
public class GameMakerObservableList<T> : ObservableCollection<T>, IVersionedList
{
    private readonly List<T> internalList;

    /// <summary>
    /// Creates a new observable list, which is empty and has default capacity.
    /// </summary>
    public GameMakerObservableList()
    {
        internalList = (List<T>)Items;
    }

    /// <summary>
    /// Creates a new observable list, which is empty and has the specified initial capacity.
    /// </summary>
    public GameMakerObservableList(int capacity) : this()
    {
        // Set capacity directly
        internalList.Capacity = capacity;
    }

    /// <summary>
    /// Creates a new observable list from the contents of the provided enumerable.
    /// </summary>
    public GameMakerObservableList(IEnumerable<T> enumerable) : this()
    {
        // Add range of elements directly to internal list
        internalList.AddRange(enumerable);
    }

    /// <summary>
    /// Sets the capacity of the internal list of this collection.
    /// </summary>
    public void SetCapacity(int capacity)
    {
        internalList.Capacity = capacity;
    }

    /// <summary>
    /// Sets the capacity of the internal list of this collection.
    /// </summary>
    public void SetCapacity(uint capacity) => SetCapacity(checked((int)capacity));

    /// <summary>
    /// Adds an item to the internal list of this collection (that is, without notifying any observers).
    /// </summary>
    /// <param name="item">Item to add.</param>
    public void InternalAdd(T item)
    {
        Version++;
        internalList.Add(item);
    }

    /// <summary>
    /// Increases with every change to the contents of this list, including <see cref="InternalAdd"/>.
    /// Lets an index built from the list detect that it is stale.
    /// </summary>
    public int Version { get; private set; }

    protected override void InsertItem(int index, T item)
    {
        Version++;
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, T item)
    {
        Version++;
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        Version++;
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        Version++;
        base.ClearItems();
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        Version++;
        base.MoveItem(oldIndex, newIndex);
    }

    /// <inheritdoc cref="List{T}.FindIndex(Predicate{T})"/>
    public int FindIndex(Predicate<T> match)
    {
        return internalList.FindIndex(match);
    }
}

internal static class GameMakerListChildCount
{
    public static uint Read<T>(GameMakerReader reader, uint count, bool allowsResourceReferences) where T : GameMakerObject, new()
    {
        if (count == 0)
        {
            return 0;
        }

        var type = typeof(T);
        if (allowsResourceReferences && type.IsAssignableTo(typeof(GameMakerResourceRef)))
        {
            reader.Position += (long)count * 4;
            return count;
        }

        if (type.IsAssignableTo(typeof(IStaticChildObjectsSize)))
        {
            var subSize = reader.GetStaticChildObjectsSize(type);
            var subCount = type.IsAssignableTo(typeof(IStaticChildObjCount)) ? reader.GetStaticChildCount(type) : 0;
            reader.Position += (long)count * subSize;
            return checked(count + checked(count * subCount));
        }

        var unserialize = reader.GetUnserializeCountFunc(type);
        uint totalCount = 0;
        uint index = 0;
        try
        {
            for (; index < count; index++)
            {
                totalCount = checked(totalCount + 1 + unserialize(reader));
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading child object count of item {index + 1} of {count} in a list of {type.FullName}", e);
        }

        return totalCount;
    }
}

/// <summary>
/// Simple list object, serialized as a 32-bit count followed immediately by all objects in the list.
/// </summary>
public class GameMakerSimpleList<T> : GameMakerObservableList<T>, GameMakerObject where T : GameMakerObject, new()
{
    /// <summary>
    /// Creates a new simple list, which is empty and has default capacity.
    /// </summary>
    public GameMakerSimpleList()
    {
    }

    /// <summary>
    /// Creates a new simple list, which is empty and has the specified initial capacity.
    /// </summary>
    public GameMakerSimpleList(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Creates a new simple list from the contents of the provided enumerable.
    /// </summary>
    public GameMakerSimpleList(IEnumerable<T> enumerable) : base(enumerable)
    {
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        // Write list count
        var count = Count;
        writer.Write((uint)count);

        // Write actual objects
        var i = 0;
        try
        {
            for (; i < count; i++)
            {
                writer.WriteGameMakerObject(this[i]);
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile writing item {i + 1} of {count} in a list of {typeof(T).FullName}", e);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Read count and set list with that capacity
        var count = reader.ReadUInt32();
        var t = typeof(T);
        if (count > reader.Length - reader.Position ||
            t.IsAssignableTo(typeof(IStaticChildObjectsSize)) &&
            (long)count * reader.GetStaticChildObjectsSize(t) > reader.Length - reader.Position)
            throw new IOException("Simple list data exceeds the available input.");
        Clear();
        SetCapacity(count);

        // Read actual objects
        uint i = 0;
        try
        {
            for (; i < count; i++)
            {
                InternalAdd(reader.ReadGameMakerObject<T>());
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading item {i + 1} of {count} in a list of {typeof(T).FullName}", e);
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return GameMakerListChildCount.Read<T>(reader, reader.ReadUInt32(), true);
    }
}

/// <summary>
/// Simple list object, specifically for strings, serialized as a count followed immediately by all strings in the list.
/// </summary>
public class GameMakerSimpleListString : GameMakerObservableList<GameMakerString>, GameMakerObject
{
    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        // Write count
        var count = Count;
        writer.Write((uint)count);

        // Write all of the strings
        var i = 0;
        try
        {
            for (; i < count; i++)
            {
                writer.WriteGameMakerString(this[i]);
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile writing item {i + 1} of {count} in a string-list", e);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Read count and set list with that capacity
        var count = reader.ReadUInt32();
        if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
            throw new IOException("String list pointers exceed the available input.");
        Clear();
        SetCapacity(count);

        // Read all of the strings
        uint i = 0;
        try
        {
            for (; i < count; i++)
            {
                InternalAdd(reader.ReadGameMakerString());
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading item {i + 1} of {count} in a string-list", e);
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var count = reader.ReadUInt32();
        if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
            throw new IOException("String list pointers exceed the available input.");

        reader.Position += (long)count * 4;
        return 0;
    }
}

/// <summary>
/// Simple list object, serialized as a 16-bit count followed immediately by all objects in the list.
/// </summary>
public class GameMakerSimpleListShort<T> : GameMakerObservableList<T>, GameMakerObject where T : GameMakerObject, new()
{
    /// <summary>
    /// Creates a new (short) simple list, which is empty and has default capacity.
    /// </summary>
    public GameMakerSimpleListShort()
    {
    }

    /// <summary>
    /// Creates a new (short) simple list, which is empty and has the specified initial capacity.
    /// </summary>
    public GameMakerSimpleListShort(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Creates a new (short) simple list from the contents of the provided enumerable.
    /// </summary>
    public GameMakerSimpleListShort(IEnumerable<T> enumerable) : base(enumerable)
    {
    }

    private void EnsureShortCount()
    {
        if (Count > short.MaxValue)
            throw new InvalidOperationException("Count of short SimpleList exceeds maximum number allowed.");
    }

    /// <inheritdoc cref="Collection{T}.Add(T)"/>
    public new void Add(T item)
    {
        base.Add(item);
        EnsureShortCount();
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        // Write list count
        var count = Count;
        writer.Write((ushort)count);

        // Write actual objects
        var i = 0;
        try
        {
            for (; i < count; i++)
            {
                writer.WriteGameMakerObject(this[i]);
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile writing item {i + 1} of {count} in a list of {typeof(T).FullName}", e);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Read count and set list with that capacity
        var count = reader.ReadUInt16();
        var t = typeof(T);
        if (t.IsAssignableTo(typeof(IStaticChildObjectsSize)) &&
            (long)count * reader.GetStaticChildObjectsSize(t) > reader.Length - reader.Position)
            throw new IOException("Short simple list data exceeds the available input.");
        Clear();
        SetCapacity(count);

        // Read actual objects
        ushort i = 0;
        try
        {
            for (; i < count; i++)
            {
                InternalAdd(reader.ReadGameMakerObject<T>());
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading item {i + 1} of {count} in a list of {typeof(T).FullName}", e);
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return GameMakerListChildCount.Read<T>(reader, reader.ReadUInt16(), false);
    }
}

/// <summary>
/// List of objects that is preceded by an array of pointers, pointing to each object in order.
/// </summary>
public class GameMakerPointerList<T> : GameMakerObservableList<T>, GameMakerObject where T : GameMakerObject, new()
{
    /// <summary>
    /// Creates a new pointer list, which is empty and has default capacity.
    /// </summary>
    public GameMakerPointerList()
    {
    }

    /// <summary>
    /// Creates a new pointer list, which is empty and has the specified initial capacity.
    /// </summary>
    public GameMakerPointerList(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Creates a new pointer list from the contents of the provided enumerable.
    /// </summary>
    public GameMakerPointerList(IEnumerable<T> enumerable) : base(enumerable)
    {
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        // Write count
        var count = Count;
        writer.Write((uint)count);

        // Write all pointers
        foreach (var obj in this)
        {
            writer.WriteGameMakerObjectPointer(obj);
        }

        // Write blobs, if necessary for the given type
        var t = typeof(T);
        if (t.IsAssignableTo(typeof(GameMakerObjectWithBlobs)))
        {
            var i = 0;
            try
            {
                for (; i < count; i++)
                {
                    if (this[i] is GameMakerObjectWithBlobs obj)
                        obj.SerializeBlobBefore(writer);
                }
            }
            catch (GameMakerSerializationException e)
            {
                throw new GameMakerSerializationException($"{e.Message}\nwhile writing blob for item {i + 1} of {count} in a list of {t.FullName}", e);
            }
        }

        // Write actual objects
        var j = 0;
        try
        {
            for (; j < count; j++)
            {
                writer.CancellationToken.ThrowIfCancellationRequested();
                var obj = this[j];

                // Don't need to write anything if the object is null
                if (obj is null)
                {
                    continue;
                }

                // Serialize pre-padding, if this is a type that requires it
                if (t.IsAssignableTo(typeof(PrePaddedObject)))
                {
                    ((PrePaddedObject)obj).SerializePrePadding(writer);
                }

                // Write object
                writer.WriteGameMakerObject(obj);

                // Serialize padding, if this is a type that requires it
                if (j != count - 1 && t.IsAssignableTo(typeof(PaddedObject)))
                {
                    // The last object does not get padding.
                    ((PaddedObject)obj).SerializePadding(writer);
                }
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile writing item {j + 1} of {count} in a list of {t.FullName}", e);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Read count and set list with that capacity
        var count = reader.ReadUInt32();
        var realCount = count;
        if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
            throw new IOException("Pointer list data exceeds the available input.");
        Clear();
        SetCapacity(count);

        // Read in all object pointers
        var t = typeof(T);
        uint i = 0;
        try
        {
            for (; i < count; i++)
            {
                var obj = reader.ReadGameMakerObjectPointer<T>();
                if (obj is null)
                {
                    // Null pointer slots identify 2024.11+ GMS2 data.
                    if (reader.gameMakerData.IsGameMaker2() && !reader.gameMakerData.IsVersionAtLeast(2024, 11))
                        reader.gameMakerData.SetGMS2Version(2024, 11);
                    realCount--;
                }
                InternalAdd(obj);
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading pointer to item {i + 1} of {count} in a list of {t.FullName}", e);
        }

        // Advance to start of first object (particularly, if blobs exist)
        if (realCount > 0)
        {
            var firstItem = this.First(obj => obj is not null);
            var pos = reader.GetAddressForGameMakerObject(firstItem);
            if (reader.AbsPosition != pos)
            {
                var skip = pos - reader.AbsPosition;
                if (skip > 0)
                {
                    reader.AbsPosition += skip;
                }
                else
                {
                    throw new IOException("First list item starts inside pointer list");
                }
            }
        }

        // Read in actual objects
        uint j = 0;
        try
        {
            for (; j < count; j++)
            {
                reader.CancellationToken.ThrowIfCancellationRequested();
                var obj = this[(int)j];
                if (obj is null)
                {
                    continue;
                }

                // Unserialize pre-padding, if this is a type that requires it
                if (t.IsAssignableTo(typeof(PrePaddedObject)))
                {
                    ((PrePaddedObject)obj).UnserializePrePadding(reader);
                }

                // Read object
                reader.ReadGameMakerObject(obj);

                // Unserialize padding, if this is a type that requires it
                if (j != count - 1 && t.IsAssignableTo(typeof(PaddedObject)))
                {
                    // The last object does not get padding.
                    ((PaddedObject)obj).UnserializePadding(reader);
                }
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading item {j + 1} of {count} in a list of {t.FullName}", e);
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        // Read base object count; short-circuit if there's no objects
        var count = reader.ReadUInt32();
        if (count == 0)
        {
            return 0;
        }
        if ((ulong)count * sizeof(uint) > (ulong)(reader.Length - reader.Position))
            throw new IOException("Pointer list data exceeds the available input.");

        var t = typeof(T);
        if (t.IsAssignableTo(typeof(IStaticChildObjectsSize)))
        {
            var nonNullCount = count;
            for (uint i = 0; i < count; i++)
            {
                if (reader.ReadUInt32() != 0)
                    continue;

                // Null pointer slots identify 2024.11+ GMS2 data.
                if (reader.gameMakerData.IsGameMaker2())
                {
                    if (!reader.gameMakerData.IsVersionAtLeast(2024, 11))
                    {
                        reader.gameMakerData.SetGMS2Version(2024, 11);
                    }
                }
                else
                {
                    reader.SubmitWarning("Null pointers found in pointer list on file built with pre-GMS2!");
                }

                nonNullCount--;
            }

            if (nonNullCount == 0)
                return 0;

            var subSize = reader.GetStaticChildObjectsSize(t);
            uint subCount = 0;

            if (t.IsAssignableTo(typeof(IStaticChildObjCount)))
            {
                subCount = reader.GetStaticChildCount(t);
            }

            reader.Position += (long)nonNullCount * subSize;

            return checked(nonNullCount + checked(nonNullCount * subCount));
        }

        // Read pointers of all variable-sized objects.
        var pointers = reader.ListPtrsPool.Rent(checked((int)count));
        for (uint i = 0; i < count; i++)
        {
            var pointer = reader.ReadUInt32();
            if (pointer == 0)
            {
                // Null pointer slots identify 2024.11+ GMS2 data.
                if (reader.gameMakerData.IsGameMaker2())
                {
                    if (!reader.gameMakerData.IsVersionAtLeast(2024, 11))
                    {
                        reader.gameMakerData.SetGMS2Version(2024, 11);
                    }
                }
                else
                {
                    reader.SubmitWarning("Null pointers found in pointer list on file built with pre-GMS2!");
                }

                i--;
                count--;
                continue;
            }
            pointers[i] = pointer;
        }

        if (count == 0)
        {
            reader.ListPtrsPool.Return(pointers);
            return 0;
        }

        // Advance to start of first object (particularly, if blobs exist)
        var pos = pointers[0];
        if (reader.AbsPosition != pos)
        {
            var skip = pos - reader.AbsPosition;
            if (skip > 0)
            {
                reader.AbsPosition += skip;
            }
            else
            {
                throw new IOException("First list item starts inside pointer list");
            }
        }

        // Determine object counts recursively for all objects
        var unserializeFunc = reader.GetUnserializeCountFunc(t);
        uint totalCount = 0;
        uint j = 0;
        try
        {
            for (; j < count; j++)
            {
                reader.AbsPosition = pointers[j];
                totalCount = checked(totalCount + 1 + unserializeFunc(reader));
            }
        }
        catch (GameMakerSerializationException e)
        {
            throw new GameMakerSerializationException($"{e.Message}\nwhile reading child object count of item {j + 1} of {count} in a list of {t.FullName}", e);
        }
        finally
        {
            reader.ListPtrsPool.Return(pointers);
        }

        return totalCount;
    }
}

/// <summary>
/// Shorthand for <see cref="GameMakerSimpleList{T}"/> containing <see cref="GameMakerResourceById{T, ChunkT}"/>.
/// </summary>
public class GameMakerSimpleResourcesList<T, ChunkT> : GameMakerSimpleList<GameMakerResourceById<T, ChunkT>> where T : GameMakerResource, new() where ChunkT : GameMakerListChunk<T>
{
}
