using G3MLib.Analyzer.Decompiler;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.ModelsDebug;

/// <summary>
/// What the source text of a code entry in generated debug data holds.
/// </summary>
public enum DebugDataMode
{
    /// <summary>Decompiled GML, with one debug info pair per entry.</summary>
    Decompiled,

    /// <summary>Disassembly with a debug info pair at stores, jumps and returns.</summary>
    Assembler,

    /// <summary>Disassembly with a debug info pair at every instruction.</summary>
    FullAssembler,
}

/// <summary>
/// Builds the data for a GameMaker 1.4 debugger (<c>.yydebug</c>) from a data file. Write the result with <see cref="GameMakerDebugIO.Write"/>.
/// </summary>
public static class DebugDataGenerator
{
    /// <summary>
    /// Creates debug data with one source text, one debug info list and the locals of every code entry, in the order of the code list.
    /// An entry that cannot be decompiled gets its error as a comment, so the other entries are not lost.
    /// </summary>
    public static GameMakerDebugData Generate(GameMakerData data, DebugDataMode mode, IProgress<GameMakerProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var code = data.Code ?? throw new InvalidOperationException("The data file has no code.");
        var variables = data.Variables;
        // The locals of a code entry are found by name; the lists need not line up by index.
        var localsByName = new Dictionary<string, GameMakerCodeLocals>(StringComparer.Ordinal);
        foreach (var block in data.CodeLocals ?? []) localsByName.TryAdd(block.Name.Content, block);

        var texts = new string[code.Count];
        var infos = new GameMakerDebugInfo[code.Count];
        var done = 0;
        var global = mode == DebugDataMode.Decompiled ? new GlobalDecompileContext(data) : null;
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        Parallel.For(0, code.Count, options, i =>
        {
            var entry = code[i];
            var info = new GameMakerDebugInfo();
            if (global is not null)
            {
                try
                {
                    texts[i] = new DecompileContext(global, entry, data.ToolInfo.DecompilerSettings).DecompileToString();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    texts[i] = "/*\nEXCEPTION!\n" + e + "\n*/";
                }

                info.Add(new GameMakerDebugInfo.DebugInfoPair { BytecodeOffset = 0, SourceCodeOffset = 0 });
            }
            else
            {
                texts[i] = entry.Disassemble(variables, localsByName.GetValueOrDefault(entry.Name.Content), true, out var lines);
                for (var n = 0; n < lines.Count; n++)
                {
                    if (mode == DebugDataMode.FullAssembler || IsMarked(entry.Instructions[n].Kind))
                        info.Add(new GameMakerDebugInfo.DebugInfoPair { BytecodeOffset = (uint)lines[n].InstructionOffset, SourceCodeOffset = (uint)lines[n].TextOffset });
                }
            }

            infos[i] = info;
            progress?.Report(new GameMakerProgress(Interlocked.Increment(ref done) / (double)Math.Max(code.Count, 1), entry.Name?.Content ?? ""));
        });

        var result = GameMakerDebugData.CreateNew();
        var known = new Dictionary<string, GameMakerString>(StringComparer.Ordinal);
        GameMakerString Intern(GameMakerString text)
        {
            if (!known.TryGetValue(text.Content, out var made))
            {
                made = text;
                known[text.Content] = made;
                result.Strings.Add(made);
            }

            return made;
        }

        for (var i = 0; i < code.Count; i++)
        {
            if (!known.TryGetValue(texts[i], out var source))
            {
                source = new GameMakerString(texts[i]);
                known[texts[i]] = source;
                result.Strings.Add(source);
            }

            result.SourceCode.Add(new GameMakerScriptSource { SourceCode = source });
            result.DebugInfo.Add(infos[i]);
            result.FORM.DBGI.CodeEntryIndices.Add((uint)result.DebugInfo.Count);
            var block = localsByName.GetValueOrDefault(code[i].Name.Content) ?? new GameMakerCodeLocals { Name = code[i].Name };
            result.LocalVars.Add(block);
            Intern(block.Name);
            foreach (var local in block.Locals) Intern(local.Name);
        }

        return result;
    }

    private static bool IsMarked(GameMakerInstruction.Opcode kind) =>
        kind is GameMakerInstruction.Opcode.Pop or GameMakerInstruction.Opcode.Popz or GameMakerInstruction.Opcode.B or GameMakerInstruction.Opcode.Bt
            or GameMakerInstruction.Opcode.Bf or GameMakerInstruction.Opcode.Ret or GameMakerInstruction.Opcode.Exit;
}
