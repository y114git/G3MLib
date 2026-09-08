using System;
using G3MLib.Analyzer;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A variable entry in a GameMaker data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerVariable : GameMakerNamedResource, ISearchable, GameMakerInstruction.IReferencedObject, IDisposable, IGMVariable
{
    /// The name of the Variable.
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The type of the variable.
    /// </summary>
    public GameMakerInstruction.InstanceType InstanceType { get; set; }

    /// <summary>
    /// The variable identifier stored in the VARI table.
    /// </summary>
    public int VarID { get; set; }

    /// <inheritdoc />
    public uint Occurrences { get; set; }

    /// <inheritdoc />
    public GameMakerInstruction FirstAddress { get; set; }

    /// <inheritdoc />
    public int NameStringID { get; set; }

    /// <summary>
    /// OBSOLETE. This variable is now located at <see cref="NameStringID"/>.
    /// </summary>
    public int UnknownChainEndingValue { get => NameStringID; set => NameStringID = value; }


    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        if (writer.gameMakerData.GeneralInfo?.BytecodeVersion >= 15)
        {
            writer.Write((int)InstanceType);
            writer.Write(VarID);
        }
        writer.Write(Occurrences);
        if (Occurrences > 0)
            writer.Write(writer.GetAddressForGameMakerObject(FirstAddress));
        else
            writer.Write(-1);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        if (reader.gameMakerData.GeneralInfo?.BytecodeVersion >= 15)
        {
            InstanceType = (GameMakerInstruction.InstanceType)reader.ReadInt32();
            VarID = reader.ReadInt32();
        }
        else
        {
            InstanceType = GameMakerInstruction.InstanceType.Undefined;
            VarID = 0;
        }
        Occurrences = reader.ReadUInt32();
        if (Occurrences > 0)
        {
            FirstAddress = reader.ReadGameMakerObjectPointer<GameMakerInstruction>();
            GameMakerInstruction.ParseReferenceChain(reader, this);
        }
        else
        {
            if (reader.ReadInt32() != -1)
                throw new GameMakerSerializationException("Variable with no occurrences, but still has a first occurrence address");
            FirstAddress = null;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content != null ? Name.Content : "<NULL_VAR_NAME>";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        FirstAddress = null;
    }

    /// <inheritdoc />
    public bool SearchMatches(string? filter)
    {
        return Name?.SearchMatches(filter) ?? false;
    }

    // G3MLib.Analyzer implementations
    IGMString IGMVariable.Name => Name;
    IGMInstruction.InstanceType IGMVariable.InstanceType => (IGMInstruction.InstanceType)InstanceType;
    int IGMVariable.VariableID => VarID;
}