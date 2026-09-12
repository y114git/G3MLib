#if UPSTREAM
using System.Diagnostics;
using System.Text.Json;
using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using UndertaleModLib;

internal static class UpstreamComparison
{
    public static void Run(string path, GameMakerData data)
    {
        var operations = new List<(string Name, Action Action)>();
        void measure(string name, Action action) => operations.Add((name, action));
        using var stream = File.OpenRead(path);
        using var upstream = UndertaleIO.Read(stream);
        if (data.Rooms.Count != upstream.Rooms.Count || data.Code.Count != upstream.Code.Count || data.Strings.Count != upstream.Strings.Count)
            throw new InvalidOperationException("Library resource counts differ.");
        measure("g3mlib_read", () => { using var input = File.OpenRead(path); using var parsed = GameMakerIO.Read(input); });
        measure("upstream_read", () => { using var input = File.OpenRead(path); using var parsed = UndertaleIO.Read(input); });
        measure("g3mlib_read_general", () => { using var input = File.OpenRead(path); using var parsed = GameMakerIO.Read(input, onlyGeneralInfo: true); });
        measure("upstream_read_general", () =>
        {
            using var input = File.OpenRead(path);
            // The upstream partial reader reports the deliberately unread GEN8 tail as padding.
            using var parsed = UndertaleIO.Read(input, warningHandler: (warning, _) =>
            {
                if (!warning.StartsWith("WARNING: File specified length ", StringComparison.Ordinal) || !warning.EndsWith(" padding?)", StringComparison.Ordinal))
                    throw new IOException(warning);
            }, onlyGeneralInfo: true);
        });
        measure("g3mlib_write", () => { using var output = new MemoryStream(); GameMakerIO.Write(output, data); });
        measure("upstream_write", () => { using var output = new MemoryStream(); UndertaleIO.Write(output, upstream); });
        string source = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"var value{i} = {i} + 1;"));
        measure("g3mlib_parse_gml", () =>
        {
            var context = new CompileContext(source, CompileScriptKind.ObjectEvent, null, new GameContextMock());
            context.Parse();
            if (context.HasErrors) throw new InvalidOperationException("G3MLib parse failed.");
        });
        measure("upstream_parse_gml", () =>
        {
            var context = new Underanalyzer.Compiler.CompileContext(source, Underanalyzer.Compiler.CompileScriptKind.ObjectEvent, null, new Underanalyzer.Mock.GameContextMock());
            context.Parse();
            if (context.HasErrors) throw new InvalidOperationException("Underanalyzer parse failed.");
        });
        measure("g3mlib_compile_gml", () =>
        {
            var context = new CompileContext(source, CompileScriptKind.ObjectEvent, null, new GameContextMock());
            context.Compile();
            if (context.HasErrors) throw new InvalidOperationException("G3MLib compilation failed.");
        });
        measure("upstream_compile_gml", () =>
        {
            var context = new Underanalyzer.Compiler.CompileContext(source, Underanalyzer.Compiler.CompileScriptKind.ObjectEvent, null, new Underanalyzer.Mock.GameContextMock());
            context.Compile();
            if (context.HasErrors) throw new InvalidOperationException("Underanalyzer compilation failed.");
        });
        if (data.Code.Count > 0)
        {
        var largest = data.Code.OrderByDescending(code => code.Instructions.Count).First();
        var upstreamLargest = upstream.Code.Single(code => code.Name.Content == largest.Name.Content);
        if (largest.Instructions.Count != upstreamLargest.Instructions.Count)
            throw new InvalidOperationException("Code instruction counts differ.");
        measure("g3mlib_decompile", () => new DecompileContext(new GlobalDecompileContext(data), largest).DecompileToString());
        measure("upstream_decompile", () => new Underanalyzer.Decompiler.DecompileContext(new UndertaleModLib.Decompiler.GlobalDecompileContext(upstream), upstreamLargest).DecompileToString());
        }
        for (int pair = 0; pair < operations.Count; pair += 2)
        {
            var left = operations[pair];
            var right = operations[pair + 1];
            int batch = left.Name.Contains("gml") ? 64 : left.Name.Contains("general") ? 16 : 1;
            void Batch(Action action) { for (int i = 0; i < batch; i++) action(); }
            for (int warmup = 0; warmup < 3; warmup++) { Batch(left.Action); Batch(right.Action); }
            var times = new[] { new double[7], new double[7] };
            var allocations = new[] { new long[7], new long[7] };
            for (int round = 0; round < 7; round++)
            {
                for (int turn = 0; turn < 2; turn++)
                {
                    int side = (round + turn) % 2;
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long before = GC.GetTotalAllocatedBytes(true);
                    var watch = Stopwatch.StartNew();
                    Batch(side == 0 ? left.Action : right.Action);
                    watch.Stop();
                    times[side][round] = watch.Elapsed.TotalMilliseconds / batch;
                    allocations[side][round] = (GC.GetTotalAllocatedBytes(true) - before) / batch;
                }
            }
            for (int side = 0; side < 2; side++)
            {
                Array.Sort(times[side]); Array.Sort(allocations[side]);
                Console.WriteLine(JsonSerializer.Serialize(new { operation = side == 0 ? left.Name : right.Name,
                    medianMs = times[side][3], minMs = times[side][0], maxMs = times[side][6],
                    allocatedMiB = allocations[side][3] / 1048576d, batch, runs = 7 }));
            }
        }
    }
}
#endif
