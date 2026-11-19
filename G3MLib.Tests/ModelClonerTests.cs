using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.References;

namespace G3MLib.Tests;

public sealed class ModelClonerTests
{
    [Fact]
    public void DictionaryValuesAreCopiedNotShared()
    {
        var tags = new GameMakerTags { AssetTags = new Dictionary<int, GameMakerSimpleListString> { [7] = new() } };
        var cloner = new ModelCloner([], _ => false, (_, _) => { }, (_, _) => { });

        var copy = (GameMakerTags)cloner.CloneValue(tags)!;

        Assert.NotSame(tags.AssetTags, copy.AssetTags);
        Assert.NotSame(tags.AssetTags[7], copy.AssetTags[7]);
        Assert.True(copy.AssetTags.ContainsKey(7));
    }

    [Fact]
    public void ArraysOfModelObjectsWithOtherShapesThrowAClearError()
    {
        var cloner = new ModelCloner([], _ => false, (_, _) => { }, (_, _) => { });
        var matrix = new GameMakerSimpleListString[1, 1];
        var shifted = Array.CreateInstance(typeof(GameMakerSimpleListString), [1], [1]);

        Assert.Contains("GameMakerSimpleListString[,]", Assert.Throws<NotSupportedException>(() => cloner.CloneValue(matrix)).Message);
        Assert.Throws<NotSupportedException>(() => cloner.CloneValue(shifted));
    }
}
