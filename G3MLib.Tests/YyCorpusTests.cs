using System.Diagnostics;
using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Interop;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using G3MLib.Modding.Resources;
using ImageMagick;

namespace G3MLib.Tests;

/// <summary>
/// Checks against GameMaker projects in the folder named by G3MLIB_PROJECTS (a project "TEST" and a project "Cover Assault" inside it). Skipped without it.
/// </summary>
public sealed class YyCorpusTests : IDisposable
{
    private static readonly string Projects = Environment.GetEnvironmentVariable("G3MLIB_PROJECTS") ?? Path.Combine(Path.GetTempPath(), "g3mlib-no-projects");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3m-yycorpus-" + Guid.NewGuid().ToString("N"));

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

    private static byte[][] Frames(GameMakerSprite sprite)
    {
        using TextureWorker worker = new();
        return [.. sprite.Textures.Select(t => { using var image = worker.GetTextureFor(t.Texture, sprite.Name.Content, true); return image.ToByteArray(MagickFormat.Rgba); })];
    }

    private static GameMakerSprite ImportSprite(GameMakerData data, string yy)
    {
        TexturePageRepacker repacker = new(data);
        var sprite = YySprite.Read(yy, data, repacker);
        repacker.Repack();
        return sprite;
    }

    // The fixture was built by GameMaker from a project with the same Sprite1 as TEST, so GameMaker's own frames are the reference.
    [Fact]
    public void TestSpriteFramesEqualGameMakersOwnBuild()
    {
        var yy = Path.Combine(Projects, "TEST", "sprites", "Sprite1", "Sprite1.yy");
        Assert.SkipUnless(File.Exists(yy), "TEST project not found");
        using var data = Load("data-2026.win");
        var built = data.Sprites.ByName("Sprite1");
        Assert.SkipUnless(built is not null, "fixture has no Sprite1");
        var reference = Frames(built);

        var imported = ImportSprite(data, yy);

        Assert.Same(built, imported);
        Assert.Equal(reference, Frames(imported));
    }

    [Fact]
    public void TestProjectResourcesImportAndWriteBack()
    {
        var project = Path.Combine(Projects, "TEST");
        Assert.SkipUnless(File.Exists(Path.Combine(project, "TEST.yyp")), "TEST project not found");
        using var data = Load("data-2026.win");
        ImportSprite(data, Path.Combine(project, "sprites", "Sprite1", "Sprite1.yy"));
        CodeImportGroup group = new(data);
        YyScript.Read(Path.Combine(project, "scripts", "Script1", "Script1.yy"), group);
        YyObject.Read(Path.Combine(project, "objects", "Object1", "Object1.yy"), group);
        YyRoom.Read(Path.Combine(project, "rooms", "Room1", "Room1.yy"), group);
        group.Import();

        var folder = Path.Combine(_root, "out");
        YySprite.Write(data.Sprites.ByName("Sprite1"), Path.Combine(folder, "sprites", "Sprite1"), YyFormat.Lts2026, "TEST");
        YyRoom.Write(data.Rooms.ByName("Room1"), data, new(data), Path.Combine(folder, "rooms", "Room1"), YyFormat.Lts2026, "TEST");

        var original = data.Rooms.ByName("Room1");
        using var second = Load("data-2026.win");
        CodeImportGroup again = new(second);
        var back = YyRoom.Read(Path.Combine(folder, "rooms", "Room1", "Room1.yy"), again);
        again.Import();
        Assert.Equal((original.Width, original.Height, original.Layers.Count), (back.Width, back.Height, back.Layers.Count));
        Assert.Equal(original.Layers.Select(l => l.LayerName.Content), back.Layers.Select(l => l.LayerName.Content));
    }

