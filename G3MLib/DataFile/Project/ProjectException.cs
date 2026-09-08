using System;
using System.Runtime.Serialization;

namespace G3MLib.DataFile.Project;

[Serializable]
public class ProjectException : Exception
{
    public ProjectException()
    {
    }

    public ProjectException(string message) : base(message)
    {
    }

    public ProjectException(string message, Exception innerException) : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    protected ProjectException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

[Serializable]
public class GameFileBackupException : Exception
{
    public GameFileBackupException()
    {
    }

    public GameFileBackupException(string message) : base(message)
    {
    }

    public GameFileBackupException(string message, Exception innerException) : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    protected GameFileBackupException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}