using G3MLib.Modding.Internal;

namespace G3MLib.Tests;

public sealed class OutputFileCommitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCommitRestoresAlreadyWrittenFiles(bool replaceExisting)
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-output-test-").FullName;
        try
        {
            var first = Path.Combine(directory, "first.tmp");
            var second = Path.Combine(directory, "second.tmp");
            var destination = Path.Combine(directory, "output.dat");
            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(first, "new");
            File.WriteAllText(second, "second");
            if (replaceExisting) File.WriteAllText(destination, "original");
            Directory.CreateDirectory(blocked);
            Assert.ThrowsAny<IOException>(() => OutputFileCommit.Commit([(first, destination), (second, blocked)]));
            if (replaceExisting) Assert.Equal("original", File.ReadAllText(destination));
            else Assert.False(File.Exists(destination));
            Assert.Empty(Directory.GetDirectories(directory, ".g3mlib-backup-*"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
