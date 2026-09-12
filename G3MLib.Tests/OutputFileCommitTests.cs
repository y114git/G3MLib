using System;
using System.IO;
using G3MLib.Modding.Internal;
using Xunit;

namespace G3MLib.Tests;

public sealed class OutputFileCommitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCommitRestoresAlreadyWrittenFiles(bool replaceExisting)
    {
        string directory = Directory.CreateTempSubdirectory("g3mlib-output-test-").FullName;
        try
        {
            string first = Path.Combine(directory, "first.tmp");
            string second = Path.Combine(directory, "second.tmp");
            string destination = Path.Combine(directory, "output.dat");
            string blocked = Path.Combine(directory, "blocked");
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
