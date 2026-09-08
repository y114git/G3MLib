using System;
using System.Collections.ObjectModel;

namespace G3MLib.DataFile.Models;

/// <summary>
/// A timeline in a data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerTimeline : GameMakerNamedResource, IDisposable
{
    /// <summary>
    /// A specific moment in a timeline.
    /// </summary>
    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class GameMakerTimelineMoment : GameMakerObject, IDisposable
    {
        /// <summary>
        /// After how many steps this moment gets executed.
        /// </summary>
        public uint Step { get; set; }

        /// <summary>
        /// The actions that get executed at this moment.
        /// </summary>
        public GameMakerPointerList<GameMakerGameObject.EventAction> Event { get; set; }

        /// <summary>
        /// Initializes a new empty instance of the <see cref="GameMakerTimelineMoment"/> class.
        /// </summary>
        public GameMakerTimelineMoment()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GameMakerTimelineMoment"/> with the specified step time and event action list.
        /// </summary>
        /// <param name="step">After how many steps the moment shall be executed.</param>
        /// <param name="events">A list of events that shall be executed.</param>
        public GameMakerTimelineMoment(uint step, GameMakerPointerList<GameMakerGameObject.EventAction> events)
        {
            Step = step;
            Event = events;
        }

        /// <inheritdoc />
        public void Serialize(GameMakerWriter writer)
        {
            // Since GM:S stores Steps first, and then Events, we can't serialize a single entry in a single function :(
        }

        /// <inheritdoc />
        public void Unserialize(GameMakerReader reader)
        {
            // Same goes for unserializing.
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Event = null;
        }
    }

    /// <summary>
    /// The name of the timeline.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The moments this timeline has. Comparable to keyframes.
    /// </summary>
    public ObservableCollection<GameMakerTimelineMoment> Moments { get; set; } = new ObservableCollection<GameMakerTimelineMoment>();

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content + " (" + GetType().Name + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var moment in Moments)
            moment?.Dispose();
        Name = null;
        Moments = new();
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);

        writer.Write(Moments.Count);
        for (int i = 0; i < Moments.Count; i++)
        {
            // Write the time point
            writer.Write(Moments[i].Step);

            // Unnecessary pointer to next array
            writer.WriteGameMakerObjectPointer(Moments[i].Event);
        }

        for (int i = 0; i < Moments.Count; i++)
        {
            // Write the actions for this moment
            writer.WriteGameMakerObject(Moments[i].Event);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Moments.Clear();

        int momentCount = reader.ReadInt32();
        if (momentCount < 0)
            throw new GameMakerSerializationException("Negative timeline moment count.");

        long momentDataSize = (long)momentCount * (sizeof(uint) + sizeof(int));
        if (momentDataSize > reader.Length - reader.Position)
            throw new GameMakerSerializationException("Timeline moment data exceeds the available input.");

        // Read the time points for each moment
        uint[] timePoints = new uint[momentCount];
        int[] unnecessaryPointers = new int[momentCount];
        for (int i = 0; i < momentCount; i++)
        {
            timePoints[i] = reader.ReadUInt32();
            unnecessaryPointers[i] = reader.ReadInt32();
        }

        // Read the actions for each moment
        for (int i = 0; i < momentCount; i++)
        {
            if (reader.AbsPosition != unnecessaryPointers[i])
                throw new GameMakerSerializationException("Invalid action list pointer");

            // Read action list and assign time point (put into list)
            var timeEvent = reader.ReadGameMakerObject<GameMakerPointerList<GameMakerGameObject.EventAction>>();
            Moments.Add(new GameMakerTimelineMoment(timePoints[i], timeEvent));
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        reader.Position += 4; // "Name"

        int momentCount = reader.ReadInt32();
        if (momentCount < 0)
            throw new GameMakerSerializationException("Negative timeline moment count.");

        long momentDataSize = (long)momentCount * (sizeof(uint) + sizeof(int));
        if (momentDataSize > reader.Length - reader.Position)
            throw new GameMakerSerializationException("Timeline moment data exceeds the available input.");

        reader.Position += (long)momentCount * 8;

        for (int i = 0; i < momentCount; i++)
            count += 1 + GameMakerPointerList<GameMakerGameObject.EventAction>.UnserializeChildObjectCount(reader);

        return count;
    }
}