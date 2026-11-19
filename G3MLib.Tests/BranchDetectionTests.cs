using G3MLib.DataFile;

namespace G3MLib.Tests;

public class BranchDetectionTests
{
    [Fact]
    public void NewDataHasNoDetectionEvidence()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        Assert.True(data.IsLTSBranch());
        Assert.Null(data.GeneralInfo.BranchSource);
    }

    [Fact]
    public void ReadingNamesTheChunkThatDecidedTheBranch()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        using var stream = new MemoryStream();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using var read = GameMakerIO.Read(stream);
        // The UILR chunk exempts the file from LTS, a later chunk then raised the version
        Assert.False(read.IsLTSBranch());
        Assert.Equal("UILR", read.GeneralInfo.BranchSource);
        Assert.Equal("FONT", read.GeneralInfo.VersionSource);
    }
}
