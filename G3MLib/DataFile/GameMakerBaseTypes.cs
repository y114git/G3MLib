using G3MLib.DataFile.Models;

namespace G3MLib.DataFile
{
    public interface GameMakerObject
    {
        /// <summary>
        /// Serializes this <see cref="GameMakerObject"/> into a specified <see cref="GameMakerWriter"/>.
        /// </summary>
        /// <param name="writer">Where to serialize to.</param>
        void Serialize(GameMakerWriter writer);

        /// <summary>
        /// Deserializes this <see cref="GameMakerObject"/> from a specified <see cref="GameMakerReader"/>.
        /// </summary>
        /// <param name="reader">Where to deserialize from.</param>
        void Unserialize(GameMakerReader reader);

        /*
         * As for C# 10, it's impossible to inherit static methods from an interface :(
         * (so this method is for inheriting XML commentary only)
         */
        /// <summary>
        /// Deserializes the total child object count of this object from specified <see cref="GameMakerReader"/>.
        /// </summary>
        /// <param name="reader">Where to deserialize from.</param>
        /// <returns>The object count.</returns>
        static uint UnserializeChildObjectCount(GameMakerReader reader) => 0;

    }

    public interface GameMakerObjectWithBlobs
    {
        /// <summary>
        /// Writes data stored before this object's pointer-table entry.
        /// </summary>
        /// <param name="writer">Where to serialize to.</param>
        void SerializeBlobBefore(GameMakerWriter writer);
    }

    public interface PaddedObject
    {
        /// <summary>
        /// Writes trailing padding after this object in a pointer list.
        /// </summary>
        /// <param name="writer">Where to serialize to.</param>
        void SerializePadding(GameMakerWriter writer);

        /// <summary>
        /// Reads trailing padding after this object in a pointer list.
        /// </summary>
        /// <param name="reader">Where to deserialize from.</param>
        void UnserializePadding(GameMakerReader reader);
    }

    public interface PrePaddedObject
    {

        /// <summary>
        /// Writes padding before this object in a pointer list.
        /// </summary>
        /// <param name="writer">Where to serialize to.</param>
        void SerializePrePadding(GameMakerWriter writer);

        /// <summary>
        /// Reads padding before this object in a pointer list.
        /// </summary>
        /// <param name="reader">Where to deserialize from.</param>
        void UnserializePrePadding(GameMakerReader reader);
    }

    public enum ResourceType
    {
        None = -1,
        Object = 0,
        Sprite = 1,
        Sound = 2,
        Room = 3,
        Unknown = 4, // Probably removed
        Path = 5,
        Script = 6,
        Font = 7,
        Timeline = 8,
        Background = 9,
        Shader = 10,

        // GMS2.3+
        Sequence = 11,
        AnimCurve = 12,

        // GM 2023+
        ParticleSystem = 13
    }

    public interface GameMakerResource : GameMakerObject
    {
    }

    public interface GameMakerNamedResource : GameMakerResource
    {
        /// <summary>
        /// Name of the resource.
        /// </summary>
        GameMakerString Name { get; set; }
    }

    public interface ISearchable
    {
        /// <summary>
        /// Returns a value indicating whether a specified substring occurs within this object.
        /// </summary>
        /// <param name="filter">The string to seek. Case insensitive.</param>
        /// <returns><see langword="true"/> if <paramref name="filter"/> occurs within this object, or if
        /// <paramref name="filter"/> is the empty string (""); otherwise, false.</returns>
        bool SearchMatches(string? filter);
    }

    public interface IStaticChildObjCount
    {
        /// <summary>
        /// The total child object count of the current object type.
        /// Used for the object count unserialization.
        /// </summary>
        public static readonly uint ChildObjectCount = 0;
    }
    public interface IStaticChildObjectsSize
    {
        /// <summary>
        /// The summary child objects size of the current object type.
        /// Used for the object count unserialization.
        /// </summary>
        public static readonly uint ChildObjectsSize = 0;
    }
}
