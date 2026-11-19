using System.Collections;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.References;

namespace G3MLib.Tests;

public sealed class ResourceCloneServiceTests
{
    private static readonly string[] Fixtures = ["data-2022.win", "data-2023.win", "data-2024.win", "data-2026.win"];

    public static TheoryData<string> FixtureNames() => [.. Fixtures];

    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    private static byte[] Save(GameMakerData data)
    {
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        return stream.ToArray();
    }

    private static IEnumerable<GameMakerNamedResource> Clonable(GameMakerData data) =>
        new IEnumerable[] { data.GameObjects, data.Sprites, data.Sounds, data.Rooms, data.Backgrounds, data.Paths, data.Scripts, data.Fonts,
                data.Timelines, data.Shaders, data.Sequences, data.AnimationCurves, data.ParticleSystems, data.Extensions }
            .SelectMany(list => (list ?? Array.Empty<object>()).Cast<object?>().OfType<GameMakerNamedResource>());

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void CloneOfEveryResourceWritesAndUndoRestoresTheFileByteForByte(string fixture)
    {
        using var data = Load(fixture);
        var before = Save(data);
        var validBefore = G3MLib.Modding.Integrity.DataIntegrityService.ValidateOnly(data).Errors.Count;
        List<ResourceClone> clones = [];
        var number = 0;
        foreach (var resource in Clonable(data).ToList())
        {
            var clone = ResourceCloneService.Clone(data, resource, $"g3m_clone_{number++}");
            Assert.NotSame(resource, clone.Resource);
            Assert.Equal(resource.GetType(), clone.Resource.GetType());
            Assert.Contains(clone.Resource, clone.Added);
            if (clone.Resource is GameMakerSprite { V2Sequence: not null } sprite)
            {
                Assert.Equal(sprite.Name.Content, sprite.V2Sequence.Name.Content);
            }
            clones.Add(clone);
        }
        Assert.NotEmpty(clones);

        Assert.Equal(validBefore, G3MLib.Modding.Integrity.DataIntegrityService.ValidateOnly(data).Errors.Count);
        var withClones = Save(data);
        using (MemoryStream stream = new(withClones))
        {
            using var reread = GameMakerIO.Read(stream);
            Assert.Equal(Clonable(data).Count(), Clonable(reread).Count());
        }

        foreach (var clone in Enumerable.Reverse(clones))
        {
            clone.Undo();
        }
        Assert.Equal(before, Save(data));
    }

    [Fact]
    public void ObjectCloneOwnsItsEventsAndCodeButSharesLinks()
    {
        using var data = Load("data-2026.win");
        var source = data.GameObjects.First(o => o.Events.Any(list => list.Any(e => e.Actions.Any(a => a.CodeId != null))));
        var name = source.Name.Content;

        var clone = (GameMakerGameObject)ResourceCloneService.Clone(data, source, "obj_g3m_copy").Resource;

        Assert.Equal("obj_g3m_copy", clone.Name.Content);
        Assert.Equal(name, source.Name.Content);
        Assert.Same(source.Sprite, clone.Sprite);
        Assert.NotSame(source.Events, clone.Events);
        var sourceCode = source.Events.SelectMany(l => l).SelectMany(e => e.Actions).Select(a => a.CodeId).Where(c => c != null).ToList();
        var cloneCode = clone.Events.SelectMany(l => l).SelectMany(e => e.Actions).Select(a => a.CodeId).Where(c => c != null).ToList();
        Assert.Equal(sourceCode.Count, cloneCode.Count);
        foreach (var code in cloneCode)
        {
            Assert.DoesNotContain(sourceCode, s => ReferenceEquals(s, code));
            Assert.Contains("obj_g3m_copy", code.Name.Content);
            Assert.Contains(code, data.Code);
        }
    }

