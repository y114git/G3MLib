using G3MLib.Analyzer;
using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile.Compiler;

/// <summary>
/// Context for managing groups of GML code compilation operations.
/// </summary>
public sealed class CompileGroup
{
    /// <summary>
    /// Associated game data for this compile context.
    /// </summary>
    public GameMakerData Data { get; }

    /// <summary>
    /// Associated global decompile context for this compile context.
    /// </summary>
    public GlobalDecompileContext GlobalContext { get; }

    /// <summary>
    /// This action will be called when main-thread operations should occur, and can be changed.
    /// </summary>
    /// <remarks>
    /// When the action is called, supplied action argument *must* be executed synchronously.
    /// That is, the action must return only after the argument callback has completed execution.
    /// </remarks>
    public Action<Action> MainThreadAction { get; set; } = static (f) => f();

    /// <summary>
    /// Whether certain linking lookups (for strings, assets, etc.) should persist between multiple compiles.
    /// This should not be preferred: generally, multiple code operations should be queued instead.
    /// </summary>
    /// <remarks>
    /// This should be used with care. When enabled, no game data should be modified between each compile; otherwise,
    /// string or asset duplication may occur, which can corrupt the data permanently.
    /// </remarks>
    public bool PersistLinkingLookups { get; set; }

    /// <summary>
    /// Hash code of the name of the current code entry being compiled.
    /// </summary>
    internal int CurrentCodeEntryNameHash { get; private set; }

    /// <summary>
    /// During main compile (not linking), this is the next try variable index that should be used.
    /// </summary>
    internal int NextTryVariableIndex { get; set; }

    /// <summary>
    /// Stores a code entry and corresponding GML code to be compiled during an operation.
    /// </summary>
    private readonly record struct QueuedOperation(GameMakerCode CodeEntry, string Code);

    /// <summary>
    /// Stores a code entry, script, and function for a newly-created child code entry during linking.
    /// </summary>
    /// <remarks>
    /// Each component (code, script, and function) may individually be <see langword="null"/>.
    /// </remarks>
    private readonly record struct ChildCodeEntryData(
        string Name, FunctionEntry FunctionEntry,
        GameMakerCode Code, GameMakerScript Script, GameMakerFunction Function,
        bool ExistingCode, bool ExistingScript, bool ExistingFunction);

    // Queued list of code to replace the contents of
    private List<QueuedOperation>? _queuedCodeReplacements;

    // During linking, a lookup of string contents to string IDs.
    private Dictionary<string, int>? _linkingStringIdLookup;

    // During linking, a lookup of function names to functions.
    private Dictionary<string, GameMakerFunction>? _linkingFunctionLookup;

    // During linking, a lookup of script names to scripts.
    private Dictionary<string, List<GameMakerScript>>? _linkingScriptLookup;

    // During linking, a unique number to use for struct variables.
    private int _linkingStructCounter = -1;

    // During linking, a lookup of all variable names that have been patched so far,
    // to the index they appear in the order of variable patches.
    private Dictionary<string, int>? _linkingVariableOrderLookup;

    // During linking, a list of all variable names that have been patched so far,
    // along with whether they have ever been used as a local variable.
    private List<(string Name, bool IsEverLocal)> _linkingVariableOrder;

    // During linking, a lookup of local variable names to references of those local variables.
    private Dictionary<string, List<GameMakerInstruction>>? _linkingLocalReferences;

    // During main compile (not linking), and when array copy-on-write is enabled, this is a lookup of name to ID.
    private int _nextNameId = 100000;
    private Dictionary<string, int>? _parsedNameIds;

    /// <summary>
    /// Initializes a new compile context.
    /// </summary>
    /// <remarks>
    /// If <paramref name="globalContext"/> is not provided, a new global context will be created.
    /// </remarks>
    public CompileGroup(GameMakerData data, GlobalDecompileContext? globalContext = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        Data = data;
        GlobalContext = globalContext ?? new(data);
    }

    /// <summary>
    /// Queues a code replace operation on this compile context.
    /// </summary>
    /// <param name="codeToModify">Existing (root) code entry to modify.</param>
    /// <param name="gmlCode">GML source code to compile as replacement code.</param>
    public void QueueCodeReplace(GameMakerCode codeToModify, string gmlCode)
    {
        ArgumentNullException.ThrowIfNull(codeToModify);
        ArgumentNullException.ThrowIfNull(gmlCode);

        _queuedCodeReplacements ??= [];
        _queuedCodeReplacements.Add(new(codeToModify, gmlCode));
    }

    /// <summary>
    /// Looks up a string ID during linking.
    /// </summary>
    /// <returns><see langword="true"/> if an ID was successfully found; <see langword="false"/> otherwise.</returns>
    internal bool LookupStringId(string contents, out int id)
    {
        return _linkingStringIdLookup.TryGetValue(contents, out id);
    }

