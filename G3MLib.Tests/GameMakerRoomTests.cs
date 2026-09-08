using System.Collections.ObjectModel;
using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class GameMakerRoomTests
{
    [Fact("GameMakerRoomTests.cs", 7)]
    public void SetupRoomSetsParents()
    {
        GameMakerRoom gameMakerRoom = new();
        GameMakerRoom.Layer layer = new();
        GameMakerRoom.Background background = new();
        ((Collection<GameMakerRoom.Layer>)(object)gameMakerRoom.Layers).Add(layer);
        ((Collection<GameMakerRoom.Background>)(object)gameMakerRoom.Backgrounds).Add(background);
        gameMakerRoom.SetupRoom(calculateGridWidth: false, calculateGridHeight: false);
        Assert.Same(gameMakerRoom, layer.ParentRoom);
        Assert.Same(gameMakerRoom, background.ParentRoom);
    }

    [Fact("GameMakerRoomTests.cs", 22)]
    public void SetupRoomUsesTheMostCommonLegacyTileSize()
    {
        GameMakerRoom room = new();
        ((Collection<GameMakerRoom.Tile>)(object)room.Tiles).Add(new GameMakerRoom.Tile
        {
            Width = 32u,
            Height = 32u
        });
        ((Collection<GameMakerRoom.Tile>)(object)room.Tiles).Add(new GameMakerRoom.Tile
        {
            Width = 32u,
            Height = 32u
        });
        ((Collection<GameMakerRoom.Tile>)(object)room.Tiles).Add(new GameMakerRoom.Tile
        {
            Width = 16u,
            Height = 16u
        });
        room.SetupRoom();
        Assert.Equal(32.0, room.GridWidth);
        Assert.Equal(32.0, room.GridHeight);
    }

    [Fact("GameMakerRoomTests.cs", 38)]
    public void CompressedTileLayerRoundTripsVerbatimAndRepeatedTiles()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerBackground tileset = new()
        {
            Name = data.Strings.MakeString("tileset_probe"),
            GMS2TileCount = 1,
            GMS2ItemsPerTileCount = 1,
            GMS2TileIds = new GameMakerSimpleList<GameMakerBackground.TileID>
            {
                new() { ID = 1 }
            }
        };
        data.Backgrounds.Add(tileset);
        data.Rooms.Single().Layers.Add(new GameMakerRoom.Layer
        {
            LayerName = data.Strings.MakeString("tiles"),
            LayerType = GameMakerRoom.LayerType.Tiles,
            Data = new GameMakerRoom.Layer.LayerTilesData
            {
                Background = tileset,
                TilesX = 130,
                TilesY = 2,
                TileData =
                [
                    [.. Enumerable.Repeat(3u, 130)],
                    [.. Enumerable.Range(0, 130).Select(static value => (uint)value)]
                ]
            }
        });

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerRoom.Layer.LayerTilesData tiles = output.Rooms.Single().Layers.Single().TilesData;

        Assert.Same(output.Backgrounds.Single(), tiles.Background);
        Assert.Equal(Enumerable.Repeat(3u, 130), tiles.TileData[0]);
        Assert.Equal(Enumerable.Range(0, 130).Select(static value => (uint)value), tiles.TileData[1]);
    }
}
