using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project.Json;

namespace G3MLib.DataFile.Project.SerializableAssets;

/// <summary>
/// A serializable version of <see cref="GameMakerSequence"/>.
/// </summary>
internal sealed class SerializableSequence : ISerializableProjectAsset
{
    /// <inheritdoc cref="GameMakerSequence.PlaybackType"/>
    public enum PlaybackType : uint
    {
        Oneshot = 0,
        Loop = 1,
        Pingpong = 2
    }

    /// <inheritdoc/>
    public string DataName { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public SerializableAssetType AssetType => SerializableAssetType.Sequence;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IndividualDirectory => false;

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OverrideOrder { get; set; }

    /// <inheritdoc cref="GameMakerSequence.Playback"/>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlaybackType Playback { get; set; }

    /// <inheritdoc cref="GameMakerSequence.PlaybackSpeed"/>
    public float PlaybackSpeed { get; set; }

    /// <inheritdoc cref="GameMakerSequence.PlaybackSpeedType"/>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SerializableSprite.ExtraAnimSpeedType PlaybackSpeedType { get; set; }

    /// <inheritdoc cref="GameMakerSequence.Length"/>
    public float Length { get; set; }

    /// <inheritdoc cref="GameMakerSequence.OriginX"/>
    public int OriginX { get; set; }

    /// <inheritdoc cref="GameMakerSequence.OriginY"/>
    public int OriginY { get; set; }

    /// <inheritdoc cref="GameMakerSequence.GameMakerSequence"/>
    public float Volume { get; set; }

    /// <inheritdoc cref="GameMakerSequence.Width"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Width { get; set; }

    /// <inheritdoc cref="GameMakerSequence.Height"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Height { get; set; }

    /// <inheritdoc cref="GameMakerSequence.BroadcastMessages"/>
    public List<Keyframe<List<string>>> BroadcastMessages { get; set; }

    /// <inheritdoc cref="GameMakerSequence.Moments"/>
    public List<Keyframe<List<string>>> Moments { get; set; }

    /// <inheritdoc cref="GameMakerSequence.Tracks"/>
    public List<Track> Tracks { get; set; }

    /// <inheritdoc cref="GameMakerSequence.FunctionIDs"/>
    public List<FunctionIDEntry> FunctionIDs { get; set; }

    /// <summary>Function identifier entry used by a sequence.</summary>
    public sealed class FunctionIDEntry
    {
        public int ID { get; set; }
        public string Function { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.Keyframe{T}"/>
    public sealed class Keyframe<T>
    {
        /// <inheritdoc cref="GameMakerSequence.Keyframe{T}.Key"/>
        public float Key { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Keyframe{T}.Length"/>
        public float Length { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Keyframe{T}.Stretch"/>
        public bool Stretch { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Keyframe{T}.Disabled"/>
        public bool Disabled { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Keyframe{T}.Channels"/>
        public List<KeyframeChannel<T>> Channels { get; set; }
    }

    /// <summary>Value assigned to one sequence keyframe channel.</summary>
    public sealed class KeyframeChannel<T>
    {
        public int Channel { get; set; }
        public T Value { get; set; }
    }

    public sealed class Track
    {
        /// <inheritdoc cref="GameMakerSequence.Track.ModelName"/>
        public string ModelName { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.Name"/>
        public string Name { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.BuiltinName"/>
        public int BuiltinName { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.Traits"/>
        public int Traits { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.IsCreationTrack"/>
        public bool IsCreationTrack { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.Tags"/>
        public List<int> Tags { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.Tracks"/>
        public List<Track> Tracks { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.Keyframes"/>
        [JsonConverter(typeof(NoPrettyPrintJsonConverter<ITrackKeyframeStore>))]
        public ITrackKeyframeStore KeyframeStore { get; set; }

        /// <inheritdoc cref="GameMakerSequence.Track.OwnedResources"/>
        public List<ISerializableProjectAsset> OwnedResources { get; set; }
    }

    // Color tracks use IntKeyframes because GameMaker stores their packed color values as 32-bit integers.
    /// <inheritdoc cref="GameMakerSequence.ITrackKeyframes"/>
    [JsonDerivedType(typeof(AudioKeyframes), "AudioKeyframes")]
    [JsonDerivedType(typeof(InstanceKeyframes), "InstanceKeyframes")]
    [JsonDerivedType(typeof(GraphicKeyframes), "GraphicKeyframes")]
    [JsonDerivedType(typeof(SequenceKeyframes), "SequenceKeyframes")]
    [JsonDerivedType(typeof(SpriteFramesKeyframes), "SpriteFramesKeyframes")]
    [JsonDerivedType(typeof(BoolKeyframes), "BoolKeyframes")]
    [JsonDerivedType(typeof(StringKeyframes), "StringKeyframes")]
    [JsonDerivedType(typeof(IntKeyframes), "IntKeyframes")]
    [JsonDerivedType(typeof(RealKeyframes), "RealKeyframes")]
    [JsonDerivedType(typeof(TextKeyframes), "TextKeyframes")]
    [JsonDerivedType(typeof(ParticleKeyframes), "ParticleKeyframes")]
    public interface ITrackKeyframeStore
    {
    }

    /// <inheritdoc cref="GameMakerSequence.AudioKeyframes"/>
    public sealed class AudioKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<AudioKeyframe>> Keyframes { get; set; }

        public sealed class AudioKeyframe
        {
            public string SoundAsset { get; set; }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public GameMakerSequence.AudioKeyframes.Data.SoundMode Mode { get; set; }
        }
    }

    /// <inheritdoc cref="GameMakerSequence.InstanceKeyframes"/>
    public sealed class InstanceKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<string>> Keyframes { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.GraphicKeyframes"/>
    public sealed class GraphicKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<string>> Keyframes { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.SequenceKeyframes"/>
    public sealed class SequenceKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<string>> Keyframes { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.SpriteFramesKeyframes"/>
    public sealed class SpriteFramesKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<int>> Keyframes { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.BoolKeyframes"/>
    public sealed class BoolKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<int>> Keyframes { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.StringKeyframes"/>
    public sealed class StringKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<string>> Keyframes { get; set; }
    }

    /// <inheritdoc cref="GameMakerSequence.IntKeyframes"/>
    public sealed class IntKeyframes : ITrackKeyframeStore
    {
        public int Interpolation { get; set; }
        public List<Keyframe<IntKeyframe>> Keyframes { get; set; }

        public sealed class IntKeyframe
        {
            public SerializableAnimationCurve AnimCurveEmbedded { get; set; }
            public string AnimCurveAsset { get; set; }
            public int Value { get; set; }
        }
    }

    /// <inheritdoc cref="GameMakerSequence.RealKeyframes"/>
    public sealed class RealKeyframes : ITrackKeyframeStore
    {
        public int Interpolation { get; set; }
        public List<Keyframe<RealKeyframe>> Keyframes { get; set; }

        public sealed class RealKeyframe
        {
            public SerializableAnimationCurve AnimCurveEmbedded { get; set; }
            public string AnimCurveAsset { get; set; }
            public float Value { get; set; }
        }
    }

    /// <inheritdoc cref="GameMakerSequence.TextKeyframes"/>
    public sealed class TextKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<TextKeyframe>> Keyframes { get; set; }

        public sealed class TextKeyframe
        {
            public string Text { get; set; }
            public bool Wrap { get; set; }
            public int AlignmentV { get; set; }
            public int AlignmentH { get; set; }
            public string FontAsset { get; set; }
        }
    }

    /// <inheritdoc cref="GameMakerSequence.ParticleKeyframes"/>
    public sealed class ParticleKeyframes : ITrackKeyframeStore
    {
        public List<Keyframe<string>> Keyframes { get; set; }
    }

    // Data asset that was located during pre-import.
    private GameMakerSequence _dataAsset = null;

    /// <summary>
    /// Populates this serializable sequence with data from an actual sequence.
    /// </summary>
    internal void PopulateFromData(ProjectContext projectContext, GameMakerSequence seq)
    {
        // Update all main properties
        DataName = seq.Name.Content;
        Playback = (PlaybackType)seq.Playback;
        PlaybackSpeed = seq.PlaybackSpeed;
        PlaybackSpeedType = (SerializableSprite.ExtraAnimSpeedType)seq.PlaybackSpeedType;
        Length = seq.Length;
        OriginX = seq.OriginX;
        OriginY = seq.OriginY;
        Volume = seq.Volume;
        Width = seq.Width;
        Height = seq.Height;

        // Update broadcast messages
        BroadcastMessages = [.. seq.BroadcastMessages.Select((keyframe) => new Keyframe<List<string>>()
        {
            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<List<string>>()
            {
                Channel = kvp.Channel,
                Value = [.. kvp.Value.Messages.Select((message) => message?.Content)]
            })]
        })];

        // Update moments
        Moments = [.. seq.Moments.Select((keyframe) => new Keyframe<List<string>>()
        {
            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<List<string>>()
            {
                Channel = kvp.Channel,
                Value = kvp.Value.Events.Select(e => e?.Content).ToList()
            })]
        })];

        // Update tracks
        Func<GameMakerSequence.Track, Track> convertTrack = null;
        convertTrack = (track) => new Track()
        {
            ModelName = track.ModelName?.Content,
            Name = track.Name?.Content,
            BuiltinName = (int)track.BuiltinName,
            Traits = (int)track.Traits,
            IsCreationTrack = track.IsCreationTrack,
            Tags = [.. track.Tags],
            Tracks = [.. track.Tracks.Select(convertTrack)],
            KeyframeStore = track.Keyframes switch
            {
                GameMakerSequence.TrackKeyframes<GameMakerSequence.AudioKeyframes.Data> keyframeStore =>
                    new AudioKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<AudioKeyframes.AudioKeyframe>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<AudioKeyframes.AudioKeyframe>()
                            {
                                Channel = kvp.Channel,
                                Value = new AudioKeyframes.AudioKeyframe()
                                {
                                    Mode = kvp.Value.Mode,
                                    SoundAsset = kvp.Value.Resource?.Resource?.Name?.Content
                                }
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.InstanceKeyframes.Data> keyframeStore =>
                    new InstanceKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<string>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<string>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Resource?.Resource?.Name?.Content
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.GraphicKeyframes.Data> keyframeStore =>
                    new GraphicKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<string>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<string>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Resource?.Resource?.Name?.Content
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.SequenceKeyframes.Data> keyframeStore =>
                    new SequenceKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<string>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<string>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Resource?.Resource?.Name?.Content
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.SpriteFramesKeyframes.Data> keyframeStore =>
                    new SpriteFramesKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<int>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<int>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Value
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.BoolKeyframes.Data> keyframeStore =>
                    new BoolKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<int>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<int>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Value
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.StringKeyframes.Data> keyframeStore =>
                    new StringKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<string>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<string>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Value?.Content
                            })]
                        })]
                    },
                GameMakerSequence.IntKeyframes keyframeStore =>
                    new IntKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<IntKeyframes.IntKeyframe>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<IntKeyframes.IntKeyframe>()
                            {
                                Channel = kvp.Channel,
                                Value = new IntKeyframes.IntKeyframe()
                                {
                                    Value = kvp.Value.Value,
                                    AnimCurveAsset = kvp.Value.AssetAnimCurve?.Resource?.Name?.Content,
                                    AnimCurveEmbedded = (SerializableAnimationCurve)kvp.Value.EmbeddedAnimCurve?.GenerateSerializableProjectAsset(projectContext)
                                }
                            })]
                        })],
                        Interpolation = keyframeStore.Interpolation
                    },
                GameMakerSequence.RealKeyframes keyframeStore =>
                    new RealKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<RealKeyframes.RealKeyframe>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<RealKeyframes.RealKeyframe>()
                            {
                                Channel = kvp.Channel,
                                Value = new RealKeyframes.RealKeyframe()
                                {
                                    Value = kvp.Value.Value,
                                    AnimCurveAsset = kvp.Value.AssetAnimCurve?.Resource?.Name?.Content,
                                    AnimCurveEmbedded = (SerializableAnimationCurve)kvp.Value.EmbeddedAnimCurve?.GenerateSerializableProjectAsset(projectContext)
                                }
                            })]
                        })],
                        Interpolation = keyframeStore.Interpolation
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.TextKeyframes.Data> keyframeStore =>
                    new TextKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<TextKeyframes.TextKeyframe>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<TextKeyframes.TextKeyframe>()
                            {
                                Channel = kvp.Channel,
                                Value = new TextKeyframes.TextKeyframe()
                                {
                                    Text = kvp.Value.Text?.Content,
                                    Wrap = kvp.Value.Wrap,
                                    AlignmentV = kvp.Value.AlignmentV,
                                    AlignmentH = kvp.Value.AlignmentH,
                                    FontAsset =
                                        (kvp.Value.FontIndex >= 0 && kvp.Value.FontIndex < projectContext.Data.Fonts.Count) ?
                                        projectContext.Data.Fonts[kvp.Value.FontIndex]?.Name?.Content :
                                        null
                                }
                            })]
                        })]
                    },
                GameMakerSequence.TrackKeyframes<GameMakerSequence.ParticleKeyframes.Data> keyframeStore =>
                    new ParticleKeyframes()
                    {
                        Keyframes = [.. keyframeStore.List.Select((keyframe) => new Keyframe<string>()
                        {
                            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                            Channels = [.. keyframe.Channels.Select((kvp) => new KeyframeChannel<string>()
                            {
                                Channel = kvp.Channel,
                                Value = kvp.Value.Resource?.Resource?.Name?.Content
                            })]
                        })]
                    },
                null => null,
                _ => throw new ProjectException($"Unknown keyframe store type {track.Keyframes.GetType().Name}")
            },
            OwnedResources = [.. track.OwnedResources.Select((resource) => resource switch
            {
                GameMakerAnimationCurve curve => (SerializableAnimationCurve)curve?.GenerateSerializableProjectAsset(projectContext),
                _ => throw new NotSupportedException($"Unknown owned resource type {resource.GetType().Name}")
            })]
        };
        Tracks = [.. seq.Tracks.Select(convertTrack)];

        // Update function IDs
        FunctionIDs = [.. seq.FunctionIDs.Select((entry) => new FunctionIDEntry()
        {
            ID = entry.ID,
            Function = entry.FunctionName?.Content
        })];
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
        if (projectContext.Data.Sequences.ByName(DataName) is GameMakerSequence existing)
        {
            // Sequence found
            _dataAsset = existing;
        }
        else
        {
            // No sequence found; create new one
            _dataAsset = new()
            {
                Name = projectContext.MakeString(DataName)
            };
            projectContext.Data.Sequences.Add(_dataAsset);
        }
    }

    /// <inheritdoc/>
    public IProjectAsset Import(ProjectContext projectContext)
    {
        GameMakerSequence seq = _dataAsset;

        // Update all main properties
        seq.Playback = (GameMakerSequence.PlaybackType)Playback;
        seq.PlaybackSpeed = PlaybackSpeed;
        seq.PlaybackSpeedType = (AnimSpeedType)PlaybackSpeedType;
        seq.Length = Length;
        seq.OriginX = OriginX;
        seq.OriginY = OriginY;
        seq.Volume = Volume;
        seq.Width = Width;
        seq.Height = Height;

        // Update broadcast messages
        seq.BroadcastMessages = [.. (BroadcastMessages ?? []).Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.BroadcastMessage>()
        {
            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
            Channels = [..
                keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.BroadcastMessage>.KeyframeChannel()
                {
                    Channel = channel.Channel,
                    Value = new GameMakerSequence.BroadcastMessage()
                    {
                        Messages = [.. channel.Value.Select((message) => projectContext.MakeString(message))],
                    }
                })]
        })];

        // Update moments
        seq.Moments = [.. (Moments ?? []).Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.Moment>()
        {
            Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
            Channels = [..
                keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.Moment>.KeyframeChannel()
                {
                    Channel = channel.Channel,
                    Value = new GameMakerSequence.Moment()
                    {
                        Events = [.. channel.Value.Select(ev => projectContext.MakeString(ev)) ]
                    }
                })]
        })];

        // Update tracks
        Func<Track, GameMakerSequence.Track> convertTrack = null;
        convertTrack = (track) => new GameMakerSequence.Track()
        {
            ModelName = projectContext.MakeString(track.ModelName),
            Name = projectContext.MakeString(track.Name),
            BuiltinName = (GameMakerSequence.Track.TrackBuiltinName)track.BuiltinName,
            Traits = (GameMakerSequence.Track.TrackTraits)track.Traits,
            IsCreationTrack = track.IsCreationTrack,
            Tags = [.. track.Tags],
            Tracks = [.. track.Tracks.Select(convertTrack)],
            Keyframes = track.KeyframeStore switch
            {
                AudioKeyframes keyframes => new GameMakerSequence.AudioKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.AudioKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.AudioKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.AudioKeyframes.Data()
                                {
                                    Mode = channel.Value.Mode,
                                    Resource = new(projectContext.FindSound(channel.Value.SoundAsset, this))
                                }
                            })]
                    })]
                },
                InstanceKeyframes keyframes => new GameMakerSequence.InstanceKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.InstanceKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.InstanceKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.InstanceKeyframes.Data()
                                {
                                    Resource = new(projectContext.FindGameObject(channel.Value, this))
                                }
                            })]
                    })]
                },
                GraphicKeyframes keyframes => new GameMakerSequence.GraphicKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.GraphicKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.GraphicKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.GraphicKeyframes.Data()
                                {
                                    Resource = new(projectContext.FindSprite(channel.Value, this))
                                }
                            })]
                    })]
                },
                SequenceKeyframes keyframes => new GameMakerSequence.SequenceKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.SequenceKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.SequenceKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.SequenceKeyframes.Data()
                                {
                                    Resource = new(projectContext.FindSequence(channel.Value, this))
                                }
                            })]
                    })]
                },
                SpriteFramesKeyframes keyframes => new GameMakerSequence.SpriteFramesKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.SpriteFramesKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.SpriteFramesKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.SpriteFramesKeyframes.Data()
                                {
                                    Value = channel.Value
                                }
                            })]
                    })]
                },
                BoolKeyframes keyframes => new GameMakerSequence.BoolKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.BoolKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.BoolKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.BoolKeyframes.Data()
                                {
                                    Value = channel.Value
                                }
                            })]
                    })]
                },
                StringKeyframes keyframes => new GameMakerSequence.StringKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.StringKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.StringKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.StringKeyframes.Data()
                                {
                                    Value = projectContext.MakeString(channel.Value)
                                }
                        })]
                    })]
                },
                IntKeyframes keyframes => new GameMakerSequence.IntKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.IntData>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.IntData>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.IntData()
                                {
                                    Value = channel.Value.Value,
                                    IsCurveEmbedded = channel.Value.AnimCurveEmbedded is not null,
                                    EmbeddedAnimCurve = channel.Value.AnimCurveEmbedded?.ImportSubResource(projectContext),
                                    AssetAnimCurve = new(projectContext.FindAnimationCurve(channel.Value.AnimCurveAsset, this))
                                }
                            })]
                    })],
                    Interpolation = keyframes.Interpolation
                },
                RealKeyframes keyframes => new GameMakerSequence.RealKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.RealData>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.RealData>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.RealData()
                                {
                                    Value = channel.Value.Value,
                                    IsCurveEmbedded = channel.Value.AnimCurveEmbedded is not null,
                                    EmbeddedAnimCurve = channel.Value.AnimCurveEmbedded?.ImportSubResource(projectContext),
                                    AssetAnimCurve = new(projectContext.FindAnimationCurve(channel.Value.AnimCurveAsset, this))
                                }
                            })]
                    })],
                    Interpolation = keyframes.Interpolation
                },
                TextKeyframes keyframes => new GameMakerSequence.TextKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.TextKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.TextKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.TextKeyframes.Data()
                                {
                                    Text = projectContext.MakeString(channel.Value.Text),
                                    Wrap = channel.Value.Wrap,
                                    AlignmentV = channel.Value.AlignmentV,
                                    AlignmentH = channel.Value.AlignmentH,
                                    FontIndex = projectContext.FindFontIndex(channel.Value.FontAsset, this)
                                }
                            })]
                    })]
                },
                ParticleKeyframes keyframes => new GameMakerSequence.ParticleKeyframes()
                {
                    List = [.. keyframes.Keyframes.Select((keyframe) => new GameMakerSequence.Keyframe<GameMakerSequence.ParticleKeyframes.Data>()
                    {
                        Key = keyframe.Key, Length = keyframe.Length, Stretch = keyframe.Stretch, Disabled = keyframe.Disabled,
                        Channels = [..
                            keyframe.Channels.Select((channel) => new GameMakerSequence.Keyframe<GameMakerSequence.ParticleKeyframes.Data>.KeyframeChannel()
                            {
                                Channel = channel.Channel,
                                Value = new GameMakerSequence.ParticleKeyframes.Data()
                                {
                                    Resource = new(projectContext.FindParticleSystem(channel.Value, this))
                                }
                            })]
                    })]
                },
                null => null,
                _ => throw new ProjectException($"Unknown keyframe store type {track.KeyframeStore.GetType().Name}")
            },
            OwnedResources = [.. track.OwnedResources.Select((resource) => resource switch
            {
                SerializableAnimationCurve curve => curve.ImportSubResource(projectContext),
                _ => throw new ProjectException($"Unknown owned resource type {resource.GetType().Name}")
            })]
        };
        seq.Tracks = [.. (Tracks ?? []).Select(convertTrack)];

        // Update function IDs
        seq.FunctionIDs = [.. (FunctionIDs ?? []).Select((entry) => new GameMakerSequence.FunctionIDEntry()
            {
                ID = entry.ID,
                FunctionName = projectContext.MakeString(entry.Function)
            })];

        return seq;
    }

    /// <summary>
    /// Imports this asset as a sub-resource of another asset.
    /// </summary>
    internal GameMakerSequence ImportSubResource(ProjectContext projectContext)
    {
        _dataAsset = new GameMakerSequence();
        if (DataName is not null)
        {
            _dataAsset.Name = projectContext.MakeString(DataName);
        }
        Import(projectContext);
        return _dataAsset;
    }
}
