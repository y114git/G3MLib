using G3MLib.DataFile;
using G3MLib.DataFile.Models;
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

    [Fact]
    public void SingleExtensionCanBeExportedAndImported()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        try
        {
            using var source = GameMakerData.CreateNew2026LTS();
            foreach (var name in new[] { "ext_a", "ext_b" })
            {
                source.Extensions.Add(new GameMakerExtension
                {
                    FolderName = source.Strings.MakeString(""),
                    Name = source.Strings.MakeString(name),
                    Version = source.Strings.MakeString(""),
                    ClassName = source.Strings.MakeString(""),
                    Files = [],
                    Options = []
                });
            }

            var results = ResourceExportService.ExportExtensions(source, directory, new HashSet<string> { "ext_b" }, null, TestContext.Current.CancellationToken);
            Assert.Equal(["ext_b"], results.Select(r => r.Name));
            Assert.False(Directory.Exists(Path.Combine(directory, "Extensions", "ext_a")));

            using var target = GameMakerData.CreateNew2026LTS();
            ResourceImportService.Import("Extensions", target, directory, ["ext_b"], cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("ext_b", Assert.Single(target.Extensions).Name.Content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
