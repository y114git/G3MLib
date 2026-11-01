/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace G3MLib.Analyzer.Mock;

/// <summary>
/// Utility class to parse mock VM assembly, for testing purposes.
/// </summary>
public static class VMAssembly
{
    private static readonly Dictionary<string, IGMInstruction.Opcode> StringToOpcode = [];
    private static readonly Dictionary<string, IGMInstruction.ExtendedOpcode> StringToExtOpcode = [];
    private static readonly Dictionary<char, IGMInstruction.DataType> CharToDataType = [];
    private static readonly Dictionary<string, AssetType> StringToAssetType = [];

    /// <summary>
    /// Initializes precomputed data for parsing VM assembly
    /// </summary>
    static VMAssembly()
    {
        // Normal opcodes
        var typeOpcode = typeof(IGMInstruction.Opcode);
        foreach (IGMInstruction.Opcode opcode in Enum.GetValues(typeOpcode))
        {
            var field = typeOpcode.GetField(Enum.GetName(typeOpcode, opcode));
            var info = field.GetCustomAttribute<IGMInstruction.OpcodeInfo>();
            StringToOpcode[info.Mnemonic] = opcode;
        }

        // Extended opcodes
        var typeExtType = typeof(IGMInstruction.ExtendedOpcode);
        foreach (IGMInstruction.ExtendedOpcode opcode in Enum.GetValues(typeExtType))
        {
            var field = typeExtType.GetField(Enum.GetName(typeExtType, opcode));
            var info = field.GetCustomAttribute<IGMInstruction.OpcodeInfo>();
            StringToOpcode[info.Mnemonic] = IGMInstruction.Opcode.Extended;
            StringToExtOpcode[info.Mnemonic] = opcode;
        }

        // Data types
        var typeDataType = typeof(IGMInstruction.DataType);
        foreach (IGMInstruction.DataType dataType in Enum.GetValues(typeDataType))
        {
            var field = typeDataType.GetField(Enum.GetName(typeDataType, dataType));
            var info = field.GetCustomAttribute<IGMInstruction.DataTypeInfo>();
            CharToDataType[info.Mnemonic] = dataType;
        }

        // Asset types
        var typeAssetType = typeof(AssetType);
        foreach (AssetType assetType in Enum.GetValues(typeAssetType))
        {
            StringToAssetType[Enum.GetName(typeAssetType, assetType)] = assetType;
        }
    }

    /// <summary>
    /// Parses VM assembly into mock instruction data.
    /// </summary>
    public static GMCode ParseAssemblyFromLines(IEnumerable<string> lines, IGameContext? context, string name = "root")
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<GMInstruction> instructions = [];
        GMCode root = new(name, instructions);

        Dictionary<string, int> branchLabelAddresses = [];
        List<(string Label, GMInstruction Instr)> branchTargets = [];

        HashSet<GMVariable> variables = new(new GMVariableComparer());
        HashSet<GMFunction> functions = new(new GMFunctionComparer());
        if (context is not null)
        {
            foreach (var func in ((Decompiler.GlobalFunctions)context.GlobalFunctions).GetFunctions().Cast<GMFunction>())
            {
                functions.Add(func);
            }
        }

