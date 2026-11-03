using System.Security.Cryptography;
using G3MLib.DataFile;
using G3MLib.Modding.Hashing;

namespace G3MLib.Tests;

public sealed class ChunkDigestTests
{
    private static byte[] Write(GameMakerData data)
    {
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        return output.ToArray();
    }

    private static IReadOnlyList<ChunkDigest> Compute(Stream stream) => ChunkDigests.Compute(stream, token: TestContext.Current.CancellationToken);

    private static byte[] FixtureBytes(string fixture) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));

    private static IReadOnlyList<ChunkDigest> Digest(byte[] file, HashAlgorithmName? algorithm = null)
    {
        using MemoryStream stream = new(file);
        return ChunkDigests.Compute(stream, algorithm, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void DigestsFollowTheChunksOfTheFile(string fixture)
    {
        var file = FixtureBytes(fixture);
        using var data = GameMakerIO.Read(new MemoryStream(file));

        var digests = Digest(file);

        Assert.Equal(data.FORM.ChunkOrder, digests.Select(d => d.Name));
        foreach (var digest in digests)
        {
            var chunk = data.FORM.Chunks[digest.Name];
            Assert.Equal(chunk.Offset, digest.Offset);
            Assert.Equal(chunk.Length, digest.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file.AsSpan((int)digest.Offset + 8, (int)digest.Length))), digest.Hash);
        }
    }

    [Fact]
    public void AlgorithmCanBeChosen()
    {
        var file = FixtureBytes("data-2022.win");

        var sha256 = Digest(file);
        var md5 = Digest(file, HashAlgorithmName.MD5);

        Assert.All(sha256, d => Assert.Equal(64, d.Hash.Length));
        Assert.All(md5, d => Assert.Equal(32, d.Hash.Length));
        Assert.Equal(sha256.Select(d => d.Name), md5.Select(d => d.Name));
    }

    [Fact]
    public void ChangingOneResourceChangesOnlyItsChunk()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var before = Digest(Write(data));

        var text = data.Strings.First(s => !string.IsNullOrEmpty(s.Content));
        text.Content = new string(text.Content[0] == 'q' ? 'z' : 'q', text.Content.Length);
        using MemoryStream changed = new(Write(data));
        var after = ChunkDigests.Compute(changed, token: TestContext.Current.CancellationToken);

        Assert.Equal(["STRG"], ChunkDigests.Compare(before, after));
        Assert.Empty(ChunkDigests.Compare(before, before));
    }

    [Fact]
    public void CompareReportsAddedRemovedAndChangedChunks()
    {
        ChunkDigest[] a = [new("GEN8", 8, 100, "aa"), new("OPTN", 116, 20, "bb"), new("STRG", 144, 50, "cc")];
        ChunkDigest[] b = [new("GEN8", 8, 100, "aa"), new("OPTN", 116, 20, "b2"), new("ZZZZ", 144, 4, "dd")];

        Assert.Equal(["OPTN", "ZZZZ", "STRG"], ChunkDigests.Compare(a, b));
        Assert.Equal(["OPTN", "STRG", "ZZZZ"], ChunkDigests.Compare(b, a));
    }

    [Fact]
    public void MovedChunkWithTheSameBodyIsNotChanged()
    {
        ChunkDigest[] a = [new("GEN8", 8, 100, "aa"), new("OPTN", 116, 20, "bb")];
        ChunkDigest[] b = [new("GEN8", 8, 100, "aa"), new("OPTN", 200, 20, "bb")];

        Assert.Empty(ChunkDigests.Compare(a, b));
    }

    [Fact]
    public void ComputeHonoursCancellation()
    {
        using MemoryStream stream = new(FixtureBytes("data-2022.win"));
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ChunkDigests.Compute(stream, token: source.Token));
    }

    private static byte[] Header(string name, uint length) => [.. System.Text.Encoding.ASCII.GetBytes(name), .. BitConverter.GetBytes(length)];

    [Fact]
    public void ComputeRejectsFilesThatAreNotDataFiles()
    {
        Assert.Throws<IOException>(() => Compute(new MemoryStream(new byte[4])));
        Assert.Throws<IOException>(() => Compute(new MemoryStream(Header("NOPE", 0))));
        Assert.Throws<IOException>(() => Compute(new MemoryStream(Header("FORM", int.MaxValue))));
        Assert.Throws<IOException>(() => Compute(new MemoryStream([.. Header("FORM", 8), .. Header("ABCD", 100)])));
        Assert.Throws<IOException>(() => Compute(new MemoryStream([.. Header("FORM", 4), 1, 2, 3, 4])));
        var file = FixtureBytes("data-2022.win");
        Assert.Throws<IOException>(() => Compute(new MemoryStream(file[..(file.Length / 2)])));
    }

    [Fact]
    public void ComputeAcceptsAnEmptyFormAndASingleChunk()
    {
        var digests = Compute(new MemoryStream(Header("FORM", 0)));
        Assert.Empty(digests);

        var chunk = Header("ABCD", 3).Concat(new byte[] { 1, 2, 3 }).ToArray();
        var digest = Assert.Single(Compute(new MemoryStream([.. Header("FORM", (uint)chunk.Length), .. chunk])));
        Assert.Equal(("ABCD", 8L, 3L), (digest.Name, digest.Offset, digest.Length));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])), digest.Hash);
    }

    [Fact]
    public void ComputeRecordsOffsetRelativeToFormOriginEvenWhenStreamOffsetNonZero()
    {
        var chunk = Header("ABCD", 3).Concat(new byte[] { 1, 2, 3 }).ToArray();
        var formData = Header("FORM", (uint)chunk.Length).Concat(chunk).ToArray();
        var padded = new byte[100].Concat(formData).ToArray();

        using MemoryStream stream = new(padded);
        stream.Position = 100;

        var digest = Assert.Single(ChunkDigests.Compute(stream, token: TestContext.Current.CancellationToken));
        Assert.Equal(8L, digest.Offset);
    }

    [Fact]
    public void CompareHandlesDuplicatedChunkNamesKeepingCounts()
    {
        ChunkDigest[] a = [new("AUDO", 8, 10, "11"), new("AUDO", 26, 10, "22")];

        ChunkDigest[] bSame = [new("AUDO", 8, 10, "22"), new("AUDO", 26, 10, "11")];
        Assert.Empty(ChunkDigests.Compare(a, bSame));

        ChunkDigest[] bMissingOne = [new("AUDO", 8, 10, "11")];
        Assert.Equal(["AUDO"], ChunkDigests.Compare(a, bMissingOne));

        ChunkDigest[] bDuplicateHash = [new("AUDO", 8, 10, "11"), new("AUDO", 26, 10, "11")];
        Assert.Equal(["AUDO"], ChunkDigests.Compare(a, bDuplicateHash));
    }
}
