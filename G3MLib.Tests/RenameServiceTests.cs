using System.Collections;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.References;

namespace G3MLib.Tests;

public class RenameServiceTests
{
    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    private static byte[] Save(GameMakerData data)
    {
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        return output.ToArray();
    }

    private static readonly (string Type, Func<GameMakerData, IList> List, Func<GameMakerData, string, GameMakerNamedResource> Create)[] Types =
    [
        ("Sprite", d => (IList)d.Sprites, (d, n) => new GameMakerSprite { Name = d.Strings.MakeString(n) }),
        ("Object", d => (IList)d.GameObjects, (d, n) => new GameMakerGameObject { Name = d.Strings.MakeString(n) }),
        ("Room", d => (IList)d.Rooms, (d, n) => new GameMakerRoom { Name = d.Strings.MakeString(n), Caption = d.Strings.MakeString("") }),
        ("Script", d => (IList)d.Scripts, (d, n) => new GameMakerScript { Name = d.Strings.MakeString(n) }),
        ("Sound", d => (IList)d.Sounds, (d, n) => new GameMakerSound { Name = d.Strings.MakeString(n) }),
        ("Font", d => (IList)d.Fonts, (d, n) => new GameMakerFont { Name = d.Strings.MakeString(n) }),
        ("Path", d => (IList)d.Paths, (d, n) => new GameMakerPath { Name = d.Strings.MakeString(n) }),
        ("Timeline", d => (IList)d.Timelines, (d, n) => new GameMakerTimeline { Name = d.Strings.MakeString(n) }),
        ("Shader", d => (IList)d.Shaders, (d, n) => new GameMakerShader { Name = d.Strings.MakeString(n) }),
        ("Sequence", d => (IList)d.Sequences, (d, n) => new GameMakerSequence { Name = d.Strings.MakeString(n) }),
        ("AnimationCurve", d => (IList)d.AnimationCurves, (d, n) => new GameMakerAnimationCurve { Name = d.Strings.MakeString(n) }),
        ("ParticleSystem", d => (IList)d.ParticleSystems, (d, n) => new GameMakerParticleSystem { Name = d.Strings.MakeString(n) }),
        ("Background", d => (IList)d.Backgrounds, (d, n) => new GameMakerBackground { Name = d.Strings.MakeString(n) }),
    ];

    public static TheoryData<string> TypeNames => [.. Types.Select(t => t.Type)];

    private static GameMakerNamedResource Pick(GameMakerData data, string type)
    {
        var entry = Types.Single(t => t.Type == type);
        var list = entry.List(data);
        if (list.OfType<GameMakerNamedResource>().FirstOrDefault(r => r.Name?.Content is not null) is { } existing)
        {
            return existing;
        }
        var created = entry.Create(data, "probe_" + type);
        list.Add(created);
        return created;
    }

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void RenamesEachResourceType(string type)
    {
        using var data = Load("data-2026.win");
        var resource = Pick(data, type);
        var oldName = resource.Name.Content;
        // A resource added bare by Pick cannot be written, so only fixture resources are saved
        var writable = resource.Name.Content != "probe_" + type;
        var before = writable ? Save(data) : [];

        var changes = RenameService.RenameResource(data, resource, "renamed_probe");

        Assert.Equal("renamed_probe", resource.Name.Content);
        Assert.Contains(changes, c => c.OldValue == oldName && c.NewValue == "renamed_probe" && c.Location.Contains(oldName, StringComparison.Ordinal));
        if (writable)
        {
            using MemoryStream stream = new(Save(data));
            using var reread = GameMakerIO.Read(stream);
            Assert.Contains(reread.Strings, s => s.Content == "renamed_probe");
        }

        changes.Undo();
        Assert.Equal(oldName, resource.Name.Content);
        if (writable)
        {
            Assert.Equal(before, Save(data));
        }
        changes.Redo();
        Assert.Equal("renamed_probe", resource.Name.Content);
    }

    [Fact]
    public void RenameThenRenameBackIsByteIdentical()
    {
        using var data = Load("data-2026.win");
        var before = Save(data);
        foreach (var type in new[] { "Object", "Script", "Room", "Sprite" })
        {
            var resource = Pick(data, type);
            var name = resource.Name.Content;
            RenameService.RenameResource(data, resource, "tmp_probe_name");
            RenameService.RenameResource(data, resource, name);
        }
        Assert.Equal(before, Save(data));
    }

