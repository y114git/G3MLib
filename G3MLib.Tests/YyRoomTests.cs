using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Interop;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class YyRoomTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3m-yyroom-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var data = GameMakerIO.Read(input);
        data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString("obj_room_test") });
        return data;
    }

    private static GameMakerData RoundTrip(GameMakerData data)
    {
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        return GameMakerIO.Read(stream);
    }

    private string WriteSource(GameMakerData data, bool creationCode)
    {
        var tileset = Tileset(data);
        var tileLayer = tileset is null ? "" : $$"""{"$GMRTileLayer":"","%Name":"Tiles","depth":50,"name":"Tiles","resourceType":"GMRTileLayer","tiles":{"SerialiseHeight":2,"SerialiseWidth":3,"TileSerialiseData":[0,1,2,3,0,5,],},"tilesetId":{"name":"{{tileset}}","path":"tilesets/{{tileset}}/{{tileset}}.yy",},"visible":true,"x":16,"y":8,},""";
        var sprite = data.Sprites.First(s => s?.Name is not null).Name.Content;
        var folder = Path.Combine(_root, "rooms", "rm_yy");
        Directory.CreateDirectory(folder);
        if (creationCode)
            File.WriteAllText(Path.Combine(folder, "RoomCreationCode.gml"), "yy_room_flag = 1;\n");
        File.WriteAllText(Path.Combine(folder, "rm_yy.yy"), $$"""
            {"$GMRoom":"v1","%Name":"rm_yy","creationCodeFile":"{{(creationCode ? "rooms/rm_yy/RoomCreationCode.gml" : "")}}","instanceCreationOrder":[{"name":"inst_b","path":"rooms/rm_yy/rm_yy.yy",},{"name":"inst_a","path":"rooms/rm_yy/rm_yy.yy",},],
             "layers":[
              {"$GMRInstanceLayer":"","%Name":"Instances","depth":0,"instances":[
                {"$GMRInstance":"v4","%Name":"inst_a","colour":4294967295,"hasCreationCode":false,"imageIndex":1,"imageSpeed":0.5,"name":"inst_a","objectId":{"name":"obj_room_test","path":"objects/obj_room_test/obj_room_test.yy",},"resourceType":"GMRInstance","rotation":90.0,"scaleX":2.0,"scaleY":1.0,"x":10.0,"y":20.0,},
                {"$GMRInstance":"v4","%Name":"inst_b","colour":4278190335,"hasCreationCode":false,"name":"inst_b","objectId":{"name":"obj_room_test","path":"objects/obj_room_test/obj_room_test.yy",},"resourceType":"GMRInstance","rotation":0.0,"scaleX":1.0,"scaleY":1.0,"x":30.0,"y":40.0,},
               ],"name":"Instances","resourceType":"GMRInstanceLayer","visible":true,},
              {{tileLayer}}
              {"$GMRBackgroundLayer":"","%Name":"Background","colour":4278190080,"depth":100,"hspeed":1.5,"htiled":true,"name":"Background","resourceType":"GMRBackgroundLayer","spriteId":{"name":"{{sprite}}","path":"sprites/{{sprite}}/{{sprite}}.yy",},"stretch":false,"visible":true,"vspeed":0.0,"vtiled":false,"x":0,"y":0,},
              {"$GMRAssetLayer":"","%Name":"Assets","assets":[
                {"$GMRSpriteGraphic":"v1","%Name":"g1","animationSpeed":2.0,"colour":4294967295,"headPosition":1.0,"name":"g1","resourceType":"GMRSpriteGraphic","rotation":0.0,"scaleX":2.0,"scaleY":1.0,"spriteId":{"name":"{{sprite}}","path":"sprites/{{sprite}}/{{sprite}}.yy",},"x":64.0,"y":32.0,},
                {"$GMRSequenceGraphic":"v1","%Name":"g2","name":"g2","resourceType":"GMRSequenceGraphic","sequenceId":null,"x":0.0,"y":0.0,},
               ],"depth":200,"name":"Assets","resourceType":"GMRAssetLayer","visible":true,},
              {"$GMREffectLayer":"","%Name":"Fx","depth":300,"effectEnabled":true,"effectType":"_filter_vignette","name":"Fx","properties":[{"name":"g_VignetteEdges","type":0,"value":"2",},],"resourceType":"GMREffectLayer","visible":true,},
              {"$GMRPathLayer":"","%Name":"Paths","depth":400,"name":"Paths","pathId":null,"resourceType":"GMRPathLayer","visible":true,},
             ],
             "name":"rm_yy","physicsSettings":{"PhysicsWorld":true,"PhysicsWorldGravityX":0.0,"PhysicsWorldGravityY":9.5,"PhysicsWorldPixToMetres":0.2,},
             "resourceType":"GMRoom","roomSettings":{"Height":480,"Width":640,"persistent":true,},
             "viewSettings":{"clearDisplayBuffer":false,"clearViewBackground":true,"enableViews":true,},
             "views":[{"hborder":16,"hport":240,"hspeed":-1,"hview":240,"objectId":{"name":"obj_room_test","path":"objects/obj_room_test/obj_room_test.yy",},"vborder":8,"visible":true,"vspeed":-1,"wport":320,"wview":320,"xport":4,"xview":2,"yport":0,"yview":0,},],}
            """);
        return Path.Combine(folder, "rm_yy.yy");
    }

    private static GameMakerRoom Import(GameMakerData data, string yy, List<string> skipped = null)
    {
        CodeImportGroup group = new(data);
        var room = YyRoom.Read(yy, group, skipped);
        group.Import();
        return room;
    }

    private static void AssertRoom(GameMakerRoom room)
    {
        Assert.Equal((640u, 480u, true, true), (room.Width, room.Height, room.Persistent, room.World));
        Assert.Equal((9.5f, 0.2f), (room.GravityY, room.MetersPerPixel));
        Assert.True(room.Flags.HasFlag(GameMakerRoom.RoomEntryFlags.EnableViews) && room.Flags.HasFlag(GameMakerRoom.RoomEntryFlags.ClearViewBackground) && room.Flags.HasFlag(GameMakerRoom.RoomEntryFlags.DoNotClearDisplayBuffer));
        var view = room.Views[0];
        Assert.Equal((true, 2, 320, 4, 16u, "obj_room_test"), (view.Enabled, view.ViewX, view.ViewWidth, view.PortX, view.BorderX, view.ObjectId.Name.Content));

        var tiles = room.Layers.Any(l => l.LayerName.Content == "Tiles");
        Assert.Equal(tiles ? ["Instances", "Tiles", "Background", "Assets", "Fx", "Paths"] : ["Instances", "Background", "Assets", "Fx", "Paths"], room.Layers.Select(l => l.LayerName.Content).ToArray());
        var assets = room.Layers.Single(l => l.LayerName.Content == "Assets");
        var graphic = Assert.Single(assets.AssetsData.Sprites);
        Assert.Equal((64, 32, 2f, 2f, 1f), (graphic.X, graphic.Y, graphic.ScaleX, graphic.AnimationSpeed, graphic.FrameIndex));
        var fx = room.Layers.Single(l => l.LayerName.Content == "Fx");
        Assert.Equal((GameMakerRoom.LayerType.Effect, "_filter_vignette", 1), (fx.LayerType, fx.EffectType.Content, fx.EffectProperties.Count));
        Assert.Equal(GameMakerRoom.LayerType.Path, room.Layers.Single(l => l.LayerName.Content == "Paths").LayerType);
        Assert.Equal(room.Layers.Select(l => l.LayerId).Distinct().Count(), room.Layers.Count);
        GameMakerRoom.GameObject[] instances = [.. room.Layers[0].InstancesData.Instances];
        Assert.Equal([(10, 20, 2f, 90f, 1, 0.5f), (30, 40, 1f, 0f, 0, 1f)], [.. instances.Select(i => (i.X, i.Y, i.ScaleX, i.Rotation, i.ImageIndex, i.ImageSpeed))]);
        Assert.Equal(0xFF0000FFu, instances[1].Color);
        Assert.Equal(room.GameObjects.Select(o => o.InstanceID).ToArray(), instances.Select(i => i.InstanceID).ToArray());
        // Data files before GameMaker 2024.13 do not store the creation order.
        if (room.InstanceCreationOrderIDs is { InstanceIDs.Count: > 0 })
            Assert.Equal([instances[1].InstanceID, instances[0].InstanceID], [.. room.InstanceCreationOrderIDs.InstanceIDs]);

        if (tiles)
        {
            var tileData = room.Layers[1].TilesData;
            Assert.Equal((3u, 2u, 50, 16f, 8f), (tileData.TilesX, tileData.TilesY, room.Layers[1].LayerDepth, room.Layers[1].XOffset, room.Layers[1].YOffset));
            Assert.Equal([[0u, 1u, 2u], [3u, 0u, 5u]], tileData.TileData);
        }
        var background = room.Layers.Single(l => l.LayerName.Content == "Background");
        Assert.True(background.BackgroundData.TiledHorizontally && !background.BackgroundData.TiledVertically);
        Assert.Equal(1.5f, background.HSpeed);
    }

    // The 2026 fixture has no named tile set, so its test room has no tile layer.
    private static string Tileset(GameMakerData data) => data.Backgrounds?.FirstOrDefault(b => b?.Name is not null)?.Name.Content;

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void ReadsRoomAndSkipsUnsupportedLayers(string fixture)
    {
        using var data = Load(fixture);
        List<string> skipped = [];
        Import(data, WriteSource(data, false), skipped);

        Assert.Equal(["Assets: GMRSequenceGraphic g2"], skipped);
        using var reloaded = RoundTrip(data);
        AssertRoom(reloaded.Rooms.ByName("rm_yy"));
    }

    [Theory]
    [InlineData("data-2022.win", YyFormat.Lts2022)]
    [InlineData("data-2026.win", YyFormat.Lts2026)]
    public void WriteThenReadGivesTheSameRoom(string fixture, YyFormat format)
    {
        using var data = Load(fixture);
        Import(data, WriteSource(data, false));
        using var reloaded = RoundTrip(data);
        var folder = Path.Combine(_root, "out", format.ToString());
        YyRoom.Write(reloaded.Rooms.ByName("rm_yy"), reloaded, new GlobalDecompileContext(reloaded), folder, format);

        using var second = Load(fixture);
        AssertRoom(Import(second, Path.Combine(folder, "rm_yy.yy")));
        YyRoom.Write(second.Rooms.ByName("rm_yy"), second, new GlobalDecompileContext(second), Path.Combine(_root, "out2", format.ToString()), format);
        Assert.Equal(File.ReadAllText(Path.Combine(folder, "rm_yy.yy")), File.ReadAllText(Path.Combine(_root, "out2", format.ToString(), "rm_yy.yy")));
    }

    [Fact]
    public void LinksCreationCode()
    {
        using var data = Load("data-2026.win");
        var room = Import(data, WriteSource(data, true));

        Assert.Equal("gml_Room_rm_yy_Create", room.CreationCodeId.Name.Content);
        using var reloaded = RoundTrip(data);
        Assert.Equal("gml_Room_rm_yy_Create", reloaded.Rooms.ByName("rm_yy").CreationCodeId.Name.Content);
        Assert.Contains(reloaded.Variables.ToList(), v => v.Name.Content == "yy_room_flag");
    }
}
