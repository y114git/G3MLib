using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class GameMakerReaderTests
{
    [Fact]
    public void StaticMetadataOverridesAreScopedToReader()
    {
        using GameMakerReader first = new(new MemoryStream());
        uint initialCount = first.GetStaticChildCount(typeof(GameMakerGameObject.Event));
        first.SetStaticChildCount(typeof(GameMakerGameObject.Event), initialCount + 1);

        using GameMakerReader second = new(new MemoryStream());

        Assert.Equal(initialCount + 1, first.GetStaticChildCount(typeof(GameMakerGameObject.Event)));
        Assert.Equal(initialCount, second.GetStaticChildCount(typeof(GameMakerGameObject.Event)));
    }
}