    [Fact]
    public void CoverAssaultSpritesSurviveYyToModelToYy()
    {
        var sprites = Path.Combine(Projects, "Cover Assault", "sprites");
        Assert.SkipUnless(Directory.Exists(sprites), "Cover Assault project not found");
        List<string> yys = [.. Directory.GetFiles(sprites, "*.yy", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Take(4)];

        using var first = Load("data-2026.win");
        using var second = Load("data-2026.win");
        foreach (var yy in yys)
        {
            var sprite = ImportSprite(first, yy);
            var folder = Path.Combine(_root, "out", sprite.Name.Content);
            YySprite.Write(sprite, folder, YyFormat.Lts2026, "Cover Assault");
            var back = ImportSprite(second, Path.Combine(folder, sprite.Name.Content + ".yy"));

            Assert.Equal((sprite.Width, sprite.Height, sprite.MarginLeft, sprite.MarginRight, sprite.MarginTop, sprite.MarginBottom, sprite.SepMasks, sprite.OriginX, sprite.OriginY),
                (back.Width, back.Height, back.MarginLeft, back.MarginRight, back.MarginTop, back.MarginBottom, back.SepMasks, back.OriginX, back.OriginY));
            Assert.Equal(Frames(sprite), Frames(back));
            Assert.Equal(File.ReadAllText(Path.Combine(folder, sprite.Name.Content + ".yy")), WriteAgain(back, sprite.Name.Content));
        }
    }

    // Imports every sprite, sound, script, object and room of a project into the fixture and exports both it and
    // GameMaker's own build of the project, so the two folders can be diffed. Needs yy-accept.txt next to the test
    // assembly: project folder, built data file, output folder and optionally a fixture name, one per line.
    [Fact]
    public void ImportedProjectExportsLikeGameMakersOwnBuild()
    {
        var settings = Path.Combine(AppContext.BaseDirectory, "yy-accept.txt");
        Assert.SkipUnless(File.Exists(settings), "yy-accept.txt not found");
        var lines = File.ReadAllLines(settings);
        string project = lines[0], built = lines[1], output = lines[2];
        var fixture = lines.Length > 3 ? lines[3] : "data-2026.win";

        using var stream = File.OpenRead(built);
        using var reference = GameMakerIO.Read(stream);
        using var data = Load(fixture);

        string[] Files(string kind) => Directory.Exists(Path.Combine(project, kind))
            ? [.. Directory.GetFiles(Path.Combine(project, kind), "*.yy", SearchOption.AllDirectories)
                .Where(p => Path.GetFileNameWithoutExtension(p) == Path.GetFileName(Path.GetDirectoryName(p))).OrderBy(p => p, StringComparer.Ordinal)] : [];
        string[] sprites = Files("sprites"), sounds = Files("sounds"), scripts = Files("scripts"), objects = Files("objects"), rooms = Files("rooms");
        // The base must lack the project's resources, so a same-named one it holds is dropped first.
        foreach (var r in data.Rooms.Where(r => reference.Rooms.ByName(r.Name.Content) is not null).ToList()) data.Rooms.Remove(r);
        foreach (var o in data.GameObjects.Where(o => reference.GameObjects.ByName(o.Name.Content) is not null).ToList()) data.GameObjects.Remove(o);
        foreach (var s in data.Sounds.Where(s => reference.Sounds.ByName(s.Name.Content) is not null).ToList()) data.Sounds.Remove(s);
        foreach (var s in data.Scripts.Where(s => reference.Scripts.ByName(s.Name.Content) is not null).ToList()) data.Scripts.Remove(s);
        foreach (var s in data.Sprites.Where(s => reference.Sprites.ByName(s.Name.Content) is not null).ToList()) data.Sprites.Remove(s);

        List<string> report = [];
        void Try(string yy, Action action)
        {
            try { action(); }
            catch (Exception e) { report.Add($"{Path.GetFileName(yy)}: {e.GetType().Name}: {e.Message}"); }
        }
        var clock = Stopwatch.StartNew();
        TexturePageRepacker repacker = new(data);
        foreach (var yy in sprites)
            Try(yy, () => YySprite.Read(yy, data, repacker));
        report.Add($"time: read {sprites.Length} sprites {clock.ElapsedMilliseconds} ms");
        repacker.Repack();
        report.Add($"time: repack {clock.ElapsedMilliseconds} ms");
        foreach (var yy in sounds)
            Try(yy, () => YySound.Read(yy, data));
        CodeImportGroup group = new(data);
        foreach (var yy in scripts)
            Try(yy, () => YyScript.Read(yy, group));
        foreach (var yy in objects)
            Try(yy, () => YyObject.Read(yy, group));
        List<string> skipped = [];
        foreach (var yy in rooms)
            Try(yy, () => YyRoom.Read(yy, group, skipped));
        Try("import", () => group.Import());
        report.Add($"time: all {clock.ElapsedMilliseconds} ms");
        report.AddRange(skipped.Select(s => "skipped layer: " + s));

        foreach (var expected in reference.Sprites)
        {
            var actual = data.Sprites.ByName(expected.Name.Content);
            if (actual is null) { report.Add($"sprite {expected.Name.Content}: missing"); continue; }
            if ((actual.Width, actual.Height, actual.MarginLeft, actual.MarginRight, actual.MarginTop, actual.MarginBottom, actual.OriginX, actual.OriginY, actual.SepMasks, actual.BBoxMode)
                != (expected.Width, expected.Height, expected.MarginLeft, expected.MarginRight, expected.MarginTop, expected.MarginBottom, expected.OriginX, expected.OriginY, expected.SepMasks, expected.BBoxMode))
                report.Add($"sprite {expected.Name.Content}: ours {actual.Width}x{actual.Height} m {actual.MarginLeft},{actual.MarginRight},{actual.MarginTop},{actual.MarginBottom} o {actual.OriginX},{actual.OriginY} {actual.SepMasks} bbox {actual.BBoxMode}; built {expected.Width}x{expected.Height} m {expected.MarginLeft},{expected.MarginRight},{expected.MarginTop},{expected.MarginBottom} o {expected.OriginX},{expected.OriginY} {expected.SepMasks} bbox {expected.BBoxMode}");
            byte[][] ours = Frames(actual), theirs = Frames(expected);
            if (!ours.SequenceEqual(theirs, new BytesComparer()))
            {
                var differing = ours.Zip(theirs, (a, b) => (a, b)).Count(p => !p.a.AsSpan().SequenceEqual(p.b));
                var visible = ours.Zip(theirs, (a, b) => (a, b)).Count(p => !Cleared(p.a).AsSpan().SequenceEqual(Cleared(p.b)));
                report.Add($"sprite {expected.Name.Content}: decoded frames differ in {differing} of {ours.Length}, {visible} after clearing the colour of transparent pixels");
            }
            if (!actual.CollisionMasks.Select(m => (m.Width, m.Height, Convert.ToBase64String(m.Data))).SequenceEqual(expected.CollisionMasks.Select(m => (m.Width, m.Height, Convert.ToBase64String(m.Data)))))
                report.Add($"sprite {expected.Name.Content}: masks ours {string.Join(";", actual.CollisionMasks.Select(m => $"{m.Width}x{m.Height}/{m.Data.Count(b => b != 0)}"))} built {string.Join(";", expected.CollisionMasks.Select(m => $"{m.Width}x{m.Height}/{m.Data.Count(b => b != 0)}"))}");
        }
        foreach (var expected in reference.Rooms)
        {
            var actual = data.Rooms.ByName(expected.Name.Content);
            if (actual is null) { report.Add($"room {expected.Name.Content}: missing"); continue; }
            string Room(GameMakerRoom r) => $"{r.Width}x{r.Height} speed {r.Speed} persistent {r.Persistent} bg {r.BackgroundColor:X} draw {r.DrawBackgroundColor} flags {r.Flags} world {r.World} bounds {r.Left},{r.Top},{r.Right},{r.Bottom} grav {r.GravityX},{r.GravityY} mpp {r.MetersPerPixel} views {r.Views.Count} objects {r.GameObjects.Count} tiles {r.Tiles.Count} bgs {r.Backgrounds.Count} code {r.CreationCodeId?.Name.Content}";
            if (Room(actual) != Room(expected)) report.Add($"room {expected.Name.Content}: {Room(actual)} != {Room(expected)}");
            string Layer(GameMakerRoom.Layer l) => $"{l.LayerName.Content} id {l.LayerId} type {l.LayerType} depth {l.LayerDepth} off {l.XOffset},{l.YOffset} speed {l.HSpeed},{l.VSpeed} visible {l.IsVisible} effect {l.EffectEnabled}/{l.EffectType?.Content ?? "<null>"}/{l.EffectProperties?.Count} data {l.Data?.GetType().Name}";
            if (!actual.Layers.Select(Layer).SequenceEqual(expected.Layers.Select(Layer)))
                report.Add($"room {expected.Name.Content}: layers differ" + Environment.NewLine + string.Join(Environment.NewLine, actual.Layers.Select(l => "  ours  " + Layer(l))) + Environment.NewLine + string.Join(Environment.NewLine, expected.Layers.Select(l => "  built " + Layer(l))));
        }

        string[] types = ["Sprites", "Sounds", "GameObjects", "Rooms", "Scripts"];
        HashSet<string> codeNames = [.. reference.Code.Select(c => c.Name.Content)];
        foreach ((var label, var source) in new[] { ("ours", data), ("built", reference) })
        {
            var folder = Path.Combine(output, label);
            Directory.CreateDirectory(folder);
            ResourceExportService.ExportTypes(source, folder, built, types, TestContext.Current.CancellationToken);
            ResourceExportService.ExportCodeEntriesSelective(source, folder, codeNames, TestContext.Current.CancellationToken);
        }
        File.WriteAllLines(Path.Combine(output, "report.txt"), report);
    }

    private static byte[] Cleared(byte[] rgba)
    {
        var copy = (byte[])rgba.Clone();
        for (var i = 0; i < copy.Length; i += 4)
        {
            if (copy[i + 3] == 0)
                copy[i] = copy[i + 1] = copy[i + 2] = 0;
        }
        return copy;
    }

    private sealed class BytesComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }

    private string WriteAgain(GameMakerSprite sprite, string name)
    {
        var folder = Path.Combine(_root, "again", name);
        YySprite.Write(sprite, folder, YyFormat.Lts2026, "Cover Assault");
        return File.ReadAllText(Path.Combine(folder, name + ".yy"));
    }
}
