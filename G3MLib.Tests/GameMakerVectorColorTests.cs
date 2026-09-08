using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public class GameMakerVectorColorTests
{
    [Fact("GameMakerVectorColorTests.cs", 8)]
    public void SolidFillAndLineStyleUseTheSameColorEncoding()
    {
        GameMakerVectorSolidFillData solid = new()
        {
            Red = 1,
            Green = 2,
            Blue = 3,
            Alpha = 4
        };
        GameMakerVectorLineStyleData line = new()
        {
            Red = 1,
            Green = 2,
            Blue = 3,
            Alpha = 4
        };
        Assert.Equal(Serialize(solid), Serialize(line));
        Assert.Equal<byte[]>([1, 2, 3, 4], Serialize(solid));
    }

    private static byte[] Serialize(GameMakerObject value)
    {
        using MemoryStream stream = new();
        GameMakerWriter writer = new(stream);
        value.Serialize(writer);
        return stream.ToArray();
    }
}