        var address = 0;
        foreach (var line in lines)
        {
            // Totally empty line; ignore
            if (line.Length == 0)
            {
                continue;
            }

            // Ignore comment lines
            if (line[0] == '#')
            {
                continue;
            }

            // Branch label
            if (line[0] == ':')
            {
                var label = line[1..].Trim();
                if (label.Length < 3 || label[0] != '[' || label[^1] != ']')
                {
                    throw new InvalidOperationException("Invalid branch header");
                }
                branchLabelAddresses[label[1..^1]] = address;
                continue;
            }

            // Sub-entry label
            if (line[0] == '>')
            {
                var decl = line[1..].Trim();

                // Split into label and parameters
                var space = decl.IndexOf(' ', StringComparison.Ordinal);
                string label, parameters;
                if (space == -1)
                {
                    label = decl;
                    parameters = string.Empty;
                }
                else
                {
                    label = decl[..space];
                    parameters = decl[(space + 1)..];
                }

                // Parse (optional) parameters
                var localCount = ParseOptionalParameter(parameters, "locals");
                var argCount = ParseOptionalParameter(parameters, "args");

                if (address == 0 && label == name)
                {
                    // If we're at the start and are the same name as the root, then just perform an update
                    if (localCount is not null)
                    {
                        root.LocalCount = localCount.Value;
                    }
                    if (argCount is not null)
                    {
                        root.ArgumentCount = argCount.Value;
                    }
                    continue;
                }

                // Add normal sub-entry as a child of the root
                root.Children.Add(new(label, instructions)
                {
                    Parent = root,
                    StartOffset = address,
                    LocalCount = localCount ?? 0,
                    ArgumentCount = argCount ?? 0
                });
                continue;
            }

            // Split line into constituent parts
            var parts = line.Trim().Split(' ');

            // Empty line; ignore
            if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
            {
                continue;
            }

            var opcodeParts = parts[0].Split('.');

            // Parse opcode
            var opcodeStr = opcodeParts[0].ToLowerInvariant();
            if (!StringToOpcode.TryGetValue(opcodeStr, out var opcode))
            {
                throw new InvalidOperationException($"Unexpected opcode \"{opcodeStr}\"");
            }

            // Parse data types
            IGMInstruction.DataType type1 = 0;
            IGMInstruction.DataType type2 = 0;
            if (opcodeParts.Length >= 2)
            {
                if (opcodeParts[1].Length != 1)
                {
                    throw new InvalidOperationException("Expected single character for data type 1");
                }
                var c = opcodeParts[1].ToLowerInvariant()[0];
                if (!CharToDataType.TryGetValue(c, out type1))
                {
                    throw new InvalidOperationException($"Unexpected data type \"{c}\"");
                }
            }
            if (opcodeParts.Length >= 3)
            {
                if (opcodeParts[2].Length != 1)
                {
                    throw new InvalidOperationException("Expected single character for data type 2");
                }
                var c = opcodeParts[2].ToLowerInvariant()[0];
                if (!CharToDataType.TryGetValue(c, out type2))
                {
                    throw new InvalidOperationException($"Unexpected data type \"{c}\"");
                }
            }
            if (opcodeParts.Length >= 4)
            {
                throw new InvalidOperationException("Too many data types");
            }

            // Construct mock instruction
            GMInstruction instr = new()
            {
                Address = address,
                Kind = opcode,
                Type1 = type1,
                Type2 = type2
            };

            // Parse additional data
            switch (opcode)
            {
                case IGMInstruction.Opcode.Compare:
                    {
                        // Parse comparison kind
                        if (parts.Length < 2)
                        {
                            throw new InvalidOperationException("Compare needs comparison kind parameter");
                        }
                        instr.ComparisonKind = parts[1].ToLowerInvariant() switch
                        {
                            "lt" => IGMInstruction.ComparisonType.LesserThan,
                            "leq" or "lte" => IGMInstruction.ComparisonType.LesserEqualThan,
                            "eq" => IGMInstruction.ComparisonType.EqualTo,
                            "neq" => IGMInstruction.ComparisonType.NotEqualTo,
                            "geq" or "gte" => IGMInstruction.ComparisonType.GreaterEqualThan,
                            "gt" => IGMInstruction.ComparisonType.GreaterThan,
                            _ => throw new InvalidOperationException("Unknown comparison kind")
                        };
                    }
                    break;
                case IGMInstruction.Opcode.Pop:
                    {
                        if (parts.Length < 2)
                        {
                            throw new InvalidOperationException("Pop needs parameter");
                        }
                        var data = string.Join(' ', parts[1..]).Trim();

                        // Parse swap variant
                        if (instr.Type1 == IGMInstruction.DataType.Int16)
                        {
                            if (!byte.TryParse(data, out var popSwapSize) || popSwapSize < 5 || popSwapSize > 6)
                            {
                                throw new InvalidOperationException("Unexpected pop swap size");
                            }
                            instr.PopSwapSize = popSwapSize;
                            break;
                        }

                        // Parse variable destination
                        if (!ParseVariableFromString(data, variables, out var variable, out var varType, out var instType))
                        {
                            throw new InvalidOperationException($"Failed to parse variable {data}");
                        }
                        instr.ResolvedVariable = variable;
                        instr.ReferenceVarType = varType;
                        instr.InstType = instType;
                    }
                    break;
                case IGMInstruction.Opcode.Duplicate:
                    {
                        // Parse normal dup size
                        if (parts.Length < 2)
                        {
                            throw new InvalidOperationException("Duplicate needs size parameter");
                        }
                        if (!byte.TryParse(parts[1], out var dupSize))
                        {
                            throw new InvalidOperationException("Failed to parse parameter");
                        }
                        instr.DuplicationSize = dupSize;

                        // Parse "swap" mode size, if parameter exists
                        if (parts.Length >= 3)
                        {
                            if (!byte.TryParse(parts[2], out var dupSize2))
                            {
                                throw new InvalidOperationException("Failed to parse parameter");
                            }
                            instr.DuplicationSize2 = dupSize2;
                        }
                    }
                    break;
                case IGMInstruction.Opcode.CallVariable:
                    {
                        // Parse argument count
                        if (parts.Length < 2)
                        {
                            throw new InvalidOperationException("CallVariable needs size parameter");
                        }
                        if (!int.TryParse(parts[1], out var argCount))
                        {
                            throw new InvalidOperationException("Failed to parse parameter");
                        }
                        instr.ArgumentCount = argCount;
                    }
                    break;
                case IGMInstruction.Opcode.Branch:
                case IGMInstruction.Opcode.BranchTrue:
                case IGMInstruction.Opcode.BranchFalse:
                case IGMInstruction.Opcode.PushWithContext:
                case IGMInstruction.Opcode.PopWithContext:
                    {
                        if (parts.Length < 2)
                        {
                            throw new InvalidOperationException("Branch instruction needs target");
                        }
                        var target = parts[1];

                        // PopWithContext has an exception to this branch target
                        if (opcode == IGMInstruction.Opcode.PopWithContext && target == "<drop>")
                        {
                            instr.PopWithContextExit = true;
                            break;
                        }

                        // Parse normal target
                        if (target.Length < 3 || target[0] != '[' || target[^1] != ']')
                        {
                            throw new InvalidOperationException("Invalid branch target format");
                        }

                        // Store this target for later
                        branchTargets.Add((target[1..^1], instr));
                    }
                    break;
                case IGMInstruction.Opcode.Push:
                case IGMInstruction.Opcode.PushLocal:
                case IGMInstruction.Opcode.PushGlobal:
                case IGMInstruction.Opcode.PushBuiltin:
                case IGMInstruction.Opcode.PushImmediate:
                    {
                        if (parts.Length < 2)
                        {
                            throw new InvalidOperationException("Push instruction needs data");
                        }
                        var data = string.Join(' ', parts[1..]).Trim();

                        switch (type1)
                        {
                            case IGMInstruction.DataType.Double:
                                if (!double.TryParse(data, out var dataDouble))
                                {
                                    throw new InvalidOperationException("Invalid double");
                                }
                                instr.ValueDouble = dataDouble;
                                break;
                            case IGMInstruction.DataType.Int32:
                                if (!int.TryParse(data, out var dataInt32))
                                {
                                    if (data.StartsWith("[function]", StringComparison.Ordinal))
                                    {
                                        // We're pushing a function index instead
                                        GMFunction function = new(data["[function]".Length..]);
                                        if (functions.TryGetValue(function, out var existingFunction))
                                        {
                                            // We found a function that was already created
                                            instr.ResolvedFunction = existingFunction;
                                        }
                                        else
                                        {
                                            instr.ResolvedFunction = function;
                                        }
                                        break;
                                    }
                                    if (data.StartsWith("[variable]", StringComparison.Ordinal))
                                    {
                                        // We're pushing a variable hash instead
                                        var variableName = data["[variable]".Length..];
                                        if (variableName.StartsWith('"'))
                                        {
                                            if (!variableName.EndsWith('"'))
                                            {
                                                throw new InvalidOperationException("Expected quote at the end of variable hash name");
                                            }
                                            variableName = UnescapeStringContents(variableName[1..^1]);
                                        }
                                        instr.ResolvedVariable = new GMVariable(new GMString(variableName))
                                        {
                                            InstanceType = IGMInstruction.InstanceType.Self
                                        };
                                        break;
                                    }
                                    throw new InvalidOperationException("Unknown push.i value");
                                }
                                instr.ValueInt = dataInt32;
                                break;
                            case IGMInstruction.DataType.Int64:
                                if (!long.TryParse(data, out var dataInt64))
                                {
                                    throw new InvalidOperationException("Invalid int64");
                                }
                                instr.ValueLong = dataInt64;
                                break;
                            case IGMInstruction.DataType.Boolean:
                                if (!bool.TryParse(data, out var dataBool))
                                {
                                    throw new InvalidOperationException("Invalid boolean");
                                }
                                instr.ValueBool = dataBool;
                                break;
                            case IGMInstruction.DataType.Variable:
                                if (!ParseVariableFromString(data, variables, out var variable, out var varType, out var instType))
                                {
                                    throw new InvalidOperationException($"Failed to parse variable {data}");
                                }
                                instr.ResolvedVariable = variable;
                                instr.ReferenceVarType = varType;
                                instr.InstType = instType;
                                break;
                            case IGMInstruction.DataType.String:
                                var contents = line[(line.IndexOf('"', StringComparison.Ordinal) + 1)..line.LastIndexOf('"')];
                                instr.ValueString = new GMString(UnescapeStringContents(contents));
                                break;
                            case IGMInstruction.DataType.Int16:
                                if (!short.TryParse(data, out var dataInt16))
                                {
                                    throw new InvalidOperationException("Invalid int16");
                                }
                                instr.ValueShort = dataInt16;
                                break;
                        }
                    }
                    break;
                case IGMInstruction.Opcode.Call:
                    {
                        if (parts.Length < 3)
                        {
                            throw new InvalidOperationException("Call needs function and argument count");
                        }

                        GMFunction function = new(parts[1]);
                        if (functions.TryGetValue(function, out var existingFunction))
                        {
                            // We found a function that was already created
                            instr.ResolvedFunction = existingFunction;
                        }
                        else
                        {
                            // This is a brand new function
                            instr.ResolvedFunction = function;
                            functions.Add(function);
                        }

                        if (!int.TryParse(parts[2], out var argCount))
                        {
                            throw new InvalidOperationException("Failed to parse argument count");
                        }
                        instr.ArgumentCount = argCount;
                    }
                    break;
                case IGMInstruction.Opcode.Extended:
                    {
                        // Parse extended opcode type
                        if (!StringToExtOpcode.TryGetValue(opcodeStr, out var extOpcode))
                        {
                            throw new InvalidOperationException($"Unexpected extended opcode \"{opcodeStr}\"");
                        }
                        instr.ExtKind = extOpcode;

                        // Parse additional arguments
                        if (extOpcode == IGMInstruction.ExtendedOpcode.PushReference)
                        {
                            if (parts.Length < 2)
                            {
                                throw new InvalidOperationException("PushReference needs reference ID (or function)");
                            }
                            if (!int.TryParse(parts[1], out var referenceID))
                            {
                                // Not a reference ID. Instead, a function reference
                                instr.ResolvedFunction = new GMFunction(parts[1]);
                                break;
                            }
                            if (parts.Length < 3)
                            {
                                throw new InvalidOperationException("PushReference needs reference ID and type parameters");
                            }
                            instr.AssetReferenceId = referenceID;
                            instr.AssetReferenceType = StringToAssetType[parts[2]];
                        }
                    }
                    break;
            }

            address += IGMInstruction.GetSize(instr);

            instructions.Add(instr);
        }

