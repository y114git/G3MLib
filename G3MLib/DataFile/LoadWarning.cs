namespace G3MLib.DataFile;

/// <summary>
/// A warning raised while a data file was read.
/// </summary>
/// <param name="Chunk">Name of the chunk that was being read, or an empty string when the warning does not belong to a chunk.</param>
/// <param name="Message">What the reader skipped, guessed or could not model.</param>
/// <param name="IsImportant">Whether the warning is deemed important (that is, may risk data loss when re-saving).</param>
/// <param name="Offset">Absolute offset in the file at which the reader was when it raised the warning.</param>
public sealed record LoadWarning(string Chunk, string Message, bool IsImportant, long Offset = 0);
