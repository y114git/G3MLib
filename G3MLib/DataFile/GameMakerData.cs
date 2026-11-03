using System.Globalization;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Decompiler.GameSpecific;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile;

/// <summary>
/// An object representing a GameMaker Studio data file.
/// </summary>
/// <remarks>This is basically the heart of the data file, which is usually named <c>data.win</c>, <c>data.unx</c>,
/// <c>data.ios</c> or <c>data.droid</c>, depending for which OS the game was compiled for.. <br/>
/// It includes all the data within it accessible by either the <see cref="FORM"/>-Chunk attribute,
/// but also via already organized attributes such as <see cref="Backgrounds"/> or <see cref="GameObjects"/>.
/// Chunks retain the binary layout; typed properties expose their resource lists.
/// <para>
/// Thread model: a data file and everything reachable from it is owned by one thread, normally the UI thread, and that
/// thread makes all edits. The model has no locking. Background jobs (decompile, export, search, diff, patch) must either
/// work on a snapshot, a second <see cref="GameMakerData"/> read from the same file, or hold <see cref="ModelLock.Read"/>
/// while the owner takes <see cref="ModelLock.Write"/> for each edit. Reading from several threads at once is safe as long
/// as nobody edits. A <see cref="Decompiler.GlobalDecompileContext"/> caches asset names and global functions, so build a new one after
/// edits that rename or add assets. The library keeps no other process-wide mutable state: the few static settings are
/// atomic, log and import scopes are per async operation, and the assembler lookup caches are per thread (call
/// <see cref="Decompiler.Assembler.ClearLookupCaches"/> when a thread is done with them).
/// </para></remarks>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class GameMakerData : IDisposable
{
    /// <summary>
    /// Indexer to access the resource list by its name.
    /// </summary>
    /// <param name="resourceTypeName">The resource name to get.</param>
    /// <exception cref="MissingMemberException"> if the data file does not contain a property with that name.</exception>
    public IList this[string resourceTypeName]
    {
        get
        {
            // Prevent recursion
            if (resourceTypeName == "Item")
                return null;

            var property = AllListProperties.FirstOrDefault(x => x.Name == resourceTypeName) ?? throw new MissingMemberException($"\"GameMakerData\" doesn't contain a resource list named \"{resourceTypeName}\".");
            return property.GetValue(this, null) as IList;
        }
        set
        {
            // Prevent recursion
            if (resourceTypeName == "Item")
                return;

            var property = AllListProperties.FirstOrDefault(x => x.Name == resourceTypeName) ?? throw new MissingMemberException($"\"GameMakerData\" doesn't contain a resource list named \"{resourceTypeName}\".");
            ReplaceList(property.GetValue(this, null) as IList, value, property.PropertyType.GetGenericArguments()[0]);
        }
    }

    /// <summary>
    /// Indexer to access the resource list by its items type.
    /// </summary>
    /// <param name="resourceType">The resource type to get.</param>
    /// <exception cref="NotSupportedException"> if the type is not an <see cref="GameMakerNamedResource"/>.</exception>
    /// <exception cref="MissingMemberException"> if the data file does not contain a property of that type.</exception>
    public IList this[Type resourceType]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(resourceType);

            // Prevent recursion
            if (resourceType == typeof(GameMakerResource))
                return null;

            if (!typeof(GameMakerResource).IsAssignableFrom(resourceType))
                throw new NotSupportedException($"\"{resourceType.FullName}\" is not an GameMakerResource.");

            var property = AllListProperties.FirstOrDefault(x => x.PropertyType.GetGenericArguments()[0] == resourceType) ?? throw new MissingMemberException($"\"GameMakerData\" doesn't contain a resource list of type \"{resourceType.FullName}\".");
            return property.GetValue(this, null) as IList;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(resourceType);

            if (!typeof(GameMakerResource).IsAssignableFrom(resourceType))
                throw new NotSupportedException($"\"{resourceType.FullName}\" is not an GameMakerResource.");

            var property = AllListProperties.FirstOrDefault(x => x.PropertyType.GetGenericArguments()[0] == resourceType) ?? throw new MissingMemberException($"\"GameMakerData\" doesn't contain a resource list of type \"{resourceType.FullName}\".");
            ReplaceList(property.GetValue(this, null) as IList, value, property.PropertyType.GetGenericArguments()[0]);
        }
    }

    private static void ReplaceList(IList target, IList source, Type elementType)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        if (ReferenceEquals(target, source))
            return;

        var items = new object[source.Count];
        source.CopyTo(items, 0);
        if (items.Any(item => item is not null && !elementType.IsInstanceOfType(item)))
            throw new ArgumentException($"List contains an item that is not a {elementType.FullName}.", nameof(source));

        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    /// <summary>
    /// The FORM chunk of the data file.
    /// </summary>
    public GameMakerChunkFORM FORM { get; set; }

    /// <summary>
    /// General info of the data file.
    /// </summary>
    public GameMakerGeneralInfo GeneralInfo => FORM.GEN8?.Object;

    /// <summary>
    /// General Options of the data file.
    /// </summary>
    public GameMakerOptions Options => FORM.OPTN?.Object;

    /// <summary>
    /// Languages of the data file.
    /// </summary>
    public GameMakerLanguage Language => FORM.LANG?.Object;

    /// <summary>
    /// The used extensions of the data file.
    /// </summary>
    public IList<GameMakerExtension> Extensions => FORM.EXTN?.List;

    /// <summary>
    /// The used sounds of the data file.
    /// </summary>
    public IList<GameMakerSound> Sounds => FORM.SOND?.List;

    /// <summary>
    /// The audio groups of the data file.
    /// </summary>
    public IList<GameMakerAudioGroup> AudioGroups => FORM.AGRP?.List;

    /// <summary>
    /// The sprites of the data file.
    /// </summary>
    public IList<GameMakerSprite> Sprites => FORM.SPRT?.List;

    /// <summary>
    /// The backgrounds (or Tilesets) of the data file.
    /// </summary>
    public IList<GameMakerBackground> Backgrounds => FORM.BGND?.List;

    /// <summary>
    /// The paths of the data file.
    /// </summary>
    public IList<GameMakerPath> Paths => FORM.PATH?.List;

    /// <summary>
    /// The scripts of the data file.
    /// </summary>
    public IList<GameMakerScript> Scripts => FORM.SCPT?.List;

    /// <summary>
    /// The global initialization scripts of the data file.
    /// </summary>
    public IList<GameMakerGlobalInit> GlobalInitScripts => FORM.GLOB?.List;

    /// <summary>
    /// The global end scripts of the data file.
    /// </summary>
    public IList<GameMakerGlobalInit> GameEndScripts => FORM.GMEN?.List;

    /// <summary>
    /// The used shaders of the data file.
    /// </summary>
    public IList<GameMakerShader> Shaders => FORM.SHDR?.List;

    /// <summary>
    /// The fonts of the data file.
    /// </summary>
    public IList<GameMakerFont> Fonts => FORM.FONT?.List;

    /// <summary>
    /// The Timelines of the data file.
    /// </summary>
    public IList<GameMakerTimeline> Timelines => FORM.TMLN?.List;

    /// <summary>
    /// The game objects of the data file.
    /// </summary>
    public IList<GameMakerGameObject> GameObjects => FORM.OBJT?.List;

    /// <summary>
    /// The rooms of the data file.
    /// </summary>
    public IList<GameMakerRoom> Rooms => FORM.ROOM?.List;
    /// <summary>
    /// The texture page items from the data file.
    /// </summary>
    public IList<GameMakerTexturePageItem> TexturePageItems => FORM.TPAG?.List;

    /// <summary>
    /// The code entries of the data file.
    /// </summary>
    public IList<GameMakerCode> Code => FORM.CODE?.List;

    /// <summary>
    /// The used variables of the data file.
    /// </summary>
    public IList<GameMakerVariable> Variables => FORM.VARI?.List;

    /// <summary>
    /// First VARI header counter. The compiler maintains it when defining variables.
    /// </summary>
    public uint VarCount1 { get => FORM.VARI.VarCount1; set => FORM.VARI.VarCount1 = value; }

    /// <summary>
    /// Second VARI header counter. Its relationship to <see cref="VarCount1"/> varies by bytecode version.
    /// </summary>
    public uint VarCount2 { get => FORM.VARI.VarCount2; set => FORM.VARI.VarCount2 = value; }

    /// <summary>
    /// Whether the two VARI header counters differ in this data file.
    /// </summary>
    public bool DifferentVarCounts { get => FORM.VARI.DifferentVarCounts; set => FORM.VARI.DifferentVarCounts = value; }

    /// <summary>
    /// Legacy alias for <see cref="VarCount1"/>.
    /// </summary>
    public uint InstanceVarCount { get => VarCount1; set => VarCount1 = value; }

    /// <summary>
    /// Legacy alias for <see cref="VarCount2"/>.
    /// </summary>
    public uint InstanceVarCountAgain { get => VarCount2; set => VarCount2 = value; }

    /// <summary>
    /// Largest local-variable table size required by compiled code.
    /// </summary>
    public uint MaxLocalVarCount { get => FORM.VARI.MaxLocalVarCount; set => FORM.VARI.MaxLocalVarCount = value; }

    /// <summary>
    /// The functions of the data file.
    /// </summary>
    public IList<GameMakerFunction> Functions => FORM.FUNC?.Functions;

    /// <summary>
    /// The code locals of the data file.
    /// </summary>
    public IList<GameMakerCodeLocals> CodeLocals => FORM.FUNC?.CodeLocals;

    /// <summary>
    /// The used strings of the data file.
    /// </summary>
    public IList<GameMakerString> Strings => FORM.STRG?.List;

    /// <summary>
    /// The embedded images of the data file. This is used to store built-in particle sprites,
    /// every time you use <c>part_sprite</c> functions.
    /// </summary>
    public IList<GameMakerEmbeddedImage> EmbeddedImages => FORM.EMBI?.List;

    /// <summary>
    /// The embedded textures of the data file.
    /// </summary>
    public IList<GameMakerEmbeddedTexture> EmbeddedTextures => FORM.TXTR?.List;

    /// <summary>
    /// The texture group infos of the data file.
    /// </summary>
    public IList<GameMakerTextureGroupInfo> TextureGroupInfo => FORM.TGIN?.List;

    /// <summary>
    /// The embedded audio of the data file.
    /// </summary>
    public IList<GameMakerEmbeddedAudio> EmbeddedAudio => FORM.AUDO?.List;

    /// <summary>
    /// The used tags of the data file.
    /// </summary>
    public GameMakerTags Tags => FORM.TAGS?.Object;

    /// <summary>
    /// The animation curves of the data file.
    /// </summary>
    public IList<GameMakerAnimationCurve> AnimationCurves => FORM.ACRV?.List;

    /// <summary>
    /// The sequences of the data file.
    /// </summary>
    public IList<GameMakerSequence> Sequences => FORM.SEQN?.List;

    /// <summary>
    /// The feature flags stored in the data file.
    /// </summary>
    public GameMakerFeatureFlags FeatureFlags => FORM.FEAT?.Object;

    /// <summary>
    /// The filter effects stored in the data file.
    /// </summary>
    public IList<GameMakerFilterEffect> FilterEffects => FORM.FEDS?.List;

    /// <summary>
    /// The particle systems stored in the data file.
    /// </summary>
    public IList<GameMakerParticleSystem> ParticleSystems => FORM.PSYS?.List;

    /// <summary>
    /// The particle system emitters stored in the data file.
    /// </summary>
    public IList<GameMakerParticleSystemEmitter> ParticleSystemEmitters => FORM.PSEM?.List;


    private readonly List<LoadWarning> _loadWarnings = [];
    private readonly object _loadWarningsLock = new();

    /// <summary>
    /// The warnings the reader raised while this data file was read, in the order they were raised.
    /// The list is complete only after the read finishes; returns a snapshot copy so concurrent access mid-read does not race.
    /// Empty for a new data file and for a file that read cleanly.
    /// </summary>
    public IReadOnlyList<LoadWarning> LoadWarnings
    {
        get
        {
            lock (_loadWarningsLock)
            {
                return _loadWarnings.ToArray();
            }
        }
    }

    /// <summary>
    /// Whether the reader skipped or could not model data (an unknown chunk, an unknown or skipped object, an unsupported bytecode version, or an important warning),
    /// so that writing the data file may not reproduce everything that was read.
    /// </summary>
    public bool MayLoseDataOnSave { get; private set; }

    internal void AddLoadWarning(LoadWarning warning, bool mayLoseData)
    {
        lock (_loadWarningsLock)
        {
            _loadWarnings.Add(warning);
            MayLoseDataOnSave |= mayLoseData;
        }
    }

    /// <summary>
    /// Whether this is an unsupported bytecode version.
    /// </summary>
    public bool UnsupportedBytecodeVersion { get; set; }

    /// <summary>
    /// Whether the Texture Page Items (TPGA) chunk is 4 byte aligned.
    /// </summary>
    public bool IsTPAG4ByteAligned { get; set; }

    /// <summary>
    /// Whether the data file has short circuiting enabled.
    /// </summary>
    public bool ShortCircuit { get; set; } = true;

    /// <summary>
    /// Whether the data file has array copy-on-write enabled.
    /// </summary>
    public bool ArrayCopyOnWrite { get; set; }

    /// <summary>
    /// The last room particle system instance ID of the data file (incrementing).
    /// </summary>
    /// <remarks>
    /// The first actual usable ID is 8388608.
    /// </remarks>
    public int LastParticleSystemInstanceID { get; set; } = 8388607;

    /// <summary>
    /// Some info for the editor to store data on.
    /// </summary>
    public ToolInfo ToolInfo { get; } = new();

    /// <summary>
    /// Shows the current padding value. <c>-1</c> indicates a pre 1.4.9999 padding, where the default is 16.
    /// </summary>
    public int PaddingAlignException { get; set; } = -1;

    /// <summary>
    /// A list of known GameMaker Studio constants and variables.
    /// </summary>
    public BuiltinList BuiltinList { get; set; }

    /// <summary>
    /// Cache for GMLv2 functions defined in global scripts. It can be refreshed by <see cref="Decompiler.GlobalDecompileContext"/>
    /// when that context is created without explicit global functions.
    /// </summary>
    public GlobalFunctions GlobalFunctions { get; set; }

    /// <summary>
    /// Registry for macro types, their resolvers, and other data specific to this game.
    /// </summary>
    public GameSpecificRegistry GameSpecificRegistry { get; set; }

    /// <summary>
    /// An array of a <see cref="GameMakerData"/> properties with <see cref="IList{T}"/> as their type.
    /// </summary>
    public PropertyInfo[] AllListProperties { get; private set; }

    /// <summary>
    /// Initializes new <see cref="GameMakerData"/> instance.
    /// </summary>
    public GameMakerData()
    {
        AllListProperties = [.. GetType().GetProperties()
                            .Where(x => x.PropertyType.IsGenericType
                                        && x.PropertyType.GetGenericTypeDefinition() == typeof(IList<>))];
    }

    /// <summary>
    /// Get a resource from the data file by name.
    /// </summary>
    /// <remarks>
    /// This does a linear search, and will thus be slow if used many times. It's recommended to build lookup maps if many searches are required.
    /// </remarks>
    /// <param name="name">The name of the desired resource.</param>
    /// <param name="ignoreCase">Whether to ignore casing while searching.</param>
    /// <returns>The <see cref="GameMakerNamedResource"/>.</returns>
    public GameMakerNamedResource ByName(string name, bool ignoreCase = false)
    {
        return
            Sounds.ByName(name, ignoreCase) ??
            Sprites.ByName(name, ignoreCase) ??
            Backgrounds.ByName(name, ignoreCase) ??
            Paths.ByName(name, ignoreCase) ??
            Scripts.ByName(name, ignoreCase) ??
            Fonts.ByName(name, ignoreCase) ??
            GameObjects.ByName(name, ignoreCase) ??
            Rooms.ByName(name, ignoreCase) ??
            Extensions.ByName(name, ignoreCase) ??
            Shaders.ByName(name, ignoreCase) ??
            Timelines.ByName(name, ignoreCase) ??
            AnimationCurves?.ByName(name, ignoreCase) ??
            Sequences?.ByName(name, ignoreCase) ??
            AudioGroups?.ByName(name, ignoreCase) ??
            FilterEffects?.ByName(name, ignoreCase) ??
            ParticleSystems?.ByName(name, ignoreCase) ??
            ParticleSystemEmitters?.ByName(name, ignoreCase) ??
            TextureGroupInfo?.ByName(name, ignoreCase) ??
            (GameMakerNamedResource)null;
    }

    /// <summary>
    /// Returns the zero-based index of the first occurrence of the specified <see cref="GameMakerResource"/>.
    /// </summary>
    /// <remarks>
    /// This does a linear search, and will thus be slow if used many times. It's recommended to build lookup maps if many searches are required.
    /// </remarks>
    /// <param name="obj">The object to get the index of.</param>
    /// <param name="panicIfInvalid">Whether to throw if <paramref name="obj"/> is not a valid object.</param>
    /// <returns>The zero-based index position of the <paramref name="obj"/> parameter if it is found, or -1 if it is not found.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="panicIfInvalid"/> is <see langword="true"/>
    /// and <paramref name="obj"/> could not be found.</exception>
    public int IndexOf(GameMakerResource obj, bool panicIfInvalid = true)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var objType = obj.GetType();
        var objListPropInfo = AllListProperties.FirstOrDefault(x => x.PropertyType.GetGenericArguments()[0] == objType);
        if (objListPropInfo?.GetValue(this) is IList list)
            return list.IndexOf(obj);

        if (panicIfInvalid)
            throw new InvalidOperationException();
        return -1;
    }


    /// <summary>
    /// Returns the zero-based index of the first occurrence of the specified <see cref="GameMakerNamedResource"/>.
    /// </summary>
    /// <remarks>
    /// This does a linear search, and will thus be slow if used many times. It's recommended to build lookup maps if many searches are required.
    /// </remarks>
    /// <param name="name">The name of the desired resource.</param>
    /// <param name="ignoreCase">Whether to ignore casing while searching.</param>
    /// <returns>The zero-based index position of the <see cref="GameMakerNamedResource"/> if it is found, or -1 if it is not found.</returns>
    public int IndexOfByName(string name, bool ignoreCase = false)
    {
        var res = Sounds.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Sprites.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Backgrounds.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Paths.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Scripts.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Fonts.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = GameObjects.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Rooms.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Extensions.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Shaders.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;
        res = Timelines.IndexOfName(name, ignoreCase);
        if (res >= 0) return res;

        if (AnimationCurves is not null)
        {
            res = AnimationCurves.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }
        if (Sequences is not null)
        {
            res = Sequences.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }
        if (AudioGroups is not null)
        {
            res = AudioGroups.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }
        if (FilterEffects is not null)
        {
            res = FilterEffects.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }
        if (ParticleSystems is not null)
        {
            res = ParticleSystems.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }
        if (ParticleSystemEmitters is not null)
        {
            res = ParticleSystemEmitters.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }
        if (TextureGroupInfo is not null)
        {
            res = TextureGroupInfo.IndexOfName(name, ignoreCase);
            if (res >= 0) return res;
        }

        return -1;
    }

    /// <summary>
    /// Reports whether the data file was built by GameMaker Studio 2.
    /// </summary>
    /// <returns><see langword="true"/> if yes, <see langword="false"/> if not.</returns>
    public bool IsGameMaker2()
    {
        return IsVersionAtLeast(2);
    }


    // Old Versions: https://store.yoyogames.com/downloads/gm-studio/release-notes-studio-old.html
    // https://web.archive.org/web/20150304025626/https://store.yoyogames.com/downloads/gm-studio/release-notes-studio.html
    // Early Access: https://web.archive.org/web/20181002232646/http://store.yoyogames.com:80/downloads/gm-studio-ea/release-notes-studio.html
    private bool TestGMS1Version(uint stableBuild, uint betaBuild, bool allowGMS2 = false)
    {
        return (allowGMS2 || !IsGameMaker2()) && (IsVersionAtLeast(1, 0, 0, stableBuild) || IsVersionAtLeast(1, 0, 0, betaBuild) && !IsVersionAtLeast(1, 0, 0, 1000));
    }

    /// <summary>
    /// Name of the chunk being read right now, recorded as the source of any version it raises.
    /// </summary>
    internal string ReadingChunkName { get; set; }

    private static void VerifyMajorGMS2Version(uint major)
    {
        if (major is not 2 and < 2022)
            throw new NotSupportedException("Attempted to set a version of GameMaker " + major + " using SetGMS2Version");
    }

    /// <summary>
    /// Sets the GameMaker runtime version in GeneralInfo.
    /// </summary>
    public void SetGMS2Version(GameMakerGeneralInfo.RuntimeVersion version)
    {
        VerifyMajorGMS2Version(version.Major);
        GeneralInfo.Version = version;
        GeneralInfo.VersionSource = ReadingChunkName;
        if (BuiltinList is not null)
            BuiltinList = new BuiltinList(this);
    }

    /// <summary>
    /// Sets the GMS2+ version flag in GeneralInfo.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="release">The release version.</param>
    /// <param name="build">The build version.</param>
    /// <param name="isLTS">If included, alter the data branch between LTS and non-LTS.</param>
    public void SetGMS2Version(uint major, uint minor = 0, uint release = 0, uint build = 0, bool? isLTS = null)
    {
        VerifyMajorGMS2Version(major);

        GeneralInfo.Major = major;
        GeneralInfo.Minor = minor;
        GeneralInfo.Release = release;
        GeneralInfo.Build = build;
        GeneralInfo.VersionSource = ReadingChunkName;

        if (isLTS is not null)
        {
            SetLTS((bool)isLTS);
        }

        if (BuiltinList is not null)
            BuiltinList = new BuiltinList(this);
    }

    /// <summary>
    /// Sets the branch type in GeneralInfo to the appropriate LTS or non-LTS version based on
    /// </summary>
    /// <param name="isLTS">If included, alter the data branch between LTS and non-LTS.</param>
    public void SetLTS(bool isLTS)
    {
        GeneralInfo.Branch = isLTS ? GameMakerGeneralInfo.BranchType.LTS2022_0 : GameMakerGeneralInfo.BranchType.Post2022_0;
    }

    /// <summary>
    /// Reports whether the version of the data file is the same or higher than a specified version.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="release">The release version.</param>
    /// <param name="build">The build version.</param>
    /// <returns>Whether the version of the data file is the same or higher than a specified version.</returns>
    public bool IsVersionAtLeast(uint major, uint minor = 0, uint release = 0, uint build = 0)
    {
        if (GeneralInfo is null)
        {
            Debug.WriteLine("\"GameMakerData.IsVersionAtLeast()\" error - \"GeneralInfo\" is null.");
            return false;
        }

        if (GeneralInfo.Major != major)
            return GeneralInfo.Major > major;

        if (GeneralInfo.Minor != minor)
            return GeneralInfo.Minor > minor;

        if (GeneralInfo.Release != release)
            return GeneralInfo.Release > release;

        if (GeneralInfo.Build != build)
            return GeneralInfo.Build > build;

        return true; // The version is exactly what supplied.
    }

    /// <summary>
    /// Reports whether the data file is the same or newer than a runtime version.
    /// </summary>
    public bool IsVersionAtLeast(GameMakerGeneralInfo.RuntimeVersion version) => IsVersionAtLeast(version.Major, version.Minor, version.Release, version.Build);

    /// <summary>
    /// Reports whether the version of the data file is the same or higher than a specified version, and off the LTS branch that lacks some features.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="release">The release version.</param>
    /// <param name="build">The build version.</param>
    /// <returns>Whether the version of the data file is the same or higher than a specified version. Always false for LTS.</returns>
    public bool IsNonLTSVersionAtLeast(uint major, uint minor = 0, uint release = 0, uint build = 0)
    {
        if (GeneralInfo is null)
        {
            Debug.WriteLine("\"GameMakerData.IsNonLTSVersionAtLeast()\" error - \"GeneralInfo\" is null.");
            return false;
        }

        if (GeneralInfo.Branch < GameMakerGeneralInfo.BranchType.Post2022_0)
            return false;

        return IsVersionAtLeast(major, minor, release, build);
    }

    /// <summary>
    /// Reports whether the data file is the same or newer than a non-LTS runtime version.
    /// </summary>
    public bool IsNonLTSVersionAtLeast(GameMakerGeneralInfo.RuntimeVersion version) => IsNonLTSVersionAtLeast(version.Major, version.Minor, version.Release, version.Build);

    /// <summary>
    /// Reports whether the data file is explicitly on an LTS branch.
    /// </summary>
    public bool IsLTSBranch()
    {
        if (GeneralInfo is null)
        {
            Debug.WriteLine("\"GameMakerData.IsLTSBranch()\" error - \"GeneralInfo\" is null.");
            return false;
        }

        return GeneralInfo.Branch == GameMakerGeneralInfo.BranchType.LTS2022_0;
    }

    /// <summary>
    /// Returns the ID of the builtin (i.e., embedded in the main data file) audio group. Varies depending on version.
    /// </summary>
    /// <returns>ID of the builtin audio group; 0 or 1.</returns>
    public int GetBuiltinSoundGroupID()
    {
        // It is known it works this way in 1.0.1266. The exact version which changed this is unknown.
        // If we find a game which does not fit the version identified here, we should fix this check.
        return TestGMS1Version(1250, 161, true) ? 0 : 1;
    }

    /// <summary>
    /// Reports whether the data file was compiled with YYC.
    /// </summary>
    /// <returns><see langword="true"/> if yes, <see langword="false"/> if not.</returns>
    public bool IsYYC()
    {
        return GeneralInfo != null && Code == null;
    }

    /// <summary>
    /// Gets the next available extension function ID.
    /// </summary>
    /// <returns>One greater than the greatest registered extension function ID.</returns>
    public uint ExtensionFindLastId()
    {
        // The reason:
        // Extension function id is literally the index of it in the Runner internal lists
        // It must never overlap
        // So, a good helper is needed.

        uint id = 1; // first Id is always one, I checked.
        foreach (var extn in Extensions ?? Enumerable.Empty<GameMakerExtension>())
        {
            if (extn?.Files is null)
                continue;

            foreach (var file in extn.Files)
            {
                if (file?.Functions is null)
                    continue;

                foreach (var func in file.Functions)
                {
                    if (func is not null && func.ID > id)
                    {
                        id = func.ID;
                    }
                }
            }
        }

        id++; // last id that *we* can use, so increment by one.
        return id;
    }

    /// <summary>
    /// Creates a new empty data file.
    /// </summary>
    /// <returns>The newly created data file.</returns>
    public static GameMakerData CreateNew()
    {
        return CreateNew(false);
    }

    /// <summary>
    /// Creates a new empty data file compatible with the GameMaker 2026.1 LTS format.
    /// </summary>
    /// <returns>The newly created data file.</returns>
    public static GameMakerData CreateNew2026LTS()
    {
        return CreateNew(true);
    }

    private static GameMakerData CreateNew(bool latestLts)
    {
        var data = new GameMakerData
        {
            FORM = new GameMakerChunkFORM()
        };
        data.FORM.Chunks["GEN8"] = new GameMakerChunkGEN8();
        data.FORM.Chunks["OPTN"] = new GameMakerChunkOPTN();
        data.FORM.Chunks["LANG"] = new GameMakerChunkLANG();
        data.FORM.Chunks["EXTN"] = new GameMakerChunkEXTN();
        data.FORM.Chunks["SOND"] = new GameMakerChunkSOND();
        data.FORM.Chunks["AGRP"] = new GameMakerChunkAGRP();
        data.FORM.Chunks["SPRT"] = new GameMakerChunkSPRT();
        data.FORM.Chunks["BGND"] = new GameMakerChunkBGND();
        data.FORM.Chunks["PATH"] = new GameMakerChunkPATH();
        data.FORM.Chunks["SCPT"] = new GameMakerChunkSCPT();
        data.FORM.Chunks["GLOB"] = new GameMakerChunkGLOB();
        data.FORM.Chunks["SHDR"] = new GameMakerChunkSHDR();
        data.FORM.Chunks["FONT"] = new GameMakerChunkFONT();
        data.FORM.Chunks["TMLN"] = new GameMakerChunkTMLN();
        data.FORM.Chunks["OBJT"] = new GameMakerChunkOBJT();
        if (latestLts)
        {
            data.FORM.Chunks["FEDS"] = new GameMakerChunkFEDS();
            data.FORM.Chunks["ACRV"] = new GameMakerChunkACRV();
            data.FORM.Chunks["SEQN"] = new GameMakerChunkSEQN();
            data.FORM.Chunks["TAGS"] = new GameMakerChunkTAGS
            {
                Object = new GameMakerTags
                {
                    Tags = [],
                    AssetTags = []
                }
            };
        }
        data.FORM.Chunks["ROOM"] = new GameMakerChunkROOM();
        if (latestLts)
            data.FORM.Chunks["UILR"] = new GameMakerChunkUILR();
        data.FORM.Chunks["DAFL"] = new GameMakerChunkDAFL();
        if (latestLts)
        {
            data.FORM.Chunks["EMBI"] = new GameMakerChunkEMBI();
            data.FORM.Chunks["PSEM"] = new GameMakerChunkPSEM();
            data.FORM.Chunks["PSYS"] = new GameMakerChunkPSYS();
        }
        data.FORM.Chunks["TPAG"] = new GameMakerChunkTPAG();
        if (latestLts)
            data.FORM.Chunks["TGIN"] = new GameMakerChunkTGIN();
        data.FORM.Chunks["CODE"] = new GameMakerChunkCODE();
        data.FORM.Chunks["VARI"] = new GameMakerChunkVARI();
        data.FORM.Chunks["FUNC"] = new GameMakerChunkFUNC();
        if (latestLts)
        {
            data.FORM.Chunks["FEAT"] = new GameMakerChunkFEAT
            {
                Object = new GameMakerFeatureFlags { List = [] }
            };
        }
        data.FORM.Chunks["STRG"] = new GameMakerChunkSTRG();
        data.FORM.Chunks["TXTR"] = new GameMakerChunkTXTR();
        data.FORM.Chunks["AUDO"] = new GameMakerChunkAUDO();
        foreach (var chunk in data.FORM.Chunks.Values)
            data.FORM.ChunksTypeDict[chunk.GetType()] = chunk;
        data.FORM.GEN8.Object = new GameMakerGeneralInfo();
        data.FORM.OPTN.Object = new GameMakerOptions();
        data.FORM.LANG.Object = new GameMakerLanguage();
        data.GeneralInfo.FileName = data.Strings.MakeString("NewGame");
        data.GeneralInfo.Config = data.Strings.MakeString("Default");
        data.GeneralInfo.Name = data.Strings.MakeString("NewGame");
        data.GeneralInfo.DisplayName = data.Strings.MakeString("New GameMaker Game");
        data.GeneralInfo.GameID = (uint)RandomNumberGenerator.GetInt32(int.MaxValue);
        data.GeneralInfo.Timestamp = (uint)new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
        data.Options.Constants.Add(new GameMakerOptions.Constant() { Name = data.Strings.MakeString("@@SleepMargin"), Value = data.Strings.MakeString(1.ToString(CultureInfo.InvariantCulture)) });
        data.Options.Constants.Add(new GameMakerOptions.Constant() { Name = data.Strings.MakeString("@@DrawColour"), Value = data.Strings.MakeString(0xFFFFFFFF.ToString(CultureInfo.InvariantCulture)) });
        data.Rooms.Add(new GameMakerRoom() { Name = data.Strings.MakeString("room0"), Caption = data.Strings.MakeString("") });
        if (latestLts)
            data.SetGMS2Version(2026, 1, isLTS: true);
        data.BuiltinList = new BuiltinList(data);
        Decompiler.GameSpecificResolver.Initialize(data);
        return data;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        var disposableType = typeof(IDisposable);
        var dataProperties = GetType().GetProperties();
        var dataDisposableProps = dataProperties.Except(AllListProperties)
                                                .Where(x => disposableType.IsAssignableFrom(x.PropertyType));

        // Dispose disposable properties
        foreach (var disposableProp in dataDisposableProps)
        {
            // If property is null
            if (disposableProp.GetValue(this) is not IDisposable disposable)
                continue;

            disposable.Dispose();
        }

        // Clear all object lists (sprites, code, etc.)
        foreach (var dataListProperty in AllListProperties)
        {
            // If it's an indexer property
            if (dataListProperty.Name == "Item")
                continue;

            // If list is null
            if (dataListProperty.GetValue(this) is not IList list)
                continue;

            // If list elements are disposable
            if (disposableType.IsAssignableFrom(list.GetType().GetGenericArguments()[0]))
            {
                foreach (IDisposable disposable in list)
                    disposable?.Dispose();
            }

            list.Clear();
        }

        // Clear other references
        FORM = null;
        GlobalFunctions = null;
        GameSpecificRegistry = null;
    }
}

/// <summary>
/// An info handle for an editor to store data on.
/// </summary>
public class ToolInfo
{
    /// <summary>
    /// Default settings to be used by the G3MLib.Analyzer decompiler,
    /// for a tool and in any scripts that desire matching the same settings.
    /// </summary>
    public IDecompileSettings DecompilerSettings { get; set; } = new DecompileSettings();

    /// <summary>
    /// Function that returns the prefix to be used when
    /// resolving instance ID references in the compiler and decompiler.
    /// </summary>
    public Func<string> InstanceIdPrefix { get; set; } = () => "inst_";
}

public static class ListExtensions
{
    /// <summary>
    /// Returns all non-null items from a list.
    /// </summary>
    public static IEnumerable<T> SkipNullItems<T>(this IList<T> list)
    {
        return list.Where(item => item is not null);
    }
}
