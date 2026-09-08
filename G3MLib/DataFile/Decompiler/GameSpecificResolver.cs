using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Decompiler;

/// <summary>
/// Class that helps load game-specific data, which tailor some features (such as the decompiler) to specific games.
/// </summary>
public class GameSpecificResolver
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Base app directory used for locating the "GameSpecificData" directory, which should be immediately inside.
    /// </summary>
    public static string BaseDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Assembly containing embedded game-specific data. When set, definitions and analyzer data are read from it instead of disk.
    /// </summary>
    public static Assembly? EmbeddedDataAssembly { get; set; }

    private enum ConditionResult
    {
        Ignore,
        Accept,
        Reject
    }

    private static readonly Dictionary<string, Func<GameMakerData, string, ConditionResult>> _conditionEvaluators = new()
    {
        ["Always"] = (GameMakerData _, string _) =>
        {
            return ConditionResult.Accept;
        },

        ["Name.Regex"] = (GameMakerData data, string value) =>
        {
            string name = data?.GeneralInfo?.Name?.Content;
            if (name is null)
            {
                return ConditionResult.Ignore;
            }

            return Regex.IsMatch(name, value, RegexOptions.CultureInvariant, RegexTimeout)
                ? ConditionResult.Accept
                : ConditionResult.Ignore;
        },

        ["DisplayName.Regex"] = (GameMakerData data, string value) =>
        {
            string displayName = data?.GeneralInfo?.DisplayName?.Content;
            if (displayName is null)
            {
                return ConditionResult.Ignore;
            }

            Match m = Regex.Match(displayName, value, RegexOptions.CultureInvariant, RegexTimeout);
            if (m.Success)
            {
                return ConditionResult.Accept;
            }

            return ConditionResult.Ignore;
        },
    };
    private static readonly Lock _lock = new();
    private static readonly List<GameSpecificDefinition> _definitions = new();
    private static bool _loadedDefinitions = false;

    public class GameSpecificCondition
    {
        /// <summary>
        /// Represents the kind of condition to be evaluated.
        /// </summary>
        public string ConditionKind { get; set; }

        /// <summary>
        /// Value to be used during evaluation of condition, if applicable.
        /// </summary>
        public string Value { get; set; }
    }

    public class GameSpecificDefinition
    {
        /// <summary>
        /// Integer representing the order this definition should be evaluated/loaded in.
        /// The lower this number is, the earlier this definition will be evaluated and loaded.
        /// </summary>
        /// <remarks>
        /// Built-in definitions currently use values 0 (for GameMaker builtins) and 1 (for games).
        /// </remarks>
        public int LoadOrder { get; set; } = 100;

        /// <summary>
        /// List of conditions that will be evaluated sequentially, to match this game-specific definition.
        /// </summary>
        public List<GameSpecificCondition> Conditions { get; set; }

        /// <summary>
        /// Filename to be loaded as an G3MLib.Analyzer game-specific config, when this definition successfully matches.
        /// If empty, null, or the file is otherwise nonexistent, this will be ignored.
        /// </summary>
        public string AnalyzerFilename { get; set; }

        /// <summary>
        /// Evaluates this game-specific definition against the given game data, returning whether this definition should be loaded.
        /// </summary>
        /// <returns>True if this definition should be loaded; false otherwise.</returns>
        public bool Evaluate(GameMakerData data)
        {
            if (Conditions is null)
            {
                return false;
            }

            foreach (var condition in Conditions)
            {
                if (condition is null || condition.ConditionKind is null ||
                    !_conditionEvaluators.TryGetValue(condition.ConditionKind, out var evaluator))
                {
                    return false;
                }

                switch (evaluator(data, condition.Value))
                {
                    case ConditionResult.Accept:
                        return true;
                    case ConditionResult.Reject:
                        return false;
                    case ConditionResult.Ignore:
                        // Pass-through to next condition
                        break;
                }
            }

            // Default to reject
            return false;
        }

        /// <summary>
        /// Loads this game-specific definition.
        /// </summary>
        public void Load(GameMakerData data)
        {
            if (!string.IsNullOrEmpty(AnalyzerFilename))
            {
                string? analyzerData = ReadGameSpecificDataFile("Analyzer", AnalyzerFilename);
                if (analyzerData is not null)
                {
                    data.GameSpecificRegistry.DeserializeFromJson(analyzerData);
                }
            }
        }
    }

    /// <summary>
    /// Forces a full reload of all game-specific definition files.
    /// </summary>
    public static void ReloadDefinitions()
    {
        lock (_lock)
        {
            // Mark definitions as loaded, and reset existing definitions
            _loadedDefinitions = true;
            _definitions.Clear();

            foreach (string definition in ReadGameSpecificDefinitionFiles())
            {
                _definitions.Add(JsonSerializer.Deserialize<GameSpecificDefinition>(definition));
            }

            // Sort all definitions by their load order
            _definitions.Sort((a, b) => a.LoadOrder - b.LoadOrder);
        }
    }

    /// <summary>
    /// Loads game-specific definitions, if not loaded already. Call <see cref="ReloadDefinitions"/> to force a full reload.
    /// </summary>
    public static void LoadDefinitions()
    {
        bool loadedDefinitions;
        lock (_lock)
        {
            loadedDefinitions = _loadedDefinitions;
        }
        if (!loadedDefinitions)
        {
            ReloadDefinitions();
        }
    }

    /// <summary>
    /// Initializes the registry of game-specific data for the given game.
    /// </summary>
    public static void Initialize(GameMakerData data)
    {
        // Ensure all definitions are loaded
        LoadDefinitions();

        // Initialize empty game-specific registry for decompiler
        data.GameSpecificRegistry = new();

        // Evaluate all definitions, and load all successful ones
        lock (_lock)
        {
            foreach (var definition in _definitions.Where(definition => definition.Evaluate(data)))
                definition.Load(data);
        }
    }

    private static string? ReadGameSpecificDataFile(string directory, string fileName)
    {
        if (EmbeddedDataAssembly is Assembly assembly)
        {
            return ReadEmbeddedDataFile(assembly, directory, fileName);
        }

        string dataDirectory = Path.Join(BaseDirectory, "GameSpecificData", directory);
        string? dataPath = Paths.TryJoinVerifyWithinDirectory(dataDirectory, fileName);
        return dataPath is not null && File.Exists(dataPath)
            ? File.ReadAllText(dataPath)
            : ReadEmbeddedDataFile(typeof(GameSpecificResolver).Assembly, directory, fileName);
    }

    private static IEnumerable<string> ReadGameSpecificDefinitionFiles()
    {
        if (EmbeddedDataAssembly is Assembly assembly)
        {
            foreach (string definition in ReadEmbeddedDefinitionFiles(assembly))
                yield return definition;
            yield break;
        }

        string directory = Path.Join(BaseDirectory, "GameSpecificData", "Definitions");
        if (!Directory.Exists(directory))
        {
            foreach (string definition in ReadEmbeddedDefinitionFiles(typeof(GameSpecificResolver).Assembly))
                yield return definition;
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            yield return File.ReadAllText(file);
        }
    }

    private static string? ReadEmbeddedDataFile(Assembly assembly, string directory, string fileName)
    {
        string resourceSuffix = ".GameSpecificData." + directory + "." + fileName.Replace('/', '.').Replace('\\', '.');
        string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));
        return resourceName is null ? null : ReadEmbeddedResource(assembly, resourceName);
    }

    private static IEnumerable<string> ReadEmbeddedDefinitionFiles(Assembly assembly)
    {
        const string definitionPrefix = ".GameSpecificData.Definitions.";
        foreach (string resourceName in assembly.GetManifestResourceNames().Where(name => name.Contains(definitionPrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)))
            yield return ReadEmbeddedResource(assembly, resourceName);
    }

    private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
