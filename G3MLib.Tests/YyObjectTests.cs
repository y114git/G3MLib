using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Interop;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class YyObjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3m-yyobject-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    private static GameMakerData RoundTrip(GameMakerData data)
    {
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        return GameMakerIO.Read(stream);
    }

    private string WriteSource()
    {
        var folder = Path.Combine(_root, "objects", "obj_yy");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Create_0.gml"), "yy_value = 5;\n");
        File.WriteAllText(Path.Combine(folder, "Step_0.gml"), "yy_value += 1;\n");
        File.WriteAllText(Path.Combine(folder, "Collision_obj_yy_other.gml"), "yy_value = 0;\n");
        File.WriteAllText(Path.Combine(folder, "obj_yy.yy"), /*lang=json*/ """
            {"$GMObject":"","%Name":"obj_yy","managed":false,
             "eventList":[
              {"$GMEvent":"v1","collisionObjectId":null,"eventNum":0,"eventType":0,"isDnD":false,"name":"","resourceType":"GMEvent",},
              {"$GMEvent":"v1","collisionObjectId":null,"eventNum":0,"eventType":3,"isDnD":false,"name":"","resourceType":"GMEvent",},
              {"$GMEvent":"v1","collisionObjectId":{"name":"obj_yy_other","path":"objects/obj_yy_other/obj_yy_other.yy",},"eventNum":0,"eventType":4,"isDnD":false,"name":"","resourceType":"GMEvent",},
             ],
             "name":"obj_yy","parentObjectId":{"name":"obj_yy_parent","path":"objects/obj_yy_parent/obj_yy_parent.yy",},"persistent":true,"physicsDensity":0.75,"physicsGroup":2,
             "physicsObject":true,"physicsShape":2,"physicsShapePoints":[{"x":1.0,"y":2.0,},{"x":3.0,"y":4.0,},],"physicsStartAwake":false,
             "resourceType":"GMObject","solid":true,"spriteId":null,"visible":false,}
            """);
        return Path.Combine(folder, "obj_yy.yy");
    }

    private static GameMakerCode EventCode(GameMakerGameObject obj, EventType type, uint subtype) =>
        obj.Events[(int)type].Single(e => e.EventSubtype == subtype).Actions[0].CodeId;

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void ImportsFieldsAndLinksCompiledEvents(string fixture)
    {
        using var data = Load(fixture);
        CodeImportGroup group = new(data);
        YyObject.Read(WriteSource(), group);
        group.Import();

        using var reloaded = RoundTrip(data);
        var obj = reloaded.GameObjects.ByName("obj_yy");
        Assert.True(obj.Persistent && obj.Solid && obj.UsesPhysics && !obj.Visible && !obj.Awake);
        Assert.Equal((CollisionShapeFlags.Custom, 2u, 0.75f), (obj.CollisionShape, obj.Group, obj.Density));
        Assert.Equal([(1f, 2f), (3f, 4f)], [.. obj.PhysicsVertices.Select(v => (v.X, v.Y))]);
        Assert.Equal("obj_yy_parent", obj.ParentId.Name.Content);
        Assert.Equal("gml_Object_obj_yy_Create_0", EventCode(obj, EventType.Create, 0).Name.Content);
        Assert.Equal("gml_Object_obj_yy_Step_0", EventCode(obj, EventType.Step, 0).Name.Content);
        var otherId = (uint)reloaded.GameObjects.IndexOfName("obj_yy_other");
        Assert.Equal("gml_Object_obj_yy_Collision_obj_yy_other", EventCode(obj, EventType.Collision, otherId).Name.Content);
    }

    [Theory]
    [InlineData("data-2022.win")]
    [InlineData("data-2026.win")]
    public void WritesFieldsAndEventsOfAnExistingObject(string fixture)
    {
        using var data = Load(fixture);
        GlobalDecompileContext context = new(data);
        var obj = data.GameObjects.First(o => o.Events.Any(l => l?.Any(e => e.Actions.Count > 0) == true));
        foreach (var format in Enum.GetValues<YyFormat>())
        {
            var folder = Path.Combine(_root, "out", format.ToString());
            YyObject.Write(obj, data, context, folder, format);
            var yy = File.ReadAllText(Path.Combine(folder, obj.Name.Content + ".yy"));
            Assert.Contains("\"resourceType\": \"GMObject\"", yy, StringComparison.Ordinal);
            Assert.Contains("\"eventList\"", yy, StringComparison.Ordinal);
            Assert.NotEmpty(Directory.GetFiles(folder, "*.gml"));
        }
    }
}
