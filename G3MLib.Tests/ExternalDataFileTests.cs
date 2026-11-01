using G3MLib.DataFile;

namespace G3MLib.Tests;

public sealed class ExternalDataFileTests
{
    [Fact]
    public void ReadWriteReadConfiguredExternalDataFiles()
    {
        var configuredFiles = Environment.GetEnvironmentVariable("G3MLIB_EXTERNAL_DATA_FILES");
        if (string.IsNullOrWhiteSpace(configuredFiles))
            return;

        foreach (var path in configuredFiles.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Assert.True(File.Exists(path), $"Configured data file does not exist: {path}");

            using var input = File.OpenRead(path);
            using var data = GameMakerIO.Read(input);
            using MemoryStream output = new();
            GameMakerIO.Write(output, data);
            output.Position = 0;
            using var reread = GameMakerIO.Read(output);

            Assert.Equal(data.GeneralInfo.BytecodeVersion, reread.GeneralInfo.BytecodeVersion);
            Assert.Equal(data.Strings.Count, reread.Strings.Count);
            Assert.Equal(data.Code.Count, reread.Code.Count);
            Assert.Equal(data.Rooms.Count, reread.Rooms.Count);
        }
    }
}
