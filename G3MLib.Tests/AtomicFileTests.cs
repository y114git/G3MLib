using G3MLib.Modding.Internal;

namespace G3MLib.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void FailedWriteKeepsTheOriginalFileAndRemovesTheTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var path = Path.Combine(directory, "out.dat");
            File.WriteAllText(path, "original");
            Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
            {
                stream.WriteByte(1);
                throw new InvalidOperationException();
            }));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SuccessfulWriteCreatesFoldersAndReplacesTheFile()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var path = Path.Combine(directory, "sub", "out.dat");
            Assert.Throws<DirectoryNotFoundException>(() => AtomicFile.Write(path, stream => stream.WriteByte(1)));
            AtomicFile.Write(path, stream => stream.WriteByte(1), createDirectory: true);
            AtomicFile.Write(path, stream => stream.Write([2, 3]));
            Assert.Equal([2, (byte)3], File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(Path.Combine(directory, "sub")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void OldTemporaryFileOfTheTargetIsRemovedAndANewOneIsKept()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var path = Path.Combine(directory, "out.dat");
            var stale = path + ".0123.g3mtmp";
            var fresh = path + ".4567.g3mtmp";
            File.WriteAllText(stale, "crash leftover");
            File.WriteAllText(fresh, "another writer");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
            AtomicFile.Write(path, stream => stream.WriteByte(1));
            Assert.Equal([path, fresh], Directory.GetFiles(directory).Order());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RootPathWithoutParentFolderThrowsClearly()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Throws<ArgumentException>(() => AtomicFile.Write(root, _ => { }));
    }

    [Fact]
    public async Task AsyncWriteSwapsOnSuccessAndKeepsTheOriginalOnFailure()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var path = Path.Combine(directory, "out.dat");
            File.WriteAllText(path, "original");
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicFile.WriteAsync(path, async stream =>
            {
                await stream.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
                throw new InvalidOperationException("boom");
            }));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(directory));

            await AtomicFile.WriteAsync(path, stream => stream.WriteAsync(new byte[] { 9 }, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal([(byte)9], File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentWritersNeverUseOrDeleteEachOthersTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var path = Path.Combine(directory, "out.dat");
            using var firstIsWriting = new ManualResetEventSlim();
            using var secondDone = new ManualResetEventSlim();
            var first = Task.Run(() => AtomicFile.Write(path, stream =>
            {
                stream.WriteByte(1);
                firstIsWriting.Set();
                secondDone.Wait(TimeSpan.FromSeconds(10));
                stream.WriteByte(1);
            }));
            Assert.True(firstIsWriting.Wait(TimeSpan.FromSeconds(10)));
            AtomicFile.Write(path, stream => stream.Write([2, 2, 2]));
            secondDone.Set();
            await first;
            Assert.Equal([(byte)1, 1], File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SymlinkedTargetKeepsItsLink()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var real = Path.Combine(directory, "real.dat");
            var link = Path.Combine(directory, "link.dat");
            File.WriteAllText(real, "old");
            try { File.CreateSymbolicLink(link, real); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
            AtomicFile.Write(link, stream => stream.WriteByte(7));
            Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Equal([(byte)7], File.ReadAllBytes(real));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DanglingSymlinkIsWrittenThrough()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var real = Path.Combine(directory, "real.dat");
            var link = Path.Combine(directory, "link.dat");
            try { File.CreateSymbolicLink(link, real); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
            AtomicFile.Write(link, stream => stream.WriteByte(7));
            Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Equal([(byte)7], File.ReadAllBytes(real));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void StaleTemporaryFilesAreSweptOncePerTarget()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-atomic-").FullName;
        try
        {
            var path = Path.Combine(directory, "out.dat");
            AtomicFile.Write(path, stream => stream.WriteByte(1));
            var late = path + ".89ab.g3mtmp";
            File.WriteAllText(late, "leftover");
            File.SetLastWriteTimeUtc(late, DateTime.UtcNow.AddDays(-2));
            AtomicFile.Write(path, stream => stream.WriteByte(2));
            Assert.True(File.Exists(late));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
