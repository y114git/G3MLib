using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Decompiler;

public static partial class Assembler
{
    /// <summary>
    /// Lookup of extended (break) instruction IDs to their mnemonics.
    /// </summary>
    internal static readonly Dictionary<short, string> ExtendedIDToName = new()
    {
        { -1,  "chkindex" },
        { -2,  "pushaf" },
        { -3,  "popaf" },
        { -4,  "pushac" },
        { -5,  "setowner" },
        { -6,  "isstaticok" },
        { -7,  "setstatic" },
        { -8,  "savearef" },
        { -9,  "restorearef" },
        { -10, "chknullish" },
        { -11, "pushref" }
    };

    /// <summary>
    /// Lookup of extended (break) instruction mnemonics to their IDs.
    /// </summary>
    internal static readonly Dictionary<string, short> NameToExtendedID = new()
    {
        { "chkindex", -1 },
        { "pushaf", -2 },
        { "popaf", -3 },
        { "pushac", -4 },
        { "setowner", -5 },
        { "isstaticok", -6 },
        { "setstatic", -7 },
        { "savearef", -8 },
        { "restorearef", -9 },
        { "chknullish", -10 },
        { "pushref", -11 }
    };

    // Regex for parsing call instruction names and argument counts
    [GeneratedRegex(@"^(.+)\(argc=([0-9]+)\)$", RegexOptions.Compiled)]
    private static partial Regex callInstrRegex();

    // Optional lookup caches for bulk assembly operations.
    [ThreadStatic]
    private static Dictionary<string, GameMakerFunction> _funcCache;
    [ThreadStatic]
    private static Dictionary<(string name, GameMakerInstruction.InstanceType inst), GameMakerVariable> _varCache;
    [ThreadStatic]
    private static Dictionary<string, int> _resourceNameCache;
    [ThreadStatic]
    private static Dictionary<string, (GameMakerString Str, int Id)> _stringCache;
    [ThreadStatic]
    private static Dictionary<string, GameMakerCode> _codeCache;
    [ThreadStatic]
    private static GameMakerData _cacheData;

    // Regex for parsing code entry local/argument counts
    [GeneratedRegex(@"^\(locals=([0-9]+)\,\s*argc=([0-9]+)\)$", RegexOptions.Compiled)]
    private static partial Regex codeEntryRegex();

    // Default passthrough action for data updates that may require the main thread to perform.
    private static readonly Action<Action> passthroughMainThreadAction = static (f) => f();

