using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class GameMakerIoTests
{
    public static TheoryData<string> DataFixtures =>
    [
        (TheoryDataRow<string>)"data-2022.win",
        (TheoryDataRow<string>)"data-2023.win",
        (TheoryDataRow<string>)"data-2024.win",
        (TheoryDataRow<string>)"data-2026.win"
    ];

    public static TheoryData<string> DebugFixtures =>
    [
        (TheoryDataRow<string>)"debug-2022.yydebug",
        (TheoryDataRow<string>)"debug-2023.yydebug",
        (TheoryDataRow<string>)"debug-2024.yydebug",
        (TheoryDataRow<string>)"debug-2026.yydebug"
    ];

    [Fact("GameMakerIoTests.cs", 7)]
    public void ReadRejectsNonFormRoot()
    {
        MemoryStream stream = new("NOPE"u8.ToArray());
        try
        {
            Assert.Throws<IOException>(() => GameMakerIO.Read(stream));
        }
        finally
        {
            if (stream != null)
            {
                ((IDisposable)stream).Dispose();
            }
        }
    }

    [Fact("GameMakerIoTests.cs", 15)]
    public void ReadRejectsFormLengthBeyondInput()
    {
        MemoryStream stream = new(((ReadOnlySpan<byte>)[70, 79, 82, 77, 1, 0, 0, 0]).ToArray());
        try
        {
            Assert.Throws<IOException>(() => GameMakerIO.Read(stream));
        }
        finally
        {
            if (stream != null)
            {
                ((IDisposable)stream).Dispose();
            }
        }
    }

    [Fact("GameMakerIoTests.cs", 23)]
    public void ReadDebugRejectsNonFormRoot()
    {
        MemoryStream stream = new("NOPE"u8.ToArray());
        try
        {
            Assert.Throws<IOException>(() => GameMakerDebugIO.Read(stream));
        }
        finally
        {
            if (stream != null)
            {
                ((IDisposable)stream).Dispose();
            }
        }
    }

    [Fact("GameMakerIoTests.cs", 31)]
    public void ReadDebugRejectsFormLengthBeyondInput()
    {
        MemoryStream stream = new(((ReadOnlySpan<byte>)[70, 79, 82, 77, 1, 0, 0, 0]).ToArray());
        try
        {
            Assert.Throws<IOException>(() => GameMakerDebugIO.Read(stream));
        }
        finally
        {
            if (stream != null)
            {
                ((IDisposable)stream).Dispose();
            }
        }
    }

    [Theory("GameMakerIoTests.cs", 55)]
    [MemberData(nameof(DataFixtures))]
    public void ReadDataFixture(string fileName)
    {
        using FileStream stream = OpenFixture(fileName);
        using GameMakerData data = GameMakerIO.Read(stream);
        Assert.NotNull(data.FORM);
        Assert.NotNull(data.GeneralInfo);
        Assert.NotNull(data.Strings);
    }

    [Fact("GameMakerIoTests.cs", 65)]
    public void ShaderRoundTripsSourceAndAttributes()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.Shaders.Add(new GameMakerShader
        {
            Name = data.Strings.MakeString("sh_probe"),
            GLSL_ES_Vertex = data.Strings.MakeString("void main() {}"),
            GLSL_ES_Fragment = data.Strings.MakeString("void main() {}"),
            HLSL11_VertexData = new GameMakerShader.GameMakerRawShaderData { IsNull = false, Data = [1, 2] },
            HLSL11_PixelData = new GameMakerShader.GameMakerRawShaderData { IsNull = false, Data = [3] },
            VertexShaderAttributes =
            [
                new() { Name = data.Strings.MakeString("in_Position") }
            ]
        });
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData output = GameMakerIO.Read(stream);
        GameMakerShader shader = output.Shaders.ByName("sh_probe");
        Assert.Equal("void main() {}", shader.GLSL_ES_Vertex.Content);
        Assert.Equal("in_Position", Assert.Single(shader.VertexShaderAttributes).Name.Content);
        Assert.Equal([1, 2], shader.HLSL11_VertexData.Data[..2]);
        Assert.Equal([3], shader.HLSL11_PixelData.Data[..1]);
    }

    [Theory("GameMakerIoTests.cs", 65)]
    [MemberData(nameof(DataFixtures))]
    public void ReadRejectsRepresentativeTruncations(string fileName)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        foreach (int length in new HashSet<int> { 0, 4, 8, original.Length / 4, original.Length / 2, original.Length - 1 })
        {
            using MemoryStream stream = new(original[..length]);
            Assert.Throws<IOException>(() => GameMakerIO.Read(stream));
        }
    }

    [Theory("GameMakerIoTests.cs", 67)]
    [MemberData(nameof(DataFixtures))]
    public void ReadDataFixtureGeneralInfoOnly(string fileName)
    {
        using FileStream stream = OpenFixture(fileName);
        using GameMakerData data = GameMakerIO.Read(stream, null, null, onlyGeneralInfo: true);
        Assert.NotNull(data.FORM);
        Assert.NotNull(data.GeneralInfo);
        Assert.Null(data.Strings);
    }

    [Theory("GameMakerIoTests.cs", 79)]
    [MemberData(nameof(DataFixtures))]
    public void ReadWriteReadDataFixture(string fileName)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        using MemoryStream input = new(original);
        using GameMakerData data = GameMakerIO.Read(input);
        using MemoryStream output = new();
        GameMakerIO.Write(output, data);
        Assert.Equal(original, output.ToArray());
        output.Position = 0L;
        using GameMakerData writtenData = GameMakerIO.Read(output);
        Assert.Equal(data.GeneralInfo.BytecodeVersion, writtenData.GeneralInfo.BytecodeVersion);
        Assert.Equal(data.Strings.Count, writtenData.Strings.Count);
    }

    [Theory("GameMakerIoTests.cs", 97)]
    [MemberData(nameof(DataFixtures))]
    public void GenerateOffsetMapForDataFixture(string fileName)
    {
        using FileStream stream = OpenFixture(fileName);
        Dictionary<uint, GameMakerObject> collection = GameMakerIO.GenerateOffsetMap(stream);
        Assert.NotEmpty(collection);
        Assert.All<KeyValuePair<uint, GameMakerObject>>(collection, delegate (KeyValuePair<uint, GameMakerObject> entry)
        {
            Assert.NotNull(entry.Value);
        });
    }

    [Theory("GameMakerIoTests.cs", 108)]
    [MemberData(nameof(DebugFixtures))]
    public void ReadDebugFixture(string fileName)
    {
        using FileStream stream = OpenFixture(fileName);
        GameMakerDebugData gameMakerDebugData = GameMakerDebugIO.Read(stream);
        Assert.NotNull(gameMakerDebugData.FORM);
        Assert.NotNull(gameMakerDebugData.Strings);
        Assert.NotNull(gameMakerDebugData.SourceCode);
    }

    [Theory("GameMakerIoTests.cs", 121)]
    [MemberData(nameof(DebugFixtures))]
    public void ReadDebugRejectsRepresentativeTruncations(string fileName)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        foreach (int length in new HashSet<int> { 0, 4, 8, original.Length / 4, original.Length / 2, original.Length - 1 })
        {
            using MemoryStream stream = new(original[..length]);
            Assert.Throws<IOException>(() => GameMakerDebugIO.Read(stream));
        }
    }

    [Theory("GameMakerIoTests.cs", 120)]
    [MemberData(nameof(DebugFixtures))]
    public void ReadWriteReadDebugFixture(string fileName)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        using MemoryStream input = new(original);
        GameMakerDebugData data = GameMakerDebugIO.Read(input);
        using MemoryStream output = new();
        GameMakerDebugIO.Write(output, data);
        Assert.Equal(original, output.ToArray());
        output.Position = 0L;
        GameMakerDebugData writtenData = GameMakerDebugIO.Read(output);
        Assert.Equal(data.Strings.Count, writtenData.Strings.Count);
    }

    [Fact("GameMakerIoTests.cs", 137)]
    public void CreateNewDataRoundTrips()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        using MemoryStream stream = new();
        Assert.True(data.IsLTSBranch());
        data.SetLTS(isLTS: false);
        Assert.False(data.IsLTSBranch());
        Assert.True(data.IsNonLTSVersionAtLeast(2026u, 1u));
        data.SetLTS(isLTS: true);
        GameMakerIO.Write(stream, data);
        stream.Position = 0L;
        using GameMakerData readData = GameMakerIO.Read(stream);
        Assert.Equal(data.GeneralInfo.BytecodeVersion, readData.GeneralInfo.BytecodeVersion);
        Assert.Equal(data.Options.LoadAlpha, readData.Options.LoadAlpha);
        Assert.True(readData.IsGameMaker2());
        Assert.True(readData.IsVersionAtLeast(2024u, 14u));
    }

    [Theory("GameMakerIoTests.cs", 221)]
    [InlineData(true)]
    [InlineData(false)]
    public void DataRoundTripsBothOptionLayouts(bool newFormat)
    {
        const GameMakerOptions.OptionsFlags optionFlags =
            GameMakerOptions.OptionsFlags.FullScreen |
            GameMakerOptions.OptionsFlags.InterpolatePixels |
            GameMakerOptions.OptionsFlags.UseNewAudio |
            GameMakerOptions.OptionsFlags.NoBorder |
            GameMakerOptions.OptionsFlags.ShowCursor |
            GameMakerOptions.OptionsFlags.Sizeable |
            GameMakerOptions.OptionsFlags.StayOnTop |
            GameMakerOptions.OptionsFlags.ChangeResolution |
            GameMakerOptions.OptionsFlags.NoButtons |
            GameMakerOptions.OptionsFlags.ScreenKey |
            GameMakerOptions.OptionsFlags.HelpKey |
            GameMakerOptions.OptionsFlags.QuitKey |
            GameMakerOptions.OptionsFlags.SaveKey |
            GameMakerOptions.OptionsFlags.ScreenShotKey |
            GameMakerOptions.OptionsFlags.CloseSec |
            GameMakerOptions.OptionsFlags.Freeze |
            GameMakerOptions.OptionsFlags.ShowProgress |
            GameMakerOptions.OptionsFlags.LoadTransparent |
            GameMakerOptions.OptionsFlags.ScaleProgress |
            GameMakerOptions.OptionsFlags.DisplayErrors |
            GameMakerOptions.OptionsFlags.WriteErrors |
            GameMakerOptions.OptionsFlags.AbortErrors |
            GameMakerOptions.OptionsFlags.VariableErrors |
            GameMakerOptions.OptionsFlags.CreationEventOrder;
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.Options.NewFormat = newFormat;
        data.Options.Info = optionFlags;
        data.Options.Scale = 2;
        data.Options.WindowColor = 0x12345678;
        data.Options.ColorDepth = 32;
        data.Options.Resolution = 4;
        data.Options.Frequency = 60;
        data.Options.VertexSync = 1;
        data.Options.Priority = 3;
        data.Options.LoadAlpha = 127;
        data.Options.Constants.Add(new GameMakerOptions.Constant
        {
            Name = data.Strings.MakeString("OPTION_TEST"),
            Value = data.Strings.MakeString("42")
        });

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData readData = GameMakerIO.Read(stream);

        Assert.Equal(newFormat, readData.Options.NewFormat);
        Assert.Equal(optionFlags, readData.Options.Info);
        Assert.Equal(2, readData.Options.Scale);
        Assert.Equal(0x12345678u, readData.Options.WindowColor);
        Assert.Equal(127u, readData.Options.LoadAlpha);
        Assert.Contains(readData.Options.Constants, constant => constant.Name.Content == "OPTION_TEST" && constant.Value.Content == "42");
    }

    [Fact("GameMakerIoTests.cs", 158)]
    public void CreateNewDebugDataRoundTrips()
    {
        GameMakerDebugData data = GameMakerDebugData.CreateNew();
        using MemoryStream stream = new();
        Assert.Empty(data.SourceCode);
        Assert.Empty(data.DebugInfo);
        int num = 1;
        List<uint> list = new(num);
        CollectionsMarshal.SetCount(list, num);
        CollectionsMarshal.AsSpan(list)[0] = 0u;
        Assert.Equal<List<uint>>(list, data.FORM.DBGI.CodeEntryIndices);
        GameMakerDebugIO.Write(stream, data);
        stream.Position = 0L;
        GameMakerDebugData readData = GameMakerDebugIO.Read(stream);
        Assert.Empty(readData.SourceCode);
        Assert.Empty(readData.DebugInfo);
        Assert.Empty(readData.InstanceVars);
        Assert.Empty(readData.LocalVars);
        Assert.Empty(readData.Strings);
        num = 1;
        List<uint> list2 = new(num);
        CollectionsMarshal.SetCount(list2, num);
        CollectionsMarshal.AsSpan(list2)[0] = 0u;
        Assert.Equal<List<uint>>(list2, readData.FORM.DBGI.CodeEntryIndices);
    }

    private static FileStream OpenFixture(string fileName)
    {
        return File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
    }
}
