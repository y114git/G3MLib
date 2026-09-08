using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project.Json;

namespace G3MLib.DataFile.Project.SerializableAssets;

/// <summary>
/// A serializable version of <see cref="GameMakerAnimationCurve"/>.
/// </summary>
internal sealed class SerializableAnimationCurve : ISerializableProjectAsset
{
    /// <inheritdoc/>
    public string DataName { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public SerializableAssetType AssetType => SerializableAssetType.AnimationCurve;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IndividualDirectory => false;

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OverrideOrder { get; set; }

    /// <inheritdoc cref="GameMakerAnimationCurve.GraphType"/>
    public int GraphType { get; set; }

    /// <inheritdoc cref="GameMakerAnimationCurve.Channels"/>
    public List<Channel> Channels { get; set; }

    /// <inheritdoc cref="GameMakerAnimationCurve.Channel"/>
    public sealed class Channel
    {
        /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Name"/>
        public string Name { get; set; }

        /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Curve"/>
        public int Curve { get; set; }

        /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Iterations"/>
        public uint Iterations { get; set; }

        /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Points"/>
        [JsonConverter(typeof(NoPrettyPrintJsonConverter<List<Point>>))]
        public List<Point> Points { get; set; }

        /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point"/>
        public sealed class Point
        {
            /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point.X"/>
            public float X { get; set; }

            /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point.Value"/>
            public float Value { get; set; }

            /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point.BezierX0"/>
            public float BezierX0 { get; set; }

            /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point.BezierY0"/>
            public float BezierY0 { get; set; }

            /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point.BezierX1"/>
            public float BezierX1 { get; set; }

            /// <inheritdoc cref="GameMakerAnimationCurve.Channel.Point.BezierY1"/>
            public float BezierY1 { get; set; }
        }
    }

    // Data asset that was located during pre-import.
    private GameMakerAnimationCurve _dataAsset = null;

    /// <summary>
    /// Populates this serializable animation curve with data from an actual animation curve.
    /// </summary>
    internal void PopulateFromData(ProjectContext projectContext, GameMakerAnimationCurve curve)
    {
        // Update all main properties
        DataName = curve.Name?.Content;
        GraphType = (int)curve.GraphType;
        Channels = new(curve.Channels.Count);
        foreach (var channel in curve.Channels)
        {
            Channels.Add(new Channel()
            {
                Name = channel.Name?.Content,
                Curve = (int)channel.Curve,
                Iterations = channel.Iterations,
                Points = [.. channel.Points.Select((point) => new Channel.Point()
                {
                    X = point.X,
                    Value = point.Value,
                    BezierX0 = point.BezierX0,
                    BezierX1 = point.BezierX1,
                    BezierY0 = point.BezierY0,
                    BezierY1 = point.BezierY1
                })]
            });
        }
    }

    /// <inheritdoc/>
    public void Serialize(ProjectContext projectContext, string destinationFile)
    {
        using FileStream fs = new(destinationFile, FileMode.Create);
        JsonSerializer.Serialize<ISerializableProjectAsset>(fs, this, ProjectContext.JsonOptions);
    }

    /// <inheritdoc/>
    public void PreImport(ProjectContext projectContext)
    {
        if (projectContext.Data.AnimationCurves.ByName(DataName) is GameMakerAnimationCurve existing)
        {
            // Animation curve found
            _dataAsset = existing;
        }
        else
        {
            // No animation curve found; create new one
            _dataAsset = new()
            {
                Name = projectContext.MakeString(DataName)
            };
            projectContext.Data.AnimationCurves.Add(_dataAsset);
        }
    }

    /// <inheritdoc/>
    public IProjectAsset Import(ProjectContext projectContext)
    {
        GameMakerAnimationCurve curve = _dataAsset;

        // Update all main properties
        curve.GraphType = (GameMakerAnimationCurve.GraphTypeEnum)GraphType;
        if (curve.Channels is null)
        {
            curve.Channels = new(Channels?.Count ?? 0);
        }
        else
        {
            curve.Channels.Clear();
            curve.Channels.SetCapacity(Channels?.Count ?? 0);
        }
        foreach (var channel in Channels ?? [])
        {
            curve.Channels.Add(new GameMakerAnimationCurve.Channel()
            {
                Name = projectContext.MakeString(channel.Name),
                Curve = (GameMakerAnimationCurve.Channel.CurveType)channel.Curve,
                Iterations = channel.Iterations,
                Points = [.. (channel.Points ?? []).Select((point) => new GameMakerAnimationCurve.Channel.Point()
                {
                    X = point.X,
                    Value = point.Value,
                    BezierX0 = point.BezierX0,
                    BezierX1 = point.BezierX1,
                    BezierY0 = point.BezierY0,
                    BezierY1 = point.BezierY1
                })]
            });
        }

        return curve;
    }

    /// <summary>
    /// Imports this asset as a sub-resource of another asset.
    /// </summary>
    internal GameMakerAnimationCurve ImportSubResource(ProjectContext projectContext)
    {
        _dataAsset = new GameMakerAnimationCurve();
        if (DataName is not null)
        {
            _dataAsset.Name = projectContext.MakeString(DataName);
        }
        Import(projectContext);
        return _dataAsset;
    }
}
