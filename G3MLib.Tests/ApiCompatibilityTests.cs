using G3MLib.Analyzer;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.Tests;

public sealed class ApiCompatibilityTests
{
    [Fact]
    public void ExposesUpstreamMetadataTypesAndVersionHelper()
    {
        Assert.Equal("OpcodeInfo", typeof(IGMInstruction.OpcodeInfo).Name);
        Assert.Equal("DataTypeInfo", typeof(IGMInstruction.DataTypeInfo).Name);
        Assert.False(string.IsNullOrWhiteSpace(GitVersion.GetGitVersion()));
        Assert.NotNull(typeof(DebugUtil).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(GameMakerResourceById<GameMakerSprite, GameMakerChunkSPRT>).GetConstructor([typeof(GameMakerSprite)]));
        Assert.NotNull(typeof(Assembler).GetMethod(nameof(Assembler.Assemble), [typeof(string), typeof(GameMakerData), typeof(Action<Action>)]));
    }
}
