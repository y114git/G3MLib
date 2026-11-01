using System.Globalization;
using System.Text;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Decompiler;

/// <summary>
/// Where an instruction was printed by the disassembler.
/// </summary>
/// <param name="InstructionOffset">Byte offset of the instruction in the code entry.</param>
/// <param name="TextOffset">Offset of the line in the disassembly text.</param>
/// <param name="Line">One-based line number.</param>
public readonly record struct DisassemblyLine(int InstructionOffset, int TextOffset, int Line);

public static class Disassembler
{
    private static void AppendLocalVarDefinitionsToStringBuilder(StringBuilder sb, GameMakerCode code, IList<GameMakerVariable> vars, GameMakerCodeLocals locals)
    {
        if (locals is null)
        {
            sb.Append("; Missing code locals, possibly due to unsupported bytecode version or brand new code entry.\n");
            return;
        }

        var referenced = code.FindReferencedLocalVars();
        if (locals.Name != code.Name)
            throw new GameMakerSerializationException("Name of the locals block does not match name of the code block!");
        foreach (var arg in locals.Locals)
        {
            sb.Append(".localvar ");
            sb.Append(arg.Index);
            sb.Append(' ');
            sb.Append(arg.Name.Content);
            var refVar = referenced.FirstOrDefault(x => x.Name == arg.Name && x.VarID == arg.Index);
            if (refVar is not null)
            {
                sb.Append(' ');
                sb.Append(vars.IndexOf(refVar));
            }
            sb.Append('\n');
        }
    }

    public static string Disassemble(this GameMakerCode code, IList<GameMakerVariable> vars, GameMakerCodeLocals? locals, bool ignoreMissingCodeLocals = false)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Disassemble(code, vars, locals, ignoreMissingCodeLocals, null);
    }

    /// <summary>
    /// Disassembles the code entry and also returns where each instruction was printed, to jump between
    /// disassembly lines and decompiler source map entries by instruction offset.
    /// </summary>
    public static string Disassemble(this GameMakerCode code, IList<GameMakerVariable> vars, GameMakerCodeLocals? locals, bool ignoreMissingCodeLocals, out IReadOnlyList<DisassemblyLine> lines)
    {
        ArgumentNullException.ThrowIfNull(code);
        List<DisassemblyLine> collected = new(code.Instructions.Count);
        var text = Disassemble(code, vars, locals, ignoreMissingCodeLocals, collected);
        Analyzer.Compiler.LineMap map = new(text);
        for (var i = 0; i < collected.Count; i++)
        {
            collected[i] = collected[i] with { Line = map.GetPosition(collected[i].TextOffset).Line };
        }
        lines = collected;
        return text;
    }

    private static string Disassemble(GameMakerCode code, IList<GameMakerVariable> vars, GameMakerCodeLocals? locals, bool ignoreMissingCodeLocals, List<DisassemblyLine>? lines)
    {
        // This StringBuilder is shared with the ToString method of the code instructions.
        // Experimentation has shown that 200 is a good enough starting value for it (now changed to 256).
        // 300 seemed too high and 100 too low. This may change in the future.
        StringBuilder sb = new(256);

        // Print locals
        var printedLocalVars = true;
        if (locals is null && !code.WeirdLocalFlag)
        {
            if (ignoreMissingCodeLocals)
            {
                printedLocalVars = false;
            }
            else
            {
                sb.Append("; WARNING: Missing code locals, possibly due to unsupported bytecode version or a brand new code entry.\n");
            }
        }
        else if (locals is null && code.WeirdLocalFlag)
        {
            printedLocalVars = false;
        }
        else
        {
            AppendLocalVarDefinitionsToStringBuilder(sb, code, vars, locals);
        }

        // Collect fragments (sub-functions/child code entries)
        Dictionary<uint, string> fragments = new(code.ChildEntries.Count);
        foreach (var dup in code.ChildEntries)
        {
            fragments.Add(dup.Offset / 4, $"{dup.Name?.Content ?? "<null>"} (locals={dup.LocalsCount}, argc={dup.ArgumentsCount})");
        }

        // Find addresses of all blocks
        var blocks = FindBlockAddresses(code);

        // Print actual instructions
        uint address = 0;
        foreach (var inst in code.Instructions)
        {
            // Print an extra newline for fragments/blocks if local vars were printed at the beginning,
            // or if any instructions have already been printed.
            var doNewline = printedLocalVars || address > 0;

            // Print fragment at current address
            if (fragments.TryGetValue(address, out var entry))
            {
                sb.Append('\n');
                sb.Append(CultureInfo.InvariantCulture, $"> {entry}\n");

                // No need for a second newline before blocks
                doNewline = false;
            }

            // Print block at current address
            if (blocks.TryGetValue(address, out var ind))
            {
                if (doNewline)
                {
                    sb.Append('\n');
                }
                sb.Append(CultureInfo.InvariantCulture, $":[{ind}]\n");
            }

            // Print actual instruction at current address
            lines?.Add(new DisassemblyLine((int)address * 4, sb.Length, 0));
            inst.ToString(sb, code, address, blocks);
            sb.Append('\n');

            // Advance address to next instruction
            address += inst.CalculateInstructionSize();
        }

        // Print ending block
        if (printedLocalVars || address > 0)
        {
            sb.Append('\n');
        }
        sb.Append(":[end]");

        return sb.ToString();
    }

    public static Dictionary<uint, uint> FindBlockAddresses(GameMakerCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        // Use a sorted set, so that block indices can be calculated
        SortedSet<uint> addresses = [];

        // Add initial block, if any instructions exist
        if (code.Instructions.Count > 0)
        {
            addresses.Add(0);
        }

        // Add all other blocks based
        uint currentAddress = 0;
        foreach (var inst in code.Instructions)
        {
            switch (inst.Kind)
            {
                case GameMakerInstruction.Opcode.B:
                case GameMakerInstruction.Opcode.Bf:
                case GameMakerInstruction.Opcode.Bt:
                case GameMakerInstruction.Opcode.PushEnv:
                    addresses.Add(currentAddress + 1);
                    addresses.Add((uint)(currentAddress + inst.JumpOffset));
                    break;
                case GameMakerInstruction.Opcode.PopEnv:
                    if (!inst.JumpOffsetPopenvExitMagic)
                    {
                        addresses.Add((uint)(currentAddress + inst.JumpOffset));
                    }
                    break;
                case GameMakerInstruction.Opcode.Exit:
                case GameMakerInstruction.Opcode.Ret:
                    addresses.Add(currentAddress + 1);
                    break;
            }
            currentAddress += inst.CalculateInstructionSize();
        }

        // Convert to an index lookup
        Dictionary<uint, uint> blockIndicesByAddress = new(addresses.Count);
        uint blockIndex = 0;
        foreach (var address in addresses)
        {
            blockIndicesByAddress.Add(address, blockIndex);
            blockIndex++;
        }
        return blockIndicesByAddress;
    }
}
