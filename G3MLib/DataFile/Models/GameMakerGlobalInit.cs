using System;
using System.ComponentModel;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A global initialization entry in a data file.
/// </summary>
/// <remarks></remarks>
// This chunk has not been observed in GM:S 1.4 data files.
public class GameMakerGlobalInit : GameMakerObject, INotifyPropertyChanged, IDisposable
{
    private GameMakerResourceById<GameMakerCode, GameMakerChunkCODE> _code = new();

    /// <summary>
    /// The <see cref="GameMakerCode"/> object which contains the code.
    /// </summary>
    /// <remarks>This code is executed at a global scope, before the first room of the game executes.</remarks>
    public GameMakerCode Code { get => _code.Resource; set { _code.Resource = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code))); } }

    /// <inheritdoc />
    public event PropertyChangedEventHandler PropertyChanged;

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        _code.Serialize(writer);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        _code = new GameMakerResourceById<GameMakerCode, GameMakerChunkCODE>();
        _code.Unserialize(reader);   // Cannot use ReadGameMakerObject, as that messes things up.
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _code.Dispose();
    }
}
