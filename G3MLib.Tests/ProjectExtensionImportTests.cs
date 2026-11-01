using G3MLib.DataFile;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class ProjectExtensionImportTests
{
    [Fact]
    public void ExtensionCreatedFromExportedFolderCanBeWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        try
        {
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2022.win"));
            using var source = GameMakerIO.Read(input);
            Assert.NotEmpty(source.Extensions);
            ResourceExportService.ExportTypes(source, directory, "", ["Extensions"], TestContext.Current.CancellationToken);

            using var target = GameMakerData.CreateNew2026LTS();
            Assert.True(ResourceImportService.Import("Extensions", target, Path.Combine(directory, "Extensions"), cancellationToken: TestContext.Current.CancellationToken));
            Assert.NotEmpty(target.Extensions);
            GameMakerIO.Write(new MemoryStream(), target);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
