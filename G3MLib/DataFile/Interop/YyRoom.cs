using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Interop;

/// <summary>
/// Converts a GameMaker room resource (<c>.yy</c> plus creation code files) to and from <see cref="GameMakerRoom"/>.
/// </summary>
/// <remarks>
/// Mapped: room size, persistence, physics, views, instance layers with their instances and creation code, background layers,
/// tile layers (uncompressed tile data only). Asset, path and effect layers and layer properties are skipped, and the
/// skipped layer names are reported. Instance and layer ids are newly assigned. The referenced objects, sprites and tile sets
/// must be in the data file already.
/// </remarks>
public static class YyRoom
{
    private const GameMakerRoom.RoomEntryFlags VersionFlags = GameMakerRoom.RoomEntryFlags.IsGMS2 | GameMakerRoom.RoomEntryFlags.IsGMS2_3 | GameMakerRoom.RoomEntryFlags.IsGM2024_13;

    /// <summary>
    /// Reads the room into the data file and queues its creation code on <paramref name="group"/>.
    /// The caller runs <see cref="CodeImportGroup.Import(bool)"/> once afterwards.
    /// </summary>
    /// <param name="skippedLayers">Receives the names of layers of unsupported types.</param>
    /// <param name="yyPath">Path of the .yy file.</param>
    /// <param name="group">The folder group the resource belongs to.</param>
    public static GameMakerRoom Read(string yyPath, CodeImportGroup group, ICollection<string>? skippedLayers = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        var data = group.Data;
        var root = YyFile.Parse(yyPath);
        var name = root.Str("name") ?? throw new InvalidDataException($"{yyPath}: room has no name.");
        var room = data.Rooms.ByName(name);
        if (room is null)
        {
            room = new() { Name = data.Strings.MakeString(name) };
            data.Rooms.Add(room);
        }

        JsonElement settings = root.Child("roomSettings"), viewSettings = root.Child("viewSettings"), physics = root.Child("physicsSettings");
        room.Caption = data.Strings.MakeString("");
        room.Width = settings.Uint("Width", 1366);
        room.Height = settings.Uint("Height", 768);
        room.Persistent = settings.Bool("persistent");
        room.World = physics.Bool("PhysicsWorld");
        room.GravityX = physics.Float("PhysicsWorldGravityX");
        room.GravityY = physics.Float("PhysicsWorldGravityY", 10);
        room.MetersPerPixel = physics.Float("PhysicsWorldPixToMetres", 0.1f);
        var inherited = data.Rooms.Where(r => r != room).Select(r => r.Flags & VersionFlags).FirstOrDefault();
        room.Flags = (inherited != 0 ? inherited : data.IsVersionAtLeast(2, 3) ? GameMakerRoom.RoomEntryFlags.IsGMS2 | GameMakerRoom.RoomEntryFlags.IsGMS2_3 : GameMakerRoom.RoomEntryFlags.IsGMS2)
            | (viewSettings.Bool("enableViews") ? GameMakerRoom.RoomEntryFlags.EnableViews : 0)
            | (viewSettings.Bool("clearViewBackground") ? GameMakerRoom.RoomEntryFlags.ClearViewBackground : 0)
            | (viewSettings.Bool("clearDisplayBuffer", true) ? 0 : GameMakerRoom.RoomEntryFlags.DoNotClearDisplayBuffer);

        room.Views.Clear();
        foreach (var v in root.Items("views"))
        {
            room.Views.Add(new GameMakerRoom.View
            {
                Enabled = v.Bool("visible"),
                ViewX = v.Int("xview"),
                ViewY = v.Int("yview"),
                ViewWidth = v.Int("wview"),
                ViewHeight = v.Int("hview"),
                PortX = v.Int("xport"),
                PortY = v.Int("yport"),
                PortWidth = v.Int("wport"),
                PortHeight = v.Int("hport"),
                BorderX = v.Uint("hborder"),
                BorderY = v.Uint("vborder"),
                SpeedX = v.Int("hspeed", -1),
                SpeedY = v.Int("vspeed", -1),
                ObjectId = Object(data, v.Child("objectId")),
            });
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(yyPath), "..", ".."));
        room.CreationCodeId = root.Str("creationCodeFile") is { Length: > 0 } codeFile && File.Exists(Path.Combine(projectRoot, codeFile)) ? QueueCode(group, $"gml_Room_{name}_Create", Path.Combine(projectRoot, codeFile)) : null;

