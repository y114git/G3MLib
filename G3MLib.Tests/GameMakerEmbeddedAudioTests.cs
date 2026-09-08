using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public class EmbeddedAudioTest
{
    [Theory]
    [InlineData(new byte[]
    {
        4, 0, 0, 0,
        252, 253, 254, 255,
    })]

    public void TestUnserialize(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var reader = new GameMakerReader(stream);
        var embeddedAudio = new GameMakerEmbeddedAudio();

        embeddedAudio.Unserialize(reader);

        Assert.True(embeddedAudio.Data.Length == BitConverter.ToInt32(data.AsSpan()[..4]));
        Assert.Equal(embeddedAudio.Data, data[4..]);
    }

    [Fact]
    public void TestSerialize()
    {
        using var stream = new MemoryStream();
        var fullData = new byte[] { 4, 0, 0, 0, 252, 253, 254, 255 };
        GameMakerEmbeddedAudio audio = new()
        {
            Name = new GameMakerString("foobar"),
            Data = fullData[4..]
        };
        var writer = new GameMakerWriter(stream);

        audio.Serialize(writer);

        Assert.True(stream.Length == fullData.Length);
        Assert.Equal(stream.ToArray(), fullData);
    }
}
