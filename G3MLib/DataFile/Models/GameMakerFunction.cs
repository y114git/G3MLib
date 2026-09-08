using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using G3MLib.Analyzer;
using static G3MLib.DataFile.Models.GameMakerGeneralInfo;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A function entry as it's used in a GameMaker data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerFunction : GameMakerNamedResource, GameMakerInstruction.IReferencedObject, IStaticChildObjectsSize, IDisposable, IGMFunction
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 12;

    public FunctionClassification Classification { get; set; }

    /// <summary>
    /// The name of the <see cref="GameMakerFunction"/>.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The index of <see cref="Name"/> in <see cref="GameMakerData.Strings"/>.
    /// </summary>
    public int NameStringID { get; set; }

    /// <summary>
    /// How often this <see cref="GameMakerFunction"/> is referenced in code.
    /// </summary>
    public uint Occurrences { get; set; }

    /// <summary>
    /// The first instruction in this function.
    /// </summary>
    public GameMakerInstruction FirstAddress { get; set; }

    public int UnknownChainEndingValue { get => NameStringID; set => NameStringID = value; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write(Occurrences);
        if (Occurrences > 0)
        {
            uint addr = writer.GetAddressForGameMakerObject(FirstAddress);
            if (writer.gameMakerData.IsVersionAtLeast(2, 3))
                writer.Write((addr == 0) ? 0 : (addr + 4)); // in GMS 2.3, it points to the actual reference rather than the instruction
            else
                writer.Write(addr);
        }
        else
            writer.Write(-1);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Occurrences = reader.ReadUInt32();
        if (Occurrences > 0)
        {
            if (reader.gameMakerData.IsVersionAtLeast(2, 3))
                FirstAddress = reader.GetGameMakerObjectAtAddress<GameMakerInstruction>(reader.ReadUInt32() - 4);
            else
                FirstAddress = reader.ReadGameMakerObjectPointer<GameMakerInstruction>();
            GameMakerInstruction.ParseReferenceChain(reader, this);
        }
        else
        {
            if (reader.ReadInt32() != -1)
                throw new GameMakerSerializationException("Function with no occurrences, but still has a first occurrence address");
            FirstAddress = null;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        FirstAddress = null;
    }

    // G3MLib.Analyzer implementations
    IGMString IGMFunction.Name => Name;
}

/// <summary>
/// Associates a code entry with its local variable metadata.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerCodeLocals : GameMakerNamedResource, IDisposable
{
    /// <summary>
    /// The name of the code local entry which corresponds to a code entry.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// A collection of local variables.
    /// </summary>
    public ObservableCollection<LocalVar> Locals { get; private set; } = new ObservableCollection<LocalVar>();

    /// <summary>
    /// Creates an empty code locals entry with the given name.
    /// </summary>
    /// <param name="data">Data to add the new code to.</param>
    /// <param name="name">Name of the new code locals entry to create.</param>
    /// <returns>The new code locals entry.</returns>
    public static GameMakerCodeLocals CreateEmptyEntry(GameMakerData data, GameMakerString name)
    {
        GameMakerCodeLocals locals = new()
        {
            Name = name
        };
        locals.Locals.Add(new()
        {
            Name = data.Strings.MakeString("arguments", out int argumentsStringId),
            Index = data.IsVersionAtLeast(2, 3) ? (uint)argumentsStringId : 0
        });
        data.CodeLocals.Add(locals);
        return locals;
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((uint)Locals.Count);
        writer.WriteGameMakerString(Name);
        foreach (LocalVar var in Locals)
        {
            writer.WriteGameMakerObject(var);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        uint count = reader.ReadUInt32();
        Name = reader.ReadGameMakerString();
        if ((long)count * LocalVar.ChildObjectsSize > reader.Length - reader.Position)
            throw new IOException("Code local variables exceed the available input.");
        List<LocalVar> newLocals = new();
        for (uint i = 0; i < count; i++)
            newLocals.Add(reader.ReadGameMakerObject<LocalVar>());
        Locals = new(newLocals);
        Util.DebugUtil.Assert(Locals.Count == count);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = reader.ReadUInt32();

        reader.Position += 4 + ((long)count * LocalVar.ChildObjectsSize);

        return count;
    }

    /// <summary>
    /// Returns whether this code local entry contains any local variables matching a name.
    /// </summary>
    /// <param name="varName">The variable name to search for.</param>
    /// <returns><see langword="true"/> if a variable matching the provided name exists, otherwise <see langword="false"/>.</returns>
    public bool HasLocal(string varName)
    {
        return Locals.Any(local => local?.Name?.Content is string name && name == varName);
    }

    /// <summary>
    /// A local variable entry associated with a code entry.
    /// </summary>
    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class LocalVar : GameMakerObject, IStaticChildObjectsSize, IDisposable
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 8;

        /// <summary>
        /// The serialized index associated with the local variable.
        /// </summary>
        public uint Index { get; set; }

        /// <summary>
        /// The name of the local variable.
        /// </summary>
        public GameMakerString Name { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(Index);
            writer.WriteGameMakerString(Name);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Index = reader.ReadUInt32();
            Name = reader.ReadGameMakerString();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Name = null;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content + " (" + GetType().Name + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        Locals.Clear();
    }
}