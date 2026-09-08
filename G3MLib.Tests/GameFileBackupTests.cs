using System;
using System.IO;
using G3MLib.DataFile.Project;
using Xunit;

namespace G3MLib.Tests;

public sealed class GameFileBackupTests
{
    [Fact("GameFileBackupTests.cs", 7)]
    public void BackupFileUsesSha256AndRestoresTheOriginalFile()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string file = Path.Combine(directory, "data.win");
            File.WriteAllText(file, "original");
            GameFileBackup gameFileBackup = new(directory);
            gameFileBackup.BackupFile(file);
            gameFileBackup.SaveManifest();
            string manifest = File.ReadAllText(Path.Combine(directory, "mod-backup-manifest.json"));
            Assert.Contains("\"HashSHA256\"", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("\"HashSHA1\"", manifest, StringComparison.Ordinal);
            File.WriteAllText(file, "modified");
            gameFileBackup.RestoreFiles();
            Assert.Equal("original", File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact("GameFileBackupTests.cs", 35)]
    public void RestoreFilesAcceptsLegacySha1Manifests()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string text = Path.Combine(directory, ".modbackup");
            Directory.CreateDirectory(text);
            File.WriteAllText(Path.Combine(text, "data.win"), "original");
            File.WriteAllText(Path.Combine(directory, "data.win"), "modified");
            File.WriteAllText(Path.Combine(directory, "mod-backup-manifest.json"), "{\"ManifestVersion\":\"v1\",\"Path\":\".modbackup\",\"Files\":[{\"Path\":\"data.win\",\"BackupPath\":\"data.win\",\"HashSHA1\":\"d73ef92426f2b11dfc4aed4d4bfc41c49ee1087c\"}],\"Directories\":[]}");
            new GameFileBackup(directory).RestoreFiles();
            Assert.Equal("original", File.ReadAllText(Path.Combine(directory, "data.win")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string text = Path.Combine(Path.GetTempPath(), "G3MLib.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(text);
        return text;
    }
}
