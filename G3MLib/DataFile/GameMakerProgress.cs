namespace G3MLib.DataFile;

/// <summary>
/// Progress of a data file read or write.
/// </summary>
/// <param name="Fraction">Completed share from 0 to 1. It never decreases and is exactly 1 when the operation is done.</param>
/// <param name="Chunk">Name of the chunk that was just finished, or "Done" for the final report.</param>
public readonly record struct GameMakerProgress(double Fraction, string Chunk);
