using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Project.SerializableAssets;

namespace G3MLib.DataFile.Models;

[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerSequence : GameMakerNamedResource, IProjectAsset, INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Possible playback modes for sequences.
    /// </summary>
    public enum PlaybackType : uint
    {
        /// <summary>
        /// Sequence plays one time and stops.
        /// </summary>
        Oneshot = 0,

        /// <summary>
        /// Sequence loops to the beginning.
        /// </summary>
        Loop = 1,

        /// <summary>
        /// Upon reaching either end, the sequence reverses direction.
        /// </summary>
        Pingpong = 2
    }

    /// <inheritdoc/>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Playback mode of the sequence.
    /// </summary>
    public PlaybackType Playback { get; set; }

    /// <summary>
    /// Playback speed of the sequence, in the unit specified by <see cref="PlaybackSpeedType"/>.
    /// </summary>
    public float PlaybackSpeed { get; set; }

    /// <summary>
    /// Unit used for <see cref="PlaybackSpeed"/>.
    /// </summary>
    public AnimSpeedType PlaybackSpeedType { get; set; }

    /// <summary>
    /// Length (duration) of the sequence.
    /// </summary>
    public float Length { get; set; }

    /// <summary>
    /// X origin of the sequence.
    /// </summary>
    public int OriginX { get; set; }

    /// <summary>
    /// Y origin of the sequence.
    /// </summary>
    public int OriginY { get; set; }

    /// <summary>
    /// Volume for audio in the sequence.
    /// </summary>
    public float Volume { get; set; }

    /// <summary>
    /// Width of the sequence, used in GameMaker 2024.13 and above.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Height of the sequence, used in GameMaker 2024.13 and above.
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    /// List of broadcast message keyframes in the sequence.
    /// </summary>
    public GameMakerSimpleList<Keyframe<BroadcastMessage>> BroadcastMessages { get; set; }

    /// <summary>
    /// List of moment keyframes in the sequence.
    /// </summary>
    public GameMakerSimpleList<Keyframe<Moment>> Moments { get; set; }

    /// <summary>
    /// List of root-level tracks in the sequence.
    /// </summary>
    public GameMakerSimpleList<Track> Tracks { get; set; }

    /// <summary>
    /// List of function IDs contained in the sequence.
    /// </summary>
    public GameMakerObservableList<FunctionIDEntry> FunctionIDs { get; set; }

    /// <inheritdoc />
#pragma warning disable CS0067 // Implemented by Fody at build time.
    public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write((uint)Playback);
        writer.Write(PlaybackSpeed);
        writer.Write((uint)PlaybackSpeedType);
        writer.Write(Length);
        writer.Write(OriginX);
        writer.Write(OriginY);
        writer.Write(Volume);
        if (writer.gameMakerData.IsVersionAtLeast(2024, 13))
        {
            writer.Write(Width);
            writer.Write(Height);
        }

        BroadcastMessages.Serialize(writer);

        Tracks.Serialize(writer);

        writer.Write(FunctionIDs.Count);
        foreach (FunctionIDEntry entry in FunctionIDs)
        {
            entry.Serialize(writer);
        }

        Moments.Serialize(writer);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Playback = (PlaybackType)reader.ReadUInt32();
        PlaybackSpeed = reader.ReadSingle();
        PlaybackSpeedType = (AnimSpeedType)reader.ReadUInt32();
        Length = reader.ReadSingle();
        OriginX = reader.ReadInt32();
        OriginY = reader.ReadInt32();
        Volume = reader.ReadSingle();
        if (reader.gameMakerData.IsVersionAtLeast(2024, 13))
        {
            Width = reader.ReadSingle();
            Height = reader.ReadSingle();
        }
        else
        {
            Width = 0;
            Height = 0;
        }

        BroadcastMessages = reader.ReadGameMakerObject<GameMakerSimpleList<Keyframe<BroadcastMessage>>>();

        Tracks = reader.ReadGameMakerObject<GameMakerSimpleList<Track>>();

        int functionIdCount = reader.ReadInt32();
        if (functionIdCount < 0)
            throw new IOException("Negative sequence function ID count.");

        if ((long)functionIdCount * (sizeof(int) + sizeof(uint)) > reader.Length - reader.Position)
            throw new IOException("Sequence function ID data exceeds the available input.");

        FunctionIDs = new GameMakerObservableList<FunctionIDEntry>(functionIdCount);
        for (int i = 0; i < functionIdCount; i++)
        {
            FunctionIDEntry entry = new();
            entry.Unserialize(reader);
            FunctionIDs.InternalAdd(entry);
        }

        Moments = reader.ReadGameMakerObject<GameMakerSimpleList<Keyframe<Moment>>>();
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        reader.Position += reader.gameMakerData.IsVersionAtLeast(2024, 13) ? 40 : 32;

        count += 1 + GameMakerSimpleList<Keyframe<BroadcastMessage>>.UnserializeChildObjectCount(reader);

        count += 1 + GameMakerSimpleList<Track>.UnserializeChildObjectCount(reader);

        int funcIDsCount = reader.ReadInt32();
        if (funcIDsCount < 0)
            throw new IOException("Negative sequence function ID count.");
        if ((long)funcIDsCount * (sizeof(int) + sizeof(uint)) > reader.Length - reader.Position)
            throw new IOException("Sequence function ID data exceeds the available input.");
        reader.Position += (long)funcIDsCount * 8;

        count += 1 + GameMakerSimpleList<Keyframe<Moment>>.UnserializeChildObjectCount(reader);

        return count;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Sequence \"{Name?.Content}\"";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        BroadcastMessages = null;
        Moments = null;
        Tracks = null;
        FunctionIDs = null;
    }

    /// <inheritdoc/>
    internal ISerializableProjectAsset GenerateSerializableProjectAsset(ProjectContext projectContext)
    {
        SerializableSequence serializable = new();
        serializable.PopulateFromData(projectContext, this);
        return serializable;
    }

    /// <inheritdoc/>
    ISerializableProjectAsset IProjectAsset.GenerateSerializableProjectAsset(ProjectContext projectContext)
    {
        SerializableSequence serializable = new();
        serializable.PopulateFromData(projectContext, this);
        return serializable;
    }

    /// <inheritdoc/>
    public string ProjectName => Name?.Content ?? "<unknown name>";

    /// <inheritdoc/>
    public SerializableAssetType ProjectAssetType => SerializableAssetType.Sequence;

    /// <inheritdoc/>
    public bool ProjectExportable => Name?.Content is not null;

    /// <summary>
    /// A keyframe of data stored within a sequence track, at a given time/duration, and for some number of channels.
    /// </summary>
    /// <typeparam name="T">Type of data this keyframe will hold.</typeparam>
    public sealed class Keyframe<T> : GameMakerObject, INotifyPropertyChanged where T : GameMakerObject, new()
    {
        /// <summary>
        /// Start time of the keyframe.
        /// </summary>
        public float Key { get; set; }

        /// <summary>
        /// Duration of the keyframe.
        /// </summary>
        public float Length { get; set; }

        /// <summary>
        /// Whether the keyframe has its serialized stretch flag enabled.
        /// </summary>
        public bool Stretch { get; set; }

        /// <summary>
        /// Whether the keyframe is disabled.
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// Channels of the keyframe, containing actual values.
        /// </summary>
        /// <remarks>
        /// Usually, there's 1 channel for 1D properties (such as image index/speed), and 2 channels for 2D properties (position, scale), etc.
        /// </remarks>
        public GameMakerSimpleList<KeyframeChannel> Channels { get; set; } = new();

        /// <summary>
        /// A channel for a keyframe, containing the channel number and the value for that channel.
        /// </summary>
        public sealed class KeyframeChannel : GameMakerObject, INotifyPropertyChanged
        {
            /// <summary>
            /// Channel ID to use. Generally starts at 0, and increments per each channel.
            /// </summary>
            public int Channel { get; set; }

            /// <summary>
            /// Value of the keyframe for this channel.
            /// </summary>
            public T Value { get; set; } = new();

            /// <inheritdoc />
#pragma warning disable CS0067 // Implemented by Fody at build time.
            public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

            /// <inheritdoc />
            public void Serialize(GameMakerWriter writer)
            {
                writer.Write(Channel);
                Value.Serialize(writer);
            }

            /// <inheritdoc />
            public void Unserialize(GameMakerReader reader)
            {
                Channel = reader.ReadInt32();
                Value.Unserialize(reader);
            }
        }

        /// <inheritdoc />
#pragma warning disable CS0067 // Implemented by Fody at build time.
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(Key);
            writer.Write(Length);
            writer.Write(Stretch);
            writer.Write(Disabled);
            Channels.Serialize(writer);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Key = reader.ReadSingle();
            Length = reader.ReadSingle();
            Stretch = reader.ReadBoolean();
            Disabled = reader.ReadBoolean();
            Channels.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            uint count = 0;

            reader.Position += 16;
            uint chCount = reader.ReadUInt32();

            Type t = typeof(T);
            if (t.IsAssignableTo(typeof(IStaticChildObjectsSize)))
            {
                uint subSize = reader.GetStaticChildObjectsSize(t);
                uint subCount = 0;

                if (t.IsAssignableTo(typeof(IStaticChildObjCount)))
                    subCount = reader.GetStaticChildCount(t);

                reader.Position += (long)chCount * (4 + subSize);

                return chCount * (1 + subCount);
            }

            var unserializeFunc = reader.GetUnserializeCountFunc(t);
            for (uint i = 0; i < chCount; i++)
            {
                reader.Position += 4; // channel ID
                count += 1 + unserializeFunc(reader);
            }

            return count;
        }
    }

    /// <summary>
    /// Broadcast message keyframe data, as used in a sequence.
    /// </summary>
    public sealed class BroadcastMessage : GameMakerObject
    {
        /// <summary>
        /// List of messages being broadcasted by this keyframe.
        /// </summary>
        public GameMakerSimpleListString Messages { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            Messages.Serialize(writer);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Messages = new GameMakerSimpleListString();
            Messages.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            return GameMakerSimpleListString.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// Moment keyframe data, as used in a sequence.
    /// </summary>
    public sealed class Moment : GameMakerObject
    {
        /// <summary>
        /// List of events being triggered by this keyframe.
        /// </summary>
        public GameMakerSimpleListString Events { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            Events.Serialize(writer);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Events = new GameMakerSimpleListString();
            Events.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            return GameMakerSimpleListString.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// A track in a sequence, potentially containing keyframes or sub-tracks.
    /// </summary>
    public sealed class Track : GameMakerObject
    {
        private const string ColourTrackModelName = "GMColourTrack";

        /// <summary>
        /// Some known builtin track names.
        /// </summary>
        public enum TrackBuiltinName
        {
            Gain = 5,
            Pitch = 6,
            Falloff = 7,
            Rotation = 8,
            BlendAdd = 9,
            BlendMultiply = 10,
            Mask = 12,
            Subject = 13,
            Position = 14,
            Scale = 15,
            Origin = 16,
            ImageSpeed = 17,
            ImageIndex = 18,
            ImageAngle = Rotation,
            ImageBlend = BlendMultiply,
            FrameSize = 20,
            CharacterSpacing = 21,
            LineSpacing = 22,
            ParagraphSpacing = 23
        }

        /// <summary>
        /// Some known track traits.
        /// </summary>
        [Flags]
        public enum TrackTraits
        {
            None,
            ChildrenIgnoreOrigin
        }

        /// <summary>
        /// Name for the type/model of track, such as "GMGroupTrack", "GMInstanceTrack", "GMRealTrack", etc.
        /// </summary>
        public GameMakerString ModelName { get; set; }

        /// <summary>
        /// Name of the track. Can be user-assigned or the name of a property or asset.
        /// </summary>
        public GameMakerString Name { get; set; }

        /// <summary>
        /// Builtin name for the track, representing the type of property, or 0 if not applicable.
        /// </summary>
        public TrackBuiltinName BuiltinName { get; set; }

        /// <summary>
        /// Traits for the track.
        /// </summary>
        public TrackTraits Traits { get; set; }

        /// <summary>
        /// Whether the track is a creation track.
        /// </summary>
        public bool IsCreationTrack { get; set; }

        /// <summary>
        /// Tags for the track.
        /// </summary>
        public List<int> Tags { get; set; }

        /// <summary>
        /// List of sub-tracks of this track.
        /// </summary>
        public List<Track> Tracks { get; set; }

        /// <summary>
        /// Keyframe store of this track, or <see langword="null"/> if none is used.
        /// </summary>
        public ITrackKeyframes Keyframes { get; set; }

        /// <summary>
        /// Owned resources of this track (such as animation curves).
        /// </summary>
        public List<GameMakerResource> OwnedResources { get; set; }

        /// <summary>
        /// String used for owned animation curve resources.
        /// </summary>
        private GameMakerString _gmAnimCurveString;

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.WriteGameMakerString(ModelName);
            writer.WriteGameMakerString(Name);
            writer.Write((int)BuiltinName);
            writer.Write((int)Traits);
            writer.Write(IsCreationTrack);

            writer.Write(Tags.Count);
            writer.Write(OwnedResources.Count);
            writer.Write(Tracks.Count);

            foreach (int i in Tags)
                writer.Write(i);

            foreach (GameMakerResource res in OwnedResources)
            {
                if (res is GameMakerAnimationCurve)
                {
                    writer.WriteGameMakerString(_gmAnimCurveString ??= new GameMakerString("GMAnimCurve"));
                    res.Serialize(writer);
                }
                else
                    throw new IOException("Expected an animation curve");
            }

            foreach (Track t in Tracks)
                writer.WriteGameMakerObject(t);

            // Now, handle specific keyframe/etc. data
            switch (ModelName.Content)
            {
                case "GMAudioTrack":
                    writer.WriteGameMakerObject(Keyframes as AudioKeyframes);
                    break;
                case "GMInstanceTrack":
                    writer.WriteGameMakerObject(Keyframes as InstanceKeyframes);
                    break;
                case "GMGraphicTrack":
                    writer.WriteGameMakerObject(Keyframes as GraphicKeyframes);
                    break;
                case "GMSequenceTrack":
                    writer.WriteGameMakerObject(Keyframes as SequenceKeyframes);
                    break;
                case "GMSpriteFramesTrack":
                    writer.WriteGameMakerObject(Keyframes as SpriteFramesKeyframes);
                    break;
                case "GMAssetTrack":
                    throw new NotSupportedException("GMAssetTrack is not supported.");
                case "GMBoolTrack":
                    writer.WriteGameMakerObject(Keyframes as BoolKeyframes);
                    break;
                case "GMStringTrack":
                    writer.WriteGameMakerObject(Keyframes as StringKeyframes);
                    break;
                case "GMColourTrack":
                    writer.WriteGameMakerObject(Keyframes as IntKeyframes);
                    break;
                case "GMRealTrack":
                case "GMAudioEffectTrack":
                    writer.WriteGameMakerObject(Keyframes as RealKeyframes);
                    break;
                case "GMTextTrack":     // Introduced in GM 2022.2
                    writer.WriteGameMakerObject(Keyframes as TextKeyframes);
                    break;
                case "GMParticleTrack": // Introduced in GM 2023.2
                    writer.WriteGameMakerObject(Keyframes as ParticleKeyframes);
                    break;
            }
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            // This reads the string content immediately, if necessary (which it should be)
            GameMakerString ForceReadString()
            {
                GameMakerString res = reader.ReadGameMakerString();
                if (res.Content is not null)
                    return res;

                reader.SwitchReaderType(false);
                long returnTo = reader.Position;
                reader.Position = reader.GetOffsetMapRev()[res];
                reader.ReadGameMakerObject<GameMakerString>();
                reader.Position = returnTo;
                reader.SwitchReaderType(true);

                return res;
            }

            ModelName = ForceReadString();
            Name = reader.ReadGameMakerString();
            BuiltinName = (TrackBuiltinName)reader.ReadInt32();
            Traits = (TrackTraits)reader.ReadInt32();
            IsCreationTrack = reader.ReadBoolean();

            int tagCount = reader.ReadInt32();
            int ownedResCount = reader.ReadInt32();
            int trackCount = reader.ReadInt32();
            if (tagCount < 0 || ownedResCount < 0 || trackCount < 0)
                throw new IOException("Negative sequence track list count.");

            long minimumDataSize = ((long)tagCount * sizeof(int)) +
                                   ((long)ownedResCount * 12) +
                                   ((long)trackCount * 36);
            if (minimumDataSize > reader.Length - reader.Position)
                throw new IOException("Sequence track data exceeds the available input.");

            Tags = new List<int>(tagCount);
            for (int i = 0; i < tagCount; i++)
                Tags.Add(reader.ReadInt32());

            OwnedResources = new List<GameMakerResource>(ownedResCount);
            for (int i = 0; i < ownedResCount; i++)
            {
                _gmAnimCurveString = ForceReadString();
                if (_gmAnimCurveString.Content != "GMAnimCurve")
                    throw new IOException("Expected GMAnimCurve");
                GameMakerAnimationCurve res = new GameMakerAnimationCurve();
                res.Unserialize(reader);
                OwnedResources.Add(res);
            }

            Tracks = new List<Track>(trackCount);
            for (int i = 0; i < trackCount; i++)
                Tracks.Add(reader.ReadGameMakerObject<Track>());

            // Now, handle specific keyframe/etc. data
            switch (ModelName.Content)
            {
                case "GMAudioTrack":
                    Keyframes = reader.ReadGameMakerObject<AudioKeyframes>();
                    break;
                case "GMInstanceTrack":
                    Keyframes = reader.ReadGameMakerObject<InstanceKeyframes>();
                    break;
                case "GMGraphicTrack":
                    Keyframes = reader.ReadGameMakerObject<GraphicKeyframes>();
                    break;
                case "GMSequenceTrack":
                    Keyframes = reader.ReadGameMakerObject<SequenceKeyframes>();
                    break;
                case "GMSpriteFramesTrack":
                    Keyframes = reader.ReadGameMakerObject<SpriteFramesKeyframes>();
                    break;
                case "GMAssetTrack":
                    throw new NotSupportedException("GMAssetTrack is not supported.");
                case "GMBoolTrack":
                    Keyframes = reader.ReadGameMakerObject<BoolKeyframes>();
                    break;
                case "GMStringTrack":
                    Keyframes = reader.ReadGameMakerObject<StringKeyframes>();
                    break;
                case "GMColourTrack":
                    Keyframes = reader.ReadGameMakerObject<IntKeyframes>();
                    break;
                case "GMRealTrack":
                case "GMAudioEffectTrack":
                    Keyframes = reader.ReadGameMakerObject<RealKeyframes>();
                    break;
                case "GMTextTrack":     // Introduced in GM 2022.2
                    if (!reader.gameMakerData.IsVersionAtLeast(2022, 2))
                        reader.gameMakerData.SetGMS2Version(2022, 2);
                    Keyframes = reader.ReadGameMakerObject<TextKeyframes>();
                    break;
                case "GMParticleTrack": // Introduced in GM 2023.2
                    Keyframes = reader.ReadGameMakerObject<ParticleKeyframes>();
                    break;

                // "GMGroupTrack" and "GMClipMaskTrack" have null keyframes
            }
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            uint count = 0;

            string ForceReadString()
            {
                uint strPtr = reader.ReadUInt32();

                reader.SwitchReaderType(false);
                long returnTo = reader.Position;
                reader.Position = strPtr - 4;
                string res = reader.ReadGMString();
                reader.Position = returnTo;
                reader.SwitchReaderType(true);

                return res;
            }

            string modelName = ForceReadString();
            reader.Position += 16;

            int tagCount = reader.ReadInt32();
            int ownedResCount = reader.ReadInt32();
            int trackCount = reader.ReadInt32();
            if (tagCount < 0 || ownedResCount < 0 || trackCount < 0)
                throw new IOException("Negative sequence track list count.");

            long minimumDataSize = ((long)tagCount * sizeof(int)) +
                                   ((long)ownedResCount * 12) +
                                   ((long)trackCount * 36);
            if (minimumDataSize > reader.Length - reader.Position)
                throw new IOException("Sequence track data exceeds the available input.");

            reader.Position += (long)tagCount * 4; // "Tags"

            for (int i = 0; i < ownedResCount; i++)
            {
                reader.Position += 4; // "GMAnimCurveString"
                count += GameMakerAnimationCurve.UnserializeChildObjectCount(reader);
            }

            // "Tracks"
            for (int i = 0; i < trackCount; i++)
                count += 1 + UnserializeChildObjectCount(reader);

            switch (modelName)
            {
                case "GMAudioTrack":
                    count += 1 + AudioKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMInstanceTrack":
                    count += 1 + InstanceKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMGraphicTrack":
                    count += 1 + GraphicKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMSequenceTrack":
                    count += 1 + SequenceKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMSpriteFramesTrack":
                    count += 1 + SpriteFramesKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMAssetTrack":
                    throw new NotSupportedException("GMAssetTrack is not supported.");
                case "GMBoolTrack":
                    count += 1 + BoolKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMStringTrack":
                    count += 1 + StringKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMColourTrack":
                    count += 1 + IntKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMRealTrack":
                case "GMAudioEffectTrack":
                    count += 1 + RealKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMTextTrack":     // Introduced in GM 2022.2
                    count += 1 + TextKeyframes.UnserializeChildObjectCount(reader);
                    break;
                case "GMParticleTrack": // Introduced in GM 2023.2
                    count += 1 + ParticleKeyframes.UnserializeChildObjectCount(reader);
                    break;

                // "GMGroupTrack" and "GMClipMaskTrack" have null keyframes
            }

            return count;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (BuiltinName != 0)
            {
                if (ModelName?.Content == "GMColourTrack")
                    return $"Sequence sub-track - \"Color\" (mode \"{BuiltinName}\")";
                else
                    return $"Sequence sub-track - \"{BuiltinName}\"";
            }
            else
                return $"Sequence track \"{Name?.Content}\"";
        }
    }

    /// <summary>
    /// Base interface used for track keyframes.
    /// </summary>
    public interface ITrackKeyframes : GameMakerObject
    {
    }

    /// <summary>
    /// Base implementation of track keyframes, containing a simple list of keyframes.
    /// </summary>
    /// <typeparam name="T">Type used for keyframe data.</typeparam>
    public abstract class TrackKeyframes<T> : ITrackKeyframes where T : GameMakerObject, new()
    {
        /// <summary>
        /// List of keyframes in the track.
        /// </summary>
        public GameMakerSimpleList<Keyframe<T>> List { get; set; }

        /// <inheritdoc />
        public virtual void Serialize(GameMakerWriter writer)
        {
            writer.Align(4);

            List.Serialize(writer);
        }

        /// <inheritdoc />
        public virtual void Unserialize(GameMakerReader reader)
        {
            reader.Align(4);

            List = new GameMakerSimpleList<Keyframe<T>>();
            List.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            while (reader.AbsPosition % 4 != 0)
                reader.Position++;

            return GameMakerSimpleList<Keyframe<T>>.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// Keyframe data implementation for a resource ID reference.
    /// </summary>
    /// <typeparam name="T">Type of the resource being referenced.</typeparam>
    /// <typeparam name="ChunkT">Type of the chunk for the resource being referenced.</typeparam>
    public abstract class ResourceData<T, ChunkT> : GameMakerObject where T : GameMakerResource, new() where ChunkT : GameMakerListChunk<T>
    {
        /// <summary>
        /// Resource ID reference.
        /// </summary>
        public GameMakerResourceById<T, ChunkT> Resource { get; set; }

        /// <inheritdoc />
        public virtual void Serialize(GameMakerWriter writer)
        {
            Resource.Serialize(writer);
        }

        /// <inheritdoc />
        public virtual void Unserialize(GameMakerReader reader)
        {
            Resource = new();
            Resource.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            reader.Position += 4;
            return 0;
        }
    }

    /// <summary>
    /// Keyframe data implementation for a simple 32-bit integer.
    /// </summary>
    public abstract class SimpleIntData : IStaticChildObjectsSize, GameMakerObject
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 4;

        /// <summary>
        /// Value of the 32-bit integer.
        /// </summary>
        public int Value { get; set; }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(Value);
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            Value = reader.ReadInt32();
        }
    }

    /// <summary>
    /// Keyframe store for audio keyframes.
    /// </summary>
    public sealed class AudioKeyframes : TrackKeyframes<AudioKeyframes.Data>
    {
        /// <summary>
        /// Audio keyframe data, referencing sound assets.
        /// </summary>
        public sealed class Data : ResourceData<GameMakerSound, GameMakerChunkSOND>
        {
            public enum SoundMode : int
            {
                PlayOnce = 0,
                Looping = 1
            }

            /// <summary>
            /// The mode of the audio keyframe.
            /// </summary>
            public SoundMode Mode { get; set; }

            /// <inheritdoc />
            public override void Serialize(GameMakerWriter writer)
            {
                base.Serialize(writer);
                writer.Write(0);
                writer.Write((int)Mode);
            }

            /// <inheritdoc />
            public override void Unserialize(GameMakerReader reader)
            {
                base.Unserialize(reader);
                if (reader.ReadUInt32() != 0)
                    throw new IOException("Expected 0 in Audio keyframe");
                Mode = (SoundMode)reader.ReadInt32();
            }

            /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
            public new static uint UnserializeChildObjectCount(GameMakerReader reader)
            {
                reader.Position += 12;
                return 0;
            }
        }
    }

    /// <summary>
    /// Keyframe store for instance keyframes.
    /// </summary>
    public sealed class InstanceKeyframes : TrackKeyframes<InstanceKeyframes.Data>
    {
        /// <summary>
        /// Instance keyframe data, referencing game object assets.
        /// </summary>
        public sealed class Data : ResourceData<GameMakerGameObject, GameMakerChunkOBJT> { }
    }

    /// <summary>
    /// Keyframe store for graphic keyframes.
    /// </summary>
    public sealed class GraphicKeyframes : TrackKeyframes<GraphicKeyframes.Data>
    {
        /// <summary>
        /// Graphic keyframe data, referencing sprite assets.
        /// </summary>
        public sealed class Data : ResourceData<GameMakerSprite, GameMakerChunkSPRT> { }
    }

    /// <summary>
    /// Keyframe store for sequence keyframes (sub-sequences).
    /// </summary>
    public sealed class SequenceKeyframes : TrackKeyframes<SequenceKeyframes.Data>
    {
        /// <summary>
        /// Sequence keyframe data, referencing sequence assets.
        /// </summary>
        public sealed class Data : ResourceData<GameMakerSequence, GameMakerChunkSEQN> { }
    }

    /// <summary>
    /// Keyframe store for sprite frame keyframes.
    /// </summary>
    public sealed class SpriteFramesKeyframes : TrackKeyframes<SpriteFramesKeyframes.Data>
    {
        /// <summary>
        /// Sprite frame keyframe data, which are simple frame indices.
        /// </summary>
        public sealed class Data : SimpleIntData { }
    }

    /// <summary>
    /// Keyframe store for boolean keyframes.
    /// </summary>
    public sealed class BoolKeyframes : TrackKeyframes<BoolKeyframes.Data>
    {
        /// <summary>
        /// Boolean keyframe data, which are simple integers.
        /// </summary>
        public sealed class Data : SimpleIntData { }
    }

    /// <summary>
    /// Keyframe store for string keyframes.
    /// </summary>
    public sealed class StringKeyframes : TrackKeyframes<StringKeyframes.Data>
    {
        /// <summary>
        /// String keyframe data, which are just string references.
        /// </summary>
        public sealed class Data : GameMakerObject, IStaticChildObjectsSize
        {
            /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
            public static readonly uint ChildObjectsSize = 4;

            /// <summary>
            /// String reference for the keyframe.
            /// </summary>
            public GameMakerString Value { get; set; }

            /// <inheritdoc />
            public void Serialize(GameMakerWriter writer)
            {
                writer.WriteGameMakerString(Value);
            }

            /// <inheritdoc />
            public void Unserialize(GameMakerReader reader)
            {
                Value = reader.ReadGameMakerString();
            }
        }
    }

    /// <summary>
    /// Base implementation for keyframe data that can use animation curves.
    /// </summary>
    public abstract class CurveData : GameMakerObject
    {
        /// <summary>
        /// Whether the referenced curve (if applicable) is an embeddded curve, using <see cref="EmbeddedAnimCurve"/>.
        /// </summary>
        public bool IsCurveEmbedded { get; set; }

        /// <summary>
        /// If <see cref="IsCurveEmbedded"/> is <see langword="true"/>, this represents the embedded animation curve.
        /// </summary>
        public GameMakerAnimationCurve EmbeddedAnimCurve { get; set; }

        /// <summary>
        /// If <see cref="IsCurveEmbedded"/> is <see langword="false"/>, this is a reference to an external animation curve asset.
        /// </summary>
        public GameMakerResourceById<GameMakerAnimationCurve, GameMakerChunkACRV> AssetAnimCurve { get; set; }

        /// <inheritdoc />
        public virtual void Serialize(GameMakerWriter writer)
        {
            writer.Write(IsCurveEmbedded);
            if (IsCurveEmbedded)
            {
                writer.Write(-1);
                EmbeddedAnimCurve.Serialize(writer, false);
            }
            else
            {
                AssetAnimCurve.Serialize(writer);
            }
        }

        /// <inheritdoc />
        public virtual void Unserialize(GameMakerReader reader)
        {
            if (reader.ReadBoolean())
            {
                // The curve data is embedded in this sequence, right here
                IsCurveEmbedded = true;
                AssetAnimCurve = null;
                if (reader.ReadInt32() != -1)
                    throw new IOException("Expected -1");
                EmbeddedAnimCurve = new GameMakerAnimationCurve();
                EmbeddedAnimCurve.Unserialize(reader, false);
            }
            else
            {
                // The curve data is an asset in the project
                IsCurveEmbedded = false;
                EmbeddedAnimCurve = null;
                AssetAnimCurve = new GameMakerResourceById<GameMakerAnimationCurve, GameMakerChunkACRV>();
                AssetAnimCurve.Unserialize(reader);
            }
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            uint count = 0;

            // "IsCurveEmbedded"
            if (reader.ReadBoolean())
            {
                reader.Position += 4;

                count += GameMakerAnimationCurve.UnserializeChildObjectCount(reader, false);
            }
            else
                reader.Position += 4;

            return count;
        }
    }

    /// <summary>
    /// Keyframe data for a 32-bit integer that can be associated with an animation curve.
    /// </summary>
    public sealed class IntData : CurveData
    {
        /// <summary>
        /// 32-bit integer value.
        /// </summary>
        public int Value { get; set; }

        /// <inheritdoc />
        public override void Serialize(GameMakerWriter writer)
        {
            writer.Write(Value);
            base.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Unserialize(GameMakerReader reader)
        {
            Value = reader.ReadInt32();
            base.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public new static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            reader.Position += 4; // "Value"

            return CurveData.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// Keyframe store for integer keyframes that can be associated with an animation curve.
    /// </summary>
    public sealed class IntKeyframes : TrackKeyframes<IntData>
    {
        /// <summary>
        /// Interpolation mode; 0 is for no interpolation, 1 is for linear interpolation.
        /// </summary>
        public int Interpolation { get; set; }

        /// <inheritdoc />
        public override void Serialize(GameMakerWriter writer)
        {
            writer.Align(4);

            writer.Write(Interpolation);

            List.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Unserialize(GameMakerReader reader)
        {
            reader.Align(4);

            Interpolation = reader.ReadInt32();

            List = new GameMakerSimpleList<Keyframe<IntData>>();
            List.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public new static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            while (reader.AbsPosition % 4 != 0)
                reader.Position++;

            reader.Position += 4; // "Interpolation"

            return GameMakerSimpleList<Keyframe<IntData>>.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// Keyframe data for a 32-bit float that can be associated with an animation curve.
    /// </summary>
    public sealed class RealData : CurveData
    {
        /// <summary>
        /// 32-bit float value.
        /// </summary>
        public float Value { get; set; }

        /// <inheritdoc />
        public override void Serialize(GameMakerWriter writer)
        {
            writer.Write(Value);
            base.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Unserialize(GameMakerReader reader)
        {
            Value = reader.ReadSingle();
            base.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public new static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            reader.Position += 4; // "Value"

            return CurveData.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// Keyframe store for float keyframes that can be associated with an animation curve.
    /// </summary>
    public sealed class RealKeyframes : TrackKeyframes<RealData>
    {
        /// <summary>
        /// Interpolation mode; 0 is for no interpolation, 1 is for linear interpolation.
        /// </summary>
        public int Interpolation { get; set; }

        /// <inheritdoc />
        public override void Serialize(GameMakerWriter writer)
        {
            writer.Align(4);

            writer.Write(Interpolation);

            List.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Unserialize(GameMakerReader reader)
        {
            reader.Align(4);

            Interpolation = reader.ReadInt32();

            List = new GameMakerSimpleList<Keyframe<RealData>>();
            List.Unserialize(reader);
        }

        /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
        public new static uint UnserializeChildObjectCount(GameMakerReader reader)
        {
            while (reader.AbsPosition % 4 != 0)
                reader.Position++;

            reader.Position += 4; // "Interpolation"

            return GameMakerSimpleList<Keyframe<RealData>>.UnserializeChildObjectCount(reader);
        }
    }

    /// <summary>
    /// Keyframe store for text keyframes.
    /// </summary>
    public sealed class TextKeyframes : TrackKeyframes<TextKeyframes.Data>
    {
        public enum WrapMode : int
        {
            Default,
            SplitWords
        }

        public enum Origin : int
        {
            TopLeft,
            TopCenter,
            TopRight,
            MiddleLeft,
            MiddleCenter,
            MiddleRight,
            BottomLeft,
            BottomCenter,
            BottomRight,
            Custom
        }

        /// <summary>
        /// Text keyframe data, containing various text display properties.
        /// </summary>
        public sealed class Data : GameMakerObject
        {
            // Backing alignment field, containing vertical and horizontal components
            private int _alignment;

            /// <summary>
            /// Text to be displayed.
            /// </summary>
            public GameMakerString Text { get; set; }

            /// <summary>
            /// Whether line wrapping is enabled.
            /// </summary>
            public bool Wrap { get; set; }

            /// <summary>
            /// Vertical alignment.
            /// </summary>
            public int AlignmentV
            {
                get => (_alignment >> 8) & 0xff;
                set => _alignment = (_alignment & 0xff) | (value & 0xff) << 8;
            }

            /// <summary>
            /// Horizontal alignment.
            /// </summary>
            public int AlignmentH
            {
                get => _alignment & 0xff;
                set => _alignment = (_alignment & ~0xff) | (value & 0xff);
            }

            /// <summary>
            /// Font asset index.
            /// </summary>
            public int FontIndex { get; set; }

            public WrapMode WrapMode { get; set; }

            public Origin Origin { get; set; }

            /// <inheritdoc />
            public void Serialize(GameMakerWriter writer)
            {
                writer.WriteGameMakerString(Text);
                writer.Write(Wrap);
                writer.Write(_alignment);
                writer.Write(FontIndex);
                if (writer.gameMakerData.IsVersionAtLeast(2024, 14))
                {
                    writer.Write((int)WrapMode);
                    writer.Write((int)Origin);
                }
            }

            /// <inheritdoc />
            public void Unserialize(GameMakerReader reader)
            {
                Text = reader.ReadGameMakerString();
                Wrap = reader.ReadBoolean();
                _alignment = reader.ReadInt32();
                FontIndex = reader.ReadInt32();
                if (reader.gameMakerData.IsVersionAtLeast(2024, 14))
                {
                    WrapMode = (WrapMode)reader.ReadInt32();
                    Origin = (Origin)reader.ReadInt32();
                }
                else
                {
                    WrapMode = default;
                    Origin = default;
                }
            }

            /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
            public static uint UnserializeChildObjectCount(GameMakerReader reader)
            {
                reader.Position += 16;

                if (reader.gameMakerData.IsVersionAtLeast(2024, 14))
                {
                    reader.Position += 8; // WrapMode, Origin
                }

                return 0;
            }
        }
    }

    /// <summary>
    /// Keyframe store for particle system keyframes.
    /// </summary>
    public sealed class ParticleKeyframes : TrackKeyframes<ParticleKeyframes.Data>
    {
        /// <summary>
        /// Particle system keyframe data, referencing particle system assets.
        /// </summary>
        public sealed class Data : ResourceData<GameMakerParticleSystem, GameMakerChunkPSYS> { }
    }

    /// <summary>
    /// A function ID entry as used in <see cref="FunctionIDs"/>.
    /// </summary>
    public sealed class FunctionIDEntry : GameMakerObject
    {
        /// <summary>
        /// ID of the function entry.
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// Name of the function.
        /// </summary>
        public GameMakerString FunctionName { get; set; }

        /// <inheritdoc/>
        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(ID);
            writer.WriteGameMakerString(FunctionName);
        }

        /// <inheritdoc/>
        public void Unserialize(GameMakerReader reader)
        {
            ID = reader.ReadInt32();
            FunctionName = reader.ReadGameMakerString();
        }
    }
}
