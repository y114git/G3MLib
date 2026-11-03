namespace G3MLib.Modding.Hashing;

/// <summary>
/// The hash of the body of one chunk of a data file.
/// </summary>
/// <param name="Name">Four character name of the chunk.</param>
/// <param name="Offset">Offset of the start of the chunk header (its name) in the data file, relative to the position where FORM begins (offset 0).</param>
/// <param name="Length">Length of the chunk body in bytes, the value stored in the chunk header. It includes any padding that follows the data.</param>
/// <param name="Hash">Lower case hexadecimal hash of the chunk body.</param>
public sealed record ChunkDigest(string Name, long Offset, long Length, string Hash);
