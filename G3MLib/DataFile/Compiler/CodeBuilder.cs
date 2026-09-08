using System;
using System.Collections.Generic;
using G3MLib.Analyzer;
using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using static G3MLib.Analyzer.IGMInstruction;

namespace G3MLib.DataFile.Compiler;

/// <summary>
/// Code builder, used to generate instructions during compilation.
/// </summary>
internal class CodeBuilder : ICodeBuilder
{
    // Data being used with this code builder.
    private readonly GlobalDecompileContext _globalContext;

    // Lookup for variables.
    private readonly Dictionary<(GameMakerString, GameMakerInstruction.InstanceType), GameMakerVariable> _variableLookup;

    public CodeBuilder(GlobalDecompileContext globalContext)
    {
        _globalContext = globalContext;

        // Make variable and function lookups (to not rely on O(n) searches)
        IList<GameMakerVariable> variables = _globalContext.Data.Variables;
        _variableLookup = new(variables.Count);
        foreach (GameMakerVariable v in variables)
        {
            if (v is null)
            {
                continue;
            }
            _variableLookup.TryAdd((v.Name, v.InstanceType), v);
        }
    }

    /// <summary>
    /// Maps an opcode from G3MLib.Analyzer to an GameMakerInstruction opcode.
    /// Also handles bytecode 14 mappings.
    /// </summary>
    private GameMakerInstruction.Opcode MapOpcode(Opcode opcode)
    {
        GameMakerInstruction.Opcode utOpcode = (GameMakerInstruction.Opcode)opcode;

        if (!_globalContext.Bytecode14OrLower)
        {
            return utOpcode;
        }

        return utOpcode switch
        {
            GameMakerInstruction.Opcode.PushBltn => GameMakerInstruction.Opcode.Push,
            GameMakerInstruction.Opcode.PushLoc => GameMakerInstruction.Opcode.Push,
            GameMakerInstruction.Opcode.PushGlb => GameMakerInstruction.Opcode.Push,
            GameMakerInstruction.Opcode.PushI => GameMakerInstruction.Opcode.Push,
            _ => utOpcode
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode)
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, DataType dataType)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            Type1 = (GameMakerInstruction.DataType)dataType
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, DataType dataType1, DataType dataType2)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            Type1 = (GameMakerInstruction.DataType)dataType1,
            Type2 = (GameMakerInstruction.DataType)dataType2
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, short value, DataType dataType1, DataType dataType2)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            Type1 = (GameMakerInstruction.DataType)dataType1,
            Type2 = (GameMakerInstruction.DataType)dataType2,
            ValueShort = value
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, int value, DataType dataType1, DataType dataType2)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            Type1 = (GameMakerInstruction.DataType)dataType1,
            Type2 = (GameMakerInstruction.DataType)dataType2,
            ValueInt = value
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, long value, DataType dataType1, DataType dataType2)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            Type1 = (GameMakerInstruction.DataType)dataType1,
            Type2 = (GameMakerInstruction.DataType)dataType2,
            ValueLong = value
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, double value, DataType dataType1, DataType dataType2)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            Type1 = (GameMakerInstruction.DataType)dataType1,
            Type2 = (GameMakerInstruction.DataType)dataType2,
            ValueDouble = value
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, Opcode opcode, ComparisonType comparisonType, DataType dataType1, DataType dataType2)
    {
        return new GameMakerInstruction()
        {
            Kind = MapOpcode(opcode),
            ComparisonKind = (GameMakerInstruction.ComparisonType)comparisonType,
            Type1 = (GameMakerInstruction.DataType)dataType1,
            Type2 = (GameMakerInstruction.DataType)dataType2
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, ExtendedOpcode extendedOpcode)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.Break,
            ExtendedKind = (short)extendedOpcode,
            Type1 = GameMakerInstruction.DataType.Int16
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateInstruction(int address, ExtendedOpcode extendedOpcode, int value)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.Break,
            ExtendedKind = (short)extendedOpcode,
            Type1 = GameMakerInstruction.DataType.Int32,
            IntArgument = value
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateDuplicateInstruction(int address, DataType dataType, byte duplicationSize)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.Dup,
            Type1 = (GameMakerInstruction.DataType)dataType,
            Extra = duplicationSize
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateDupSwapInstruction(int address, DataType dataType, byte duplicationSize, byte duplicationSize2)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.Dup,
            Type1 = (GameMakerInstruction.DataType)dataType,
            Extra = duplicationSize,
            ComparisonKind = (GameMakerInstruction.ComparisonType)((duplicationSize2 << 3) | 0x80)
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreatePopSwapInstruction(int address, byte swapSize)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.Pop,
            Type1 = GameMakerInstruction.DataType.Int16,
            Type2 = GameMakerInstruction.DataType.Variable,
            SwapExtra = swapSize
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateWithExitInstruction(int address)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.PopEnv,
            JumpOffsetPopenvExitMagic = true,
            JumpOffset = 0xF00000
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateCallInstruction(int address, int argumentCount)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.Call,
            Type1 = GameMakerInstruction.DataType.Int32,
            ArgumentsCount = (ushort)argumentCount
        };
    }

    /// <inheritdoc/>
    public IGMInstruction CreateCallVariableInstruction(int address, int argumentCount)
    {
        return new GameMakerInstruction()
        {
            Kind = GameMakerInstruction.Opcode.CallV,
            Type1 = GameMakerInstruction.DataType.Variable,
            Extra = (byte)argumentCount
        };
    }

    /// <inheritdoc/>
    public void PatchInstruction(IGMInstruction instruction, string variableName, InstanceType variableInstanceType, InstanceType instructionInstanceType, VariableType variableType, bool isBuiltin, bool keepInstanceType)
    {
        if (instruction is GameMakerInstruction utInstruction)
        {
            // Transform instance type into Self when not using simple variables, either in GMLv2 or when instance type is an object/instance
            if (!keepInstanceType && (variableInstanceType >= 0 || _globalContext.UsingGMLv2) && variableType != VariableType.Normal && variableType != VariableType.Instance)
            {
                variableInstanceType = InstanceType.Self;
                instructionInstanceType = InstanceType.Self;
            }

            // Transform Argument into Builtin, but not marked as an actual builtin (only on the variable registration itself)
            if (variableInstanceType == InstanceType.Argument)
            {
                variableInstanceType = InstanceType.Builtin;
                isBuiltin = false;
            }

            // Transform irregular instance types to Self (including Other, apparently)
            if (variableInstanceType is not (InstanceType.Self or InstanceType.Local or InstanceType.Builtin or
                                             InstanceType.Global or InstanceType.Static))
            {
                variableInstanceType = InstanceType.Self;
            }

            // Lookup variable (or create new one)
            if (variableInstanceType == InstanceType.Local)
            {
                // Queue reference to be patched later
                _globalContext.CurrentCompileGroup.RegisterLocalVariable(utInstruction, variableName);
            }
            else
            {
                // Register/define non-local variable, and update variable on instruction immediately
                _globalContext.CurrentCompileGroup.RegisterNonLocalVariable(variableName);
                GameMakerString nameString = _globalContext.CurrentCompileGroup.MakeString(variableName, out int nameStringId);
                GameMakerInstruction.InstanceType actualInstType =
                    _globalContext.Data.Variables.CalculateInstType((GameMakerInstruction.InstanceType)variableInstanceType, isBuiltin, _globalContext.Data);
                if (_variableLookup.TryGetValue((nameString, actualInstType), out GameMakerVariable existingVariable))
                {
                    utInstruction.ValueVariable = existingVariable;
                }
                else
                {
                    GameMakerVariable newVariable = _globalContext.Data.Variables.Define(nameString, nameStringId, actualInstType, isBuiltin, _globalContext.Data);
                    _variableLookup.Add((nameString, actualInstType), newVariable);
                    utInstruction.ValueVariable = newVariable;
                }
            }

            // Update other parts of instruction
            utInstruction.ReferenceType = (GameMakerInstruction.VariableType)variableType;
            if (variableType is VariableType.Normal or VariableType.Instance)
            {
                utInstruction.TypeInst = (GameMakerInstruction.InstanceType)instructionInstanceType;
            }
        }
    }

    /// <inheritdoc/>
    public void PatchVariableHashInstruction(IGMInstruction instruction, string variableName, bool isBuiltin)
    {
        if (instruction is GameMakerInstruction utInstruction)
        {
            // Register/define non-local variable, and update variable on instruction immediately
            _globalContext.CurrentCompileGroup.RegisterNonLocalVariable(variableName);
            GameMakerString nameString = _globalContext.CurrentCompileGroup.MakeString(variableName, out int nameStringId);
            if (_variableLookup.TryGetValue((nameString, GameMakerInstruction.InstanceType.Self), out GameMakerVariable existingVariable))
            {
                utInstruction.ValueVariable = existingVariable;
            }
            else
            {
                GameMakerVariable newVariable = _globalContext.Data.Variables.Define(
                    nameString, nameStringId, GameMakerInstruction.InstanceType.Self, isBuiltin, _globalContext.Data);
                _variableLookup.Add((nameString, GameMakerInstruction.InstanceType.Self), newVariable);
                utInstruction.ValueVariable = newVariable;
            }
        }
    }

    /// <inheritdoc/>
    public void PatchInstruction(IGMInstruction instruction, FunctionScope scope, string functionName, IBuiltinFunction builtinFunction)
    {
        if (instruction is GameMakerInstruction utInstruction)
        {
            // Resolve reference
            GameMakerFunction reference;
            if (scope.TryGetDeclaredFunction(_globalContext, functionName, out FunctionEntry entry))
            {
                reference = entry.Function as GameMakerFunction ?? throw new InvalidOperationException("Function not resolved for function entry");
            }
            else if (_globalContext.Builtins.LookupBuiltinFunction(functionName) is not null)
            {
                reference = _globalContext.Data.Functions.EnsureDefined(functionName, _globalContext.Data.Strings);
            }
            else if (_globalContext.GlobalFunctions.TryGetFunction(functionName, out IGMFunction function))
            {
                reference = (GameMakerFunction)function;
            }
            else if (_globalContext.GetScriptId(functionName, out int _))
            {
                reference = _globalContext.Data.Functions.EnsureDefined(functionName, _globalContext.Data.Strings);
            }
            else
            {
                throw new CompilerException($"Failed to look up function \"{functionName}\"");
            }

            // Put reference on instruction
            utInstruction.ValueFunction = reference;
        }
    }

    /// <inheritdoc/>
    public void PatchInstruction(IGMInstruction instruction, FunctionEntry functionEntry)
    {
        if (instruction is GameMakerInstruction utInstruction)
        {
            // Resolve reference
            GameMakerFunction reference = functionEntry.Function as GameMakerFunction ?? throw new InvalidOperationException("Function not resolved for function entry");

            // Put reference on instruction
            utInstruction.ValueFunction = reference;
        }
    }

    /// <inheritdoc/>
    public void PatchInstruction(IGMInstruction instruction, string stringContent)
    {
        if (instruction is GameMakerInstruction utInstruction)
        {
            // Make/find string
            GameMakerString str = _globalContext.CurrentCompileGroup.MakeString(stringContent, out int strIndex);

            // Update instruction
            utInstruction.ValueString = new GameMakerResourceById<GameMakerString, GameMakerChunkSTRG>(str, strIndex);
        }
    }

    /// <inheritdoc/>
    public void PatchInstruction(IGMInstruction instruction, int value)
    {
        if (instruction is GameMakerInstruction utInstruction)
        {
            if (utInstruction.Kind == GameMakerInstruction.Opcode.Push)
            {
                utInstruction.ValueInt = value;
            }
            else
            {
                utInstruction.JumpOffset = value / 4;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsGlobalFunctionName(string name)
    {
        // Awaiting official Underminers resolution: pre-2.3 function declaration behavior.
        return _globalContext.GlobalFunctions.FunctionNameExists(name);
    }

    /// <inheritdoc/>
    public int GenerateTryVariableID(int internalIndex)
    {
        return _globalContext.CurrentCompileGroup.NextTryVariableIndex++;
    }

    /// <inheritdoc/>
    public long GenerateArrayOwnerID(string variableName, long functionIndex, bool isDot)
    {
        long id = _globalContext.UsingNewArrayOwners ? (functionIndex << 16) : 0;
        if (isDot)
        {
            // Double the ID at this point, apparently...
            id += id;
        }
        if (variableName is not null)
        {
            id += _globalContext.CurrentCompileGroup.RegisterName(variableName);
        }
        id += _globalContext.CurrentCompileGroup.CurrentCodeEntryNameHash;

        // Wrap around 31-bit unsigned integer limit, to stay within a pushi.e or push.i instruction
        return Math.Abs(id % int.MaxValue);
    }

    /// <inheritdoc/>
    public void OnParseNameIdentifier(string name)
    {
        // Awaiting official Underminers resolution: original resource ID allocation is runner-version-specific.
    }
}