        room.GameObjects.Clear();
        room.Layers ??= [];
        room.Layers.Clear();
        uint nextLayerId = 0;
        Dictionary<string, uint> instanceIds = [];
        foreach (var layer in root.Items("layers"))
        {
            var layerName = layer.Str("name");
            var type = layer.Str("resourceType");
            GameMakerRoom.LayerType? layerType = type switch
            {
                "GMRInstanceLayer" => GameMakerRoom.LayerType.Instances,
                "GMRBackgroundLayer" => GameMakerRoom.LayerType.Background,
                "GMRTileLayer" => GameMakerRoom.LayerType.Tiles,
                "GMRAssetLayer" => GameMakerRoom.LayerType.Assets,
                "GMREffectLayer" => GameMakerRoom.LayerType.Effect,
                "GMRPathLayer" => GameMakerRoom.LayerType.Path,
                _ => null,
            };
            if (layerType is null)
            {
                skippedLayers?.Add(layerName);
                continue;
            }
            // A path layer holds no data in the data file; the path itself is not kept.
            GameMakerRoom.Layer.LayerData layerData = layerType switch
            {
                GameMakerRoom.LayerType.Instances => InstanceData(layer, name, group, room, instanceIds),
                GameMakerRoom.LayerType.Background => BackgroundData(layer, data),
                GameMakerRoom.LayerType.Tiles => TileData(layer, data),
                GameMakerRoom.LayerType.Assets => AssetData(layer, layerName, data, skippedLayers),
                GameMakerRoom.LayerType.Effect => new GameMakerRoom.Layer.LayerEffectData { EffectType = data.Strings.MakeString(layer.Str("effectType") ?? ""), Properties = EffectProperties(layer, data) },
                _ => null,
            };
            var offsets = layerType is GameMakerRoom.LayerType.Tiles or GameMakerRoom.LayerType.Background;
            room.Layers.Add(new GameMakerRoom.Layer
            {
                LayerName = data.Strings.MakeString(layerName),
                LayerId = nextLayerId++,
                LayerType = layerType.Value,
                LayerDepth = layer.Int("depth"),
                XOffset = offsets ? layer.Float("x") : 0,
                YOffset = offsets ? layer.Float("y") : 0,
                HSpeed = layer.Float("hspeed"),
                VSpeed = layer.Float("vspeed"),
                IsVisible = layer.Bool("visible", true),
                EffectEnabled = layer.Bool("effectEnabled", true),
                EffectType = layer.Str("effectType") is { } effect ? data.Strings.MakeString(effect) : null,
                EffectProperties = EffectProperties(layer, data),
                Data = layerData,
            });
        }

        // GameMaker 2.3 and later keep the room colour in a background layer and write zero for the legacy fields.
        room.Speed = 0;
        room.Backgrounds.Clear();
        (room.Left, room.Top, room.Right, room.Bottom) = (0, 0, 0, 0);
        room.BackgroundColor = 0xFF000000;
        room.DrawBackgroundColor = false;

