using G3MLib.DataFile;

namespace G3MLib.Tests;

/// <summary>
/// Reads each data file of the local game corpus, writes it back and compares the bytes. Skipped when no corpus folder exists.
/// The folders come from G3MLIB_CORPUS, separated like a PATH; without it the test is skipped.
/// </summary>
public sealed class CorpusRoundTripTests
{
    public static IEnumerable<string> CorpusFiles()
    {
        var configured = Environment.GetEnvironmentVariable("G3MLIB_CORPUS");
        var roots = string.IsNullOrWhiteSpace(configured)
            ? []
            : configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return roots.Where(Directory.Exists).SelectMany(root => Directory.GetFiles(root, "data.win", SearchOption.AllDirectories)).Order(StringComparer.Ordinal);
    }

    [Fact]
    public void WrittenCorpusFilesEqualTheOriginals()
    {
        List<string> files = [.. CorpusFiles()];
        Assert.SkipWhen(files.Count == 0, "G3MLIB_CORPUS is not set or names no folder");

        List<string> differences = [];
        foreach (var path in files)
        {
            var original = File.ReadAllBytes(path);
            using var data = GameMakerIO.Read(new MemoryStream(original));
            using MemoryStream output = new(original.Length);
            GameMakerIO.Write(output, data);
            ReadOnlySpan<byte> written = output.GetBuffer().AsSpan(0, (int)output.Length);
            if (!written.SequenceEqual(original))
                differences.Add($"{path}: {original.Length} bytes read, {written.Length} written, first difference at {written.CommonPrefixLength(original)}");
        }
        Assert.Empty(differences);
    }
}