    [Fact]
    public void ScriptAndSpriteClonesTakeTheNewNameEverywhere()
    {
        using var data = Load("data-2026.win");
        var script = data.Scripts.First(x => x.Code != null);
        var scriptClone = (GameMakerScript)ResourceCloneService.Clone(data, script, "scr_g3m_copy").Resource;
        Assert.EndsWith("_scr_g3m_copy", scriptClone.Code.Name.Content);
        Assert.NotSame(script.Code, scriptClone.Code);
        Assert.Contains(scriptClone.Code, data.Code);
        if (data.CodeLocals != null && data.CodeLocals.For(script.Code) != null) Assert.NotNull(data.CodeLocals.For(scriptClone.Code));

        var sprite = data.Sprites.First(x => x.Textures.Count > 0 && x.Textures[0].Texture != null);
        var spriteClone = (GameMakerSprite)ResourceCloneService.Clone(data, sprite, "spr_g3m_copy").Resource;
        Assert.Equal("spr_g3m_copy", spriteClone.Name.Content);
        Assert.Equal(sprite.V2Sequence is null, spriteClone.V2Sequence is null);
        if (sprite.V2Sequence != null)
        {
            Assert.Equal("spr_g3m_copy", spriteClone.V2Sequence.Name.Content);
            Assert.Equal(sprite.Name.Content, sprite.V2Sequence.Name.Content);
        }
        Assert.Same(sprite.Textures[0].Texture, spriteClone.Textures[0].Texture);
        Assert.NotSame(sprite.Textures, spriteClone.Textures);
    }

    [Fact]
    public void RoomCloneGetsNewInstanceIdsAndJoinsTheRoomOrder()
    {
        using var data = Load("data-2026.win");
        var source = data.Rooms.First(r => r.GameObjects.Count > 0);
        var oldIds = source.GameObjects.Select(i => i.InstanceID).ToHashSet();
        var orderBefore = data.GeneralInfo.RoomOrder.Count;

        var clone = (GameMakerRoom)ResourceCloneService.Clone(data, source, "rm_g3m_copy").Resource;

        Assert.Equal(source.GameObjects.Count, clone.GameObjects.Count);
        Assert.All(clone.GameObjects, i => Assert.DoesNotContain(i.InstanceID, oldIds));
        Assert.Equal(clone.GameObjects.Count, clone.GameObjects.Select(i => i.InstanceID).Distinct().Count());
        Assert.Equal(orderBefore + 1, data.GeneralInfo.RoomOrder.Count);
        Assert.Equal(oldIds, source.GameObjects.Select(i => i.InstanceID).ToHashSet());
    }

    [Fact]
    public void SoundCloneGetsItsOwnEmbeddedAudio()
    {
        using var data = Load("data-2026.win");
        var source = data.Sounds.FirstOrDefault(s => s.AudioFile is { Data.Length: > 0 } && s.GroupID <= 0 && s.AudioID >= 0);
        if (source is null)
        {
            return;
        }

        var clone = (GameMakerSound)ResourceCloneService.Clone(data, source, "snd_g3m_copy").Resource;

        Assert.NotSame(source.AudioFile, clone.AudioFile);
        Assert.Equal(source.AudioFile.Data, clone.AudioFile.Data);
        Assert.Same(clone.AudioFile, data.EmbeddedAudio[clone.AudioID]);
    }

    [Fact]
    public void RefusesBadNamesAndUnsupportedTypes()
    {
        using var data = Load("data-2026.win");
        var sprite = data.Sprites.First();

        Assert.Equal(RenameFailure.InvalidName, Assert.Throws<RenameException>(() => ResourceCloneService.Clone(data, sprite, "not valid")).Reason);
        Assert.Equal(RenameFailure.NameInUse, Assert.Throws<RenameException>(() => ResourceCloneService.Clone(data, sprite, data.Sprites[^1].Name.Content)).Reason);
        Assert.Throws<NotSupportedException>(() => ResourceCloneService.Clone(data, data.Code.First(), "x_code"));
        Assert.Throws<NotSupportedException>(() => ResourceCloneService.Clone(data, data.Functions.First(), "x_function"));
    }

