using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Interop;

/// <summary>
/// Converts a GameMaker object resource (<c>.yy</c> plus one <c>.gml</c> file per event) to and from <see cref="GameMakerGameObject"/>.
/// </summary>
/// <remarks>
/// Event code is compiled by <see cref="CodeImportGroup"/>, which links it to the object. Drag-and-drop events and
/// object variables (<c>properties</c>) are not kept. A parent, sprite or collision partner that is not in the data file
/// yet becomes an empty object, so objects can be read in any order; a missing sprite is an error.
/// </remarks>
public static class YyObject
{
    /// <summary>
    /// Reads the object's fields into the data file and queues its event code on <paramref name="group"/>.
    /// The caller runs <see cref="CodeImportGroup.Import(bool)"/> once after all objects are read.
    /// </summary>
    public static GameMakerGameObject Read(string yyPath, CodeImportGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var data = group.Data;
        group.GameMakerEventActions = true;
        var root = YyFile.Parse(yyPath);
        var name = root.Str("name") ?? throw new InvalidDataException($"{yyPath}: object has no name.");
        var obj = Shell(data, name);

        obj.Sprite = Sprite(data, root.Child("spriteId"));
        obj.TextureMaskId = Sprite(data, root.Child("spriteMaskId"));
        obj.ParentId = root.Child("parentObjectId").Str("name") is { } parent ? Shell(data, parent) : null;
        obj.Visible = root.Bool("visible", true);
        obj.Solid = root.Bool("solid");
        obj.Persistent = root.Bool("persistent");
        obj.Managed = root.Bool("managed", true);
        obj.UsesPhysics = root.Bool("physicsObject");
        obj.IsSensor = root.Bool("physicsSensor");
        obj.CollisionShape = (CollisionShapeFlags)root.Int("physicsShape", 1);
        obj.Group = (uint)root.Int("physicsGroup", 1);
        obj.Density = root.Float("physicsDensity", 0.5f);
        obj.Restitution = root.Float("physicsRestitution", 0.1f);
        obj.LinearDamping = root.Float("physicsLinearDamping", 0.1f);
        obj.AngularDamping = root.Float("physicsAngularDamping", 0.1f);
        obj.Friction = root.Float("physicsFriction", 0.2f);
        obj.Awake = root.Bool("physicsStartAwake", true);
        obj.Kinematic = root.Bool("physicsKinematic");
        obj.PhysicsVertices.Clear();
        foreach (var point in root.Items("physicsShapePoints"))
            obj.PhysicsVertices.Add(new GameMakerGameObject.GameMakerPhysicsVertex { X = point.Float("x"), Y = point.Float("y") });

        var directory = Path.GetDirectoryName(yyPath);
        foreach (var ev in root.Items("eventList"))
        {
            var type = ev.Int("eventType");
            var number = ev.Int("eventNum");
            var other = ev.Child("collisionObjectId").Str("name");
            var entry = EntryName(name, type, number, other);
            var file = Path.Combine(directory, EventFile(type, number, other));
            if (!File.Exists(file))
                continue;
            if (other is not null)
                Shell(data, other);
            group.QueueReplace(entry, File.ReadAllText(file));
        }
        return obj;
    }

    private static GameMakerGameObject Shell(GameMakerData data, string name)
    {
        var obj = data.GameObjects.ByName(name);
        if (obj is null)
        {
            obj = new() { Name = data.Strings.MakeString(name) };
            data.GameObjects.Add(obj);
        }
        return obj;
    }

    private static GameMakerSprite Sprite(GameMakerData data, JsonElement reference)
    {
        var name = reference.Str("name");
        if (name is null)
            return null;
        return data.Sprites.ByName(name) ?? throw new InvalidDataException($"Sprite \"{name}\" is not in the data file.");
    }

    // Collision events are named after the other object, every other event after its number.
    private static string EventFile(int type, int number, string other) =>
        $"{(EventType)type}_{other ?? number.ToString(CultureInfo.InvariantCulture)}.gml";

