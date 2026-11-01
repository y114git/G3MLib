using G3MLib.Modding.Hashing;

namespace G3MLib.Tests;

public sealed class FileHashTests
{
    [Fact("FileHashTests.cs", 12)]
    public async Task ComputesMd5AndComparesFileSizes()
    {
        var firstPath = Path.GetTempFileName();
        var secondPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(firstPath, "hello world", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(secondPath, "hello world", TestContext.Current.CancellationToken);

            Assert.Equal("5eb63bbbe01eeed093cb22bb8f5acdc3", FileHash.ComputeMd5(firstPath));
            Assert.Equal(FileHash.ComputeMd5(firstPath), await FileHash.ComputeMd5Async(secondPath, TestContext.Current.CancellationToken));
            Assert.True(FileHash.HaveSameSize(firstPath, secondPath));
            await File.WriteAllTextAsync(secondPath, "different length", TestContext.Current.CancellationToken);
            Assert.False(FileHash.HaveSameSize(firstPath, secondPath));
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }
}
