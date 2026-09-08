using System.IO;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class SpriteSerializationTests
{
    [Fact("SpriteSerializationTests.cs", 10)]
    public void ModernNormalSpriteRoundTripsTextureAndCollisionMask()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerTexturePageItem pageItem = new()
        {
            Name = data.Strings.MakeString("page_item"),
            SourceWidth = 2,
            SourceHeight = 2,
            TargetWidth = 2,
            TargetHeight = 2,
            BoundingWidth = 2,
            BoundingHeight = 2
        };
        data.TexturePageItems.Add(pageItem);
        GameMakerSprite sprite = new()
        {
            Name = data.Strings.MakeString("spr_probe"),
            Width = 2,
            Height = 2,
            MarginLeft = 0,
            MarginRight = 1,
            MarginTop = 0,
            MarginBottom = 1,
            OriginX = 1,
            OriginY = 1,
            BBoxMode = 2,
            SepMasks = GameMakerSprite.SepMaskType.Precise
        };
        sprite.Textures.Add(new GameMakerSprite.TextureEntry { Texture = pageItem });
        GameMakerSprite.MaskEntry mask = sprite.NewMaskEntry(data);
        mask.Data[0] = 0x03;
        mask.Data[1] = 0x02;
        sprite.CollisionMasks.Add(mask);
        data.Sprites.Add(sprite);

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerSprite roundTripped = output.Sprites.ByName("spr_probe");

        Assert.Same(Assert.Single(output.TexturePageItems), Assert.Single(roundTripped.Textures).Texture);
        GameMakerSprite.MaskEntry roundTrippedMask = Assert.Single(roundTripped.CollisionMasks);
        Assert.Equal(new byte[] { 0x03, 0x02 }, roundTrippedMask.Data);
        Assert.Equal((1, 1), (roundTripped.OriginX, roundTripped.OriginY));
        Assert.Equal(GameMakerSprite.SepMaskType.Precise, roundTripped.SepMasks);
    }

    [Fact("SpriteSerializationTests.cs", 61)]
    public void CollisionMaskDimensionsFollowTheDataVersion()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerSprite sprite = new()
        {
            Width = 16,
            Height = 4,
            MarginLeft = 4,
            MarginRight = 7,
            MarginTop = 1,
            MarginBottom = 2
        };

        data.SetGMS2Version(2024, 2, isLTS: false);
        Assert.Equal((16, 4), sprite.CalculateMaskDimensions(data));
        Assert.Equal(8, sprite.NewMaskEntry(data).Data.Length);

        data.SetGMS2Version(2024, 6, isLTS: false);
        Assert.Equal((4, 2), sprite.CalculateMaskDimensions(data));
        Assert.Equal(2, sprite.NewMaskEntry(data).Data.Length);
    }

    [Fact("SpriteSerializationTests.cs", 84)]
    public void SpriteExportabilityRejectsIncompleteAssets()
    {
        GameMakerSprite sprite = new();
        Assert.False(sprite.ProjectExportable);

        sprite.Name = new GameMakerString("spr_probe");
        sprite.IsSpecialType = true;
        sprite.SSpriteType = GameMakerSprite.SpriteType.SWF;
        Assert.False(sprite.ProjectExportable);

        sprite.IsSpecialType = false;
        Assert.False(sprite.ProjectExportable);
        sprite.Textures.Add(new GameMakerSprite.TextureEntry());
        Assert.False(sprite.ProjectExportable);

        sprite.Textures[0].Texture = new GameMakerTexturePageItem();
        sprite.CollisionMasks.Add(new GameMakerSprite.MaskEntry());
        Assert.False(sprite.ProjectExportable);

        sprite.CollisionMasks[0].Data = [];
        Assert.True(sprite.ProjectExportable);
    }

    [Theory("SpriteSerializationTests.cs", 107)]
    [InlineData(2)]
    [InlineData(3)]
    public void ModernSpineSpriteRoundTripsMetadata(int spineVersion)
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.SetLTS(isLTS: false);
        GameMakerSprite sprite = new()
        {
            Name = data.Strings.MakeString("spr_spine"),
            IsSpecialType = true,
            SSpriteType = GameMakerSprite.SpriteType.Spine,
            SVersion = 3,
            SpineVersion = spineVersion,
            SpineCacheVersion = spineVersion == 3 ? 1 : 0,
            SpineJSON = "{\"skeleton\":{}}",
            SpineAtlas = "atlas",
            SpineTextures = [],
            Textures = []
        };
        data.Sprites.Add(sprite);

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerSprite roundTripped = output.Sprites.ByName("spr_spine");

        Assert.True(roundTripped.IsSpecialType);
        Assert.Equal(GameMakerSprite.SpriteType.Spine, roundTripped.SSpriteType);
        Assert.Equal(spineVersion, roundTripped.SpineVersion);
        Assert.Equal(sprite.SpineJSON, roundTripped.SpineJSON);
        Assert.Equal(sprite.SpineAtlas, roundTripped.SpineAtlas);
        Assert.Empty(roundTripped.SpineTextures);
        Assert.False(roundTripped.SpineHasTextureData);
    }

    [Fact("SpriteSerializationTests.cs", 142)]
    public void SwfSpriteRoundTrips()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.SetLTS(isLTS: false);
        data.Sprites.Add(new GameMakerSprite
        {
            Name = data.Strings.MakeString("spr_swf"),
            IsSpecialType = true,
            SSpriteType = GameMakerSprite.SpriteType.SWF,
            SVersion = 3,
            SWFVersion = 8,
            Textures = [],
            YYSWF = new GameMakerYYSWF
            {
                Version = 8,
                JPEGTable = [1, 2, 3],
                Timeline = new GameMakerYYSWFTimeline
                {
                    Framerate = 30,
                    MaskWidth = 16,
                    MaskHeight = 8,
                    UsedItems =
                    [
                        new() {
                            ID = 42,
                            ItemType = GameMakerYYSWFItemType.ItemBitmap,
                            BitmapData = new GameMakerYYSWFBitmapData { TPEIndex = 7, Width = 2, Height = 1 }
                        }
                    ],
                    Frames =
                    [
                        new() {
                            FrameObjects =
                            [
                                new() {
                                    CharID = 42,
                                    ColorMatrix = new GameMakerYYSWFMatrixColor { Additive = new int[4], Multiply = new int[4] },
                                    TransformationMatrix = new GameMakerVectorMatrix33 { Values = new float[9] }
                                }
                            ]
                        }
                    ],
                    CollisionMasks =
                    [
                        new() { RLEData = [0x80] }
                    ]
                }
            }
        });

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerSprite sprite = output.Sprites.ByName("spr_swf");

        Assert.Equal(GameMakerSprite.SpriteType.SWF, sprite.SSpriteType);
        Assert.Equal(8, sprite.SWFVersion);
        Assert.Equal([1, 2, 3], sprite.YYSWF.JPEGTable);
        Assert.Equal(30, sprite.YYSWF.Timeline.Framerate);
        Assert.Equal(16, sprite.YYSWF.Timeline.MaskWidth);
        GameMakerYYSWFItem bitmap = Assert.Single(sprite.YYSWF.Timeline.UsedItems);
        Assert.Equal(42, bitmap.ID);
        Assert.Equal(7, bitmap.BitmapData.TPEIndex);
        Assert.Single(sprite.YYSWF.Timeline.Frames);
        Assert.Equal([0x80], Assert.Single(sprite.YYSWF.Timeline.CollisionMasks).RLEData);
    }

    [Fact("SpriteSerializationTests.cs", 196)]
    public void LegacySwfSpriteRoundTripsEmbeddedBitmap()
    {
        using GameMakerData data = GameMakerData.CreateNew();
        data.Sprites.Add(new GameMakerSprite
        {
            Name = data.Strings.MakeString("spr_legacy_swf"),
            IsSpecialType = true,
            SSpriteType = GameMakerSprite.SpriteType.SWF,
            SVersion = 1,
            SWFVersion = 7,
            YYSWF = new GameMakerYYSWF
            {
                Version = 7,
                Timeline = new GameMakerYYSWFTimeline
                {
                    UsedItems =
                    [
                        new() {
                            ID = 1,
                            ItemType = GameMakerYYSWFItemType.ItemBitmap,
                            BitmapData = new GameMakerYYSWFBitmapData
                            {
                                Type = GameMakerYYSWFBitmapType.TypeJPEG,
                                Width = 1,
                                Height = 1,
                                ImageData = [1, 2],
                                AlphaData = [3],
                                ColorPaletteData = [4]
                            }
                        },
                        new() {
                            ID = 2,
                            ItemType = GameMakerYYSWFItemType.ItemShape,
                            ShapeData = new GameMakerShapeData<GameMakerYYSWFSubShapeData>
                            {
                                StyleGroups =
                                [
                                    new() {
                                        FillStyles = [],
                                        LineStyles = [],
                                        Subshapes =
                                        [
                                            new() {
                                                Points = [new() { X = 1, Y = 2 }],
                                                Lines = [new() { X = 3, Y = 4 }],
                                                Triangles = [0, 1, 2],
                                                LinePoints = [new() { X = 5, Y = 6 }],
                                                LineTriangles = [2, 1, 0],
                                                AALines = [new() { X = 7, Y = 8 }],
                                                AAVectors = [new() { X = 9, Y = 10 }],
                                                LineAALines = [new() { X = 11, Y = 12 }],
                                                LineAAVectors = [new() { X = 13, Y = 14 }]
                                            }
                                        ]
                                    }
                                ]
                            }
                        }
                    ],
                    Frames = [],
                    CollisionMasks = []
                }
            }
        });

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerYYSWFBitmapData bitmap = output.Sprites.ByName("spr_legacy_swf").YYSWF.Timeline.UsedItems
            .Single(item => item.ItemType == GameMakerYYSWFItemType.ItemBitmap).BitmapData;

        Assert.Null(bitmap.TPEIndex);
        Assert.Equal([1, 2], bitmap.ImageData);
        Assert.Equal([3], bitmap.AlphaData);
        Assert.Equal([4], bitmap.ColorPaletteData);
        GameMakerYYSWFSubShapeData subshape = output.Sprites.ByName("spr_legacy_swf").YYSWF.Timeline.UsedItems
            .Single(item => item.ItemType == GameMakerYYSWFItemType.ItemShape).ShapeData.StyleGroups.Single().Subshapes.Single();
        Assert.Equal((1f, 2f), (subshape.Points.Single().X, subshape.Points.Single().Y));
        Assert.Equal([0, 1, 2], subshape.Triangles);
        Assert.Equal([2, 1, 0], subshape.LineTriangles);
        Assert.Equal((13f, 14f), (subshape.LineAAVectors.Single().X, subshape.LineAAVectors.Single().Y));
    }

    [Theory("SpriteSerializationTests.cs", 184)]
    [InlineData(3)]
    [InlineData(4)]
    public void VectorSpriteRoundTripsCollisionMask(int vectorVersion)
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.SetLTS(isLTS: false);
        GameMakerVectorShapeData shape = new()
        {
            Version = vectorVersion,
            StyleGroups =
            [
                new() {
                    FillStyles = [],
                    LineStyles = [],
                    Subshapes =
                    [
                        new() {
                            Points = [new() { X = 1, Y = 2 }],
                            PointColors = [0xFF00FFFF],
                            Triangles = [0, 1, 2],
                            AALines = [new() { X = 3, Y = 4 }],
                            AAVectors = [new() { X = 5, Y = 6 }]
                        }
                    ]
                }
            ]
        };
        data.Sprites.Add(new GameMakerSprite
        {
            Name = data.Strings.MakeString("spr_vector"),
            IsSpecialType = true,
            SSpriteType = GameMakerSprite.SpriteType.Vector,
            SVersion = 3,
            VectorVersion = vectorVersion,
            Textures = [],
            VectorFrameToShapeMap = [0],
            VectorShapes = [shape],
            VectorCollisionMaskWidth = 8,
            VectorCollisionMaskHeight = 1,
            VectorCollisionMaskRLEData = ["?"u8.ToArray()]
        });

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerSprite sprite = output.Sprites.ByName("spr_vector");

        Assert.Equal(vectorVersion, sprite.VectorVersion);
        Assert.Equal(8, sprite.VectorCollisionMaskWidth);
        Assert.Equal(1, sprite.VectorCollisionMaskHeight);
        Assert.Equal([0x3F], sprite.VectorCollisionMaskRLEData.Single());
        GameMakerVectorSubShapeData subshape = sprite.VectorShapes.Single().StyleGroups.Single().Subshapes.Single();
        Assert.Equal(0xFF00FFFFu, subshape.PointColors.Single());
        Assert.Equal([0, 1, 2], subshape.Triangles);
        Assert.Equal((5f, 6f), (subshape.AAVectors.Single().X, subshape.AAVectors.Single().Y));
    }
}
