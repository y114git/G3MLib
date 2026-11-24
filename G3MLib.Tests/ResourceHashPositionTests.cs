using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class ResourceHashPositionTests
{
    // One 8x4 page; the sprite frame is 4x4 at frameX. Only the frame area carries the given colour.
    private static GameMakerData SpriteData(ushort frameX, byte colour, int pageCount = 1)
    {
        var data = GameMakerData.CreateNew2026LTS();
        GameMakerEmbeddedTexture? page = null;
        for (var p = 0; p < pageCount; p++)
        {
            var image = new GMImage(8, 4);
            var pixels = image.GetData();
            for (var y = 0; y < 4; y++)
                for (var x = frameX; x < frameX + 4; x++)
                    pixels[(y * 8 + x) * 4 + 1] = colour;
            page = new GameMakerEmbeddedTexture { Name = data.Strings.MakeString("Texture " + p), TextureData = new GameMakerEmbeddedTexture.TexData { Image = image } };
            data.EmbeddedTextures.Add(page);
        }
        var item = new GameMakerTexturePageItem
        {
            Name = data.Strings.MakeString("PageItem 0"),
            SourceX = frameX, SourceY = 0, SourceWidth = 4, SourceHeight = 4,
            TargetWidth = 4, TargetHeight = 4, BoundingWidth = 4, BoundingHeight = 4,
            TexturePage = page!,
        };
        data.TexturePageItems.Add(item);
        var sprite = new GameMakerSprite { Name = data.Strings.MakeString("spr_a"), Width = 4, Height = 4, MarginRight = 3, MarginBottom = 3 };
        sprite.Textures.Add(new GameMakerSprite.TextureEntry { Texture = item });
        data.Sprites.Add(sprite);
        return data;
    }

    private static string SpriteHash(GameMakerData data) => ResourceHashService.HashAll(data)["Sprites"]["spr_a"];

    [Fact]
    public void ASpriteFrameThatOnlyMovedOnItsPageKeepsItsHash()
    {
        using var first = SpriteData(0, 200);
        using var moved = SpriteData(4, 200);
        using var otherPage = SpriteData(0, 200, pageCount: 2);
        Assert.Equal(SpriteHash(first), SpriteHash(moved));
        Assert.Equal(SpriteHash(first), SpriteHash(otherPage));
    }

    [Fact]
    public void ASpriteFrameWithChangedPixelsChangesItsHash()
    {
        using var first = SpriteData(0, 200);
        using var changed = SpriteData(0, 201);
        Assert.NotEqual(SpriteHash(first), SpriteHash(changed));
    }

    private static GameMakerData SoundData(int audioId, byte seed)
    {
        var data = GameMakerData.CreateNew2026LTS();
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
        for (var i = 0; i <= audioId; i++)
            data.EmbeddedAudio.Add(new GameMakerEmbeddedAudio { Data = [(byte)(i == audioId ? seed : 0)] });
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("snd_a"), Type = data.Strings.MakeString(".wav"), GroupID = 0, AudioID = audioId, AudioFile = data.EmbeddedAudio[audioId] });
        return data;
    }

    private static string SoundHash(GameMakerData data) => ResourceHashService.HashAll(data)["Sounds"]["snd_a"];

    [Fact]
    public void ASoundThatOnlyMovedToAnotherAudioSlotKeepsItsHashButNewAudioChangesIt()
    {
        using var first = SoundData(0, 5);
        using var moved = SoundData(3, 5);
        using var changed = SoundData(0, 6);
        Assert.Equal(SoundHash(first), SoundHash(moved));
        Assert.NotEqual(SoundHash(first), SoundHash(changed));
    }
}
