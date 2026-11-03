using G3MLib.DataFile;

namespace G3MLib.Tests;

public sealed class ChunkOffsetTests
{
    private static string Name(byte[] file, long offset) => System.Text.Encoding.ASCII.GetString(file, (int)offset, 4);

    private static void AssertOffsetsPointAtTheNames(byte[] file, GameMakerData data)
    {
        Assert.Equal(0, data.FORM.Offset);
        Assert.Equal("FORM", Name(file, data.FORM.Offset));
        long previous = data.FORM.Offset;
        foreach (var name in data.FORM.ChunkOrder)
        {
            var chunk = data.FORM.Chunks[name];
            Assert.Equal(name, Name(file, chunk.Offset));
            Assert.Equal(chunk.Length, BitConverter.ToUInt32(file, (int)chunk.Offset + 4));
            Assert.True(chunk.Offset > previous);
            previous = chunk.Offset;
        }
    }

    [Fact]
    public void OffsetsOfAWrittenThenReadFilePointAtTheChunkNames()
    {
        using var created = GameMakerData.CreateNew2026LTS();
        created.FORM.AddChunk(new GameMakerRawChunk("ZZZZ") { RawData = new byte[16] });
        using MemoryStream output = new();
        GameMakerIO.Write(output, created);
        var file = output.ToArray();

        using var data = GameMakerIO.Read(new MemoryStream(file));

        AssertOffsetsPointAtTheNames(file, data);
        Assert.Contains("ZZZZ", data.FORM.ChunkOrder);
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void OffsetsOfFixtureFilesPointAtTheChunkNames(string fixture)
    {
        var file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        using var data = GameMakerIO.Read(new MemoryStream(file));

        AssertOffsetsPointAtTheNames(file, data);
        Assert.Equal(8, data.FORM.Chunks[data.FORM.ChunkOrder[0]].Offset);
    }

    [Fact]
    public async Task ReadAsyncFillsTheOffsets()
    {
        var file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2022.win"), TestContext.Current.CancellationToken);
        using var data = await GameMakerIO.ReadAsync(new MemoryStream(file), cancellationToken: TestContext.Current.CancellationToken);

        AssertOffsetsPointAtTheNames(file, data);
    }

    [Fact]
    public void ChunksThatWereNotReadHaveNoOffset()
    {
        using var created = GameMakerData.CreateNew2026LTS();

        Assert.All(created.FORM.Chunks.Values, chunk => Assert.Equal(0, chunk.Offset));
    }
}
