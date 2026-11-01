using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.DataFile.Interop;

/// <summary>
/// Converts a GameMaker sprite resource (<c>.yy</c> plus frame PNGs) to and from <see cref="GameMakerSprite"/>.
/// </summary>
/// <remarks>
/// Only the first image layer's composite (the <c>{frame}.png</c> next to the <c>.yy</c>) is read; layers, tags and
/// swatches are not kept. Collision kinds ellipse and diamond become axis-aligned rectangles.
/// </remarks>
public static class YySprite
{
    private const int MaskAlphaThreshold = 0;

    /// <summary>
    /// Reads a sprite and queues its frames on <paramref name="repacker"/>. The caller runs <see cref="TexturePageRepacker.Repack"/>
    /// once after all sprites are read. A sprite with the same name is overwritten in place.
    /// </summary>
    public static GameMakerSprite Read(string yyPath, GameMakerData data, TexturePageRepacker repacker, GameMakerTextureGroupInfo? group = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(repacker);
        var root = YyFile.Parse(yyPath);
        var name = root.Str("name") ?? throw new InvalidDataException($"{yyPath}: sprite has no name.");
        var directory = Path.GetDirectoryName(yyPath);
        var sequence = root.Child("sequence");

        var sprite = data.Sprites.ByName(name);
        if (sprite is null)
        {
            sprite = new() { Name = data.Strings.MakeString(name) };
            data.Sprites.Add(sprite);
        }

        sprite.Width = (uint)root.Int("width");
        sprite.Height = (uint)root.Int("height");
        sprite.MarginLeft = root.Int("bbox_left");
        sprite.MarginRight = root.Int("bbox_right");
        sprite.MarginTop = root.Int("bbox_top");
        sprite.MarginBottom = root.Int("bbox_bottom");
        sprite.BBoxMode = (uint)root.Int("bboxMode");
        sprite.Smooth = root.Bool("edgeFiltering");
        sprite.Transparent = false;
        sprite.Preload = false;
        var kind = root.Int("collisionKind", 1);
        sprite.SepMasks = kind switch
        {
            0 or 2 or 3 or 4 => GameMakerSprite.SepMaskType.Precise,
            5 => GameMakerSprite.SepMaskType.RotatedRect,
            _ => GameMakerSprite.SepMaskType.AxisAlignedRect,
        };
        (sprite.OriginX, sprite.OriginY) = Origin(root, sequence);

        sprite.Textures.Clear();
        sprite.CollisionMasks.Clear();
        string[] frames = [.. root.Items("frames").Select(f => f.Str("name"))];
        for (var i = 0; i < frames.Length; i++)
        {
            var png = Path.Combine(directory, frames[i] + ".png");
            using var image = TextureWorker.ReadBGRAImageFromFile(png);
            // GameMaker stores no mask for rectangle kinds; the runtime derives it from the bounding box.
            if (sprite.SepMasks == GameMakerSprite.SepMaskType.Precise && (i == 0 || kind == 4))
                sprite.CollisionMasks.Add(Mask(sprite, data, image, root.Int("collisionTolerance")));
            sprite.Textures.Add(new GameMakerSprite.TextureEntry { Texture = repacker.Add(new MagickImage(image), group) });
        }

        sprite.IsSpecialType = true;
        sprite.SSpriteType = GameMakerSprite.SpriteType.Normal;
        sprite.SVersion = data.Sprites.Where(s => s.IsSpecialType).Select(s => s.SVersion).DefaultIfEmpty(data.IsVersionAtLeast(2023, 2) ? 3u : 2u).Max();
        sprite.GMS2PlaybackSpeed = sequence.Float("playbackSpeed", 30);
        sprite.GMS2PlaybackSpeedType = (AnimSpeedType)sequence.Int("playbackSpeedType");
        sprite.V2Sequence = NewSequence(sprite, data, frames.Length, sequence);
        sprite.V3NineSlice = sprite.SVersion >= 3 ? NineSlice(root.Child("nineSlice")) : null;
        return sprite;
    }

