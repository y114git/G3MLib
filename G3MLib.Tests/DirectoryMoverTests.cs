using G3MLib.Modding.Internal;

namespace G3MLib.Tests;

public sealed class DirectoryMoverTests
{
    [Fact]
    public void MoveWaitsForAFileAScannerHoldsForAMoment()
    {
        var root = Directory.CreateTempSubdirectory("g3mlib-mover-").FullName;
        try
        {
            var source = Path.Combine(root, "staging", "Scripts");
            Directory.CreateDirectory(source);
            var file = Path.Combine(source, "a.gml");
            File.WriteAllText(file, "x");
            var target = Path.Combine(root, "Scripts");
            // On Windows a folder with an open file in it cannot be renamed; elsewhere the move simply works at once.
            var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = Task.Run(() =>
            {
                Thread.Sleep(250);
                held.Dispose();
            });
            DirectoryMover.Move(source, target);
            release.Wait();
            Assert.Equal("x", File.ReadAllText(Path.Combine(target, "a.gml")));
            Assert.False(Directory.Exists(source));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MoveIntoAnExistingFolderFailsAtOnceAndLeavesBothAlone()
    {
        var root = Directory.CreateTempSubdirectory("g3mlib-mover-").FullName;
        try
        {
            var source = Path.Combine(root, "a");
            var target = Path.Combine(root, "b");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "new.txt"), "n");
            File.WriteAllText(Path.Combine(target, "old.txt"), "o");
            Assert.ThrowsAny<IOException>(() => DirectoryMover.Move(source, target));
            Assert.True(File.Exists(Path.Combine(source, "new.txt")));
            Assert.False(File.Exists(Path.Combine(target, "new.txt")));
            Assert.Equal("o", File.ReadAllText(Path.Combine(target, "old.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MoveOntoOrIntoItselfIsRejectedBeforeAnythingChanges()
    {
        var root = Directory.CreateTempSubdirectory("g3mlib-mover-").FullName;
        try
        {
            var source = Path.Combine(root, "a");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "f.txt"), "x");
            Assert.Throws<IOException>(() => DirectoryMover.Move(source, source));
            Assert.Throws<IOException>(() => DirectoryMover.Move(source, Path.Combine(source, "inner")));
            Assert.Equal("x", File.ReadAllText(Path.Combine(source, "f.txt")));
            Assert.Single(Directory.GetFileSystemEntries(source));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AFolderThatCannotBeRenamedIsCopiedWholeWithoutLeftovers()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A folder with an open file can be renamed outside Windows.");
        var root = Directory.CreateTempSubdirectory("g3mlib-mover-").FullName;
        try
        {
            var source = Path.Combine(root, "staging", "Scripts");
            Directory.CreateDirectory(Path.Combine(source, "sub"));
            File.WriteAllText(Path.Combine(source, "a.gml"), "a");
            File.WriteAllText(Path.Combine(source, "sub", "b.gml"), "b");
            var target = Path.Combine(root, "Scripts");
            using (new FileStream(Path.Combine(source, "a.gml"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                DirectoryMover.Move(source, target);
            }
            Assert.Equal("a", File.ReadAllText(Path.Combine(target, "a.gml")));
            Assert.Equal("b", File.ReadAllText(Path.Combine(target, "sub", "b.gml")));
            Assert.Equal([Path.Combine(root, "Scripts"), Path.Combine(root, "staging")], Directory.GetDirectories(root).Order());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AFailedCopyLeavesNoTargetAndNoStagingFolder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An exclusive lock blocks a rename and a copy only on Windows.");
        var root = Directory.CreateTempSubdirectory("g3mlib-mover-").FullName;
        try
        {
            var source = Path.Combine(root, "src");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.gml"), "a");
            var target = Path.Combine(root, "Scripts");
            using (new FileStream(Path.Combine(source, "a.gml"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => DirectoryMover.Move(source, target));
            }
            Assert.False(Directory.Exists(target));
            Assert.Equal([source], Directory.GetDirectories(root));
            Assert.Equal("a", File.ReadAllText(Path.Combine(source, "a.gml")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ACopyNeverFollowsOrCopiesALink()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The rename only fails under a lock on Windows.");
        var root = Directory.CreateTempSubdirectory("g3mlib-mover-").FullName;
        try
        {
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "k");
            var source = Path.Combine(root, "src");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.gml"), "a");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(source, "link"), outside);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Creating a symbolic link needs developer mode or elevation.");
            }
            var target = Path.Combine(root, "Scripts");
            using (new FileStream(Path.Combine(source, "a.gml"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => DirectoryMover.Move(source, target));
            }
            Assert.False(Directory.Exists(target));
            Assert.Equal("k", File.ReadAllText(Path.Combine(outside, "keep.txt")));
            Assert.Equal([outside, source], Directory.GetDirectories(root).Order());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
