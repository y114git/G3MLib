using System;

namespace G3MLib.DataFile.Models;

/// <summary>
/// Details the possible extension kinds for a GameMaker data file.
/// </summary>
public enum GameMakerExtensionKind : uint
{
    /// <summary>
    /// A DLL extension.
    /// </summary>
    Dll = 1,
    /// <summary>
    /// A GML extension.
    /// </summary>
    GML = 2,
    /// <summary>
    /// Legacy Action Library extension type.
    /// </summary>
    ActionLib = 3,
    /// <summary>
    /// Generic legacy extension type.
    /// </summary>
    Generic = 4,
    /// <summary>
    /// A JavaScript extension.
    /// </summary>
    Js = 5
}

/// <summary>
/// Details the possible variable types for GameMaker extensions.
/// </summary>
public enum GameMakerExtensionVarType : uint
{
    /// <summary>
    /// A string variable.
    /// </summary>
    String = 1,
    /// <summary>
    /// A double variable.
    /// </summary>
    Double = 2
}

/// <summary>
/// A class representing an argument for <see cref="GameMakerExtensionFunction"/>s.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerExtensionFunctionArg : GameMakerObject, IStaticChildObjectsSize
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 4;

    /// <summary>
    /// The variable type of this argument.
    /// </summary>
    public GameMakerExtensionVarType Type { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="GameMakerExtensionFunctionArg"/>.
    /// </summary>
    public GameMakerExtensionFunctionArg()
    {
        Type = GameMakerExtensionVarType.Double;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GameMakerExtensionFunctionArg"/> with a specified <see cref="GameMakerExtensionVarType"/>.
    /// </summary>
    /// <param name="type"></param>
    public GameMakerExtensionFunctionArg(GameMakerExtensionVarType type)
    {
        Type = type;
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write((uint)Type);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Type = (GameMakerExtensionVarType)reader.ReadUInt32();
    }
}

/// <summary>
/// A function in a <see cref="GameMakerExtension"/>.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerExtensionFunction : GameMakerObject, IDisposable
{
    /// <summary>
    /// The name of the function.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// An identification number of the function.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Runner-defined function calling convention.
    /// </summary>
    public uint Kind { get; set; }

    /// <summary>
    /// The return type of the function.
    /// </summary>
    public GameMakerExtensionVarType RetType { get; set; }

    /// <summary>
    /// External function name used by the runner to bind this declaration.
    /// </summary>
    public GameMakerString ExtName { get; set; }

    /// <summary>
    /// A list of arguments this function takes.
    /// </summary>
    public GameMakerSimpleList<GameMakerExtensionFunctionArg> Arguments { get; set; } = new GameMakerSimpleList<GameMakerExtensionFunctionArg>();

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write(ID);
        writer.Write(Kind);
        writer.Write((uint)RetType);
        writer.WriteGameMakerString(ExtName);
        writer.WriteGameMakerObject(Arguments);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        ID = reader.ReadUInt32();
        Kind = reader.ReadUInt32();
        RetType = (GameMakerExtensionVarType)reader.ReadUInt32();
        ExtName = reader.ReadGameMakerString();
        Arguments = reader.ReadGameMakerObject<GameMakerSimpleList<GameMakerExtensionFunctionArg>>();
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 20;

        return 1 + GameMakerSimpleList<GameMakerExtensionFunctionArg>.UnserializeChildObjectCount(reader);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content + " (" + ExtName?.Content + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Arguments = new();
        Name = null;
        ExtName = null;
    }
}

/// <summary>
/// A file that's used in an <see cref="GameMakerExtension"/>.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerExtensionFile : GameMakerObject, IDisposable
{
    /// <summary>
    /// The filename of this extension file.
    /// </summary>
    public GameMakerString Filename { get; set; }

    /// <summary>
    /// The script name that gets called when the game ends.
    /// </summary>
    public GameMakerString? CleanupScript { get; set; }

    /// <summary>
    /// The script name that gets called when the game starts.
    /// </summary>
    public GameMakerString? InitScript { get; set; }

    /// <summary>
    /// The type of extension this belongs to.
    /// </summary>
    public GameMakerExtensionKind Kind { get; set; }

    /// <summary>
    /// The functions this file has defined.
    /// </summary>
    public GameMakerPointerList<GameMakerExtensionFunction> Functions { get; set; } = new GameMakerPointerList<GameMakerExtensionFunction>();

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Filename);
        writer.WriteGameMakerString(CleanupScript);
        writer.WriteGameMakerString(InitScript);
        writer.Write((uint)Kind);
        writer.WriteGameMakerObject(Functions);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Filename = reader.ReadGameMakerString();
        CleanupScript = reader.ReadGameMakerString();
        InitScript = reader.ReadGameMakerString();
        Kind = (GameMakerExtensionKind)reader.ReadUInt32();
        Functions = reader.ReadGameMakerObject<GameMakerPointerList<GameMakerExtensionFunction>>();
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        reader.Position += 16;

        count += 1 + GameMakerPointerList<GameMakerExtensionFunction>.UnserializeChildObjectCount(reader);

        return count;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Filename?.Content is string filename ? filename + " (" + GetType().Name + ")" : "(Unknown extension file)";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Functions is not null)
        {
            foreach (GameMakerExtensionFunction func in Functions)
                func?.Dispose();
        }
        Filename = null;
        CleanupScript = null;
        InitScript = null;
        Functions = new();
    }
}

