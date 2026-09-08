using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;

namespace G3MLib.Tests;

public sealed class BuiltinListTests
{
    [Theory]
    [InlineData(2023, 10, false)]
    [InlineData(2023, 11, true)]
    [InlineData(2024, 1, true)]
    public void Includes2023_11FunctionsAtTheCorrectRuntimeVersion(uint major, uint minor, bool expected)
    {
        GameMakerData data = GameMakerData.CreateNew();
        data.GeneralInfo.Major = major;
        data.GeneralInfo.Minor = minor;

        BuiltinList builtins = new(data);

        Assert.Equal(expected, builtins.Functions.ContainsKey("move_and_collide"));
        Assert.Equal(expected, builtins.Functions.ContainsKey("dbg_view"));
    }
}
