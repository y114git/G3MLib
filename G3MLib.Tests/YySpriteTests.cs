using G3MLib.DataFile;
using G3MLib.DataFile.Interop;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using ImageMagick;

namespace G3MLib.Tests;

public sealed class YySpriteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3m-yysprite-" + Guid.NewGuid().ToString("N"));

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

    // Two frames of 8x6: frame 0 is opaque on the left half, frame 1 on the right half.
    private string WriteSource(bool trailingCommas)
    {
        var folder = Path.Combine(_root, "sprites", "spr_test");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 2; i++)
        {
            using MagickImage image = new(MagickColors.Transparent, 8, 6);
            using MagickImage half = new(new MagickColor((byte)(100 + i * 100), 20, 30, 255), 4, 6);
            image.Composite(half, i * 4, 0, CompositeOperator.Over);
            image.Write(Path.Combine(folder, $"frame{i}.png"), MagickFormat.Png32);
        }
        var comma = trailingCommas ? "," : "";
        File.WriteAllText(Path.Combine(folder, "spr_test.yy"), $$"""
            {
              "$GMSprite":"v2","%Name":"spr_test","bboxMode":2,"bbox_bottom":5,"bbox_left":1,"bbox_right":6,"bbox_top":0,
              "collisionKind":4,"collisionTolerance":0,"edgeFiltering":true,"width":8,"height":6,
              "frames":[{"name":"frame0","resourceType":"GMSpriteFrame"{{comma}}},{"name":"frame1","resourceType":"GMSpriteFrame"{{comma}}}{{comma}}],
              "name":"spr_test","origin":9,"resourceType":"GMSprite",
              "nineSlice":{"enabled":true,"left":1,"top":1,"right":2,"bottom":2,"tileMode":[0,1,2,3,4]{{comma}}},
              "sequence":{"playbackSpeed":12.0,"playbackSpeedType":0,"playback":1,"xorigin":3,"yorigin":2{{comma}}}{{comma}}
            }
            """);
        return Path.Combine(folder, "spr_test.yy");
    }

    private static byte[][] Frames(GameMakerData data, string name)
    {
        using TextureWorker worker = new();
        return [.. data.Sprites.ByName(name).Textures.Select(t => { using var image = worker.GetTextureFor(t.Texture, name, true); return image.ToByteArray(MagickFormat.Rgba); })];
    }

    private static GameMakerSprite Import(GameMakerData data, string yy)
    {
        TexturePageRepacker repacker = new(data);
        var sprite = YySprite.Read(yy, data, repacker);
        repacker.Repack();
        return sprite;
    }

    [Theory]
    [InlineData("data-2022.win", true)]
    [InlineData("data-2026.win", false)]
    public void ReadsFieldsFramesAndMasks(string fixture, bool trailingCommas)
    {
        using var data = Load(fixture);
        var sprite = Import(data, WriteSource(trailingCommas));

        Assert.Equal((8u, 6u), (sprite.Width, sprite.Height));
        Assert.Equal((1, 6, 0, 5), (sprite.MarginLeft, sprite.MarginRight, sprite.MarginTop, sprite.MarginBottom));
        Assert.Equal(2u, sprite.BBoxMode);
        Assert.True(sprite.Smooth);
        Assert.Equal((3, 2), (sprite.OriginX, sprite.OriginY));
        Assert.Equal(GameMakerSprite.SepMaskType.Precise, sprite.SepMasks);
        Assert.Equal(2, sprite.Textures.Count);
        Assert.Equal(2, sprite.CollisionMasks.Count);
        Assert.NotEqual(sprite.CollisionMasks[0].Data, sprite.CollisionMasks[1].Data);

        using MagickImage expected = new(Path.Combine(_root, "sprites", "spr_test", "frame0.png"));
        Assert.Equal(expected.ToByteArray(MagickFormat.Rgba), Frames(data, "spr_test")[0]);

        using var reloaded = RoundTrip(data);
        Assert.Equal(Frames(data, "spr_test"), Frames(reloaded, "spr_test"));
    }

    [Theory]
    [InlineData(YyFormat.Lts2022)]
    [InlineData(YyFormat.Lts2026)]
    public void WriteThenReadIsStable(YyFormat format)
    {
        using var first = Load("data-2026.win");
        Import(first, WriteSource(false));
        using var reloaded = RoundTrip(first);
        var folder = Path.Combine(_root, "out", format.ToString());
        YySprite.Write(reloaded.Sprites.ByName("spr_test"), folder, format);
        var yy = File.ReadAllText(Path.Combine(folder, "spr_test.yy"));

        using var second = Load("data-2026.win");
        var again = Import(second, Path.Combine(folder, "spr_test.yy"));

        Assert.Equal(Frames(first, "spr_test"), Frames(second, "spr_test"));
        Assert.Equal((8u, 6u, 2u), (again.Width, again.Height, again.BBoxMode));
        Assert.Equal((3, 2), (again.OriginX, again.OriginY));
        Assert.Equal(GameMakerSprite.SepMaskType.Precise, again.SepMasks);
        Assert.Equal(2, again.CollisionMasks.Count);

        YySprite.Write(again, Path.Combine(_root, "out2", format.ToString()), format);
        Assert.Equal(yy, File.ReadAllText(Path.Combine(_root, "out2", format.ToString(), "spr_test.yy")));
    }
}