    /// <summary>
    /// Looks up a string, or creates one, during linking.
    /// </summary>
    internal GameMakerString MakeString(string contents)
    {
        // Try to look up existing string first
        if (_linkingStringIdLookup.TryGetValue(contents, out var id))
        {
            return Data.Strings[id];
        }

        // Create new string, and update lookup
        GameMakerString str = new(contents);
        var strIndex = Data.Strings.Count;
        _linkingStringIdLookup[contents] = strIndex;
        Data.Strings.Add(str);
        return str;
    }

    /// <summary>
    /// Looks up a string, or creates one, during linking. Also outputs the ID of the string.
    /// </summary>
    internal GameMakerString MakeString(string contents, out int id)
    {
        // Try to look up existing string first
        if (_linkingStringIdLookup.TryGetValue(contents, out id))
        {
            return Data.Strings[id];
        }

        // Create new string, and update lookup
        GameMakerString str = new(contents);
        id = Data.Strings.Count;
        _linkingStringIdLookup[contents] = id;
        Data.Strings.Add(str);
        return str;
    }

    /// <summary>
    /// Registers a non-local variable during linking.
    /// </summary>
    internal void RegisterNonLocalVariable(string name)
    {
        if (!_linkingVariableOrderLookup.ContainsKey(name))
        {
            // Add to variable order list, but not as a local
            _linkingVariableOrderLookup.Add(name, _linkingVariableOrder.Count);
            _linkingVariableOrder.Add((name, false));
        }
    }

    /// <summary>
    /// Registers a local variable during linking, queuing the reference to be patched later.
    /// </summary>
    internal void RegisterLocalVariable(GameMakerInstruction reference, string name)
    {
        // Queue reference to be patched later
        if (!_linkingLocalReferences.TryGetValue(name, out var referenceList))
        {
            referenceList = new(16);
            _linkingLocalReferences[name] = referenceList;
        }
        referenceList.Add(reference);

        // Update variable order
        if (_linkingVariableOrderLookup.TryGetValue(name, out var existingIndex))
        {
            // If not already marked as "ever local," mark it as such now
            if (!_linkingVariableOrder[existingIndex].IsEverLocal)
            {
                _linkingVariableOrder[existingIndex] = (name, true);
            }
        }
        else
        {
            // Add to variable order list as a local
            _linkingVariableOrderLookup.Add(name, _linkingVariableOrder.Count);
            _linkingVariableOrder.Add((name, true));
        }
    }

    /// <summary>
    /// Registers a name token during initial main compile/parse, and returns its ID.
    /// </summary>
    internal int RegisterName(string name)
    {
        if (_parsedNameIds.TryGetValue(name, out var id))
        {
            return id;
        }
        return _parsedNameIds[name] = _nextNameId++;
    }

    /// <summary>
    /// Defines a single code local variable.
    /// </summary>
    private void DefineCodeLocal(GameMakerCodeLocals locals, bool linkingModern, string name, uint currentLocalIndex)
    {
        var str = MakeString(name, out var stringId);
        if (linkingModern)
        {
            // In 2.3+, local variables use variable ID rather than local index
            locals.Locals.Add(new GameMakerCodeLocals.LocalVar()
            {
                Index = (uint)stringId, // variable ID is based on string index
                Name = str
            });
        }
        else
        {
            // Prior to 2.3, local variables simply use local index
            locals.Locals.Add(new GameMakerCodeLocals.LocalVar()
            {
                Index = currentLocalIndex,
                Name = str
            });
        }
    }

    /// <summary>
    /// Returns whether the two code entry names are similar (that is, the original code entry
    /// name is identical aside from some numbers that the new name does not exactly match).
    /// </summary>
    private static bool SimilarCodeEntryNames(string originalName, string newNameNoNumbers)
    {
        var originalPos = 0;
        for (var i = 0; i < newNameNoNumbers.Length; i++)
        {
            if (newNameNoNumbers[i] == '%')
            {
                // Special control character: read any amount of digits from original name
                while (originalPos < originalName.Length && char.IsAsciiDigit(originalName[originalPos]))
                {
                    originalPos++;
                }
                continue;
            }
            if (newNameNoNumbers[i] == '#')
            {
                // Special control character: read any amount of hex digits from original name
                while (originalPos < originalName.Length && char.IsAsciiHexDigitUpper(originalName[originalPos]))
                {
                    originalPos++;
                }
                continue;
            }
            if (originalPos >= originalName.Length)
            {
                // New name is too long
                return false;
            }
            if (originalName[originalPos] != newNameNoNumbers[i])
            {
                // New name doesn't match somewhere other than through numbers
                return false;
            }
            originalPos++;
        }
        if (originalPos != originalName.Length)
        {
            // New name is too short
            return false;
        }

        // Everything seems to be similar enough!
        return true;
    }

