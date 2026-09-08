using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Decompiler
{
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
            // This StringBuilder is shared with the ToString method of the code instructions.
            // Experimentation has shown that 200 is a good enough starting value for it (now changed to 256).
            // 300 seemed too high and 100 too low. This may change in the future.
            StringBuilder sb = new(256);

            // Print locals
            bool printedLocalVars = true;
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
                fragments.Add(dup.Offset / 4, $"{(dup.Name?.Content ?? "<null>")} (locals={dup.LocalsCount}, argc={dup.ArgumentsCount})");
            }

            // Find addresses of all blocks
            Dictionary<uint, uint> blocks = FindBlockAddresses(code);

            // Print actual instructions
            uint address = 0;
            foreach (GameMakerInstruction inst in code.Instructions)
            {
                // Print an extra newline for fragments/blocks if local vars were printed at the beginning,
                // or if any instructions have already been printed.
                bool doNewline = printedLocalVars || address > 0;

                // Print fragment at current address
                if (fragments.TryGetValue(address, out string entry))
                {
                    sb.Append('\n');
                    sb.Append($"> {entry}\n");

                    // No need for a second newline before blocks
                    doNewline = false;
                }

                // Print block at current address
                if (blocks.TryGetValue(address, out uint ind))
                {
                    if (doNewline)
                    {
                        sb.Append('\n');
                    }
                    sb.Append($":[{ind}]\n");
                }

                // Print actual instruction at current address
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
            // Use a sorted set, so that block indices can be calculated
            SortedSet<uint> addresses = new();

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
            foreach (uint address in addresses)
            {
                blockIndicesByAddress.Add(address, blockIndex);
                blockIndex++;
            }
            return blockIndicesByAddress;
        }
    }
}
