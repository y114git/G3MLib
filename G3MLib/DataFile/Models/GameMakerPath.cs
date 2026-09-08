using System;
using System.ComponentModel;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Project.SerializableAssets;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A Path entry in a GameMaker data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerPath : GameMakerNamedResource, IProjectAsset, INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// The name of <see cref="GameMakerPath"/>.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Whether the Path is smooth between points, or is completely straight.
    /// </summary>
    public bool IsSmooth { get; set; }

    /// <summary>
    /// Whether this Path is closed, aka if the last <see cref="PathPoint"/> connecting back to the first.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// A number from 1 to 8 detailing how smooth a <see cref="GameMakerPath"/> is between <see cref="PathPoint"/>,
    /// with 1 being completely straight and 8 being the smoothest. <br/>
    /// Only used if <see cref="IsSmooth"/> is enabled.
    /// </summary>
    public uint Precision { get; set; } = 4;

    /// <summary>
    /// The collection of <see cref="PathPoint"/>s this <see cref="GameMakerPath"/> has.
    /// </summary>
    public GameMakerSimpleList<PathPoint> Points { get; set; } = new GameMakerSimpleList<PathPoint>();

    /// <inheritdoc />
#pragma warning disable CS0067 // Implemented by Fody at build time.
    public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

    /// <summary>
    /// A point in a <see cref="GameMakerPath"/>.
    /// </summary>
    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class PathPoint : GameMakerObject, INotifyPropertyChanged, IStaticChildObjectsSize
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 12;

        /// <summary>
        /// The X position of the <see cref="PathPoint"/>.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// The Y position of the <see cref="PathPoint"/>.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// A percentage of how fast an <see cref="GameMakerGameObject"/> moves until it hits the next <see cref="PathPoint"/>. <br/>.
        /// </summary>
        public float Speed { get; set; } = 1f;

        /// <inheritdoc />
#pragma warning disable CS0067 // Implemented by Fody at build time.
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Speed);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            Speed = reader.ReadSingle();
        }
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write(IsSmooth);
        writer.Write(IsClosed);
        writer.Write(Precision);
        writer.WriteGameMakerObject(Points);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        IsSmooth = reader.ReadBoolean();
        IsClosed = reader.ReadBoolean();
        Precision = reader.ReadUInt32();
        Points = reader.ReadGameMakerObject<GameMakerSimpleList<PathPoint>>();
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 16;

        return 1 + GameMakerSimpleList<PathPoint>.UnserializeChildObjectCount(reader);
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
        Points = new();
    }

    /// <inheritdoc/>
    ISerializableProjectAsset IProjectAsset.GenerateSerializableProjectAsset(ProjectContext projectContext)
    {
        SerializablePath serializable = new();
        serializable.PopulateFromData(projectContext, this);
        return serializable;
    }

    /// <inheritdoc/>
    public string ProjectName => Name?.Content ?? "<unknown name>";

    /// <inheritdoc/>
    public SerializableAssetType ProjectAssetType => SerializableAssetType.Path;

    /// <inheritdoc/>
    public bool ProjectExportable => Name?.Content is not null;
}