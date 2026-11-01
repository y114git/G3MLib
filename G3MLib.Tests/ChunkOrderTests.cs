using G3MLib.DataFile;

namespace G3MLib.Tests;

public sealed class ChunkOrderTests
{
    private static GameMakerData Load()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        return GameMakerIO.Read(input);
    }

    private static List<string> WrittenOrder(GameMakerData data)
    {
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        output.Position = 0;
        using var reread = GameMakerIO.Read(output);
        return [.. reread.FORM.ChunkOrder];
    }

    [Fact]
    public void RemoveThenAddPutsTheChunkBackInCanonicalPlace()
    {
        using var data = Load();
        List<string> before = [.. data.FORM.ChunkOrder];
        var tags = data.FORM.Chunks["TAGS"];

        Assert.True(data.FORM.RemoveChunk("TAGS"));
        Assert.DoesNotContain("TAGS", data.FORM.ChunkOrder);
        Assert.False(data.FORM.ChunksTypeDict.ContainsKey(tags.GetType()));
        data.FORM.AddChunk(tags);

        Assert.Equal(before, data.FORM.ChunkOrder);
        Assert.Same(tags, data.FORM.ChunksTypeDict[tags.GetType()]);
        Assert.Equal(before, WrittenOrder(data));
    }

    [Fact]
    public void AddingSeveralChunksAfterRemovalsKeepsTheCanonicalOrder()
    {
        using var data = Load();
        List<string> before = [.. data.FORM.ChunkOrder];
        Dictionary<string, GameMakerChunk> removed = [];
        foreach (var name in new[] { "ACRV", "SEQN", "STRG", "GEN8" })
        {
            removed[name] = data.FORM.Chunks[name];
            data.FORM.RemoveChunk(name);
        }

        foreach (var name in new[] { "STRG", "GEN8", "SEQN", "ACRV" })
            data.FORM.AddChunk(removed[name]);

        Assert.Equal(before, data.FORM.ChunkOrder);
    }

    [Fact]
    public void UnknownChunkKeepsItsPlaceWhileKnownOnesMove()
    {
        using var data = Load();
        data.FORM.AddChunk(new GameMakerRawChunk("ZZZZ") { RawData = new byte[16] });
        data.FORM.SetChunkOrder(["GEN8", "ZZZZ", .. data.FORM.ChunkOrder.Where(n => n is not ("GEN8" or "ZZZZ"))]);
        var room = data.FORM.Chunks["ROOM"];
        data.FORM.RemoveChunk("ROOM");
        data.FORM.AddChunk(room);

        List<string> order = [.. data.FORM.ChunkOrder];
        Assert.Equal(["GEN8", "ZZZZ", "OPTN"], order.GetRange(0, 3));
        Assert.Equal("ROOM", order[order.IndexOf("TAGS") + 1]);
    }

    [Fact]
    public void SetChunkOrderRefusesNamesThatAreNotLoaded()
    {
        using var data = Load();

        Assert.Throws<ArgumentException>(() => data.FORM.SetChunkOrder(["GEN8", "NOPE"]));
        Assert.Equal("GEN8", data.FORM.ChunkOrder[0]);
    }

    [Fact]
    public void CanonicalOrderMatchesTheOrderOfTheFile()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(input);

        Assert.Equal(data.FORM.ChunkOrder, GameMakerChunkFORM.CanonicalOrder.Where(n => data.FORM.Chunks.ContainsKey(n)));
    }
}