    [Fact]
    public void ObjectRenameUpdatesEventCodeNames()
    {
        using var data = Load("data-2026.win");
        var obj = data.GameObjects.First(o => data.Code.Any(c => c.Name.Content.StartsWith("gml_Object_" + o.Name.Content + "_Create", StringComparison.Ordinal)));
        var old = obj.Name.Content;
        List<GameMakerCode> owned = [.. data.Code.Where(c => c.Name.Content.StartsWith("gml_Object_" + old + "_Create", StringComparison.Ordinal))];

        var changes = RenameService.RenameResource(data, obj, "obj_renamed_probe");

        Assert.All(owned, c => Assert.StartsWith("gml_Object_obj_renamed_probe_Create", c.Name.Content, StringComparison.Ordinal));
        Assert.Contains(changes, c => c.Location.StartsWith("Code", StringComparison.Ordinal));
        changes.Undo();
        Assert.All(owned, c => Assert.StartsWith("gml_Object_" + old + "_Create", c.Name.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void ScriptRenameUpdatesCodeAndFunctionNames()
    {
        using var data = Load("data-2026.win");
        var script = data.Scripts.First(s => data.Functions.Any(f => f.Name.Content == "gml_Script_" + s.Name.Content));
        var old = script.Name.Content;
        var function = data.Functions.First(f => f.Name.Content == "gml_Script_" + old);

        RenameService.RenameResource(data, script, "scr_renamed_probe");

        Assert.Equal("gml_Script_scr_renamed_probe", function.Name.Content);
        Assert.DoesNotContain(data.Code, c => c.Name.Content == "gml_Script_" + old || c.Name.Content == "gml_GlobalScript_" + old);
    }

    [Fact]
    public void RoomRenameUpdatesCreationCodeNames()
    {
        using var data = Load("data-2026.win");
        var room = data.Rooms[0];
        var old = room.Name.Content;
        GameMakerCode.CreateEmptyEntry(data, "gml_Room_" + old + "_Create");
        GameMakerCode.CreateEmptyEntry(data, "gml_RoomCC_" + old + "_100001_Create");

        RenameService.RenameResource(data, room, "rm_renamed_probe");

        Assert.Contains(data.Code, c => c.Name.Content.StartsWith("gml_Room_rm_renamed_probe_", StringComparison.Ordinal));
        Assert.DoesNotContain(data.Code, c => c.Name.Content.StartsWith("gml_Room_" + old + "_", StringComparison.Ordinal));
        Assert.Contains(data.Code, c => c.Name.Content == "gml_RoomCC_rm_renamed_probe_100001_Create");
    }

    [Fact]
    public void CodeLiteralsEqualToTheNameAreRenamedAndListed()
    {
        using var data = Load("data-2026.win");
        var named = data.Sprites[0];
        var literal = data.Code.SelectMany(c => c.Instructions).First(i => i.ValueString is not null);
        literal.ValueString.Resource = named.Name;
        var index = ReferenceIndex.Build(data, TestContext.Current.CancellationToken);

        var changes = RenameService.RenameResource(data, named, "literal_probe", index);

        Assert.Contains(changes.Uses, u => u.Member == "String");
        Assert.All(changes.Uses.Where(u => u.Member == "String"), u => Assert.Equal("literal_probe", ((GameMakerString)u.Target).Content));
    }

    [Fact]
    public void RenamesAVariableInEveryEntry()
    {
        using var data = Load("data-2026.win");
        var variable = data.Variables.First(v => v.InstanceType == GameMakerInstruction.InstanceType.Global);
        var old = variable.Name.Content;
        var count = data.Variables.Count(v => v.Name.Content == old);

        var changes = RenameService.RenameVariable(data, old, "var_renamed_probe");

        Assert.Equal(count, data.Variables.Count(v => v.Name.Content == "var_renamed_probe"));
        Assert.DoesNotContain(data.Variables, v => v.Name.Content == old);
        changes.Undo();
        Assert.Equal(count, data.Variables.Count(v => v.Name.Content == old));
    }

    [Fact]
    public void RenamesAGmlFunctionAndItsCodeEntries()
    {
        using var data = Load("data-2026.win");
        var function = data.Functions.First(f => f.Name.Content.StartsWith("gml_Script_", StringComparison.Ordinal) && !f.Name.Content.StartsWith("gml_Script_anon", StringComparison.Ordinal)
            && !data.Scripts.Any(s => s.Name.Content == f.Name.Content["gml_Script_".Length..]));
        var old = function.Name.Content["gml_Script_".Length..];

        var changes = RenameService.RenameFunction(data, old, "fn_renamed_probe");

        Assert.Equal("gml_Script_fn_renamed_probe", function.Name.Content);
        Assert.DoesNotContain(data.Code, c => c.Name.Content == "gml_Script_" + old);
        changes.Undo();
        Assert.Equal("gml_Script_" + old, function.Name.Content);
    }

    [Fact]
    public void RefusesInvalidAndCollidingNames()
    {
        using var data = Load("data-2026.win");
        var sprite = data.Sprites[0];
        var other = data.GameObjects[0].Name.Content;
        string[] names = [.. data.Variables.Select(v => v.Name.Content).Where(n => n.All(ch => char.IsLetterOrDigit(ch) || ch == '_')).Distinct()];
        var firstVariable = names[0];
        var otherVariable = names[1];

        Assert.Equal(RenameFailure.InvalidName, Assert.Throws<RenameException>(() => RenameService.RenameResource(data, sprite, "1bad")).Reason);
        Assert.Equal(RenameFailure.InvalidName, Assert.Throws<RenameException>(() => RenameService.RenameResource(data, sprite, "has space")).Reason);
        Assert.Equal(RenameFailure.InvalidName, Assert.Throws<RenameException>(() => RenameService.RenameResource(data, sprite, "")).Reason);
        Assert.Equal(RenameFailure.InvalidName, Assert.Throws<RenameException>(() => RenameService.RenameResource(data, sprite, "while")).Reason);
        var collision = Assert.Throws<RenameException>(() => RenameService.RenameResource(data, sprite, other));
        Assert.Equal(RenameFailure.NameInUse, collision.Reason);
        Assert.Contains(other, collision.Message, StringComparison.Ordinal);
        Assert.Equal(RenameFailure.NameInUse, Assert.Throws<RenameException>(() => RenameService.RenameVariable(data, firstVariable, otherVariable)).Reason);
        Assert.Equal(RenameFailure.NotFound, Assert.Throws<RenameException>(() => RenameService.RenameVariable(data, "no_such_variable_here", "fine_name")).Reason);
        Assert.Equal(RenameFailure.Builtin, Assert.Throws<RenameException>(() => RenameService.RenameFunction(data, data.Functions.First(f => !f.Name.Content.StartsWith("gml_Script_", StringComparison.Ordinal) && data.Scripts.All(s => s.Name.Content != f.Name.Content)).Name.Content, "fine_name")).Reason);
    }

    [Fact]
    public void RefusedRenameChangesNothing()
    {
        using var data = Load("data-2026.win");
        var before = Save(data);
        Assert.Throws<RenameException>(() => RenameService.RenameResource(data, data.Sprites[0], data.Rooms[0].Name.Content));
        Assert.Equal(before, Save(data));
    }

    [Fact]
    public void UndoRefusesWhenTheNameChangedSince()
    {
        using var data = Load("data-2026.win");
        var changes = RenameService.RenameResource(data, data.Sprites[0], "undo_probe");
        data.Sprites[0].Name.Content = "edited_elsewhere";
        Assert.Throws<InvalidOperationException>(changes.Undo);
    }

    public static TheoryData<string> ExternalFiles()
    {
        var configured = Environment.GetEnvironmentVariable("G3MLIB_RENAME_DATA_FILES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ["-"];
        }
        return [.. configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    [Theory]
    [MemberData(nameof(ExternalFiles))]
    public void RenameAndBackKeepsExternalFilesByteIdentical(string path)
    {
        if (path == "-")
        {
            return;
        }
        using var data = GameMakerIO.Read(File.OpenRead(path));
        var before = Save(data);
        var renamed = 0;
        foreach (var type in new[] { "Object", "Script", "Room", "Sprite", "Sound", "Font", "Timeline" })
        {
            var list = Types.Single(t => t.Type == type).List(data);
            if (list is null || list.Count == 0)
            {
                continue;
            }
            var resource = (GameMakerNamedResource)list[list.Count / 2];
            var name = resource.Name.Content;
            RenameService.RenameResource(data, resource, "tmp_probe_name");
            Assert.Equal("tmp_probe_name", resource.Name.Content);
            RenameService.RenameResource(data, resource, name);
            renamed++;
        }
        var variableName = data.Variables.First(v => v.InstanceType == GameMakerInstruction.InstanceType.Global).Name.Content;
        RenameService.RenameVariable(data, variableName, "tmp_probe_var");
        RenameService.RenameVariable(data, "tmp_probe_var", variableName);

        Assert.True(renamed > 0);
        Assert.Equal(before, Save(data));
    }
}
