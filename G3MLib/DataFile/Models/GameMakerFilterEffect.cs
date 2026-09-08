using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A filter effect as it's used in a GameMaker data file. These are GameMaker: Studio 2.3.6+ only.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerFilterEffect : GameMakerNamedResource, IStaticChildObjectsSize, IDisposable
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 8;

    /// <summary>
    /// The name of the <see cref="GameMakerFilterEffect"/>.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Runner-defined filter-effect value associated with <see cref="Name"/>.
    /// </summary>
    public GameMakerString Value { get; set; }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.WriteGameMakerString(Value);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Value = reader.ReadGameMakerString();
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
        Value = null;
    }
}