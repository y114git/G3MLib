using System;
using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class GameMakerUINodeTests
{
    [Theory("GameMakerUINodeTests.cs", 10)]
    [InlineData([typeof(GameMakerUIGameObject)])]
    [InlineData([typeof(GameMakerUISequenceInstance)])]
    [InlineData([typeof(GameMakerUISpriteInstance)])]
    [InlineData([typeof(GameMakerUITextItemInstance)])]
    public void FlexInstanceNodesRoundTrip(Type nodeType)
    {
        IGameMakerFlexInstanceProperties sourceProperties = (IGameMakerFlexInstanceProperties)(IGameMakerUINodeData)Activator.CreateInstance(nodeType);
        sourceProperties.Visible = true;
        sourceProperties.Anchor = 4;
        sourceProperties.StretchWidth = true;
        sourceProperties.StretchHeight = true;
        sourceProperties.TileH = true;
        sourceProperties.TileV = true;
        sourceProperties.KeepAspect = true;
        using MemoryStream stream = new();
        IGameMakerFlexInstanceProperties.Serialize(new GameMakerWriter(stream), sourceProperties);
        stream.Position = 0L;
        IGameMakerUINodeData resultData = (IGameMakerUINodeData)Activator.CreateInstance(nodeType);
        IGameMakerFlexInstanceProperties.Unserialize(new GameMakerReader(stream), (IGameMakerFlexInstanceProperties)resultData);
        IGameMakerFlexInstanceProperties resultProperties = (IGameMakerFlexInstanceProperties)resultData;
        Assert.True(resultProperties.Visible);
        Assert.Equal(4, resultProperties.Anchor);
        Assert.True(resultProperties.StretchWidth);
        Assert.True(resultProperties.StretchHeight);
        Assert.True(resultProperties.TileH);
        Assert.True(resultProperties.TileV);
        Assert.True(resultProperties.KeepAspect);
    }
}
