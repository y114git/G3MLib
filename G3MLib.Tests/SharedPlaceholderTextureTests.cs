using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class SharedPlaceholderTextureTests
{
    [Fact]
    public void DisposingOneFilesPlaceholderTextureKeepsTheNextFilesImage()
    {
        var first = new GameMakerEmbeddedTexture { TextureData = null! };
        Assert.NotNull(first.TextureData.Image);
        first.Dispose();

        var second = new GameMakerEmbeddedTexture { TextureData = null! };
        Assert.NotNull(second.TextureData.Image);
    }
}
