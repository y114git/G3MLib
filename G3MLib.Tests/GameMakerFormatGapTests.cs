using G3MLib.DataFile;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class GameMakerFormatGapTests
{
    private const int Gen8VersionOffset = 60;

    private static byte[] FixtureBytes(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static readonly string[] VersionMarkerChunks = ["UILR", "PSEM", "FEAT", "FEDS", "SEQN", "TGIN"];

    // A fixture with its GEN8 version replaced and the chunks that imply a version renamed, so only GEN8 decides.
    private static byte[] FixtureWithStoredVersion(uint major, uint minor, uint release, uint build)
    {
        var file = FixtureBytes("data-2022.win");
        uint[] version = [major, minor, release, build];
        for (var i = 0; i < 4; i++)
            BitConverter.GetBytes(version[i]).CopyTo(file, Gen8VersionOffset + i * 4);
        var pos = 8;
        while (pos < file.Length)
        {
            if (Array.IndexOf(VersionMarkerChunks, System.Text.Encoding.ASCII.GetString(file, pos, 4)) >= 0)
                file[pos + 3] = (byte)'_';
            pos += 8 + BitConverter.ToInt32(file, pos + 4);
        }
        return file;
    }

    [Fact]
    public void Gen8KeepsStoredGameMaker1Version()
    {
        using MemoryStream input = new(FixtureWithStoredVersion(1, 0, 0, 1539));
        using var data = GameMakerIO.Read(input, onlyGeneralInfo: true);
        Assert.Equal(1u, data.GeneralInfo.Major);
        Assert.Equal(1539u, data.GeneralInfo.Build);
    }

    [Theory]
    [InlineData(1539u, false)]
    [InlineData(1773u, true)]
    public void ExtensionProductDataFollowsStoredBuild(uint build, bool eligible)
    {
        using MemoryStream input = new(FixtureWithStoredVersion(1, 0, 0, build));
        using var data = GameMakerIO.Read(input, onlyGeneralInfo: true);
        Assert.Equal(eligible, GameMakerExtension.ProductDataEligible(data));
    }

    [Fact]
    public void Gen8KeepsStoredGameMaker2Version()
    {
        using MemoryStream input = new(FixtureWithStoredVersion(2, 0, 0, 0));
        using var data = GameMakerIO.Read(input, onlyGeneralInfo: true);
        Assert.Equal((2u, 0u, 0u), (data.GeneralInfo.Major, data.GeneralInfo.Minor, data.GeneralInfo.Release));
    }

    [Fact]
    public void BytesAfterFormEndSurviveRoundTrip()
    {
        var tail = new byte[32];
        for (var i = 0; i < tail.Length; i++)
            tail[i] = (byte)(i + 1);
        byte[] original = [.. FixtureBytes("data-2022.win"), .. tail];
        using MemoryStream input = new(original);
        using var data = GameMakerIO.Read(input);
        Assert.Equal(tail, data.FORM.TrailingBytes);
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        Assert.Equal(original, output.ToArray());
    }

    private static byte[] Written2024_14(bool lts)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.SetGMS2Version(2024, 14, 0, 0, isLTS: lts);
        data.GeneralInfo.Timestamp = 1;
        data.GeneralInfo.GameID = 1;
        data.GeneralInfo.GMS2GameGUID = new byte[16];
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("snd_probe"), AudioLength = 1.5f });
        data.Fonts.Add(new GameMakerFont { Name = data.Strings.MakeString("fnt_probe") });
        GameMakerSprite sprite = new()
        {
            Name = data.Strings.MakeString("spr_probe"),
            Width = 4,
            Height = 4,
            MarginRight = 3,
            MarginBottom = 3,
            Textures = []
        };
        sprite.CollisionMasks.Add(new GameMakerSprite.MaskEntry { Data = new byte[4] });
        data.Sprites.Add(sprite);
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        return output.ToArray();
    }

    [Fact]
    public void Version2024_14WritesTheSameLayoutOnLtsAndNonLts()
    {
        Assert.Equal(Written2024_14(lts: false), Written2024_14(lts: true));
    }

    [Fact]
    public void UnknownChunkPassesThroughInPlace()
    {
        var payload = new byte[16];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(0xA0 + i);
        byte[] injected;
        using (MemoryStream input = new(FixtureBytes("data-2022.win")))
        using (var data = GameMakerIO.Read(input))
        {
            Dictionary<string, GameMakerChunk> ordered = [];
            foreach ((var name, var chunk) in data.FORM.Chunks)
            {
                ordered[name] = chunk;
                if (name == "GEN8")
                    ordered["ZZZZ"] = new GameMakerRawChunk("ZZZZ") { RawData = payload };
            }
            data.FORM.Chunks = ordered;
            using MemoryStream output = new();
            GameMakerIO.Write(output, data);
            injected = output.ToArray();
        }

        using MemoryStream reread = new(injected);
        using var readBack = GameMakerIO.Read(reread);
        List<string> names = [.. readBack.FORM.Chunks.Keys];
        Assert.Equal(["GEN8", "ZZZZ", "OPTN"], names.GetRange(0, 3));
        Assert.Equal(payload, ((GameMakerRawChunk)readBack.FORM.Chunks["ZZZZ"]).RawData[..payload.Length]);
        using MemoryStream second = new();
        GameMakerIO.Write(second, readBack);
        Assert.Equal(injected, second.ToArray());
    }

    private static int ChunkLength(byte[] file, string name)
    {
        var pos = 8;
        while (pos < file.Length)
        {
            var length = BitConverter.ToInt32(file, pos + 4);
            if (System.Text.Encoding.ASCII.GetString(file, pos, 4) == name)
                return length;
            pos += 8 + length;
        }
        return -1;
    }

    [Fact]
    public void ParticleEmittersKeepTheirChunkFormatWhenTheFileVersionIsRaised()
    {
        byte[] legacy;
        using (var data = GameMakerData.CreateNew2026LTS())
        {
            foreach (var marker in new[] { "UILR", "FEAT", "FEDS", "SEQN", "TGIN", "PSYS", "EMBI", "ROOM" })
                data.FORM.Chunks.Remove(marker);
            data.SetGMS2Version(2023, 4);
            data.ParticleSystemEmitters.Add(new GameMakerParticleSystemEmitter { Name = data.Strings.MakeString("em_probe") });
            using MemoryStream output = new();
            GameMakerIO.Write(output, data);
            legacy = output.ToArray();
        }

        using MemoryStream input = new(legacy);
        using var read = GameMakerIO.Read(input);
        Assert.Equal("em_probe", Assert.Single(read.ParticleSystemEmitters).Name.Content);
        read.SetGMS2Version(2024, 14);
        using MemoryStream rewritten = new();
        GameMakerIO.Write(rewritten, read);
        Assert.Equal(ChunkLength(legacy, "PSEM"), ChunkLength(rewritten.ToArray(), "PSEM"));
    }

    [Theory]
    [InlineData(2024, 4)]
    [InlineData(2023, 8)]
    public void AudioGroupAssetTypeSharesTheSoundId(uint major, uint minor)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.SetGMS2Version(major, minor);
        var adapt = typeof(GameMakerInstruction).GetMethod(
            "AdaptAssetTypeId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.Equal(
            adapt.Invoke(null, [data, Analyzer.AssetType.Sound]),
            adapt.Invoke(null, [data, Analyzer.AssetType.AudioGroup]));
    }

    private static GameMakerData WriteAndReadFresh2026Lts()
    {
        using var fresh = GameMakerData.CreateNew2026LTS();
        using MemoryStream output = new();
        GameMakerIO.Write(output, fresh);
        output.Position = 0;
        return GameMakerIO.Read(output);
    }

    [Fact]
    public void EmptyObjectChunkDoesNotImplyVersion2026_1()
    {
        using var data = WriteAndReadFresh2026Lts();
        Assert.False(data.IsVersionAtLeast(2026, 1));
    }

    [Theory]
    [InlineData(2022u, 8u, 0u, "GM 2022.8 LTS")]
    [InlineData(2023u, 6u, 0u, "GM 2023.6 LTS")]
    [InlineData(2024u, 14u, 1u, "GM 2024.14.1 LTS")]
    [InlineData(2026u, 1u, 0u, "GM 2026.1 LTS")]
    public void LtsDisplayNamesTheDetectedVersion(uint major, uint minor, uint release, string expected)
    {
        GameMakerGeneralInfo info = new()
        {
            Version = new GameMakerGeneralInfo.RuntimeVersion(major, minor, release, 0, GameMakerGeneralInfo.BranchType.LTS2022_0)
        };
        Assert.Equal(expected, Modding.Internal.GeneralInfoUtil.GetVersionDisplay(info));
    }

    [Fact]
    public void VersionBasisNamesTheChunkThatSetTheVersion()
    {
        using var detected = WriteAndReadFresh2026Lts();
        // The last chunk to raise the version wins, here the font chunk after UILR.
        Assert.Equal("FONT", detected.GeneralInfo.VersionSource);
        Assert.Equal("detected from FONT", Modding.Internal.GeneralInfoUtil.GetVersionBasis(detected.GeneralInfo));

        using MemoryStream input = new(FixtureWithStoredVersion(1, 0, 0, 1539));
        using var stored = GameMakerIO.Read(input, onlyGeneralInfo: true);
        Assert.Null(stored.GeneralInfo.VersionSource);
        Assert.Equal("stored in GEN8", Modding.Internal.GeneralInfoUtil.GetVersionBasis(stored.GeneralInfo));
    }

    [Fact]
    public void SequenceAudioTrackKeepsAnOutOfRangeSoundId()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.GeneralInfo.Timestamp = 1;
        data.GeneralInfo.GameID = 1;
        data.GeneralInfo.GMS2GameGUID = new byte[16];
        data.FORM.Chunks.Remove("ROOM"); // the default room is not stable across a round trip
        GameMakerSequence.Keyframe<GameMakerSequence.AudioKeyframes.Data> keyframe = new();
        keyframe.Channels.Add(new GameMakerSequence.Keyframe<GameMakerSequence.AudioKeyframes.Data>.KeyframeChannel
        {
            Value = new GameMakerSequence.AudioKeyframes.Data { Resource = new GameMakerResourceById<GameMakerSound, GameMakerChunkSOND>(0) }
        });
        GameMakerSequence.Track track = new()
        {
            ModelName = data.Strings.MakeString("GMAudioTrack"),
            Name = data.Strings.MakeString("audio"),
            Tags = [],
            OwnedResources = [],
            Tracks = [],
            Keyframes = new GameMakerSequence.AudioKeyframes { List = [keyframe] }
        };
        data.Sequences.Add(new GameMakerSequence
        {
            Name = data.Strings.MakeString("seq_probe"),
            BroadcastMessages = [],
            Moments = [],
            Tracks = [track],
            FunctionIDs = []
        });
        Assert.Empty(data.Sounds);

        using MemoryStream first = new();
        GameMakerIO.Write(first, data);
        using MemoryStream input = new(first.ToArray());
        using var read = GameMakerIO.Read(input);
        var audio = (GameMakerSequence.AudioKeyframes)read.Sequences[0].Tracks[0].Keyframes;
        Assert.Equal(0, audio.List[0].Channels[0].Value.Resource.CachedId);
        using MemoryStream second = new();
        GameMakerIO.Write(second, read);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Theory]
    [InlineData("GMGraphicTrack")]
    [InlineData("GMInstanceTrack")]
    public void SequenceTracksKeepOutOfRangeResourceIds(string modelName)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.GeneralInfo.Timestamp = 1;
        data.GeneralInfo.GameID = 1;
        data.GeneralInfo.GMS2GameGUID = new byte[16];
        data.FORM.Chunks.Remove("ROOM"); // the default room is not stable across a round trip
        GameMakerSequence.ITrackKeyframes keyframes;
        if (modelName == "GMGraphicTrack")
        {
            GameMakerSequence.Keyframe<GameMakerSequence.GraphicKeyframes.Data> keyframe = new();
            keyframe.Channels.Add(new GameMakerSequence.Keyframe<GameMakerSequence.GraphicKeyframes.Data>.KeyframeChannel
            {
                Value = new GameMakerSequence.GraphicKeyframes.Data { Resource = new GameMakerResourceById<GameMakerSprite, GameMakerChunkSPRT>(3) }
            });
            keyframes = new GameMakerSequence.GraphicKeyframes { List = [keyframe] };
        }
        else
        {
            GameMakerSequence.Keyframe<GameMakerSequence.InstanceKeyframes.Data> keyframe = new();
            keyframe.Channels.Add(new GameMakerSequence.Keyframe<GameMakerSequence.InstanceKeyframes.Data>.KeyframeChannel
            {
                Value = new GameMakerSequence.InstanceKeyframes.Data { Resource = new GameMakerResourceById<GameMakerGameObject, GameMakerChunkOBJT>(2) }
            });
            keyframes = new GameMakerSequence.InstanceKeyframes { List = [keyframe] };
        }
        data.Sequences.Add(new GameMakerSequence
        {
            Name = data.Strings.MakeString("seq_probe"),
            BroadcastMessages = [],
            Moments = [],
            Tracks =
            [
                new GameMakerSequence.Track
                {
                    ModelName = data.Strings.MakeString(modelName),
                    Name = data.Strings.MakeString("track"),
                    Tags = [],
                    OwnedResources = [],
                    Tracks = [],
                    Keyframes = keyframes
                }
            ],
            FunctionIDs = []
        });

        using MemoryStream first = new();
        GameMakerIO.Write(first, data);
        using MemoryStream input = new(first.ToArray());
        using var read = GameMakerIO.Read(input);
        using MemoryStream second = new();
        GameMakerIO.Write(second, read);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Theory]
    [InlineData(2022u, 8u, 0u, "GM 2022.8 LTS")]
    [InlineData(2026u, 1u, 0u, "GM 2026.1 LTS")]
    public void GeneralInfoToStringUsesTheDisplayLabel(uint major, uint minor, uint release, string label)
    {
        GameMakerGeneralInfo info = new()
        {
            Version = new GameMakerGeneralInfo.RuntimeVersion(major, minor, release, 0, GameMakerGeneralInfo.BranchType.LTS2022_0)
        };
        Assert.Equal($" ({label})", info.ToString());
    }

    [Fact]
    public void FreshLtsDataWithItsDefaultRoomIsStableAcrossWriteAndRead()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.GeneralInfo.Timestamp = 1;
        data.GeneralInfo.GameID = 1;
        data.GeneralInfo.GMS2GameGUID = new byte[16];
        using MemoryStream first = new();
        GameMakerIO.Write(first, data);
        using MemoryStream input = new(first.ToArray());
        using var read = GameMakerIO.Read(input);
        using MemoryStream second = new();
        GameMakerIO.Write(second, read);
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal("room0", Assert.Single(read.Rooms).Name.Content);
    }

    [Fact]
    public void ParticleEmittersWithTwoEntriesReadByTheirSpacingAndRoundTrip()
    {
        byte[] legacy;
        using (var data = GameMakerData.CreateNew2026LTS())
        {
            foreach (var marker in new[] { "UILR", "FEAT", "FEDS", "SEQN", "TGIN", "PSYS", "EMBI", "ROOM" })
                data.FORM.Chunks.Remove(marker);
            data.GeneralInfo.Timestamp = 1;
            data.GeneralInfo.GameID = 1;
            data.GeneralInfo.GMS2GameGUID = new byte[16];
            data.SetGMS2Version(2023, 4);
            data.ParticleSystemEmitters.Add(new GameMakerParticleSystemEmitter { Name = data.Strings.MakeString("em_a") });
            data.ParticleSystemEmitters.Add(new GameMakerParticleSystemEmitter { Name = data.Strings.MakeString("em_b") });
            using MemoryStream output = new();
            GameMakerIO.Write(output, data);
            legacy = output.ToArray();
        }

        using MemoryStream input = new(legacy);
        using var read = GameMakerIO.Read(input);
        Assert.Equal(["em_a", "em_b"], read.ParticleSystemEmitters.Select(e => e.Name.Content));
        using MemoryStream rewritten = new();
        GameMakerIO.Write(rewritten, read);
        Assert.Equal(legacy, rewritten.ToArray());
    }

    [Fact]
    public void Gen8KeepsStoredGameMaker2VersionNumbersOnWrite()
    {
        var original = FixtureBytes("data-2022.win");
        uint[] stored = [2, 3, 1, 5];
        for (var i = 0; i < 4; i++)
            BitConverter.GetBytes(stored[i]).CopyTo(original, Gen8VersionOffset + i * 4);
        using MemoryStream input = new(original);
        using var data = GameMakerIO.Read(input);
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        Assert.Equal(original, output.ToArray());
    }

    [Theory]
    [InlineData(2024u, 14u, GameMakerGeneralInfo.BranchType.Post2022_0, "GM 2024.14 or later")]
    [InlineData(2024u, 14u, GameMakerGeneralInfo.BranchType.LTS2022_0, "GM 2024.14 or later LTS")]
    [InlineData(2024u, 13u, GameMakerGeneralInfo.BranchType.Post2022_0, "GM 2024.13")]
    public void Version2024_14IsLabelledOrLaterBecauseNoMarkerFollowsIt(uint major, uint minor, GameMakerGeneralInfo.BranchType branch, string expected)
    {
        GameMakerGeneralInfo info = new() { Version = new GameMakerGeneralInfo.RuntimeVersion(major, minor, 0, 0, branch) };
        Assert.Equal(expected, Modding.Internal.GeneralInfoUtil.GetVersionDisplay(info));
    }

    [Theory]
    [InlineData(2025u)]
    [InlineData(2027u)]
    public void SetVersionAcceptsMajorVersionsAfter2024(uint major)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.SetGMS2Version(major, 1);
        Assert.Equal(major, data.GeneralInfo.Major);
    }

    [Fact]
    public void SetVersionStillRejectsUnknownMajorVersions()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        Assert.Throws<NotSupportedException>(() => data.SetGMS2Version(3, 0));
        Assert.Throws<NotSupportedException>(() => data.SetGMS2Version(2021, 0));
    }
}
