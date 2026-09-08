using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// List of feature flag entries in a GameMaker data file, version 2022.8 and above.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerFeatureFlags : GameMakerObject, IDisposable
{
    /// <summary>
    /// The list of feature flags.
    /// </summary>
    public GameMakerSimpleListString List { get; set; }


    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        List.Serialize(writer);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        List = new GameMakerSimpleListString();
        List.Unserialize(reader);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        return GameMakerSimpleListString.UnserializeChildObjectCount(reader);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        List = null;
    }
}