    /// <summary>
    /// Build lookup caches for bulk assembly operations.
    /// </summary>
    public static void SetLookupCaches(GameMakerData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _cacheData = data;
        _funcCache = new Dictionary<string, GameMakerFunction>();
        foreach (var f in data.Functions)
            if (f?.Name?.Content != null)
                _funcCache.TryAdd(f.Name.Content, f);

        _varCache = new Dictionary<(string, GameMakerInstruction.InstanceType), GameMakerVariable>();
        foreach (var v in data.Variables)
            if (v?.Name?.Content != null)
                _varCache.TryAdd((v.Name.Content, v.InstanceType), v);

        _resourceNameCache = new Dictionary<string, int>();
        void CacheList<T>(IList<T> list) where T : GameMakerNamedResource
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is T item && item.Name?.Content != null)
                    _resourceNameCache.TryAdd(item.Name.Content, i);
            }
        }
        CacheList(data.Sounds);
        CacheList(data.Sprites);
        CacheList(data.Backgrounds);
        CacheList(data.Paths);
        CacheList(data.Scripts);
        CacheList(data.Fonts);
        CacheList(data.GameObjects);
        CacheList(data.Rooms);
        CacheList(data.Extensions);
        CacheList(data.Shaders);
        CacheList(data.Timelines);
        if (data.AnimationCurves is not null) CacheList(data.AnimationCurves);
        if (data.Sequences is not null) CacheList(data.Sequences);
        if (data.AudioGroups is not null) CacheList(data.AudioGroups);
        if (data.FilterEffects is not null) CacheList(data.FilterEffects);
        if (data.ParticleSystems is not null) CacheList(data.ParticleSystems);
        if (data.ParticleSystemEmitters is not null) CacheList(data.ParticleSystemEmitters);
        if (data.TextureGroupInfo is not null) CacheList(data.TextureGroupInfo);

        _stringCache = new Dictionary<string, (GameMakerString Str, int Id)>(data.Strings.Count);
        for (int i = 0; i < data.Strings.Count; i++)
        {
            var s = data.Strings[i];
            if (s?.Content != null)
                _stringCache.TryAdd(s.Content, (s, i));
        }

        _codeCache = new Dictionary<string, GameMakerCode>(data.Code.Count);
        foreach (var c in data.Code)
        {
            if (c?.Name?.Content != null)
                _codeCache.TryAdd(c.Name.Content, c);
        }
    }

    /// <summary>
    /// Clear lookup caches after bulk assembly completes.
    /// </summary>
    public static void ClearLookupCaches()
    {
        _cacheData = null;
        _funcCache = null;
        _varCache = null;
        _resourceNameCache = null;
        _stringCache = null;
        _codeCache = null;
    }

    private static void EnsureCachesMatch(GameMakerData data)
    {
        if (_cacheData is not null && !ReferenceEquals(_cacheData, data))
            ClearLookupCaches();
    }

    private static GameMakerFunction FindFunction(string name, IList<GameMakerFunction> list)
    {
        if (_funcCache != null)
        {
            if (_funcCache.TryGetValue(name, out var cached))
                return cached;

            var result = list.ByName(name);
            if (result != null)
                _funcCache[name] = result;
            return result;
        }

        return list.ByName(name);
    }

    private static GameMakerVariable FindVariable(string name, GameMakerInstruction.InstanceType instType, IList<GameMakerVariable> list)
    {
        if (name is null)
            return null;

        if (_varCache != null)
        {
            if (_varCache.TryGetValue((name, instType), out var cached))
                return cached;

            var result = list.FirstOrDefault(variable => variable?.Name?.Content == name && variable.InstanceType == instType);
            if (result != null)
                _varCache[(name, instType)] = result;
            return result;
        }

        return list.FirstOrDefault(variable => variable?.Name?.Content == name && variable.InstanceType == instType);
    }

    /// <summary>
    /// Assembles a single <see cref="GameMakerInstruction"/>, with the provided data, and possibly local variables. Labels are not allowed.
    /// </summary>
    /// <param name="source">Single assembly instruction to assemble.</param>
    /// <param name="data"><see cref="GameMakerData"/> instance to use for finding information, and updating code.</param>
    /// <param name="localvars">Lookup map of local variables to use when assembling the instruction.</param>
    /// <param name="mainThreadAction">Main thread action to be called when updating data, if necessary. Must execute the supplied callback synchronously.</param>
    public static GameMakerInstruction AssembleOne(string source, GameMakerData data, Dictionary<string, GameMakerVariable> localvars = null, Action<Action> mainThreadAction = null)
    {
        GameMakerInstruction instr = AssembleOne(source, data, localvars, out string label, mainThreadAction);
        if (label is not null)
        {
            throw new FormatException("Cannot use labels in this context");
        }
        return instr;
    }

    /// <summary>
    /// Assembles a single <see cref="GameMakerInstruction"/>, with the provided data, and no local variables. Labels are not allowed.
    /// </summary>
    /// <param name="source">Single assembly instruction to assemble.</param>
    /// <param name="data"><see cref="GameMakerData"/> instance to use for finding information, and updating code.</param>
    /// <param name="mainThreadAction">Main thread action to be called when updating data, if necessary. Must execute the supplied callback synchronously.</param>
    public static GameMakerInstruction AssembleOne(string source, GameMakerData data, Action<Action> mainThreadAction)
    {
        GameMakerInstruction instr = AssembleOne(source, data, null, out string label, mainThreadAction);
        if (label is not null)
        {
            throw new FormatException("Cannot use labels in this context");
        }
        return instr;
    }

    /// <summary>
    /// Assembles a single <see cref="GameMakerInstruction"/>, with the provided data, and possibly local variables. Labels are not allowed.
    /// </summary>
    /// <param name="source">Single assembly instruction to assemble.</param>
    /// <param name="data"><see cref="GameMakerData"/> instance to use for finding information, and updating code.</param>
    /// <param name="localvars">Lookup map of local variables to use when assembling the instruction.</param>
    /// <param name="label">Output label name, if one was found while parsing the instruction, or <see langword="null"/> if not.</param>
    /// <param name="mainThreadAction">Main thread action to be called when updating data, if necessary. Must execute the supplied callback synchronously.</param>
    public static GameMakerInstruction AssembleOne(string source, GameMakerData data, Dictionary<string, GameMakerVariable> localvars, out string label, Action<Action> mainThreadAction = null)
    {
        EnsureCachesMatch(data);

        // Default label output to null
        label = null;

        // Remove comments from end of line
        string line = source.Split(" ;;; ", 2)[0];

        // Split apart opcode and data types
        string opcode = line;
        int space = opcode.IndexOf(' ', StringComparison.InvariantCulture);
        if (space >= 0)
        {
            opcode = line[..space];
            line = line[(space + 1)..].Trim();
        }
        else
        {
            line = "";
        }
        string[] types = opcode.Split('.');
        if (types.Length > 3)
        {
            throw new FormatException("Too many type parameters");
        }

        // Start creating instruction with opcode and types
        string kind = types[0];
        GameMakerInstruction instr = new();
        if (NameToExtendedID.TryGetValue(kind.ToLower(CultureInfo.InvariantCulture), out short extendedId))
        {
            // Extended opcode, which uses Break under the hood
            instr.Kind = GameMakerInstruction.Opcode.Break;
        }
        else
        {
            // Regular opcode, just use the normal enum
            instr.Kind = Enum.Parse<GameMakerInstruction.Opcode>(kind, true);
        }
        if (types.Length >= 2)
        {
            // Parse first type
            instr.Type1 = GameMakerInstructionUtil.FromOpcodeParam(types[1]);
        }
        if (types.Length >= 3)
        {
            // Parse second type
            instr.Type2 = GameMakerInstructionUtil.FromOpcodeParam(types[2]);
        }

        // Parse depending on overarching instruction type
        switch (GameMakerInstruction.GetInstructionType(instr.Kind))
        {
            case GameMakerInstruction.InstructionType.SingleTypeInstruction:
                // Single type instructions generally don't have anything, except for dup and callv
                if (instr.Kind is GameMakerInstruction.Opcode.Dup or GameMakerInstruction.Opcode.CallV)
                {
                    // Dup instructions have the additional possibility of being in "dup swap" mode, which has an extra parameter
                    if (instr.Kind is GameMakerInstruction.Opcode.Dup)
                    {
                        space = line.IndexOf(' ', StringComparison.InvariantCulture);
                        if (space >= 0)
                        {
                            byte spec = byte.Parse(line[(space + 1)..].Trim());
                            instr.ComparisonKind = (GameMakerInstruction.ComparisonType)(spec | 0x80);
                            line = line[..space];
                        }
                    }

                    // Parse regular (first) parameter
                    instr.Extra = byte.Parse(line);
                    line = "";
                }
                break;

            case GameMakerInstruction.InstructionType.DoubleTypeInstruction:
                // Double type instructions never have anything extra to parse
                break;

            case GameMakerInstruction.InstructionType.ComparisonInstruction:
                // Comparison instructions always have a comparison kind to parse
                instr.ComparisonKind = Enum.Parse<GameMakerInstruction.ComparisonType>(line, true);
                line = "";
                break;

            case GameMakerInstruction.InstructionType.GotoInstruction:
                // Goto (jump/branch) instructions have a few formatting options...
                if (line[0] == '$')
                {
                    // Old-style formatting, using a direct offset
                    instr.JumpOffset = int.Parse(line[1..]);
                }
                else if (line == "<drop>")
                {
                    // Special case for popenv instructions, signifying a break out of a with() loop
                    instr.JumpOffsetPopenvExitMagic = true;
                    instr.JumpOffset = 0xF00000;
                }
                else if (line[0] == '[' && line[^1] == ']')
                {
                    // New-style formatting, using a block label (extract the name)
                    label = line[1..^1];
                }
                else
                {
                    // Nothing was valid
                    throw new FormatException($"Unknown goto target \"{line}\"");
                }

                line = "";
                break;

            case GameMakerInstruction.InstructionType.PopInstruction:
                // Pop instruction, which has one special case for pop swap
                if (instr.Type1 == GameMakerInstruction.DataType.Int16)
                {
                    // Pop swap instruction (see #129)
                    instr.SwapExtra = byte.Parse(line);
                }
                else
                {
                    // Regular variable pop; parse reference
                    GameMakerInstruction.InstanceType inst = instr.TypeInst;
                    (instr.ValueVariable, instr.ReferenceType) = ParseVariableReference(line, data, localvars, ref inst);
                    instr.TypeInst = inst;
                }

                line = "";
                break;

            case GameMakerInstruction.InstructionType.PushInstruction:
                // Push instruction, parsing depends on data type being pushed
                switch (instr.Type1)
                {
                    case GameMakerInstruction.DataType.Double:
                        // Simple 64-bit float
                        instr.ValueDouble = double.Parse(line, CultureInfo.InvariantCulture);
                        break;
                    case GameMakerInstruction.DataType.Int32:
                        // Either a simple 32-bit integer, or a reference to something...
                        if (int.TryParse(line, out int ival))
                        {
                            // Simple integer
                            instr.ValueInt = ival;
                        }
                        else if (line.StartsWith("[variable]", StringComparison.Ordinal))
                        {
                            // Variable reference
                            line = line["[variable]".Length..];
                            instr.ValueVariable = data.Variables.EnsureDefined(
                                data.Strings.MakeString(line, out int nameStringId), nameStringId,
                                GameMakerInstruction.InstanceType.Self, false, data);
                        }
                        else if (line.StartsWith("[function]", StringComparison.Ordinal))
                        {
                            // Function reference
                            line = line["[function]".Length..];
                            instr.ValueFunction = FindFunction(line, data.Functions);
                        }
                        else if (FindFunction(line, data.Functions) is GameMakerFunction f)
                        {
                            // Function reference (old-style syntax)
                            instr.ValueFunction = f;
                        }
                        else
                        {
                            // Resource name (for convenience in assembly editing older GM games that don't use pushref)
                            instr.ValueInt = ParseResourceName(line, data);
                        }
                        break;
                    case GameMakerInstruction.DataType.Int64:
                        // Either a simple 64-bit integer, or a resource name for convenience
                        if (long.TryParse(line, out long lval))
                        {
                            instr.ValueLong = lval;
                        }
                        else
                        {
                            instr.ValueLong = ParseResourceName(line, data);
                        }
                        break;
                    case GameMakerInstruction.DataType.Variable:
                        // Simple variable reference
                        GameMakerInstruction.InstanceType inst2 = instr.TypeInst;
                        (instr.ValueVariable, instr.ReferenceType) = ParseVariableReference(line, data, localvars, ref inst2);
                        instr.TypeInst = inst2;
                        break;
                    case GameMakerInstruction.DataType.String:
                        // Simple string reference
                        instr.ValueString = ParseStringReference(line, data.Strings);
                        break;
                    case GameMakerInstruction.DataType.Int16:
                        // Simple 16-bit integer, or a resource name for convenience
                        if (short.TryParse(line, out short sval))
                        {
                            instr.ValueShort = sval;
                        }
                        else
                        {
                            instr.ValueShort = (short)ParseResourceName(line, data);
                        }
                        break;
                    default:
                        // Invalid (or unused) data type
                        throw new FormatException($"Invalid push data type {instr.Type1}");
                }

                line = "";
                break;

            case GameMakerInstruction.InstructionType.CallInstruction:
                // Call instructions - match function name and argument count using a regular expression
                Match match = callInstrRegex().Match(line);
                if (!match.Success)
                {
                    throw new FormatException("Call instruction format error; should be formatted like my_func_name(argc=3)");
                }

                // Find function being referenced
                string funcName = match.Groups[1].Value;
                GameMakerFunction func = FindFunction(funcName, data.Functions) ??
                    throw new KeyNotFoundException($"Could not find function with name \"{funcName}\"");
                instr.ValueFunction = func;

                // Parse argument count
                instr.ArgumentsCount = ushort.Parse(match.Groups[2].Value);

                line = "";
                break;

            case GameMakerInstruction.InstructionType.BreakInstruction:
                // Break instruction - could be a regular break (with an additional extended kind), or an extended opcode that we already determined
                if (extendedId != 0)
                {
                    // Use predetermined ID
                    instr.ExtendedKind = extendedId;

                    // For pushref (push reference) instructions, additional data is needed for its referenced asset
                    if (extendedId == -11)
                    {
                        // Parse additional int argument
                        if (int.TryParse(line, out int intArgument))
                        {
                            instr.IntArgument = intArgument;
                        }
                        else
                        {
                            // Or alternatively parse function!
                            GameMakerFunction extFunc = FindFunction(line, data.Functions) ??
                                throw new KeyNotFoundException($"Could not find function specified by extended pushref instruction: \"{line}\"");
                            instr.ValueFunction = extFunc;
                        }
                    }
                }
                else
                {
                    // Old-style break syntax (not recommended to be used, especially for pushref)
                    instr.ExtendedKind = short.Parse(line);
                }

                line = "";
                break;
        }

        // Make sure there's no remaining line
        if (line != "")
        {
            throw new FormatException($"Expected end of line; found remaining string: \"{line}\"");
        }

        return instr;
    }

    /// <summary>
    /// Parses a resource name, using the given data.
    /// </summary>
    private static int ParseResourceName(string line, GameMakerData data)
    {
        if (_resourceNameCache != null)
        {
            if (_resourceNameCache.TryGetValue(line, out int cachedId))
            {
                return cachedId;
            }

            int fallbackId = data.IndexOfByName(line);
            if (fallbackId >= 0)
            {
                _resourceNameCache[line] = fallbackId;
                return fallbackId;
            }

            throw new FormatException($"Unable to parse \"{line}\" as a number or resource name");
        }

        int id = data.IndexOfByName(line);
        if (id < 0)
        {
            throw new FormatException($"Unable to parse \"{line}\" as a number or resource name");
        }
        return id;
    }

    /// <summary>
    /// Assembles many instructions, separated by newlines, using the provided data.
    /// </summary>
    /// <param name="source">Assembly source to assemble.</param>
    /// <param name="data"><see cref="GameMakerData"/> instance to use for finding information, and updating code.</param>
    /// <param name="mainThreadAction">Main thread action to be called when updating data, if necessary. Must execute the supplied callback synchronously.</param>
    public static List<GameMakerInstruction> Assemble(string source, GameMakerData data, Action<Action> mainThreadAction = null)
    {
        return Assemble(source, data, mainThreadAction, null);
    }

    public static List<GameMakerInstruction> Assemble(string source, GameMakerData data, Action<Action> mainThreadAction, GameMakerCode ownerCode)
    {
        EnsureCachesMatch(data);

        // Use a passthrough main thread action if none was supplied
        mainThreadAction ??= passthroughMainThreadAction;

        // Initialize structures
        Dictionary<string, uint> labels = new();
        List<(GameMakerInstruction Instruction, uint InstructionAddress, string Label)> labelTargets = new();
        List<GameMakerInstruction> instructions = new(16);
        Dictionary<string, GameMakerVariable> localvars = new();
        List<GameMakerCodeLocals.LocalVar> codeLocalDefinitions = new();

        // Start reading instructions
        uint address = 0;
        StringReader strReader = new(source);
        string fullLine;
        while ((fullLine = strReader.ReadLine()) is not null)
        {
            // Trim line, and skip if it's empty or a comment
            string line = fullLine.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            // Handle sub-code entries
            if (line[0] == '>')
            {
                // Parse sub-code entry name, and make sure it exists
                line = line[2..].Trim();
                int space = line.IndexOf(' ', StringComparison.InvariantCulture);
                string codeName = line[..space];
                GameMakerCode code = null;
                if (ownerCode?.ChildEntries != null)
                {
                    foreach (var child in ownerCode.ChildEntries)
                    {
                        if (child?.Name?.Content == codeName)
                        {
                            code = child;
                            break;
                        }
                    }
                }

                if (code == null && _codeCache != null)
                {
                    if (!_codeCache.TryGetValue(codeName, out code))
                    {
                        code = data.Code.ByName(codeName);
                        if (code != null)
                        {
                            _codeCache[codeName] = code;
                        }
                    }
                }
                else if (code == null)
                {
                    code = data.Code.ByName(codeName);
                }

                if (code is null)
                    throw new KeyNotFoundException($"Failed to find code entry with name \"{codeName}\"");

                // Parse additional info (local/argument count), using a regular expression
                string info = line[(space + 1)..];
                Match match = codeEntryRegex().Match(info);
                if (!match.Success)
                {
                    throw new FormatException("Sub-code entry format error; should be formatted like \"> gml_Script_some_script (locals=6, argc=7)\"");
                }

                // Update info on the code entry
                mainThreadAction(() =>
                {
                    code.LocalsCount = ushort.Parse(match.Groups[1].Value);
                    code.ArgumentsCount = ushort.Parse(match.Groups[2].Value);
                    code.Offset = address * 4;
                });
                continue;
            }

            // Handle block labels
            if (line[0] == ':' && line.Length >= 3 && line[1] == '[')
            {
                // Extract label name
                string label = line[2..line.IndexOf(']', StringComparison.InvariantCulture)];

                // Make sure label name isn't invalid or a duplicate
                if (string.IsNullOrEmpty(label))
                {
                    throw new FormatException("Invalid block label syntax");
                }
                if (labels.ContainsKey(label))
                {
                    throw new FormatException($"Duplicate label: \"label\"");
                }

                // Register label for later resolving and parsing
                labels.Add(label, address);
                continue;
            }

            // Handle assembler directives
            if (line[0] == '.')
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == ".localvar")
                {
                    if (parts.Length is < 3 or > 4)
                    {
                    throw new FormatException("Local variable definition format error; should be formatted like \".localvar index name [variable-id]\"");
                    }

                    GameMakerString localName;
                    if (parts.Length == 4)
                    {
                        // Find variable using its ID, verify it's a local variable, and add it to structure
                        if (!int.TryParse(parts[3], CultureInfo.InvariantCulture, out int variableIndex) ||
                            variableIndex < 0 || variableIndex >= data.Variables.Count ||
                            data.Variables[variableIndex] is not GameMakerVariable variable)
                        {
                            throw new KeyNotFoundException($"No variable exists at index {parts[3]}");
                        }
                        if (data.GeneralInfo?.BytecodeVersion >= 15 && variable.InstanceType != GameMakerInstruction.InstanceType.Local)
                        {
                            throw new ArgumentException($"Variable with index {parts[3]} actually has instance type {variable.InstanceType} instead of Local", nameof(source));
                        }
                        if (variable.Name?.Content != parts[2])
                        {
                            throw new ArgumentException($"Variable with index {parts[3]} actually has name {variable.Name} instead of the specified name \"{parts[2]}\"", nameof(source));
                        }
                        localvars.Add(parts[2], variable);
                        localName = variable.Name;
                    }
                    else
                    {
                        localName = data.Strings.MakeString(parts[2]);
                    }

                    codeLocalDefinitions.Add(new()
                    {
                        Index = uint.Parse(parts[1], CultureInfo.InvariantCulture),
                        Name = localName
                    });
                }
                else
                {
                    throw new FormatException($"Unknown assembler directive: \"{parts[0]}\"");
                }
                continue;
            }

            // Assemble individual instruction
            GameMakerInstruction instr = AssembleOne(line, data, localvars, out string labelTarget);
            if (labelTarget is not null)
            {
                // Instruction references a label. Track it for later resolving
                labelTargets.Add((instr, address, labelTarget));
            }

            // Add instruction to list
            instructions.Add(instr);
            address += instr.CalculateInstructionSize();
        }

        // Resolve jump offsets for instructions that reference labels
        foreach ((GameMakerInstruction instr, uint instrAddress, string label) in labelTargets)
        {
            instr.JumpOffset = (int)labels[label] - (int)instrAddress;
        }

        if (ownerCode is not null && codeLocalDefinitions.Count > 0 && data.CodeLocals is not null)
        {
            mainThreadAction(() =>
            {
                GameMakerCodeLocals locals = data.CodeLocals.For(ownerCode)
                    ?? GameMakerCodeLocals.CreateEmptyEntry(data, ownerCode.Name);
                locals.Locals.Clear();
                foreach (GameMakerCodeLocals.LocalVar local in codeLocalDefinitions)
                {
                    locals.Locals.Add(local);
                }
            });
        }

        return instructions;
    }

    /// <summary>
    /// Parses a string reference in assembly, using the given string list (may create new strings).
    /// </summary>
    private static GameMakerResourceById<GameMakerString, GameMakerChunkSTRG> ParseStringReference(string line, IList<GameMakerString> strg)
    {
        // Parse ID at the end of the string, if given
        string str = line;
        int at = str.LastIndexOf('@');
        int id = -1;
        if (at >= 0 && (at - 1) == str.LastIndexOf('"'))
        {
            // First make certain that this is actually an ID, not part of the string content
            id = int.Parse(str[(at + 1)..]);
            str = str[..at];
        }

        // Parse string contents
        if (!string.IsNullOrEmpty(str))
        {
            if (str[0] != '"' || str[^1] != '"')
            {
                throw new FormatException("Bad string format");
            }
            str = GameMakerString.UnescapeText(str[1..^1]);
        }
        else
        {
            str = null;
        }

        // Get existing string object using ID
        GameMakerString strobj = (id >= 0) ? strg[id] : null;
        if (strobj is not null)
        {
            // Update string contents, or retain original value if empty string passed (e.g. "push.s @300")
            if (str is not null)
            {
                strobj.Content = str;
            }
        }
        else if (_stringCache != null && str is not null && _stringCache.TryGetValue(str, out var cached))
        {
            strobj = cached.Str;
            id = cached.Id;
        }
        else
        {
            // New string needs to be created
            strobj = strg.MakeString(str, out int newId);
            id = newId;
            if (_stringCache != null && str is not null)
            {
                _stringCache.TryAdd(str, (strobj, id));
            }
        }

        return new GameMakerResourceById<GameMakerString, GameMakerChunkSTRG>() { Resource = strobj, CachedId = id };
    }

    private static (GameMakerVariable Variable, GameMakerInstruction.VariableType ReferenceType) ParseVariableReference(
        string line, GameMakerData data, Dictionary<string, GameMakerVariable> localvars, ref GameMakerInstruction.InstanceType instance)
    {
        ReadOnlySpan<char> str = line.AsSpan();
        int strPosition = 0;

        // Variable type, and instance type as stored in VARI chunk, adjusted based on context
        GameMakerInstruction.VariableType type = GameMakerInstruction.VariableType.Normal;
        GameMakerInstruction.InstanceType variInstanceType = instance;

        // Parse instance type, if at the beginning
        if (str[strPosition] != '[')
        {
            // Read up until first dot character
            int instanceTypeDot = str.IndexOf('.');
            if (instanceTypeDot >= 0)
            {
                ReadOnlySpan<char> instanceTypeStr = str[..instanceTypeDot];
                if (short.TryParse(instanceTypeStr, out short instNum))
                {
                    // This is a valid 16-bit integer, probably an object or room instance ID
                    instance = (GameMakerInstruction.InstanceType)instNum;
                }
                else
                {
                    // Otherwise, this should always be one of the valid instance type enum values
                    instance = Enum.Parse<GameMakerInstruction.InstanceType>(instanceTypeStr, true);
                }
            }
            else
            {
                // Instance type is missing seemingly, so just use undefined
                instance = GameMakerInstruction.InstanceType.Undefined;
            }

            // Adjust VARI instance type based on existing type
            variInstanceType = instance switch
            {
                >= 0                                        => GameMakerInstruction.InstanceType.Self,
                GameMakerInstruction.InstanceType.Other     => GameMakerInstruction.InstanceType.Self,
                GameMakerInstruction.InstanceType.Arg       => GameMakerInstruction.InstanceType.Builtin,
                GameMakerInstruction.InstanceType.Builtin   => GameMakerInstruction.InstanceType.Self,      // used with @@This@@
                GameMakerInstruction.InstanceType.Stacktop  => GameMakerInstruction.InstanceType.Self,      // used with @@GetInstance@@
                _                                           => instance
            };

            // Set up for parsing after the dot
            strPosition = instanceTypeDot + 1;
        }

        // Parse variable type, if present here, as well as the alternate location of the instance type, if present (directly after it)
        if (strPosition < str.Length && str[strPosition] == '[')
        {
            // Read up until closing bracket character
            int variableTypeEnd = str[(strPosition + 1)..].IndexOf(']') + (strPosition + 1);
            if (variableTypeEnd < (strPosition + 1))
            {
                // Invalid formatting, objectively
                throw new FormatException("Missing ']' character in variable reference");
            }

            // Variable type should always be one of the enum values
            ReadOnlySpan<char> variableTypeStr = str[(strPosition + 1)..variableTypeEnd];
            type = Enum.Parse<GameMakerInstruction.VariableType>(variableTypeStr, true);

            // Parse instance type, if present
            int instanceTypeDot = str[(variableTypeEnd + 1)..].IndexOf('.') + (variableTypeEnd + 1);
            if (instanceTypeDot >= (variableTypeEnd + 1))
            {
                // This instance type should always be one of the enum values
                ReadOnlySpan<char> instanceTypeStr = str[(variableTypeEnd + 1)..instanceTypeDot];
                variInstanceType = Enum.Parse<GameMakerInstruction.InstanceType>(instanceTypeStr, true);

                // Set up parsing after the dot
                strPosition = instanceTypeDot + 1;
            }
            else
            {
                // Older versions of the assembly syntax did not print out instance types for array/stacktop references, which loses info in GMS 2.3+
                if (type == GameMakerInstruction.VariableType.Array ||
                    type == GameMakerInstruction.VariableType.StackTop)
                {
                throw new NotSupportedException("Old instruction format is incompatible (missing instance type in array or stacktop)");
                }

                // Adjust VARI instance type based on existing type
                if (variInstanceType >= 0 || variInstanceType == GameMakerInstruction.InstanceType.Other)
                {
                    variInstanceType = GameMakerInstruction.InstanceType.Self;
                }

                // Set up parsing after the variable type's closing bracket
                strPosition = variableTypeEnd + 1;
            }
        }

        // In older versions, VARI does not assign instance types properly, so account for that
        if (data.GeneralInfo?.BytecodeVersion <= 14)
        {
            variInstanceType = GameMakerInstruction.InstanceType.Undefined;
        }

        // Locate variable from either local variables, or VARI chunk
        GameMakerVariable locatedVariable;
        string variableName = str[strPosition..].ToString();
        if (variInstanceType == GameMakerInstruction.InstanceType.Local && data.CodeLocals is not null)
        {
            locatedVariable = localvars.GetValueOrDefault(variableName);
            locatedVariable ??= FindVariable(variableName, variInstanceType, data.Variables);
        }
        else
        {
            locatedVariable = FindVariable(variableName, variInstanceType, data.Variables);
        }

        // If nothing is found, throw an error, as we cannot properly assemble it
        if (locatedVariable is null)
        {
            throw new KeyNotFoundException($"Failed to find existing variable: {variInstanceType.ToString().ToLower(CultureInfo.InvariantCulture)}.{variableName}");
        }

        // Return reference to be used in instruction
        return (locatedVariable, type);
    }
}
