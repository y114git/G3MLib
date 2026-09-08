using System;
using System.Collections.Generic;
using System.Linq;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile;

/// <summary>
/// Extension methods for <see cref="GameMakerData"/>.
/// </summary>
public static class GameMakerDataExtensionMethods
{
    /// <summary>
    /// An extension method, that returns the element in a <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s
    /// that has a specified <paramref name="name"/>.
    /// </summary>
    /// <param name="list">The <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s to search in.</param>
    /// <param name="name">The name of the <see cref="GameMakerNamedResource"/> to find.</param>
    /// <param name="ignoreCase">Whether casing should be ignored for searching.</param>
    /// <typeparam name="T">A type of <see cref="GameMakerNamedResource"/>.</typeparam>
    /// <returns>The element that has the specified name.</returns>
    public static T ByName<T>(this IList<T> list, string name, bool ignoreCase = false) where T : GameMakerNamedResource
    {
        StringComparison comparisonType = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (T item in list)
        {
            if (item is null)
            {
                continue;
            }
            if (item.Name?.Content is string content && content.Equals(name, comparisonType))
            {
                return item;
            }
        }

        return default;
    }

    /// <summary>
    /// An extension method, that returns the index of an element in a <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s
    /// that has a specified <paramref name="name"/>.
    /// </summary>
    /// <param name="list">The <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s to search in.</param>
    /// <param name="name">The name of the <see cref="GameMakerNamedResource"/> to find.</param>
    /// <param name="ignoreCase">Whether casing should be ignored for searching.</param>
    /// <typeparam name="T">A type of <see cref="GameMakerNamedResource"/>.</typeparam>
    /// <returns>The index of the element that has the specified name, or -1 if none is found.</returns>
    public static int IndexOfName<T>(this IList<T> list, string name, bool ignoreCase = false) where T : GameMakerNamedResource
    {
        StringComparison comparisonType = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not T item)
            {
                continue;
            }
            if (item.Name?.Content is string content && content.Equals(name, comparisonType))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// An extension method, that returns the element in a <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s
    /// that has a specified <paramref name="name"/>.
    /// </summary>
    /// <param name="list">The <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s to search in.</param>
    /// <param name="name">The name of the <see cref="GameMakerNamedResource"/> to find.</param>
    /// <param name="ignoreCase">Whether casing should be ignored for searching.</param>
    /// <typeparam name="T">A type of <see cref="GameMakerNamedResource"/>.</typeparam>
    /// <returns>The element that has the specified name.</returns>
    public static T ByName<T>(this IList<T> list, ReadOnlySpan<char> name, bool ignoreCase = false) where T : GameMakerNamedResource
    {
        StringComparison comparisonType = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (T item in list)
        {
            if (item is null)
            {
                continue;
            }
            if (item.Name?.Content is string content && content.AsSpan().Equals(name, comparisonType))
            {
                return item;
            }
        }

        return default;
    }

