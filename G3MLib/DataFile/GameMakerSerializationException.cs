using System;
using System.Runtime.Serialization;

namespace G3MLib.DataFile
{
    [Serializable]
    public class GameMakerSerializationException : Exception
    {
        public GameMakerSerializationException()
        {
        }

        public GameMakerSerializationException(string message) : base(message)
        {
        }

        public GameMakerSerializationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        [Obsolete("Formatter-based serialization is obsolete.")]
        protected GameMakerSerializationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}