    private static string EntryName(string obj, int type, int number, string other) =>
        $"gml_Object_{obj}_{EventFile(type, number, other)[..^4]}";

    /// <summary>
    /// Writes <paramref name="obj"/> as <c>{folder}/{name}.yy</c> with one decompiled <c>.gml</c> file per event that has code.
    /// </summary>
    /// <param name="parent">Folder name shown in the asset browser (LTS 2022), or the project name (LTS 2026).</param>
    /// <param name="obj">The object to write.</param>
    /// <param name="globalContext">The decompile context shared by all code.</param>
    /// <param name="format">The project file format.</param>
    /// <param name="folder">The folder to write into.</param>
    /// <param name="data">The game data it belongs to.</param>
    public static void Write(GameMakerGameObject obj, GameMakerData data, GlobalDecompileContext globalContext, string folder, YyFormat format, string parent = "Objects")
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(obj);
        var name = obj.Name.Content;
        Directory.CreateDirectory(folder);
        JsonArray events = [];
        for (var type = 0; type < obj.Events.Count; type++)
        {
            if (obj.Events[type] is not { } list)
                continue;
            foreach (var ev in list)
            {
                var other = type == (int)EventType.Collision ? data.GameObjects[(int)ev.EventSubtype].Name.Content : null;
                var number = other is null ? (int)ev.EventSubtype : 0;
                if (ev.Actions is [{ CodeId: { } code }, ..])
                {
                    var text = new DecompileContext(globalContext, code, new DecompileSettings()).DecompileToString();
                    File.WriteAllText(Path.Combine(folder, EventFile(type, number, other)), text);
                }
                var e = YyFile.Resource(format, "GMEvent", "", "1.0", "v1");
                e["isDnD"] = false;
                e["eventNum"] = number;
                e["eventType"] = type;
                e["collisionObjectId"] = other is null ? null : YyFile.Ref("objects", other);
                events.Add(e);
            }
        }

        var root = YyFile.Resource(format, "GMObject", name, "1.0", "");
        root["spriteId"] = obj.Sprite is null ? null : YyFile.Ref("sprites", obj.Sprite.Name.Content);
        root["solid"] = obj.Solid;
        root["visible"] = obj.Visible;
        root["spriteMaskId"] = obj.TextureMaskId is null ? null : YyFile.Ref("sprites", obj.TextureMaskId.Name.Content);
        root["persistent"] = obj.Persistent;
        root["parentObjectId"] = obj.ParentId is null ? null : YyFile.Ref("objects", obj.ParentId.Name.Content);
        root["physicsObject"] = obj.UsesPhysics;
        root["physicsSensor"] = obj.IsSensor;
        root["physicsShape"] = (int)obj.CollisionShape;
        root["physicsGroup"] = (int)obj.Group;
        root["physicsDensity"] = (double)obj.Density;
        root["physicsRestitution"] = (double)obj.Restitution;
        root["physicsLinearDamping"] = (double)obj.LinearDamping;
        root["physicsAngularDamping"] = (double)obj.AngularDamping;
        root["physicsFriction"] = (double)obj.Friction;
        root["physicsStartAwake"] = obj.Awake;
        root["physicsKinematic"] = obj.Kinematic;
        JsonArray points = [];
        foreach (var v in obj.PhysicsVertices)
            points.Add(new JsonObject { ["x"] = (double)v.X, ["y"] = (double)v.Y });
        root["physicsShapePoints"] = points;
        root["eventList"] = events;
        root["properties"] = new JsonArray();
        root["overriddenProperties"] = new JsonArray();
        if (format == YyFormat.Lts2026)
            root["managed"] = obj.Managed;
        root["parent"] = format == YyFormat.Lts2026
            ? new JsonObject { ["name"] = parent, ["path"] = parent + ".yyp" }
            : new JsonObject { ["name"] = parent, ["path"] = $"folders/{parent}.yy" };
        if (format == YyFormat.Lts2022)
            root["tags"] = new JsonArray();
        YyFile.Write(Path.Combine(folder, name + ".yy"), root);
    }
}