    [Fact]
    public void UndoKeepsIdsAndStringsThatLaterEditsDependOn()
    {
        using var data = Load("data-2026.win");
        var source = data.Rooms.First(r => r.GameObjects.Count > 0);
        var clone = ResourceCloneService.Clone(data, source, "rm_g3m_undo");
        var (obj, tile) = (data.GeneralInfo.LastObj, data.GeneralInfo.LastTile);
        data.GeneralInfo.LastObj++;
        var later = data.Strings.MakeString("g3m_string_added_after_the_clone");

        clone.Undo();

        Assert.Equal(obj + 1, data.GeneralInfo.LastObj);
        Assert.Equal(tile, data.GeneralInfo.LastTile);
        Assert.Same(later, data.Strings[^1]);
        Assert.DoesNotContain(clone.Resource, data.Rooms);
        Assert.Contains(data.Strings, text => text.Content == "rm_g3m_undo");
    }

    [Fact]
    public async Task CheckRunsInsideTheGateSoANameAddedWhileWaitingIsSeen()
    {
        using var data = Load("data-2026.win");
        var source = data.Sprites[0];
        var holder = data.Gate.BeginWrite();
        Task<ResourceClone> clone;
        using (ExecutionContext.SuppressFlow())
            clone = Task.Run(() => ResourceCloneService.Clone(data, source, "spr_g3m_race"), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        data.Sprites.Add(new GameMakerSprite { Name = data.Strings.MakeString("spr_g3m_race") });
        holder.Dispose();

        var error = await Assert.ThrowsAsync<RenameException>(() => clone);
        Assert.Equal(RenameFailure.NameInUse, error.Reason);
        Assert.Equal(1, data.Sprites.Count(sprite => sprite.Name.Content == "spr_g3m_race"));
    }

    [Fact]
    public void ScriptCloneGetsFunctionEntriesAndRoomCloneListsSharedCode()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var code = new GameMakerCode { Name = data.Strings.MakeString("gml_Script_scr_a") };
        data.Code.Add(code);
        data.Scripts.Add(new GameMakerScript { Name = data.Strings.MakeString("scr_a"), Code = code });
        data.Functions.Add(new GameMakerFunction { Name = data.Strings.MakeString("gml_Script_scr_a") });
        data.Functions.Add(new GameMakerFunction { Name = data.Strings.MakeString("scr_a") });

        var scriptClone = ResourceCloneService.Clone(data, data.Scripts[0], "scr_b");

        Assert.Contains(data.Functions, f => f.Name.Content == "gml_Script_scr_b");
        Assert.Contains(data.Functions, f => f.Name.Content == "scr_b");
        scriptClone.Undo();
        Assert.DoesNotContain(data.Functions, f => f.Name.Content == "scr_b");

        var custom = new GameMakerCode { Name = data.Strings.MakeString("custom_creation_code") };
        data.Code.Add(custom);
        var room = new GameMakerRoom { Name = data.Strings.MakeString("rm_a") };
        room.GameObjects.Add(new GameMakerRoom.GameObject { InstanceID = 100001, CreationCode = custom });
        data.Rooms.Add(room);

        var roomClone = ResourceCloneService.Clone(data, room, "rm_b");

        Assert.Same(custom, Assert.Single(roomClone.SharedCode));
    }

    [Fact]
    public void RoomCloneWithStaleLastObjKeepsEveryInstanceCodeAndItsLocalsApart()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var room = new GameMakerRoom { Name = data.Strings.MakeString("rm_a") };
        foreach (var id in new uint[] { 100, 51 })
        {
            var code = new GameMakerCode { Name = data.Strings.MakeString($"gml_RoomCC_rm_a_{id}_Create") };
            data.Code.Add(code);
            data.CodeLocals.Add(new GameMakerCodeLocals { Name = code.Name });
            room.GameObjects.Add(new GameMakerRoom.GameObject { InstanceID = id, CreationCode = code });
        }
        data.Rooms.Add(room);
        data.GeneralInfo.LastObj = 50;

        var clone = (GameMakerRoom)ResourceCloneService.Clone(data, room, "rm_b").Resource;

        Assert.All(clone.GameObjects, i => Assert.True(i.InstanceID > 100));
        var names = clone.GameObjects.Select(i => i.CreationCode.Name.Content).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var instance in clone.GameObjects)
        {
            Assert.Equal($"gml_RoomCC_rm_b_{instance.InstanceID}_Create", instance.CreationCode.Name.Content);
            Assert.Contains(data.CodeLocals, locals => ReferenceEquals(locals.Name, instance.CreationCode.Name));
        }
    }
}