/// <summary>
/// An option that's used in an <see cref="GameMakerExtension"/>.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerExtensionOption : GameMakerNamedResource, IStaticChildObjectsSize, IDisposable
{
    /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
    public static readonly uint ChildObjectsSize = 12;

    /// <summary>
    /// The type of what the option value is.
    /// </summary>
    public enum OptionKind : uint
    {
        /// <summary>
        /// The option value is a boolean-
        /// </summary>
        Boolean = 0,
        /// <summary>
        /// The option value is a number.
        /// </summary>
        Number = 1,
        /// <summary>
        /// The option value is a string.
        /// </summary>
        String = 2
    }

    /// <inheritdoc />
    public GameMakerString Name { get; set; }

    /// <summary>
    /// The value of this option.
    /// </summary>
    public GameMakerString Value { get; set; }

    /// <summary>
    /// The type of this option.
    /// </summary>
    public OptionKind Kind { get; set; } = OptionKind.String;

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.WriteGameMakerString(Value);
        writer.Write((uint)Kind);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Value = reader.ReadGameMakerString();
        Kind = (OptionKind)reader.ReadUInt32();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Name = null;
        Value = null;
    }
}

/// <summary>
/// An extension entry in a GameMaker data file.
/// </summary>
[PropertyChanged.AddINotifyPropertyChangedInterface]
public class GameMakerExtension : GameMakerNamedResource, IDisposable
{
    /// <summary>
    /// Legacy extension-editor folder name.
    /// </summary>
    /// <remarks>This is a remnant from the legacy GameMaker7-8.1 extension editor (aka ExtMaker). <br/>
    /// The runner reads this name, but ignores it. This probably shouldn't be changed anyway.</remarks>
    public GameMakerString FolderName { get; set; }

    /// <summary>
    /// The name of the extension.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Serialized extension class identifier.
    /// </summary>
    public GameMakerString ClassName { get; set; }

    /// <summary>
    /// The version of the extension.
    /// </summary>
    public GameMakerString Version { get; set; }

    /// <summary>
    /// The files that this extension contains.
    /// </summary>
    public GameMakerPointerList<GameMakerExtensionFile> Files { get; set; } = new GameMakerPointerList<GameMakerExtensionFile>();

    /// <summary>
    /// The options that this extension contains.
    /// </summary>
    public GameMakerPointerList<GameMakerExtensionOption> Options { get; set; } = new GameMakerPointerList<GameMakerExtensionOption>();

    /// <inheritdoc />
    public override string ToString()
    {
        return Name?.Content + " (" + GetType().Name + ")";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Files is not null)
            foreach (GameMakerExtensionFile file in Files)
                file?.Dispose();
        Files = new();
        if (Options is not null)
            foreach (GameMakerExtensionOption opt in Options)
                opt?.Dispose();
        Options = new();
        FolderName = null;
        Name = null;
        ClassName = null;
        Version = null;
    }

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        if (FolderName is null)
            throw new GameMakerSerializationException($"{nameof(FolderName)} on {nameof(GameMakerExtension)} is null; this causes crashes upon loading");

        writer.WriteGameMakerString(FolderName);
        writer.WriteGameMakerString(Name);
        if (writer.gameMakerData.IsVersionAtLeast(2023, 4))
            writer.WriteGameMakerString(Version);
        writer.WriteGameMakerString(ClassName);
        if (writer.gameMakerData.IsVersionAtLeast(2022, 6))
        {
            writer.WriteGameMakerObjectPointer(Files);
            writer.WriteGameMakerObjectPointer(Options);
            writer.WriteGameMakerObject(Files);
            writer.WriteGameMakerObject(Options);
        }
        else
        {
            writer.WriteGameMakerObject(Files);
        }
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        FolderName = reader.ReadGameMakerString();
        Name = reader.ReadGameMakerString();
        if (reader.gameMakerData.IsVersionAtLeast(2023, 4))
            Version = reader.ReadGameMakerString();
        else
            Version = null;
        ClassName = reader.ReadGameMakerString();
        if (reader.gameMakerData.IsVersionAtLeast(2022, 6))
        {
            Files = reader.ReadGameMakerObjectPointer<GameMakerPointerList<GameMakerExtensionFile>>();
            Options = reader.ReadGameMakerObjectPointer<GameMakerPointerList<GameMakerExtensionOption>>();
            reader.ReadGameMakerObject(Files);
            reader.ReadGameMakerObject(Options);
        }
        else
        {
            Files = reader.ReadGameMakerObject<GameMakerPointerList<GameMakerExtensionFile>>();
            Options = null;
        }
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        if (reader.gameMakerData.IsVersionAtLeast(2023, 4))
            reader.Position += 12 + 4; // + "Version"
        else
            reader.Position += 12;

        if (reader.gameMakerData.IsVersionAtLeast(2022, 6))
        {
            uint filesPtr = reader.ReadUInt32();
            uint optionsPtr = reader.ReadUInt32();

            reader.AbsPosition = filesPtr;
            count += 1 + GameMakerPointerList<GameMakerExtensionFile>.UnserializeChildObjectCount(reader);
            reader.AbsPosition = optionsPtr;
            count += 1 + GameMakerPointerList<GameMakerExtensionOption>.UnserializeChildObjectCount(reader);
        }
        else
            count += 1 + GameMakerPointerList<GameMakerExtensionFile>.UnserializeChildObjectCount(reader);

        return count;
    }

    public static bool ProductDataEligible(GameMakerData data)
    {
        uint major = data?.GeneralInfo?.Major ?? 0;
        if (major >= 2)
        {
            return true;
        }
        uint build = data?.GeneralInfo?.Build ?? 0;
        if (build >= 1773 || build == 1559) // NOTE: unknown if 1773 is the earliest version which contains product IDs/data
        {
            return true;
        }
        return false;
    }
}