        room.InstanceCreationOrderIDs = new GameMakerRoom.InstanceIDList();
        foreach (var order in root.Items("instanceCreationOrder"))
        {
            if (instanceIds.TryGetValue(order.Str("name"), out var id))
                room.InstanceCreationOrderIDs.InstanceIDs.Add(id);
        }
        return room;
    }

    private static GameMakerGameObject Object(GameMakerData data, JsonElement reference)
    {
        var name = reference.Str("name");
        return name is null ? null : data.GameObjects.ByName(name) ?? throw new InvalidDataException($"Object \"{name}\" is not in the data file.");
    }

    private static GameMakerCode QueueCode(CodeImportGroup group, string entryName, string file)
    {
        var code = group.Data.Code.ByName(entryName) ?? GameMakerCode.CreateEmptyEntry(group.Data, group.Data.Strings.MakeString(entryName));
        group.QueueReplace(code, File.ReadAllText(file));
        return code;
    }

    private static GameMakerRoom.Layer.LayerInstancesData InstanceData(JsonElement layer, string room, CodeImportGroup group, GameMakerRoom target, Dictionary<string, uint> ids)
    {
        var data = group.Data;
        GameMakerRoom.Layer.LayerInstancesData result = new();
        foreach (var i in layer.Items("instances"))
        {
            var name = i.Str("name");
            var id = ++data.GeneralInfo.LastObj;
            ids[name] = id;
            GameMakerRoom.GameObject instance = new()
            {
                ObjectDefinition = Object(data, i.Child("objectId")),
                X = (int)i.Float("x"),
                Y = (int)i.Float("y"),
                ScaleX = i.Float("scaleX", 1),
                ScaleY = i.Float("scaleY", 1),
                Color = i.Uint("colour", 0xFFFFFFFF),
                Rotation = i.Float("rotation"),
                ImageSpeed = i.Float("imageSpeed", 1),
                ImageIndex = i.Int("imageIndex"),
                InstanceID = id,
            };
            if (i.Bool("hasCreationCode"))
                throw new NotSupportedException($"Instance \"{name}\" in room \"{room}\" has creation code, which is not supported for instances yet.");
            target.GameObjects.Add(instance);
            result.Instances.Add(instance);
        }
        return result;
    }

    private static GameMakerSimpleList<GameMakerRoom.EffectProperty> EffectProperties(JsonElement layer, GameMakerData data)
    {
        GameMakerSimpleList<GameMakerRoom.EffectProperty> result = [];
        foreach (var p in layer.Items("properties"))
        {
            result.Add(new GameMakerRoom.EffectProperty
            {
                Kind = (GameMakerRoom.EffectProperty.PropertyType)p.Int("type"),
                Name = data.Strings.MakeString(p.Str("name") ?? ""),
                Value = data.Strings.MakeString(p.Str("value") ?? ""),
            });
        }
        return result;
    }

    // Only sprite graphics are read; sequence, particle system and text graphics are listed in skipped as "layer: type name".
    private static GameMakerRoom.Layer.LayerAssetsData AssetData(JsonElement layer, string layerName, GameMakerData data, ICollection<string> skipped)
    {
        GameMakerRoom.Layer.LayerAssetsData result = new()
        {
            LegacyTiles = [],
            Sprites = [],
            Sequences = data.IsVersionAtLeast(2, 3) ? new() : null,
            NineSlices = data.IsVersionAtLeast(2, 3) && !data.IsVersionAtLeast(2, 3, 2) ? new() : null,
            ParticleSystems = data.IsVersionAtLeast(2, 3) && data.IsNonLTSVersionAtLeast(2023, 2) ? new() : null,
            TextItems = data.IsVersionAtLeast(2, 3) && data.IsVersionAtLeast(2024, 6) ? new() : null,
        };
        foreach (var asset in layer.Items("assets"))
        {
            var type = asset.Str("resourceType");
            if (type != "GMRSpriteGraphic")
            {
                skipped?.Add($"{layerName}: {type} {asset.Str("name")}");
                continue;
            }
            var sprite = asset.Child("spriteId").Str("name");
            result.Sprites.Add(new GameMakerRoom.SpriteInstance
            {
                Name = data.Strings.MakeString(asset.Str("name") ?? ""),
                Sprite = sprite is null ? null : data.Sprites.ByName(sprite) ?? throw new InvalidDataException($"Sprite \"{sprite}\" is not in the data file."),
                X = (int)asset.Float("x"),
                Y = (int)asset.Float("y"),
                ScaleX = asset.Float("scaleX", 1),
                ScaleY = asset.Float("scaleY", 1),
                Color = asset.Uint("colour", 0xFFFFFFFF),
                AnimationSpeed = asset.Float("animationSpeed", 1),
                FrameIndex = asset.Float("headPosition"),
                Rotation = asset.Float("rotation"),
            });
        }
        return result;
    }

    private static GameMakerRoom.Layer.LayerBackgroundData BackgroundData(JsonElement layer, GameMakerData data)
    {
        var sprite = layer.Child("spriteId").Str("name");
        return new GameMakerRoom.Layer.LayerBackgroundData
        {
            Visible = layer.Bool("visible", true),
            Foreground = false,
            Sprite = sprite is null ? null : data.Sprites.ByName(sprite) ?? throw new InvalidDataException($"Sprite \"{sprite}\" is not in the data file."),
            TiledHorizontally = layer.Bool("htiled"),
            TiledVertically = layer.Bool("vtiled"),
            Stretch = layer.Bool("stretch"),
            Color = layer.Uint("colour", 0xFF000000),
            AnimationSpeed = layer.Float("animationFPS", 15),
            AnimationSpeedType = (AnimationSpeedType)layer.Int("animationSpeedType"),
        };
    }

    private static GameMakerRoom.Layer.LayerTilesData TileData(JsonElement layer, GameMakerData data)
    {
        var tiles = layer.Child("tiles");
        if (tiles.ValueKind == JsonValueKind.Object && tiles.Child("TileCompressedData").ValueKind != JsonValueKind.Undefined)
            throw new NotSupportedException("Compressed tile data is not supported.");
        uint width = tiles.Uint("SerialiseWidth"), height = tiles.Uint("SerialiseHeight");
        uint[] flat = [.. tiles.Items("TileSerialiseData").Select(t => (uint)t.GetDouble())];
        var rows = new uint[height][];
        for (var y = 0; y < height; y++)
        {
            rows[y] = new uint[width];
            Array.Copy(flat, y * (int)width, rows[y], 0, Math.Min((int)width, Math.Max(0, flat.Length - y * (int)width)));
        }
        var tileset = layer.Child("tilesetId").Str("name");
        return new GameMakerRoom.Layer.LayerTilesData
        {
            Background = tileset is null ? null : data.Backgrounds.ByName(tileset) ?? throw new InvalidDataException($"Tile set \"{tileset}\" is not in the data file."),
            TilesX = width,
            TilesY = height,
            TileData = rows,
        };
    }

    /// <summary>
    /// Writes <paramref name="room"/> as <c>{folder}/{name}.yy</c>. Creation code is decompiled to <c>RoomCreationCode.gml</c> in the same folder.
    /// </summary>
    /// <param name="parent">Folder name shown in the asset browser (LTS 2022), or the project name (LTS 2026).</param>
    /// <param name="room">The room to write.</param>
    /// <param name="globalContext">The decompile context shared by all code.</param>
    /// <param name="format">The project file format.</param>
    /// <param name="folder">The folder to write into.</param>
    /// <param name="data">The game data it belongs to.</param>
    public static void Write(GameMakerRoom room, GameMakerData data, GlobalDecompileContext globalContext, string folder, YyFormat format, string parent = "Rooms")
    {
        ArgumentNullException.ThrowIfNull(room);
        if (room.Layers is null)
            throw new NotSupportedException($"Room \"{room.Name.Content}\" has no layers (data file before GameMaker Studio 2).");
        var name = room.Name.Content;
        Directory.CreateDirectory(folder);
        var instanceNames = room.GameObjects.ToDictionary(o => o.InstanceID, o => $"inst_{o.InstanceID:X8}");

        JsonArray layers = [];
        foreach (var layer in room.Layers)
        {
            var l = layer.LayerType switch
            {
                GameMakerRoom.LayerType.Instances => InstanceLayer(layer, format, instanceNames),
                GameMakerRoom.LayerType.Background => BackgroundLayer(layer, format),
                GameMakerRoom.LayerType.Tiles => TileLayer(layer, format),
                GameMakerRoom.LayerType.Assets => AssetLayer(layer, format),
                GameMakerRoom.LayerType.Effect => LayerBase(layer, "GMREffectLayer", format),
                GameMakerRoom.LayerType.Path => PathLayer(layer, format),
                _ => null,
            };
            if (l is not null)
                layers.Add(l);
        }

        JsonArray views = [];
        foreach (var v in room.Views)
        {
            views.Add(new JsonObject
            {
                ["inherit"] = false,
                ["visible"] = v.Enabled,
                ["xview"] = v.ViewX,
                ["yview"] = v.ViewY,
                ["wview"] = v.ViewWidth,
                ["hview"] = v.ViewHeight,
                ["xport"] = v.PortX,
                ["yport"] = v.PortY,
                ["wport"] = v.PortWidth,
                ["hport"] = v.PortHeight,
                ["hborder"] = (int)v.BorderX,
                ["vborder"] = (int)v.BorderY,
                ["hspeed"] = v.SpeedX,
                ["vspeed"] = v.SpeedY,
                ["objectId"] = v.ObjectId is null ? null : YyFile.Ref("objects", v.ObjectId.Name.Content),
            });
        }

        var codeFile = "";
        if (room.CreationCodeId is { } code)
        {
            codeFile = $"rooms/{name}/RoomCreationCode.gml";
            File.WriteAllText(Path.Combine(folder, "RoomCreationCode.gml"), new DecompileContext(globalContext, code, new DecompileSettings()).DecompileToString());
        }

        var root = YyFile.Resource(format, "GMRoom", name, "1.0", "v1");
        root["isDnd"] = false;
        root["volume"] = 1.0;
        root["parentRoom"] = null;
        root["views"] = views;
        root["layers"] = layers;
        root["inheritLayers"] = false;
        root["creationCodeFile"] = codeFile;
        root["inheritCode"] = false;
        root["instanceCreationOrder"] = new JsonArray((room.InstanceCreationOrderIDs?.InstanceIDs ?? []).Where(instanceNames.ContainsKey)
            .Select(id => (JsonNode)new JsonObject { ["name"] = instanceNames[id], ["path"] = $"rooms/{name}/{name}.yy" }).ToArray());
        root["inheritCreationOrder"] = false;
        root["sequenceId"] = null;
        root["roomSettings"] = new JsonObject { ["inheritRoomSettings"] = false, ["Width"] = (int)room.Width, ["Height"] = (int)room.Height, ["persistent"] = room.Persistent };
        root["viewSettings"] = new JsonObject
        {
            ["inheritViewSettings"] = false,
            ["enableViews"] = room.Flags.HasFlag(GameMakerRoom.RoomEntryFlags.EnableViews),
            ["clearViewBackground"] = room.Flags.HasFlag(GameMakerRoom.RoomEntryFlags.ClearViewBackground),
            ["clearDisplayBuffer"] = !room.Flags.HasFlag(GameMakerRoom.RoomEntryFlags.DoNotClearDisplayBuffer),
        };
        root["physicsSettings"] = new JsonObject
        {
            ["inheritPhysicsSettings"] = false,
            ["PhysicsWorld"] = room.World,
            ["PhysicsWorldGravityX"] = (double)room.GravityX,
            ["PhysicsWorldGravityY"] = (double)room.GravityY,
            ["PhysicsWorldPixToMetres"] = (double)room.MetersPerPixel,
        };
        root["parent"] = format == YyFormat.Lts2026
            ? new JsonObject { ["name"] = parent, ["path"] = parent + ".yyp" }
            : new JsonObject { ["name"] = parent, ["path"] = $"folders/{parent}.yy" };
        if (format == YyFormat.Lts2022)
            root["tags"] = new JsonArray();
        YyFile.Write(Path.Combine(folder, name + ".yy"), root);
    }

    private static JsonObject LayerBase(GameMakerRoom.Layer layer, string type, YyFormat format)
    {
        var l = YyFile.Resource(format, type, layer.LayerName.Content, "1.0", "");
        l["visible"] = layer.IsVisible;
        l["depth"] = layer.LayerDepth;
        l["userdefinedDepth"] = false;
        l["inheritLayerDepth"] = false;
        l["inheritLayerSettings"] = false;
        l["gridX"] = 32;
        l["gridY"] = 32;
        l["layers"] = new JsonArray();
        l["hierarchyFrozen"] = false;
        l["effectEnabled"] = layer.EffectEnabled;
        l["effectType"] = layer.EffectType?.Content;
        JsonArray properties = [];
        foreach (var p in layer.EffectProperties ?? [])
            properties.Add(new JsonObject { ["name"] = p.Name.Content, ["type"] = (int)p.Kind, ["value"] = p.Value.Content });
        l["properties"] = properties;
        return l;
    }

    private static JsonObject PathLayer(GameMakerRoom.Layer layer, YyFormat format)
    {
        var l = LayerBase(layer, "GMRPathLayer", format);
        l["pathId"] = null;
        l["colour"] = 4278190335.0;
        return l;
    }

    // Sequence, particle system and text items of an asset layer are not written.
    private static JsonObject AssetLayer(GameMakerRoom.Layer layer, YyFormat format)
    {
        var l = LayerBase(layer, "GMRAssetLayer", format);
        JsonArray assets = [];
        foreach (var s in layer.AssetsData.Sprites ?? [])
        {
            var a = YyFile.Resource(format, "GMRSpriteGraphic", s.Name.Content, "1.0", "v1");
            a["spriteId"] = s.Sprite is null ? null : YyFile.Ref("sprites", s.Sprite.Name.Content);
            a["headPosition"] = (double)s.FrameIndex;
            a["rotation"] = (double)s.Rotation;
            a["scaleX"] = (double)s.ScaleX;
            a["scaleY"] = (double)s.ScaleY;
            a["animationSpeed"] = (double)s.AnimationSpeed;
            a["colour"] = (double)s.Color;
            a["inheritedItemId"] = null;
            a["frozen"] = false;
            a["ignore"] = false;
            a["inheritItemSettings"] = false;
            a["x"] = (double)s.X;
            a["y"] = (double)s.Y;
            assets.Add(a);
        }
        l["assets"] = assets;
        return l;
    }

    private static JsonObject InstanceLayer(GameMakerRoom.Layer layer, YyFormat format, Dictionary<uint, string> names)
    {
        var l = LayerBase(layer, "GMRInstanceLayer", format);
        JsonArray instances = [];
        foreach (var o in layer.InstancesData.Instances.Where(i => !i.Nonexistent))
        {
            var i = YyFile.Resource(format, "GMRInstance", names[o.InstanceID], "1.0", "v4");
            i["properties"] = new JsonArray();
            i["isDnd"] = false;
            i["objectId"] = o.ObjectDefinition is null ? null : YyFile.Ref("objects", o.ObjectDefinition.Name.Content);
            i["inheritCode"] = false;
            i["hasCreationCode"] = false;
            i["colour"] = (double)o.Color;
            i["rotation"] = (double)o.Rotation;
            i["scaleX"] = (double)o.ScaleX;
            i["scaleY"] = (double)o.ScaleY;
            i["imageIndex"] = o.ImageIndex;
            i["imageSpeed"] = (double)o.ImageSpeed;
            i["inheritedItemId"] = null;
            i["frozen"] = false;
            i["ignore"] = false;
            i["inheritItemSettings"] = false;
            i["x"] = (double)o.X;
            i["y"] = (double)o.Y;
            instances.Add(i);
        }
        l["instances"] = instances;
        return l;
    }

    private static JsonObject BackgroundLayer(GameMakerRoom.Layer layer, YyFormat format)
    {
        var d = layer.BackgroundData;
        var l = LayerBase(layer, "GMRBackgroundLayer", format);
        l["spriteId"] = d.Sprite is null ? null : YyFile.Ref("sprites", d.Sprite.Name.Content);
        l["colour"] = (double)d.Color;
        l["x"] = (int)layer.XOffset;
        l["y"] = (int)layer.YOffset;
        l["htiled"] = d.TiledHorizontally;
        l["vtiled"] = d.TiledVertically;
        l["hspeed"] = (double)layer.HSpeed;
        l["vspeed"] = (double)layer.VSpeed;
        l["stretch"] = d.Stretch;
        l["animationFPS"] = (double)d.AnimationSpeed;
        l["animationSpeedType"] = (int)d.AnimationSpeedType;
        l["userdefinedAnimFPS"] = false;
        return l;
    }

    private static JsonObject TileLayer(GameMakerRoom.Layer layer, YyFormat format)
    {
        var d = layer.TilesData;
        var l = LayerBase(layer, "GMRTileLayer", format);
        l["tilesetId"] = d.Background is null ? null : new JsonObject { ["name"] = d.Background.Name.Content, ["path"] = $"tilesets/{d.Background.Name.Content}/{d.Background.Name.Content}.yy" };
        l["x"] = (int)layer.XOffset;
        l["y"] = (int)layer.YOffset;
        l["tiles"] = new JsonObject
        {
            ["TileDataFormat"] = 1,
            ["SerialiseWidth"] = (int)d.TilesX,
            ["SerialiseHeight"] = (int)d.TilesY,
            ["TileSerialiseData"] = new JsonArray(d.TileData.SelectMany(r => r).Select(t => (JsonNode)(double)t).ToArray()),
        };
        return l;
    }
}