    /// <summary>
    /// After verifying that an original code entry name and a new code entry name are similar,
    /// this method can be used to find the original short name from the original code entry name,
    /// being supplied the new short name without numbers.
    /// </summary>
    private static string FindOriginalShortName(string originalName, string newShortNameNoNumbers)
    {
        // Start reading after prefix
        var startOriginalPos = "gml_Script_".Length;
        var originalPos = startOriginalPos;
        for (var i = 0; i < newShortNameNoNumbers.Length; i++)
        {
            if (newShortNameNoNumbers[i] == '%')
            {
                // Special control character: read any amount of digits from original name
                while (originalPos < originalName.Length && char.IsAsciiDigit(originalName[originalPos]))
                {
                    originalPos++;
                }
                continue;
            }
            if (newShortNameNoNumbers[i] == '#')
            {
                // Special control character: read any amount of hex digits from original name
                while (originalPos < originalName.Length && char.IsAsciiHexDigitUpper(originalName[originalPos]))
                {
                    originalPos++;
                }
                continue;
            }
            if (originalPos < originalName.Length)
            {
                originalPos++;
            }
        }

        // Now, originalPos is placed at the end of the short name. Return substring from that location!
        return originalName[startOriginalPos..originalPos];
    }

    // Helper during linking, to track data correctly.
    private sealed record CodeEntryNameGroup
    {
        public Queue<GameMakerCode> RemainingOriginalEntries { get; init; }
        public int EntriesUsed { get; set; }

        public CodeEntryNameGroup(Queue<GameMakerCode> originalEntries, int entriesUsed)
        {
            RemainingOriginalEntries = originalEntries;
            EntriesUsed = entriesUsed;
        }
    }

    /// <summary>
    /// Performs all compilation operations that have been queued on this context.
    /// </summary>
    /// <remarks>
    /// Code replacement operations will be performed first, followed by code append operations.
    /// </remarks>
    public CompileResult Compile() => CompileCore(false, default);

    /// <summary>
    /// Compiles everything that has been queued without linking: syntax and semantic errors are found, and nothing in the game data changes.
    /// </summary>
    /// <remarks>
    /// The queue is emptied. Cancelling throws <see cref="OperationCanceledException"/>; nothing needs to be discarded.
    /// </remarks>
    public CompileResult Check(CancellationToken cancellationToken = default) => CompileCore(true, cancellationToken);

    /// <summary>
    /// Like <see cref="Compile()"/> with a cancel point before each operation. After a cancel the game data may hold part of the operations, so discard it.
    /// </summary>
    public CompileResult Compile(CancellationToken cancellationToken) => CompileCore(false, cancellationToken);