        // Resolve branch targets
        foreach ((var targetLabel, var targetInstr) in branchTargets)
        {
            // Look up label
            if (!branchLabelAddresses.TryGetValue(targetLabel, out var labelAddress))
            {
                throw new InvalidOperationException($"Did not find matching label for \"{targetLabel}\"");
            }

            targetInstr.BranchOffset = labelAddress - targetInstr.Address;
        }

        // Update code lengths
        root.Length = address;
        foreach (var child in root.Children)
        {
            child.Length = address;
        }

        return root;
    }

    private static bool ParseVariableFromString(
        string str, HashSet<GMVariable> variables, [MaybeNullWhen(false)] out GMVariable variable,
        out IGMInstruction.VariableType varType, out IGMInstruction.InstanceType instType)
    {
        // Default data
        varType = IGMInstruction.VariableType.Normal;
        instType = IGMInstruction.InstanceType.Self;
        variable = null;

        // If too small, exit early
        if (str.Length < 2)
        {
            return false;
        }

        // Parse variable type
        if (str[0] == '[')
        {
            var closingBracket = str.IndexOf(']', StringComparison.Ordinal);
            if (closingBracket == -1)
            {
                return false;
            }

            var varTypeStr = str[1..closingBracket].ToLowerInvariant();
            varType = varTypeStr switch
            {
                "array" => IGMInstruction.VariableType.Array,
                "stacktop" => IGMInstruction.VariableType.StackTop,
                "normal" => IGMInstruction.VariableType.Normal,
                "instance" => IGMInstruction.VariableType.Instance,
                "multipush" => IGMInstruction.VariableType.MultiPush,
                "multipushpop" => IGMInstruction.VariableType.MultiPushPop,
                _ => throw new InvalidOperationException("Unknown variable type")
            };

            // Get rid of this part of the variable string
            str = str[(closingBracket + 1)..];
        }

        // Parse instance type
        var dot = str.IndexOf('.', StringComparison.Ordinal);
        if (dot == -1)
        {
            return false;
        }
        var instTypeStr = str[..dot];
        if (short.TryParse(instTypeStr, out var instTypeObjectId))
        {
            instType = (IGMInstruction.InstanceType)instTypeObjectId;
        }
        else
        {
            instType = instTypeStr.ToLowerInvariant() switch
            {
                "self" => IGMInstruction.InstanceType.Self,
                "other" => IGMInstruction.InstanceType.Other,
                "all" => IGMInstruction.InstanceType.All,
                "global" => IGMInstruction.InstanceType.Global,
                "builtin" => IGMInstruction.InstanceType.Builtin,
                "local" => IGMInstruction.InstanceType.Local,
                "stacktop" => IGMInstruction.InstanceType.StackTop,
                "arg" or "argument" => IGMInstruction.InstanceType.Argument,
                "static" => IGMInstruction.InstanceType.Static,
                _ => throw new InvalidOperationException("Unknown instance type")
            };
        }

        // Get actual variable name
        str = str[(dot + 1)..];

        // If the variable name starts with ", treat it as a string we need to deal with escapes in
        if (str.StartsWith('"'))
        {
            if (!str.EndsWith('"'))
            {
                throw new InvalidOperationException("Expected end quote at end of variable name");
            }
            str = UnescapeStringContents(str[1..^1]);
        }

        // Update variable
        variable = new GMVariable(new GMString(str))
        {
            InstanceType = instType,
            VariableID = 0 // The mock does not model the data-file variable table.
        };

        // Check existing variables - if this already exists, return existing variable
        if (variables.TryGetValue(variable, out var existingVariable))
        {
            variable = existingVariable;
        }
        else
        {
            // This is a brand new variable
            variables.Add(variable);
        }

        return true;
    }

    private static string UnescapeStringContents(string str)
    {
        StringBuilder sb = new();

        var escapeActive = false;
        foreach (var c in str)
        {
            if (escapeActive)
            {
                sb.Append(c switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => throw new InvalidOperationException("Unrecognized escape sequence")
                });
                escapeActive = false;
                continue;
            }

            if (c == '\\')
            {
                escapeActive = true;
                continue;
            }

            sb.Append(c);
        }

        if (escapeActive)
        {
            throw new InvalidOperationException("Invalid escape at end of string");
        }

        return sb.ToString();
    }

    private static int? ParseOptionalParameter(string str, string paramName)
    {
        var paramPos = str.IndexOf($"{paramName}=", StringComparison.Ordinal);
        if (paramPos != -1)
        {
            var digitStart = paramPos + $"{paramName}=".Length;
            var digitEnd = digitStart;
            while (digitEnd < str.Length)
            {
                if (!char.IsDigit(str[digitEnd]))
                {
                    break;
                }
                digitEnd++;
            }
            if (int.TryParse(str[digitStart..digitEnd], out var paramValue))
            {
                return paramValue;
            }
        }
        return null;
    }
}