    private static (int X, int Y) Origin(JsonElement root, JsonElement sequence)
    {
        if (sequence.ValueKind == JsonValueKind.Object)
            return (sequence.Int("xorigin"), sequence.Int("yorigin"));
        int w = root.Int("width"), h = root.Int("height"), origin = root.Int("origin", 9);
        return origin >= 9 ? (0, 0) : (origin % 3 * (w / 2), origin / 3 * (h / 2));
    }

    private static GameMakerSprite.NineSlice NineSlice(JsonElement e)
    {
        GameMakerSprite.NineSlice slice = new()
        {
            Enabled = e.Bool("enabled"),
            Left = e.Int("left"),
            Top = e.Int("top"),
            Right = e.Int("right"),
            Bottom = e.Int("bottom"),
        };
        var i = 0;
        foreach (var mode in e.Items("tileMode"))
        {
            if (i < slice.TileModes.Length)
                slice.TileModes[i++] = (GameMakerSprite.NineSlice.TileMode)(int)mode.GetDouble();
        }
        return slice;
    }

    private static GameMakerSequence NewSequence(GameMakerSprite sprite, GameMakerData data, int frameCount, JsonElement seq)
    {
        GameMakerSequence.Track track = new()
        {
            ModelName = data.Strings.MakeString("GMSpriteFramesTrack"),
            Name = data.Strings.MakeString("frames"),
            Tags = [],
            Tracks = [],
            OwnedResources = [],
            Keyframes = new GameMakerSequence.SpriteFramesKeyframes { List = [] },
        };
        var keyframes = (GameMakerSequence.SpriteFramesKeyframes)track.Keyframes;
        for (var i = 0; i < frameCount; i++)
        {
            GameMakerSequence.Keyframe<GameMakerSequence.SpriteFramesKeyframes.Data> key = new() { Key = i, Length = 1 };
            key.Channels.Add(new() { Channel = 0, Value = new() { Value = i } });
            keyframes.List.Add(key);
        }

        GameMakerSequence sequence = new()
        {
            Name = sprite.Name,
            Playback = (GameMakerSequence.PlaybackType)seq.Int("playback", 1),
            PlaybackSpeed = sprite.GMS2PlaybackSpeed,
            PlaybackSpeedType = sprite.GMS2PlaybackSpeedType,
            Length = frameCount,
            OriginX = sprite.OriginX,
            OriginY = sprite.OriginY,
            Volume = seq.Float("volume", 1),
            Width = sprite.Width,
            Height = sprite.Height,
            BroadcastMessages = [],
            Moments = [],
            Tracks = [],
            FunctionIDs = [],
        };
        sequence.Tracks.Add(track);
        return sequence;
    }

