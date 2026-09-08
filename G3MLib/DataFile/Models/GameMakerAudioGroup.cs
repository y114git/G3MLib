using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// Audio group entry in a data file.
/// </summary>
/// <remarks>Audio Groups allow you to manage a set sound entries easier.
/// You can use these for memory management, volume control and more. <br/>
/// Audio Groups are only available to use in the regular audio system</remarks>
/// <seealso cref="GameMakerSound.AudioEntryFlags.Regular"/>
/// <seealso cref="GameMakerSound"/>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerAudioGroup : GameMakerNamedResource, IStaticChildObjectsSize, IDisposable
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 4;

    /// <summary>
    /// The name of the audio group.
    /// </summary>
    /// <remarks>This is how the audio group is referenced from code.</remarks>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Relative path (from the main data file) to the audio group file, in GameMaker 2024.14 and above.
    /// </summary>
    /// <remarks>
    /// Prior to 2024.14, audio groups were all numerically assigned filenames and all in the root directory.
    /// </remarks>
    public GameMakerString Path { get; set; } = null;

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        if (writer.gameMakerData.IsVersionAtLeast(2024, 14))
        {
            writer.WriteGameMakerString(Path);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        if (reader.gameMakerData.IsVersionAtLeast(2024, 14))
        {
            Path = reader.ReadGameMakerString();
        }
        else
        {
            Path = null;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name?.Content} ({GetType().Name})";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        Path = null;
    }
}