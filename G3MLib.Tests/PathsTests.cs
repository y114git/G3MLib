using G3MLib.DataFile.Util;

namespace G3MLib.Tests;

public sealed class PathsTests
{
    [Fact]
    public void ContainmentRespectsDirectoryBoundariesAndPlatformCasing()
    {
        string root = Path.Combine(Path.GetTempPath(), "GameData");
        Assert.True(Paths.IsWithinDirectory(root, Path.Combine(root, "audio", "group.dat")));
        Assert.False(Paths.IsWithinDirectory(root, Path.Combine(root, "..", "outside.dat")));
        Assert.False(Paths.IsWithinDirectory(root, root + "Sibling" + Path.DirectorySeparatorChar + "group.dat"));
        Assert.Equal(OperatingSystem.IsWindows(), Paths.IsWithinDirectory(root, Path.Combine(Path.GetTempPath(), "gamedata", "group.dat")));
    }
}
