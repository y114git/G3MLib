using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A tag entry in a GameMaker data file. Tags are a GameMaker: Studio 2.3+ feature.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerTags : GameMakerObject, IDisposable
{
    public GameMakerSimpleListString Tags { get; set; }
    public Dictionary<int, GameMakerSimpleListString> AssetTags { get; set; }

    public static int GetAssetTagID(GameMakerData data, GameMakerNamedResource resource)
    {
        ResourceType type = resource switch
        {
            GameMakerGameObject => ResourceType.Object,
            GameMakerSprite => ResourceType.Sprite,
            GameMakerSound => ResourceType.Sound,
            GameMakerRoom => ResourceType.Room,
            GameMakerPath => ResourceType.Path,
            GameMakerScript => ResourceType.Script,
            GameMakerFont => ResourceType.Font,
            GameMakerTimeline => ResourceType.Timeline,
            GameMakerBackground => ResourceType.Background,
            GameMakerShader => ResourceType.Shader,
            GameMakerSequence => ResourceType.Sequence,
            GameMakerAnimationCurve => ResourceType.AnimCurve,
            GameMakerParticleSystem => ResourceType.ParticleSystem,
            _ => throw new ArgumentException("Invalid resource type!")
        };
        IList list = data[resource.GetType()];

        int offset = resource is GameMakerScript ? 100000 : 0;

        return ((int)type << 24) | ((list.IndexOf(resource) + offset) & 0xFFFFFF);
    }


    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        Tags.Serialize(writer);
        GameMakerPointerList<TempAssetTags> temp = new GameMakerPointerList<TempAssetTags>();
        foreach (var kvp in AssetTags)
            temp.Add(new TempAssetTags() { ID = kvp.Key, Tags = kvp.Value });
        temp.Serialize(writer);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Tags = new GameMakerSimpleListString();
        Tags.Unserialize(reader);
        GameMakerPointerList<TempAssetTags> temp = reader.ReadGameMakerObject<GameMakerPointerList<TempAssetTags>>();
        AssetTags = new Dictionary<int, GameMakerSimpleListString>();
        foreach (TempAssetTags t in temp.Where(t => t is not null))
            AssetTags[t.ID] = t.Tags;
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        count += GameMakerSimpleListString.UnserializeChildObjectCount(reader);
        count += 1 + GameMakerPointerList<TempAssetTags>.UnserializeChildObjectCount(reader);

        return count;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        AssetTags = null;
        Tags = null;
    }

    private sealed class TempAssetTags : GameMakerObject, IDisposable
    {
        public int ID { get; set; }
        public GameMakerSimpleListString Tags { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(ID);
            Tags.Serialize(writer);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            ID = reader.ReadInt32();
            Tags = reader.ReadGameMakerObject<GameMakerSimpleListString>();
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            reader.Position += 4;
            return 1 + GameMakerSimpleListString.UnserializeChildObjectCount(reader);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Tags = null;
        }
    }
}