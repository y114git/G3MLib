using System.Collections.ObjectModel;
using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class InstanceIDListTests
{
    [Fact("InstanceIDListTests.cs", 8)]
    public void RoundTripPreservesUnsignedInstanceIds()
    {
        GameMakerRoom.InstanceIDList source = new();
        ((Collection<uint>)(object)source.InstanceIDs).Add(uint.MaxValue);
        using MemoryStream stream = new();
        GameMakerWriter writer = new(stream);
        source.Serialize(writer);
        stream.Position = 0L;
        GameMakerRoom.InstanceIDList destination = new();
        destination.Unserialize(new GameMakerReader(stream));
        GameMakerObservableList<uint> gameMakerObservableList = [];
        ((Collection<uint>)(object)gameMakerObservableList).Add(uint.MaxValue);
        Assert.Equal<GameMakerObservableList<uint>>(gameMakerObservableList, destination.InstanceIDs);
    }
}