    private CompileResult CompileCore(bool dryRun, CancellationToken cancellationToken)
    {
        try
        {
            return CompileBody(dryRun, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            GlobalContext.CurrentCompileGroup = null;
            _queuedCodeReplacements?.Clear();
            UnreferenceLinkingLookups();
            throw;
        }
    }

    private CompileResult CompileBody(bool dryRun, CancellationToken cancellationToken)
    {
        // Ensure global context is prepared for compilation
        GlobalContext.PrepareForCompilation(!PersistLinkingLookups);

        // List to use for any errors generated, but don't allocate memory unless needed
        List<CompileError>? errors = null;

        // Version checks:
        // In 2.3 and above, new processes are used for linking in general.
        // In 2023.11 and above, a new naming process is used for code entries.
        // This was updated a bit in 2024.2 and 2024.4 as well.
        var linkingModern = Data.IsVersionAtLeast(2, 3);
        var newNamingProcess = Data.IsVersionAtLeast(2023, 11);
        var staticAnonymousNames = Data.IsVersionAtLeast(2024, 2);
        var childFunctionNameFix = Data.IsVersionAtLeast(2024, 4);

        // Mark this group as compiling
        GlobalContext.CurrentCompileGroup = this;

        // If queue hasn't been created yet, create an empty one
        _queuedCodeReplacements ??= [];

        // If global scripts are present in the replacement queue, parse their code in advance to collect global functions
        List<CompileContext>? globalScriptContexts = null;
        foreach (var operation in _queuedCodeReplacements)
        {
            // Guess script kind and global script name, based on code entry name
            (var scriptKind, var globalScriptName) = GuessScriptKindFromName(operation.CodeEntry.Name?.Content);

            // If not a global script, ignore
            if (scriptKind is not CompileScriptKind.GlobalScript)
            {
                continue;
            }

            // Create a compile context early
            CompileContext context = new(operation.Code, scriptKind, globalScriptName, GlobalContext);

            // Perform parse
            try
            {
                context.Parse();

                // Check for compile errors
                if (context.HasErrors)
                {
                    errors ??= new(context.Errors.Count);
                    foreach (var error in context.Errors)
                    {
                        errors.Add(new CompileError(operation.CodeEntry, error));
                    }
                }
            }
            catch (Exception e)
            {
                // Exception thrown; make compile error for it
                errors ??= [];
                errors.Add(new CompileError(operation.CodeEntry, e));
            }

            // Add context to global script context list, for reuse
            globalScriptContexts ??= [];
            globalScriptContexts.Add(context);
        }

        // Define global functions, if applicable
        List<(string Name, IGMFunction NewFunction, IGMFunction OldFunction)> newlyDefinedGlobalFunctions = null;
        if (globalScriptContexts is not null && !dryRun)
        {
            // Perform linking on main thread
            MainThreadAction(() =>
            {
                // Setup for linking
                InitializeLinkingLookups();

                // Iterate over all global script compile contexts, to find functions
                foreach (var context in globalScriptContexts)
                {
                    if (context.OutputGlobalFunctionNames is null)
                    {
                        continue;
                    }
                    foreach (var functionName in context.OutputGlobalFunctionNames)
                    {
                        // Get old function entry, if one exists (in case this needs to be rolled back due to an error)
                        Data.GlobalFunctions.TryGetFunction(functionName, out var oldFunctionEntry);

                        // Define function using standard naming convention
                        var functionEntryName = $"gml_Script_{functionName}";
                        if (!_linkingFunctionLookup.TryGetValue(functionEntryName, out var newFunctionEntry))
                        {
                            // Create a new function entry; will be added to game data later
                            newFunctionEntry = new()
                            {
                                Name = MakeString(functionEntryName, out var id),
                                NameStringID = id
                            };
                        }
                        Data.GlobalFunctions.DefineFunction(functionName, newFunctionEntry);
                        newlyDefinedGlobalFunctions ??= [];
                        newlyDefinedGlobalFunctions.Add((functionName, newFunctionEntry, oldFunctionEntry));
                    }
                }
            });
        }

        // Work through replacement queue, for main compilation and linking
        var globalScriptContextIndex = 0;
        foreach (var operation in _queuedCodeReplacements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Guess script kind and global script name, based on code entry name
            (var scriptKind, var globalScriptName) = GuessScriptKindFromName(operation.CodeEntry.Name?.Content);

            // Prepare data for main compile
            CurrentCodeEntryNameHash = (int)MurmurHash.Hash(operation.CodeEntry.Name.Content);
            if (linkingModern)
            {
                NextTryVariableIndex = Math.Max(0, operation.CodeEntry.FindFirstTryLocalIndex());

                if (Data.ArrayCopyOnWrite)
                {
                    _parsedNameIds = [];
                    _nextNameId = 100000;
                }
            }

            // Perform initial compile step
            CompileContext context;
            if (scriptKind is not CompileScriptKind.GlobalScript)
            {
                // Create a brand new compile context
                context = new(operation.Code, scriptKind, globalScriptName, GlobalContext);
            }
            else
            {
                // Reuse compile context from initial global script parse
                context = globalScriptContexts[globalScriptContextIndex];
                globalScriptContextIndex++;
            }
            try
            {
                context.Compile();

                // Check for compile errors
                if (context.HasErrors)
                {
                    errors ??= new(context.Errors.Count);
                    foreach (var error in context.Errors)
                    {
                        errors.Add(new CompileError(operation.CodeEntry, error));
                    }
                }
            }
            catch (Exception e)
            {
                // Exception thrown; make compile error for it (and also get rid of context)
                context = null;
                errors ??= [];
                errors.Add(new CompileError(operation.CodeEntry, e));
            }

            // Un-prepare for array copy-on-write
            if (Data.ArrayCopyOnWrite)
            {
                _parsedNameIds = null;
            }

            // If any errors have occurred in general (even in other operations), don't proceed to linking
            if (errors is not null || dryRun)
            {
                continue;
            }

            // Perform linking on main thread
            MainThreadAction(() =>
            {
                // Setup for linking
                InitializeLinkingLookups();

                // Make list of reusable child code entry names (and set of child entries remaining)
                List<string> originalChildEntryNames = new(operation.CodeEntry.ChildEntries.Count);
                Dictionary<string, CodeEntryNameGroup> remainingChildEntries = new(operation.CodeEntry.ChildEntries.Count);
                foreach (var childEntry in operation.CodeEntry.ChildEntries)
                {
                    var currentChildName = childEntry.Name.Content;
                    originalChildEntryNames.Add(currentChildName);
                    if (remainingChildEntries.TryGetValue(currentChildName, out var existing))
                    {
                        existing.RemainingOriginalEntries.Enqueue(childEntry);
                    }
                    else
                    {
                        Queue<GameMakerCode> newQueue = new();
                        CodeEntryNameGroup newGroup = new(newQueue, 0);
                        newQueue.Enqueue(childEntry);
                        remainingChildEntries[currentChildName] = newGroup;
                    }
                }

                // Resolve function entries. Either pair up with existing child code entries, or make new ones.
                List<ChildCodeEntryData> childDataList = new(context.OutputFunctionEntries.Count);
                HashSet<string> usedChildEntryNames = new(operation.CodeEntry.ChildEntries.Count);
                var rootCodeEntryName = operation.CodeEntry.Name.Content;
                var anonCounter = 0;
                foreach (var functionEntry in context.OutputFunctionEntries)
                {
                    // Determine name of parent function, or null if none exists
                    var parentFunctionName = functionEntry.Parent?.ChildFunctionName;
                    if (scriptKind != CompileScriptKind.GlobalScript)
                    {
                        // Non-global scripts always use root code entry name as a base
                        parentFunctionName ??= rootCodeEntryName;
                    }

                    // Determine short name
                    string shortName, shortNameNoNumbers;
                    if (functionEntry.FunctionName is not null)
                    {
                        // Named function; trivial
                        shortName = functionEntry.FunctionName;
                        if (shortName.StartsWith("gml_Script_", StringComparison.Ordinal))
                        {
                            // Prefabs cause problematic linking behavior unless we strip this prefix
                            shortName = shortName["gml_Script_".Length..];
                        }
                        shortNameNoNumbers = shortName;
                    }
                    else if (functionEntry.Kind == FunctionEntryKind.StructInstantiation)
                    {
                        // Struct instantiation; create name
                        shortName = $"___struct___{_linkingStructCounter}";
                        shortNameNoNumbers = "___struct___%";
                    }
                    else
                    {
                        // Anonymous function; determine first part
                        string anonName;
                        if (staticAnonymousNames && functionEntry.StaticVariableName is not null)
                        {
                            anonName = $"{functionEntry.StaticVariableName}@anon";
                        }
                        else
                        {
                            anonName = "anon";
                        }

                        // Use other arbitrary information to differentiate this function from others
                        if (parentFunctionName is null)
                        {
                            if (newNamingProcess)
                            {
                                var rootWithoutGlobalScript = rootCodeEntryName.Replace("gml_GlobalScript_", "", StringComparison.Ordinal);
                                shortName = $"{anonName}@{anonCounter++}@{rootWithoutGlobalScript}";
                                shortNameNoNumbers = $"{anonName}@%@{rootWithoutGlobalScript}";
                            }
                            else
                            {
                                shortName = $"{anonName}_{operation.Code.GetHashCode(StringComparison.Ordinal):X8}_{anonCounter++}";
                                shortNameNoNumbers = $"{anonName}_#_%";
                            }
                        }
                        else
                        {
                            if (newNamingProcess)
                            {
                                shortName = $"{anonName}@{anonCounter++}";
                                shortNameNoNumbers = $"{anonName}@%";
                            }
                            else
                            {
                                shortName = $"{anonName}_{parentFunctionName}_{anonCounter++}";
                                shortNameNoNumbers = $"{anonName}_{parentFunctionName}_%";
                            }
                        }
                    }

                    // Determine full code entry name
                    string codeEntryName, codeEntryNameNoNumbers;
                    if (parentFunctionName is null)
                    {
                        codeEntryName = $"gml_Script_{shortName}";
                        codeEntryNameNoNumbers = $"gml_Script_{shortNameNoNumbers}";
                    }
                    else
                    {
                        if (newNamingProcess)
                        {
                            var parentWithoutGlobalScript = parentFunctionName.Replace("gml_GlobalScript_", "", StringComparison.Ordinal);
                            codeEntryName = $"gml_Script_{shortName}@{parentWithoutGlobalScript}";
                            codeEntryNameNoNumbers = $"gml_Script_{shortNameNoNumbers}@{parentWithoutGlobalScript}";
                        }
                        else
                        {
                            codeEntryName = $"gml_Script_{shortName}_{parentFunctionName}";
                            codeEntryNameNoNumbers = $"gml_Script_{shortNameNoNumbers}_{parentFunctionName}";
                        }
                    }

                    // Try to pair up with an existing child code entry, or otherwise create a new one
                    GameMakerCode? existingCodeEntry = null, newCodeEntry = null;
                    GameMakerScript? existingScript = null, newScript = null;
                    GameMakerFunction? existingFunction = null, newFunction = null;
                    for (var i = 0; i < originalChildEntryNames.Count; i++)
                    {
                        var originalEntryName = originalChildEntryNames[i];
                        if (remainingChildEntries.TryGetValue(originalEntryName, out var originalCodeEntries) &&
                            SimilarCodeEntryNames(originalEntryName, codeEntryNameNoNumbers))
                        {
                            // Names are similar enough - use this existing child.
                            codeEntryName = originalEntryName;

                            // Try to find the original short name used in the code.
                            shortName = FindOriginalShortName(originalEntryName, shortNameNoNumbers);

                            // Take existing code entry (guaranteed to be here)
                            existingCodeEntry = newCodeEntry = originalCodeEntries.RemainingOriginalEntries.Dequeue();

                            // Try to take existing script, if available
                            if (_linkingScriptLookup.TryGetValue(originalEntryName, out var scripts) &&
                                originalCodeEntries.EntriesUsed < scripts.Count)
                            {
                                // Pair up with next script index
                                existingScript = newScript = scripts[originalCodeEntries.EntriesUsed];
                            }

                            // Try to take existing function, if available
                            if (!_linkingFunctionLookup.TryGetValue(originalEntryName, out existingFunction))
                            {
                                // If this is a somehow a new global function (corrupt data?), take existing function from earlier
                                if (scriptKind == CompileScriptKind.GlobalScript &&
                                    functionEntry is { DeclaredInRootScope: true, FunctionName: not null } &&
                                    Data.GlobalFunctions.TryGetFunction(functionEntry.FunctionName, out var newGlobalFunction))
                                {
                                    // Don't set existing function, so that this new function will be added to the main data later
                                    newFunction = (GameMakerFunction)newGlobalFunction;
                                }
                            }
                            else
                            {
                                newFunction = existingFunction;
                            }

                            // If all code entries from name group are taken, remove it from remaining lookup
                            if (originalCodeEntries.RemainingOriginalEntries.Count == 0)
                            {
                                remainingChildEntries.Remove(originalEntryName);
                            }

                            // Increment number of original code entries used under this name group
                            originalCodeEntries.EntriesUsed++;
                            break;
                        }
                    }
                    if (existingCodeEntry is null)
                    {
                        // Verify that code entry name isn't used; if it is, as a contingency, add a suffix
                        if (usedChildEntryNames.Contains(codeEntryName))
                        {
                            var suffixId = 0;
                            var baseCodeEntryName = codeEntryName;
                            do
                            {
                                codeEntryName = $"{baseCodeEntryName}_{suffixId++}";
                            }
                            while (usedChildEntryNames.Contains(codeEntryName));
                        }

                        // Need to make a new code entry
                        newCodeEntry = new()
                        {
                            Name = MakeString(codeEntryName),
                            ParentEntry = operation.CodeEntry
                        };
                    }
                    if (existingScript is null)
                    {
                        // Need to make a new script entry
                        newScript = new()
                        {
                            Name = MakeString(codeEntryName),
                            Code = newCodeEntry
                        };
                    }
                    if (existingFunction is null)
                    {
                        // If this is a new global function, take existing function from earlier, if available
                        if (scriptKind == CompileScriptKind.GlobalScript &&
                            functionEntry is { DeclaredInRootScope: true, FunctionName: not null })
                        {
                            if (!_linkingFunctionLookup.TryGetValue($"gml_Script_{functionEntry.FunctionName}", out existingFunction))
                            {
                                if (Data.GlobalFunctions.TryGetFunction(functionEntry.FunctionName, out var newGlobalFunction))
                                {
                                    // Don't set existing function, so that this new function will be added to the main data later
                                    newFunction = (GameMakerFunction)newGlobalFunction;
                                }
                            }
                            else
                            {
                                newFunction = existingFunction;
                            }
                        }

                        // If not assigned already, need to make a new function entry
                        newFunction ??= new()
                        {
                            Name = MakeString(codeEntryName, out var id),
                            NameStringID = id
                        };
                    }

                    // Resolve function and child function name
                    string childFunctionName;
                    if (childFunctionNameFix)
                    {
                        childFunctionName = $"{shortName}@{parentFunctionName ?? rootCodeEntryName}";
                    }
                    else
                    {
                        childFunctionName = $"{shortName}_{parentFunctionName ?? rootCodeEntryName}";
                    }
                    functionEntry.ResolveFunction(newFunction, childFunctionName);

                    // Resolve final struct name
                    if (functionEntry.Kind == FunctionEntryKind.StructInstantiation)
                    {
                        functionEntry.ResolveStructName(shortName);

                        // If not using existing name, increment struct counter
                        if (existingCodeEntry is null)
                        {
                            _linkingStructCounter++;
                        }
                    }

                    // Add data to list for later addition to main data
                    usedChildEntryNames.Add(codeEntryName);
                    childDataList.Add(
                        new(
                            codeEntryName,
                            functionEntry,
                            newCodeEntry,
                            newScript,
                            newFunction,
                            existingCodeEntry is not null,
                            existingScript is not null,
                            existingFunction is not null
                        )
                    );
                }

                // Create structures for linking local variables
                _linkingVariableOrderLookup ??= new(16);
                _linkingVariableOrder ??= new(16);
                _linkingLocalReferences ??= new(16);

                // Perform main link
                try
                {
                    context.Link();

                    // Check for link errors
                    if (context.HasErrors)
                    {
                        errors ??= new(context.Errors.Count);
                        foreach (var error in context.Errors)
                        {
                            errors.Add(new CompileError(operation.CodeEntry, error));
                        }
                    }
                }
                catch (Exception e)
                {
                    // Exception thrown; make compile error for it (and also get rid of context)
                    context = null;
                    errors ??= [];
                    errors.Add(new CompileError(operation.CodeEntry, e));
                }

                // Collect all local variable names, and resolve references (if not errored)
                List<string>? localsOrder = null;
                if (errors is null)
                {
                    localsOrder = new(_linkingLocalReferences.Count);
                    var originalReferencedLocals = operation.CodeEntry.FindReferencedLocalVars();
                    foreach ((var name, var isEverLocal) in _linkingVariableOrder)
                    {
                        if (isEverLocal)
                        {
                            // Add to local order
                            localsOrder.Add(name);

                            // Ensure local name string is generated
                            var localString = MakeString(name, out var nameStringIndex);

                            // Get variable ID for this local (GM 2.3+ uses string index, prior uses local index)
                            var varId = linkingModern ? nameStringIndex : localsOrder.Count;

                            // Use existing registered variables if possible
                            GameMakerVariable? variable = null;
                            foreach (var referenced in originalReferencedLocals)
                            {
                                if (referenced.Name == localString && referenced.VarID == varId)
                                {
                                    variable = referenced;
                                    break;
                                }
                            }

                            // If not already referenced, define a new variable, and store in lookup
                            variable ??= Data.Variables.DefineLocal(Data, varId, localString, nameStringIndex);

                            // Update all references
                            foreach (var reference in _linkingLocalReferences[name])
                            {
                                reference.ValueVariable = variable;
                            }
                        }
                    }
                }

                // Clear out local variable structures for any further operations
                _linkingVariableOrderLookup.Clear();
                _linkingVariableOrder.Clear();
                _linkingLocalReferences.Clear();

                // If any errors occurred, don't commit any further modifications to main data (avoid too much corruption)
                if (errors is not null)
                {
                    return;
                }

                // Set max local variable count, if applicable
                var codeLocalsCount = (uint)(1 + localsOrder.Count);
                if (codeLocalsCount > Data.MaxLocalVarCount)
                {
                    Data.MaxLocalVarCount = codeLocalsCount;
                }

                // Update code locals, if they exist
                if (Data.CodeLocals?.For(operation.CodeEntry) is GameMakerCodeLocals locals)
                {
                    // Clear out all existing locals
                    locals.Locals.Clear();

                    // Create "arguments" local
                    DefineCodeLocal(locals, linkingModern, "arguments", 0);

                    // Create new locals
                    uint codeLocalsIndex = 1;
                    foreach (var local in localsOrder)
                    {
                        DefineCodeLocal(locals, linkingModern, local, codeLocalsIndex);
                        codeLocalsIndex++;
                    }
                }

                // Remove old child code entries/scripts/functions
                foreach ((var name, var codeEntryNameGroup) in remainingChildEntries)
                {
                    // Remove all remaining code entries associated with the given name
                    while (codeEntryNameGroup.RemainingOriginalEntries.TryDequeue(out var code))
                    {
                        // Remove code entry
                        Data.Code.Remove(code);
                        operation.CodeEntry.ChildEntries.Remove(code);

                        // Remove script at correct index, if possible
                        if (_linkingScriptLookup.TryGetValue(name, out var scripts) &&
                            codeEntryNameGroup.EntriesUsed < scripts.Count)
                        {
                            Data.Scripts.Remove(scripts[codeEntryNameGroup.EntriesUsed]);
                            scripts.RemoveAt(codeEntryNameGroup.EntriesUsed);
                            if (scripts.Count == 0)
                            {
                                _linkingScriptLookup.Remove(name);
                            }
                        }
                    }

                    // If no entries were used, remove function, as long as it's not a global function (since it could still be referenced)
                    if (codeEntryNameGroup.EntriesUsed == 0 &&
                        _linkingFunctionLookup.TryGetValue(name, out var function) &&
                        !Data.GlobalFunctions.FunctionExists(function))
                    {
                        Data.Functions.Remove(function);
                        _linkingFunctionLookup.Remove(name);
                    }
                }

                // Commit new child code entries
                var childIndex = 0;
                var rootCodeEntryIndex = -1;
                foreach (var childData in childDataList)
                {
                    // Attach latest information to code entry
                    childData.Code.Length = (uint)context.OutputLength;
                    childData.Code.Offset = (uint)childData.FunctionEntry.BytecodeOffset;
                    childData.Code.ArgumentsCount = (ushort)childData.FunctionEntry.ArgumentCount;
                    childData.Code.LocalsCount = (uint)childData.FunctionEntry.Scope.LocalCount;

                    // Attach latest information to script
                    childData.Script.IsConstructor = childData.FunctionEntry.IsConstructor;

                    // Define code entry
                    if (!childData.ExistingCode)
                    {
                        operation.CodeEntry.ChildEntries.Insert(childIndex, childData.Code);
                        if (rootCodeEntryIndex == -1)
                        {
                            // Get index of root code entry only when required
                            rootCodeEntryIndex = Data.Code.IndexOf(operation.CodeEntry);
                        }
                        Data.Code.Insert(rootCodeEntryIndex + childIndex, childData.Code);
                    }

                    // Define script
                    if (!childData.ExistingScript)
                    {
                        if (_linkingScriptLookup.TryGetValue(childData.Name, out var existing))
                        {
                            existing.Add(childData.Script);
                        }
                        else
                        {
                            _linkingScriptLookup[childData.Name] = [childData.Script];
                        }
                        Data.Scripts.Add(childData.Script);
                    }

                    // Define function
                    if (!childData.ExistingFunction)
                    {
                        _linkingFunctionLookup[childData.Name] = childData.Function;
                        Data.Functions.Add(childData.Function);
                    }

                    childIndex++;
                }

                // Commit instructions and other data to main code entry
                static IEnumerable<GameMakerInstruction> EnumerateInstructions(IReadOnlyList<IGMInstruction> instructionList)
                {
                    foreach (var instruction in instructionList)
                    {
                        yield return (GameMakerInstruction)instruction;
                    }
                }
                operation.CodeEntry.Replace(EnumerateInstructions(context.OutputInstructions));
                operation.CodeEntry.Length = (uint)context.OutputLength;
                operation.CodeEntry.Offset = 0;
                operation.CodeEntry.ArgumentsCount = 0;
                operation.CodeEntry.LocalsCount = (uint)(1 + context.OutputRootScope.LocalCount);
            });
        }

        // Unmark this group as compiling
        GlobalContext.CurrentCompileGroup = null;

        // Clear queues
        _queuedCodeReplacements?.Clear();

        // If errors occurred, undefine global functions that were newly-defined (so they don't cause consistency issues)
        if (errors is not null && newlyDefinedGlobalFunctions is not null)
        {
            // Iterate in reverse order, in case the same function name was defined twice in different scripts (to restore original function)
            for (var i = newlyDefinedGlobalFunctions.Count - 1; i >= 0; i--)
            {
                (var functionName, var newFunction, var oldFunction) = newlyDefinedGlobalFunctions[i];

                // Undefine function
                Data.GlobalFunctions.UndefineFunction(functionName, newFunction);

                // Restore old function if possible
                if (oldFunction is not null)
                {
                    Data.GlobalFunctions.DefineFunction(functionName, oldFunction);
                }
            }
        }

        // Unreference other structures
        UnreferenceLinkingLookups();

        // Return result
        if (errors is null)
        {
            return CompileResult.SuccessfulResult;
        }
        return new CompileResult(false, errors);
    }

    /// <summary>
    /// Initializes linking lookup structures, if they have not already been.
    /// </summary>
    private void InitializeLinkingLookups()
    {
        // Create string -> ID lookup
        if (_linkingStringIdLookup is null)
        {
            _linkingStringIdLookup = new(Data.Strings.Count);
            for (var i = 0; i < Data.Strings.Count; i++)
            {
                if (Data.Strings[i]?.Content is string content)
                {
                    _linkingStringIdLookup[content] = i;
                }
            }
        }

        // Create function name -> function lookup
        if (_linkingFunctionLookup is null)
        {
            _linkingFunctionLookup = new(Data.Functions.Count);
            for (var i = 0; i < Data.Functions.Count; i++)
            {
                var function = Data.Functions[i];
                if (function?.Name?.Content is string name)
                {
                    _linkingFunctionLookup[name] = function;
                }
            }
        }

        // Create script name -> script lookup
        if (_linkingScriptLookup is null)
        {
            _linkingScriptLookup = new(Data.Scripts.Count);
            for (var i = 0; i < Data.Scripts.Count; i++)
            {
                var script = Data.Scripts[i];
                if (script?.Name?.Content is string name)
                {
                    if (_linkingScriptLookup.TryGetValue(name, out var existing))
                    {
                        existing.Add(script);
                    }
                    else
                    {
                        _linkingScriptLookup[name] = [script];
                    }
                }
            }
        }

        // Check for next usable struct ID
        if (_linkingStructCounter == -1)
        {
            _linkingStructCounter = 0;
            foreach (var variable in Data.Variables)
            {
                if (variable.Name?.Content is string name)
                {
                    const string structPrefix = "___struct___";
                    if (name.StartsWith(structPrefix, StringComparison.Ordinal) &&
                        int.TryParse(name.AsSpan(structPrefix.Length), out var id))
                    {
                        _linkingStructCounter = Math.Max(_linkingStructCounter, id + 1);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Dereferences all internal linking structures to reduce potential memory usage.
    /// </summary>
    private void UnreferenceLinkingLookups()
    {
        if (!PersistLinkingLookups)
        {
            _linkingStringIdLookup = null;
            _linkingFunctionLookup = null;
            _linkingStructCounter = -1;
        }
        _linkingVariableOrderLookup = null;
        _linkingVariableOrder = null;
        _linkingLocalReferences = null;
    }

    /// <summary>
    /// Guesses the type of a script from a code entry's name.
    /// </summary>
    private static (CompileScriptKind ScriptKind, string GlobalScriptName) GuessScriptKindFromName(string codeName)
    {
        // If null, just assume script
        if (codeName is null)
        {
            return (CompileScriptKind.Script, null);
        }

        // Compare prefixes against known ones
        const string globalScriptPrefix = "gml_GlobalScript_";
        if (codeName.StartsWith(globalScriptPrefix, StringComparison.Ordinal))
        {
            // Output global script name as well
            return (CompileScriptKind.GlobalScript, codeName[globalScriptPrefix.Length..]);
        }
        if (codeName.StartsWith("gml_Script", StringComparison.Ordinal))
        {
            return (CompileScriptKind.Script, null);
        }
        if (codeName.StartsWith("gml_Object", StringComparison.Ordinal))
        {
            return (CompileScriptKind.ObjectEvent, null);
        }
        if (codeName.StartsWith("gml_Room", StringComparison.Ordinal))
        {
            return (CompileScriptKind.RoomCreationCode, null);
        }
        if (codeName.StartsWith("Timeline", StringComparison.Ordinal))
        {
            return (CompileScriptKind.Timeline, null);
        }

        // Unknown; default to script
        return (CompileScriptKind.Script, null);
    }
}