    /// <summary>
    /// An extension method, that returns the index of an element in a <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s
    /// that has a specified <paramref name="name"/>.
    /// </summary>
    /// <param name="list">The <see cref="IList{T}"/> of <see cref="GameMakerNamedResource"/>s to search in.</param>
    /// <param name="name">The name of the <see cref="GameMakerNamedResource"/> to find.</param>
    /// <param name="ignoreCase">Whether casing should be ignored for searching.</param>
    /// <typeparam name="T">A type of <see cref="GameMakerNamedResource"/>.</typeparam>
    /// <returns>The index of the element that has the specified name, or -1 if none is found.</returns>
    public static int IndexOfName<T>(this IList<T> list, ReadOnlySpan<char> name, bool ignoreCase = false) where T : GameMakerNamedResource
    {
        StringComparison comparisonType = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not T item)
            {
                continue;
            }
            if (item.Name?.Content is string content && content.AsSpan().Equals(name, comparisonType))
            {
                return i;
            }
        }

        return -1;
    }

    public static GameMakerCodeLocals For(this IList<GameMakerCodeLocals> list, GameMakerCode code)
    {
        return code.Name is null ? null : list.FirstOrDefault(x =>
            x?.Name == code.Name || x?.Name?.Content == code.Name.Content);
    }

    /// <summary>
    /// Creates <paramref name="content"/> as a new <see cref="GameMakerString"/>,
    /// adds it to a <see cref="List{T}"/> of <see cref="GameMakerString"/> if it does not exist yet, and returns it.
    /// </summary>
    /// <param name="list">The <see cref="List{T}"/> of <see cref="GameMakerString"/>.</param>
    /// <param name="content">The string to create a <see cref="GameMakerString"/> of.</param>
    /// <param name="createNew">Whether to create a new <see cref="GameMakerString"/> if the one with the same content exists.</param>
    /// <returns><paramref name="content"/> as a <see cref="GameMakerString"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static GameMakerString MakeString(this IList<GameMakerString> list, string content, bool createNew = false)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!createNew)
            return list.MakeString(content, out _);

        GameMakerString newString = new(content);
        list.Add(newString);
        return newString;
    }

    /// <summary>
    /// Creates <paramref name="content"/> as a new <see cref="GameMakerString"/>,
    /// adds it to a <see cref="List{T}"/> of <see cref="GameMakerString"/> if it does not exist yet, and returns it.
    /// </summary>
    /// <param name="list">The <see cref="List{T}"/> of <see cref="GameMakerString"/>.</param>
    /// <param name="content">The string to create a <see cref="GameMakerString"/> of.</param>
    /// <param name="index">The index where the newly created <see cref="GameMakerString"/> is located in <paramref name="list"/>.</param>
    /// <returns><paramref name="content"/> as a <see cref="GameMakerString"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static GameMakerString MakeString(this IList<GameMakerString> list, string content, out int index)
    {
        ArgumentNullException.ThrowIfNull(content);

        // String indices are referenced throughout the data file, so unused entries are intentionally retained.
        for (int i = 0; i < list.Count; i++)
        {
            GameMakerString str = list[i];
            if (str?.Content == content)
            {
                index = i;
                return str;
            }
        }

        GameMakerString newString = new GameMakerString(content);
        index = list.Count;
        list.Add(newString);
        return newString;
    }

    public static GameMakerFunction Define(this IList<GameMakerFunction> list, string name, IList<GameMakerString> strg)
    {
        GameMakerString str = strg.MakeString(name, out int id);
        GameMakerFunction func = new()
        {
            Name = str,
            NameStringID = id
        };
        list.Add(func);
        return func;
    }

    public static GameMakerFunction EnsureDefined(this IList<GameMakerFunction> list, string name, IList<GameMakerString> strg)
    {
        GameMakerFunction func = list.ByName(name);
        if (func is null)
        {
            return list.Define(name, strg);
        }
        return func;
    }

    public static GameMakerInstruction.InstanceType CalculateInstType(this IList<GameMakerVariable> list, GameMakerInstruction.InstanceType inst, bool isBuiltin, GameMakerData data)
    {
        // Local variables are defined distinctly
        if (inst == GameMakerInstruction.InstanceType.Local)
        {
            throw new InvalidOperationException("Use DefineLocal instead");
        }

        // Handle builtin variables always using "self"
        if (isBuiltin)
        {
            inst = GameMakerInstruction.InstanceType.Self;
        }

        // Handle bytecode 14 differences
        bool bytecode14 = data.GeneralInfo.BytecodeVersion <= 14;
        if (bytecode14)
        {
            // In bytecode 14, instance types are always undefined for some reason
            inst = GameMakerInstruction.InstanceType.Undefined;
        }

        return inst;
    }

    public static GameMakerVariable Define(this IList<GameMakerVariable> list, GameMakerString nameString, int nameStringId, GameMakerInstruction.InstanceType inst, bool isBuiltin, GameMakerData data)
    {
        // Update variables counts first
        uint oldId = data.VarCount1;
        bool bytecode14 = data.GeneralInfo.BytecodeVersion <= 14;
        if (!bytecode14)
        {
            if (data.IsVersionAtLeast(2, 3))
            {
                // GMS 2.3+
                if (!isBuiltin)
                {
                    data.VarCount1++;
                    data.VarCount2 = data.VarCount1;
                }
                oldId = (uint)nameStringId;
            }
            else if (!data.DifferentVarCounts)
            {
                // Bytecode 16+
                data.VarCount1++;
                data.VarCount2++;
            }
            else
            {
                // Bytecode 15
                if (inst == GameMakerInstruction.InstanceType.Self && !isBuiltin)
                {
                    oldId = data.VarCount2;
                    data.VarCount2++;
                }
                else if (inst == GameMakerInstruction.InstanceType.Global)
                {
                    data.VarCount1++;
                }
            }
        }

        // Actually create new variable
        int variableId;
        if (bytecode14)
        {
            variableId = 0;
        }
        else if (isBuiltin)
        {
            variableId = (int)GameMakerInstruction.InstanceType.Builtin;
        }
        else
        {
            variableId = (int)oldId;
        }
        GameMakerVariable newVariable = new()
        {
            Name = nameString,
            InstanceType = inst,
            VarID = variableId,
            NameStringID = nameStringId
        };
        list.Add(newVariable);

        return newVariable;
    }

    public static GameMakerVariable EnsureDefined(this IList<GameMakerVariable> list, GameMakerString nameString, int nameStringId, GameMakerInstruction.InstanceType inst, bool isBuiltin, GameMakerData data)
    {
        // Calculate correct instance type
        inst = list.CalculateInstType(inst, isBuiltin, data);

        // Search for existing variable that can be used
        foreach (GameMakerVariable variable in list)
        {
            if (variable?.Name == nameString && variable.InstanceType == inst)
            {
                return variable;
            }
        }

        // Otherwise, make a new variable
        return list.Define(nameString, nameStringId, inst, isBuiltin, data);
    }

    public static GameMakerVariable DefineLocal(this IList<GameMakerVariable> list, GameMakerData data, int varId, GameMakerString nameString, int nameStringId)
    {
        // In bytecode 14, look up on entire variable list for existing locals...
        bool bytecode14 = data.GeneralInfo.BytecodeVersion <= 14;
        if (bytecode14)
        {
            foreach (GameMakerVariable variable in list)
            {
                if (variable?.Name == nameString && (bytecode14 || variable.InstanceType == GameMakerInstruction.InstanceType.Local))
                {
                    return variable;
                }
            }
        }

        // Define new local
        GameMakerVariable vari = new()
        {
            Name = nameString,
            InstanceType = bytecode14 ? GameMakerInstruction.InstanceType.Undefined : GameMakerInstruction.InstanceType.Local,
            VarID = bytecode14 ? 0 : varId,
            NameStringID = nameStringId
        };
        list.Add(vari);
        return vari;
    }

    public static GameMakerExtensionFunction DefineExtensionFunction(this IList<GameMakerExtensionFunction> extfuncs, IList<GameMakerFunction> funcs, IList<GameMakerString> strg, uint id, uint kind, string name, GameMakerExtensionVarType rettype, string extname, params GameMakerExtensionVarType[] args)
    {
        var func = new GameMakerExtensionFunction()
        {
            ID = id,
            Name = strg.MakeString(name),
            ExtName = strg.MakeString(extname),
            Kind = kind,
            RetType = rettype
        };
        foreach(var a in args)
            func.Arguments.Add(new GameMakerExtensionFunctionArg() { Type = a });
        extfuncs.Add(func);
        funcs.EnsureDefined(name, strg);
        return func;
    }
}