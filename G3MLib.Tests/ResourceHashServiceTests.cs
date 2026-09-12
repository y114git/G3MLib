using System.IO;
using G3MLib.DataFile;
using G3MLib.Modding.Resources;
using Xunit;

namespace G3MLib.Tests;

public sealed class ResourceHashServiceTests
{
    [Fact]
    public void HashingRawTexturesPreservesPixelsAndMatchesCompressedTextures()
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(stream);
        var expected = ResourceHashService.HashAll(data)["Sprites"];
        foreach (var texture in data.EmbeddedTextures)
            texture.TextureData.Image = texture.TextureData.Image.ConvertToRawBgra();
        byte[][] pixels = data.EmbeddedTextures.Select(texture => texture.TextureData.Image.GetData().ToArray()).ToArray();

        for (int iteration = 0; iteration < 2; iteration++)
        {
            Assert.Equal(expected, ResourceHashService.HashAll(data)["Sprites"]);
            for (int index = 0; index < pixels.Length; index++)
                Assert.Equal(pixels[index], data.EmbeddedTextures[index].TextureData.Image.GetData());
        }
    }

    [Fact("ResourceHashServiceTests.cs", 9)]
    public void SpriteHashUsesStableRgbaPixels()
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(stream);

        Dictionary<string, string> sprites = ResourceHashService.HashAll(data)["Sprites"];

        Assert.Equal("197db752c95e6dcfe6b8854c9d7dce8fd8b0eb35a5d850d35821023039a77b33", sprites["spr_probe"]);
    }

    [Fact("ResourceHashServiceTests.cs", 22)]
    public void SequenceHashUsesCanonicalSerializedData()
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
        using var data = GameMakerIO.Read(stream);

        Dictionary<string, string> sequences = ResourceHashService.HashAll(data)["Sequences"];

        Assert.Equal("b331a2087573c546d1a77856231923eb23760ec700508f4a519614e77b4ff351", sequences["seq_probe"]);
        Assert.Equal("5436f186cb52deef50d89e712d1238776ccbaf3439a6907ff3c4db746ce0ebe0", sequences["seq_probe_2"]);
    }
}
