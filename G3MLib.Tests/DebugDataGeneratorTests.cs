using G3MLib.DataFile;
using G3MLib.DataFile.ModelsDebug;

namespace G3MLib.Tests;

public sealed class DebugDataGeneratorTests
{
    private static GameMakerData Load()
    {
        using MemoryStream stream = new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2022.win")));
        return GameMakerIO.Read(stream);
    }

    [Theory]
    [InlineData(DebugDataMode.Decompiled)]
    [InlineData(DebugDataMode.Assembler)]
    [InlineData(DebugDataMode.FullAssembler)]
    public void DebugDataHasOneEntryPerCodeEntryAndReadsBack(DebugDataMode mode)
    {
        var data = Load();
        var debug = DebugDataGenerator.Generate(data, mode, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(data.Code.Count, debug.SourceCode.Count);
        Assert.Equal(data.Code.Count, debug.DebugInfo.Count);

        using MemoryStream stream = new();
        GameMakerDebugIO.Write(stream, debug);
        stream.Position = 0;
        var back = GameMakerDebugIO.Read(stream);
        Assert.Equal(debug.SourceCode.Count, back.SourceCode.Count);
        Assert.Equal(debug.SourceCode[0].SourceCode.Content, back.SourceCode[0].SourceCode.Content);
    }

    [Fact]
    public void FullAssemblerMarksMoreInstructionsThanAssembler()
    {
        var data = Load();
        var some = DebugDataGenerator.Generate(data, DebugDataMode.Assembler, cancellationToken: TestContext.Current.CancellationToken);
        var all = DebugDataGenerator.Generate(data, DebugDataMode.FullAssembler, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(all.DebugInfo.Sum(i => i.Count) > some.DebugInfo.Sum(i => i.Count));
    }

    [Fact]
    public void GenerationHonoursCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => DebugDataGenerator.Generate(Load(), DebugDataMode.Assembler, cancellationToken: source.Token));
    }
}
