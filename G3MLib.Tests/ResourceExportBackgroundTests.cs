using G3MLib.DataFile;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class ResourceExportBackgroundTests
{
    [Fact]
    public void BackgroundAndTilesetExportSkipEmptySlots()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        try
        {
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
            using var data = GameMakerIO.Read(input);
            Assert.Contains(data.Backgrounds, background => background is null);
            ResourceExportService.ExportTypes(data, directory, "", ["Backgrounds", "Tilesets"], TestContext.Current.CancellationToken);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