    // Bit set where the frame is more opaque than the tolerance, MSB first, over the collision rectangle.
    private static GameMakerSprite.MaskEntry Mask(GameMakerSprite sprite, GameMakerData data, MagickImage image, int tolerance)
    {
        var mask = sprite.NewMaskEntry(data);
        var bbox = data.IsVersionAtLeast(2024, 6);
        int left = bbox ? sprite.MarginLeft : 0, top = bbox ? sprite.MarginTop : 0;
        var rgba = image.ToByteArray(MagickFormat.Rgba);
        var stride = (mask.Width + 7) / 8;
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                int sx = left + x, sy = top + y;
                if (sx < 0 || sy < 0 || sx >= image.Width || sy >= image.Height)
                    continue;
                if (rgba[(sy * (int)image.Width + sx) * 4 + 3] > Math.Max(tolerance, MaskAlphaThreshold))
                    mask.Data[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return mask;
    }

    /// <summary>
    /// Writes <paramref name="sprite"/> as <c>{folder}/{name}.yy</c> with its frame PNGs.
    /// </summary>
    /// <param name="parent">Folder name shown in the asset browser (LTS 2022), or the project name (LTS 2026).</param>
    /// <param name="sprite">The sprite to write.</param>
    /// <param name="format">The project file format.</param>
    /// <param name="folder">The folder to write into.</param>
    public static void Write(GameMakerSprite sprite, string folder, YyFormat format, string parent = "Sprites")
    {
        ArgumentNullException.ThrowIfNull(sprite);
        var name = sprite.Name.Content;
        Directory.CreateDirectory(folder);
        var self = YyFile.Ref("sprites", name);
        JsonArray frames = [];
        var layerId = YyFile.StableGuid($"{name}/layer");
        string[] frameIds = [.. Enumerable.Range(0, sprite.Textures.Count).Select(i => YyFile.StableGuid($"{name}/frame/{i}"))];

        using TextureWorker worker = new();
        for (var i = 0; i < frameIds.Length; i++)
        {
            var png = Path.Combine(folder, frameIds[i] + ".png");
            worker.ExportAsPNG(sprite.Textures[i].Texture, png, name, true);
            var layerPng = Path.Combine(folder, "layers", frameIds[i], layerId + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(layerPng));
            File.Copy(png, layerPng, true);
            var frame = YyFile.Resource(format, "GMSpriteFrame", frameIds[i], "1.0", "v1");
            if (format == YyFormat.Lts2022)
            {
                JsonObject FrameRef(string layer) => new() { ["FrameId"] = new JsonObject { ["name"] = frameIds[i], ["path"] = self["path"].ToString() }, ["LayerId"] = layer is null ? null : new JsonObject { ["name"] = layer, ["path"] = self["path"].ToString() } };
                frame["compositeImage"] = FrameRef(null);
                frame["images"] = new JsonArray(FrameRef(layerId));
                frame["parent"] = self.DeepClone();
            }
            frames.Add(frame);
        }

        var perFrame = sprite.CollisionMasks.Count > 1;
        var root = YyFile.Resource(format, "GMSprite", name, "1.0", "v2");
        root["bboxMode"] = (int)sprite.BBoxMode;
        root["collisionKind"] = sprite.SepMasks switch
        {
            GameMakerSprite.SepMaskType.Precise => perFrame ? 4 : 0,
            GameMakerSprite.SepMaskType.RotatedRect => 5,
            _ => 1,
        };
        root["type"] = 0;
        root["origin"] = 9;
        root["preMultiplyAlpha"] = false;
        root["edgeFiltering"] = sprite.Smooth;
        root["collisionTolerance"] = 0;
        root["swfPrecision"] = 2.525;
        root["bbox_left"] = sprite.MarginLeft;
        root["bbox_right"] = sprite.MarginRight;
        root["bbox_top"] = sprite.MarginTop;
        root["bbox_bottom"] = sprite.MarginBottom;
        root["HTile"] = false;
        root["VTile"] = false;
        root["For3D"] = false;
        root["width"] = (int)sprite.Width;
        root["height"] = (int)sprite.Height;
        root["textureGroupId"] = new JsonObject { ["name"] = "Default", ["path"] = "texturegroups/Default" };
        root["swatchColours"] = null;
        root["gridX"] = 0;
        root["gridY"] = 0;
        root["frames"] = frames;
        root["sequence"] = Sequence(sprite, format, self, frameIds);
        root["layers"] = new JsonArray(Layer(format, layerId));
        root["nineSlice"] = NineSliceJson(sprite, format);
        root["parent"] = format == YyFormat.Lts2026
            ? new JsonObject { ["name"] = parent, ["path"] = parent + ".yyp" }
            : new JsonObject { ["name"] = parent, ["path"] = $"folders/{parent}.yy" };
        if (format == YyFormat.Lts2022)
            root["tags"] = new JsonArray();
        YyFile.Write(Path.Combine(folder, name + ".yy"), root);
    }

    private static JsonObject Layer(YyFormat format, string id)
    {
        var layer = YyFile.Resource(format, "GMImageLayer", id, "1.0", "");
        layer["visible"] = true;
        layer["isLocked"] = false;
        layer["blendMode"] = 0;
        layer["opacity"] = 100.0;
        layer["displayName"] = "default";
        return layer;
    }

    private static JsonObject NineSliceJson(GameMakerSprite sprite, YyFormat format)
    {
        var slice = sprite.V3NineSlice;
        var o = YyFile.Resource(format, "GMNineSliceData", "", "1.0", "");
        o.Remove("name");
        o.Remove("%Name");
        o["enabled"] = slice?.Enabled ?? false;
        o["left"] = slice?.Left ?? 0;
        o["top"] = slice?.Top ?? 0;
        o["right"] = slice?.Right ?? 0;
        o["bottom"] = slice?.Bottom ?? 0;
        o["guideColour"] = new JsonArray(4294902015, 4294902015, 4294902015, 4294902015);
        o["highlightColour"] = 1728023040;
        o["highlightStyle"] = 0;
        o["tileMode"] = new JsonArray((slice?.TileModes ?? new GameMakerSprite.NineSlice.TileMode[5]).Select(m => (JsonNode)(int)m).ToArray());
        return o;
    }

    private static JsonObject Sequence(GameMakerSprite sprite, YyFormat format, JsonObject self, string[] frameIds)
    {
        var name = sprite.Name.Content;
        JsonObject Store(string type, JsonArray keys)
        {
            JsonObject store = [];
            if (format == YyFormat.Lts2026)
                store[$"$KeyframeStore<{type}>"] = "";
            store["Keyframes"] = keys;
            store["resourceType"] = $"KeyframeStore<{type}>";
            store["resourceVersion"] = format == YyFormat.Lts2026 ? "2.0" : "1.0";
            return store;
        }
        JsonObject FrameRef(string id) => new() { ["name"] = id, ["path"] = self["path"].ToString() };
        JsonArray keys = [];
        for (var i = 0; i < frameIds.Length; i++)
        {
            JsonObject key = new()
            {
                ["id"] = YyFile.StableGuid($"{name}/key/{i}"),
                ["Key"] = (double)i,
                ["Length"] = 1.0,
                ["Stretch"] = false,
                ["Disabled"] = false,
                ["IsCreationKey"] = false,
                ["Channels"] = new JsonObject
                {
                    ["0"] = new JsonObject { ["Id"] = FrameRef(frameIds[i]), ["resourceVersion"] = format == YyFormat.Lts2026 ? "2.0" : "1.0", ["resourceType"] = "SpriteFrameKeyframe" },
                },
                ["resourceVersion"] = format == YyFormat.Lts2026 ? "2.0" : "1.0",
                ["resourceType"] = "Keyframe<SpriteFrameKeyframe>",
            };
            keys.Add(key);
        }
        var track = YyFile.Resource(format, "GMSpriteFramesTrack", "frames", "1.0", "");
        track["spriteId"] = null;
        track["keyframes"] = Store("SpriteFrameKeyframe", keys);
        track["trackColour"] = 0;
        track["inheritsTrackColour"] = true;
        track["builtinName"] = 0;
        track["traits"] = 0;
        track["interpolation"] = 1;
        track["tags"] = new JsonArray();
        track["events"] = new JsonArray();
        track["isCreationTrack"] = false;
        track["modifiers"] = new JsonArray();

        var seq = YyFile.Resource(format, "GMSequence", name, "1.4", "v1");
        seq["spriteId"] = self.DeepClone();
        seq["timeUnits"] = 1;
        seq["playback"] = (int)(sprite.V2Sequence?.Playback ?? GameMakerSequence.PlaybackType.Loop);
        seq["playbackSpeed"] = (double)sprite.GMS2PlaybackSpeed;
        seq["playbackSpeedType"] = (int)sprite.GMS2PlaybackSpeedType;
        seq["autoRecord"] = true;
        seq["volume"] = (double)(sprite.V2Sequence?.Volume ?? 1);
        seq["length"] = (double)frameIds.Length;
        seq["events"] = Store("MessageEventKeyframe", []);
        seq["moments"] = Store("MomentsEventKeyframe", []);
        seq["tracks"] = new JsonArray(track);
        seq["visibleRange"] = null;
        seq["lockOrigin"] = false;
        seq["showBackdrop"] = true;
        seq["showBackdropImage"] = false;
        seq["backdropImagePath"] = "";
        seq["backdropImageOpacity"] = 0.5;
        seq["backdropWidth"] = 1366;
        seq["backdropHeight"] = 768;
        seq["backdropXOffset"] = 0.0;
        seq["backdropYOffset"] = 0.0;
        seq["xorigin"] = sprite.OriginX;
        seq["yorigin"] = sprite.OriginY;
        seq["eventToFunction"] = new JsonObject();
        seq["eventStubScript"] = null;
        return seq;
    }
}
