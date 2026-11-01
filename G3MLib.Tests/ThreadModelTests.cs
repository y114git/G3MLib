using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;

namespace G3MLib.Tests;

public class ThreadModelTests : IDisposable
{
    private static readonly byte[] FixtureBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3mlib-threads-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    public static TheoryData<int> Runs => [.. Enumerable.Range(1, 20)];

    private static GameMakerData Load()
    {
        using MemoryStream input = new(FixtureBytes);
        return GameMakerIO.Read(input);
    }

    private static string Decompile(GlobalDecompileContext context, GameMakerCode code)
    {
        try
        {
            return new DecompileContext(context, code, context.Data.ToolInfo.DecompilerSettings).DecompileToString();
        }
        catch (Exception ex)
        {
            return "error: " + ex.GetType().Name;
        }
    }

    private string Export(GameMakerData data, string name)
    {
        var directory = Path.Combine(_root, name);
        HashSet<string> codeNames = [.. data.Code.Select(c => c.Name.Content)];
        G3MProjectService.Export(data, directory, null, new G3MProjectOptions
        {
            Name = "P",
            Mode = ProjectExportMode.Delta,
            Only = new Dictionary<string, IReadOnlySet<string>>
            {
                ["CodeEntries"] = codeNames,
                ["Scripts"] = data.Scripts.Select(s => s.Name.Content).ToHashSet(),
                ["GameObjects"] = data.GameObjects.Select(o => o.Name.Content).ToHashSet()
            }
        });
        return directory;
    }

    private static Dictionary<string, string> Snapshot(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".g3mproj", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
    }

    private static void Edit(GameMakerData data, int step)
    {
        data.Sprites[0].OriginX = step % 50;
        data.Sprites[0].OriginY = step % 70;
        data.Sounds[0].Volume = step % 10 / 10f;
        data.Fonts[0].EmSize = 8 + step % 8;
        data.Fonts[0].Bold = step % 2 == 0;
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public void DecompileAndExportInBackgroundWhileUnrelatedResourcesAreEdited(int run)
    {
        using var data = Load();
        GlobalDecompileContext context = new(data);
        List<GameMakerCode> codes = [.. data.Code];
        var expected = codes.ToDictionary(c => c, c => Decompile(context, c));
        var expectedFiles = Snapshot(Export(data, "expected"));
        Assert.NotEmpty(expectedFiles);

        using CancellationTokenSource stop = new();
        var step = 0;
        var editor = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                Edit(data, step++);
            }
        }, TestContext.Current.CancellationToken);
        List<Task> jobs = [];
        for (var worker = 0; worker < 3; worker++)
        {
            jobs.Add(Task.Run(() =>
            {
                for (var pass = 0; pass < 3; pass++)
                {
                    foreach (var code in codes)
                    {
                        Assert.Equal(expected[code], Decompile(context, code));
                    }
                }
            }, TestContext.Current.CancellationToken));
        }
        jobs.Add(Task.Run(() =>
        {
            for (var pass = 0; pass < 2; pass++)
            {
                Assert.Equal(expectedFiles, Snapshot(Export(data, $"job{run}-{pass}")));
            }
        }, TestContext.Current.CancellationToken));

        try
        {
            Task.WaitAll([.. jobs]);
        }
        finally
        {
            // A busy runner may not have scheduled the editor yet; let it make one edit before it is stopped.
            SpinWait.SpinUntil(() => Volatile.Read(ref step) > 0, TimeSpan.FromSeconds(30));
            stop.Cancel();
            editor.Wait(TestContext.Current.CancellationToken);
        }
        Assert.True(step > 0);
    }

    [Fact]
    public void LockedStructuralEditsNeverReachRunningJobs()
    {
        using var data = Load();
        using ModelLock modelLock = new();
        GlobalDecompileContext context = new(data);
        List<GameMakerCode> codes = [.. data.Code];
        var expected = codes.ToDictionary(c => c, c => Decompile(context, c));

        using CancellationTokenSource stop = new();
        var edits = 0;
        var editor = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                using (modelLock.Write())
                {
                    GameMakerSprite sprite = new() { Name = data.Strings.MakeString("tmp_sprite_" + edits) };
                    data.Sprites.Add(sprite);
                    data.Sprites.Remove(sprite);
                    edits++;
                }
            }
        }, TestContext.Current.CancellationToken);
        // The jobs may otherwise finish before the editor takes the write lock once.
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref edits) > 0, TimeSpan.FromSeconds(10)));
        Task[] jobs = [.. Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var code in codes)
                {
                    using (modelLock.Read())
                    {
                        Assert.Equal(expected[code], Decompile(context, code));
                    }
                }
            }
        }))];

        try
        {
            Task.WaitAll(jobs, TestContext.Current.CancellationToken);
        }
        finally
        {
            stop.Cancel();
            editor.Wait(TestContext.Current.CancellationToken);
        }
        Assert.True(edits > 0);
    }

    [Fact]
    public void WriteLockExcludesReadersAndReadersShare()
    {
        using ModelLock modelLock = new();
        Task writer;
        using (modelLock.Read())
        {
            var second = Task.Run(() =>
            {
                using (modelLock.Read())
                {
                    return true;
                }
            });
            Assert.True(second.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) && second.Result);

            writer = Task.Run(() =>
            {
                using (modelLock.Write())
                {
                }
            }, TestContext.Current.CancellationToken);
            Assert.False(writer.Wait(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
            Task.Run(() => { }, TestContext.Current.CancellationToken).Wait(TestContext.Current.CancellationToken);
        }
        // Reader released: the queued writer proceeds
        Assert.True(writer.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(Task.Run(() => { using (modelLock.Write()) { } }, TestContext.Current.CancellationToken).Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReadingAndWritingInParallelGivesTheSameFile()
    {
        byte[] reference;
        using (var data = Load())
        using (MemoryStream output = new())
        {
            GameMakerIO.Write(output, data);
            reference = output.ToArray();
        }

        byte[][] results = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var data = Load();
                GlobalDecompileContext context = new(data);
                foreach (var code in data.Code.Take(20))
                {
                    Decompile(context, code);
                }
                using MemoryStream output = new();
                GameMakerIO.Write(output, data);
                if (!output.ToArray().AsSpan().SequenceEqual(reference))
                {
                    return [];
                }
            }
            return reference;
        })).ToArray().Select(t => t.Result)];

        Assert.All(results, r => Assert.Equal(reference.Length, r.Length));
    }

    [Fact]
    public void ContextsBuiltConcurrentlyForOneModelAgree()
    {
        using var data = Load();
        GlobalDecompileContext first = new(data);
        List<GameMakerCode> codes = [.. data.Code.Take(40)];
        var expected = codes.ToDictionary(c => c, c => Decompile(first, c));

        Task[] jobs = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var pass = 0; pass < 3; pass++)
            {
                GlobalDecompileContext context = new(data);
                context.PrepareForCompilation();
                foreach (var code in codes)
                {
                    Assert.Equal(expected[code], Decompile(context, code));
                }
            }
        }))];

        Task.WaitAll(jobs, TestContext.Current.CancellationToken);
    }
}
