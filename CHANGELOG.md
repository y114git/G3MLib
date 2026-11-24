# Changelog

## [1.1.6] - 2026-11-25

### Fixed

- A diff, patch or merge no longer reports hundreds of sprites and embedded textures as changed when nothing changed. Reading a second data file blanked the shared 1x1 placeholder texture of every file after the first (disposing one file disposed it for all), so the textures that point at it hashed differently in the second file.
- A sprite frame that only moved to another spot or page of the texture atlas hashes the same while its pixels match, so a repacked atlas no longer marks untouched sprites as changed. A real pixel change still does.
- A sound that only moved to another audio slot hashes the same while its audio matches. The slot only counts when the audio cannot be hashed.
- Merging patches one after another no longer lets two new sounds that name the same audio slot overwrite each other's audio: a new sound whose slot is held by another sound gets a free slot and keeps its own audio.
- The data cache layout changed (schema 4), so caches written by older versions are rebuilt once.

## [1.1.5] - 2026-11-20

### Fixed

- A project export no longer fails with "Access to the path ... is denied" when a virus scanner or sync tool still holds freshly written files: moving the staging folders and deleting the old ones is retried for a short time, a folder that still cannot be renamed is copied, and a leftover staging folder never turns a finished export into a failure.
- Sub-projects inherit `AllowScripts` and the script message and warning handlers of the project that lists them. Before, an untrusted project with scripts turned off still ran the scripts of its sub-projects.
- Export folder names stay distinct: a resource named like the generated folder of another (`a_b__` plus 12 hex digits) now gets its own hash suffix. Other names keep their folders.
- Version detection of the font glyph layout skips fonts without glyphs and uses the first font that has some, so a file whose first font is empty no longer fails with "Reading out of chunk bounds".
- Writing one data object from several threads at once no longer fails with a `NullReferenceException`: the writes take turns.
- A font chunk whose pointer leads outside the chunk fails with a message that names the chunk and the font.
- A 2024.14.1 file whose last background ends inside the chunk end padding is detected, so it no longer drifts into "Invalid boolean value" errors.
- A folder move that must fall back to copying builds the copy beside the target and renames it, never leaves a half copy, and only retries sharing violations, lock violations and access denied.
- A project export moves the old type folders aside before it moves the new ones in, and puts them back if a move fails, so a failed export no longer loses an old folder. A folder copy never follows or copies a link and fails with a clear message instead; on systems other than Windows a permission error is not retried.
- Patch archives and exports made by 1.1.4 keep working for code entries named like a hashed folder (`x__` plus 12 hex digits).
- `ScriptInputDialog`, `SimpleTextInput`, `PromptLoadFile`, `PromptSaveFile` and `PromptChooseDirectory` are annotated as returning `string?`: a cancelled prompt returns null.


## [1.1.4] - 2026-11-19

### Security

- Patch archives extracted for a diff are checked for entry names and total size like the ones loaded for an apply, and unsafe manifest paths are skipped. Extraction can be cancelled between entries and chunks.
- `PatchFileSystem` refuses a corrupt `g3mpatch.json`, a JSON `null` manifest included, with the patch path in the message. Export, import and patch paths stay inside their folder.

### Added

- `ScriptGlobals.ScriptInputDialog(title, label, defaultInput, allowMultiline, showDialog)`, the short form that earlier G3MLib hosts offered to scripts; it maps to the seven-argument form and returns the default without asking when `showDialog` is false.
- Macros declared in other code entries, or given in `CompileGroup.Macros` (also filled by `CompileGroup.AddMacros`), are expanded when an entry is compiled. `CompileContext.ExternalMacros` takes the same table. A macro that names itself or grows without end is reported as an error.
- Data files, patch archives and project files are written to a temporary file and then swapped in (`AtomicFile`), so a failed write keeps the old file. Each write uses its own temp file name, a symlinked target keeps its link, and temp files older than a day left by a crash are removed at the next write of the same target.
- `ScriptRunner.RunAsync` takes a `CancellationToken` and a `timeout` (`Timeout.InfiniteTimeSpan` for no limit). A script run without data holds no gate, and a data object the script creates is not gated. Scripts read the token as `ScriptGlobals.CancellationToken`; `ScriptGlobals` members and the library's bulk operations stop at the next call once it is cancelled. `ScriptRunResult.Cancelled` tells a cancelled run from a failed one.
- `GameMakerData.Gate` (`DataAccessGate`) is a reader/writer gate with `BeginRead` and `BeginWrite` scopes, a timeout and a token. Hosts take it to keep a save from racing with a script or an import. The scope of a running script is bound to the thread that runs it, so a host must not save or import from a callback that runs on that thread while the script waits, such as a click handler during a script dialog. Scripts, resource import, `ResourceImportService.ImportAssetOrder`, `ReferenceIndex.ClearUsages` and `G3MProjectService.Build` take it as a writer; data file writes, `ExportTypes`, `ExportSelectiveExceptCode` and `G3MProjectService.Export` and `Update` take it as a reader.
- `GameMakerGeneralInfo.BranchSource` names the chunk that decided the LTS branch while reading. `SetLTS` takes an optional `source`.
- `ReferenceIndex.ClearUsages(target)` sets every model reference to the target to none and removes it from lists that hold it, so it can be deleted. It returns `ClearedReferences` with `Undo()`; uses in code are listed in `Skipped`.
- `RenameService.IsValidName` and `RenameService.IsNameInUse` expose the name rules that renames already apply.
- `ResourceImportService.Import("CodeEntries", ...)` compiles the `.gml` file of each exported code entry folder, with one result per entry and an optional name filter. Entries with only an `.asm` file are skipped. Project import uses it too.
- `DataIntegrityResult.Issues` lists every error and repair as an `IntegrityIssue` with `Severity`, `Code` (such as `ROOM.MissingSprite`), `Chunk`, `Resource` and `Message`. `Errors` and `Repairs` keep the same text.
- `ExportSounds` and `AudioGroupReader` take a null data file path: the audio held in the data file itself (group 0) is then written into the sound folder, and `ImportSounds` reads it back.
- `ResourceCloneService.Clone(data, source, newName)` copies a resource in memory: objects, scripts, sprites, paths, animation curves, sounds, fonts, shaders, timelines, backgrounds, rooms, sequences, particle systems and extensions. The copy owns its events, layers, frames and tracks, and shares other resources, texture page items, variables and functions. Code entries and locals named after the resource are copied and renamed with the `RenameService` rules, a room gets new instance ids and joins the room order. `ResourceClone.Undo()` removes everything the copy added. Instance ids and strings that later edits depend on are kept. A room copy takes instance ids above every id in the file, even when `GeneralInfo.LastObj` is lower. The function entries of a copied script or object are copied with it, child code entries get unique names, and `ResourceClone.SharedCode` lists the code entries the copy shares with the source. `Clone` runs its checks inside the data gate and takes an optional `timeout` and `cancellationToken` for the wait. Code entries, functions, variables, locals, audio groups, texture groups, filter effects and embedded audio and textures throw `NotSupportedException`.
- `ExportExtensions` takes an optional name filter, like the other resource types, so one extension can be exported. The import already accepted a filter.
- `ClearedReferences.ChangedOwners` lists the owners whose references were cleared, for `ReferenceIndex.Update` after `Undo` and for refreshing a UI.
- `GameMakerTexturePageItem.PrepareReplace` does the image work of `ReplaceTexture` and returns a `PreparedTextureReplace`; its `Commit()` only swaps the page image, so hosts can prepare outside a write lock. `Commit()` throws `InvalidOperationException` when the page image changed or the item was pointed at another page or cleared.
- `G3MLib.Benchmarks` prints comparison rows with `--markdown`, and the README holds new benchmark numbers.

### Fixed

- `AudioGroupFile.Save` over the opened file throws an `IOException` that says the file was saved when reopening it fails; the instance must then be disposed. The path comparison is case sensitive on Linux.
- Font glyph data of 2024.14 and the texture group list of LTS files are read back correctly.
- Nested ternaries and chained `??` compile. An empty `#macro` no longer swallows the next line.
- Saving a YYC file with an empty code chunk works. The last chunk is no longer padded when chunks are written in a different order.
- A BPS patch whose copy starts at or past the write position stops with an error instead of hanging.
- A room with a tile layer that has one empty dimension is refused, and 2024.4 tile padding is detected from the layer data pointer. A sprite with collision masks but no mask bytes no longer reads past the data.
- Importing a project requests the audio group from the JSON group id. Project export keeps the folders of types that failed or were not exported.
- Export folders keep names that differ only in an invalid character apart, and import reads the resource name from the JSON `name`, so a resource named `a:b` imports as `a:b`, not as its folder name. Old exports without a `name` still import by their folder name.
- Merging two patches that make the same edit is a clean merge. Log suppression is reset when a merge fails, so later work keeps logging.
- Diff reports use an LCS with context hunks and a size guard. Event names of keyboard, trigger and gesture events are parsed.
- Merge and import filters find resources in patch archives and exports made before the hash suffix, in both folder name forms. Patched and merged data files, project files and audio group files are written through a temporary file, so a failed write keeps the old file.
- Swallowed exceptions in temp cleanup and JSON fallbacks are logged or narrowed, and debug output goes through `LogService`.

### Changed

- Breaking: an export folder name gets a 12 character hash suffix when a character had to be replaced, such as `a:b` (now `a_b__<hash>`), so two names cannot share a folder. Exports made by 1.1.3 keep their folder names and still import; a new export of such a resource writes a different folder.
- A malformed `g3mpatch.json` makes loading a patch fail with `InvalidDataException`, where 1.1.3 ignored it. Apply, diff and merge return an error result that names the patch path.
- Macro scanning in `CompileGroup` uses the compiler's lexer, so line continuations and CRLF work, and `#macro` inside comments and strings is ignored.

## [1.1.3] - 2026-11-15

### Fixed

- Merging patches that each add entries to the same list no longer loses the entries of all but the last patch. With property merge on, global init scripts, game end scripts, feature flags, tags and the room order are merged by name against the original list: additions and removals from every patch are kept, and the earlier patch's additions come first at a shared spot. A list with repeated names is merged as before.
- A texture page item without a texture page now fails with `InvalidDataException` (`TextureWorker.GetTextureFor` and `ExportAsPNG` name the image, `CutBgra`) or `InvalidOperationException` (`ReplaceTexture`), where a null key escaped from the page cache. `GetEmbeddedTexture(null)` throws `ArgumentNullException`.

### Changed

- Data files are built in memory and copied to a seekable file stream in one write, about four times faster than writing to the file while the writer seeks back for lengths and pointers. A file too large for one buffer is still written directly.
- A data file that was just saved and validated is not parsed again to copy its audio groups, commit it or validate it a second time. Applying a patch to a 12 MB file takes 1.7 s instead of 4.3 s, applying eight merged patches 2.7 s instead of 5.4 s.
- Updated `Microsoft.CodeAnalysis` packages to 5.12.0.

## [1.1.2] - 2026-11-04

### Removed

- `ModConfig` and `ModPatchOperation`, added in 1.1.1. They read the manifest of one mod manager, which does not belong in a general library; an application that needs it reads the manifest itself.

### Security

- Patch archives are refused when an entry name is rooted, holds a drive or stream colon, or has a `..` segment, or when the entries declare more than 4 GiB in total (`PatchFileSystem.MaxTotalPayloadBytes`, settable, `long.MaxValue` removes the limit). Embedded texture paths are joined with a containment check. `g3mpatch.json` larger than 64 MiB is refused.
- Resource names `.`, `..`, trailing dots or spaces and Windows device names are made safe when files are written, with a hash suffix so they cannot collide with another resource. A colon is replaced on every platform.
- Recursion guards in the GML parser (including chained prefix operators), and in the UI node and sequence readers (including their child count pass), so deeply nested input raises an error instead of a stack overflow.
- Allocation caps: image decompression size hint, tile layer size; `MurmurHash` no longer stack-allocates by input length.
- Patch JSON is validated with a streaming reader instead of a DOM. Merge reference scans stop after 2 seconds per pattern and log a warning when one does. `g3mpatch.json` is capped in every load path. Archive entries over 64 MiB are read in chunks. Absolute paths are passed to xdelta.

### Fixed

- `return` is terminated with a semicolon again when `DecompileSettings.UseSemicolon` is off, as the setting documents, so a following statement that starts with `(` or `[` is not read as part of the returned value.

### Changed

- `PatchFileSystem.MaxPayloadBytes` now holds the payload size cap that `ModConfig` carried, lowered from 4 GiB to the 2 GiB an array can hold, so a larger payload is refused with a clear error.
- `ProjectContext.AllowScripts` can be set to false by a host that opens untrusted projects; it stays true by default.
- An in-memory patch apply writes no files: audio groups stored outside the data file are skipped, where 1.1.1 rewrote the group files next to the data file.
- `ResourceImportService.Import` with a dictionary of names checks every type before importing, so an unsupported type no longer leaves a half-applied import.

## [1.1.1] - 2026-11-03

### Added

- `PatchService.ApplyPatchAsync` overloads that patch a `GameMakerData` in memory, from a file or a stream, with an optional validation pass before any change; `ModConfig` reads a mod `mod_config.json` and lists its patch operations.
- `GameMakerData.LoadWarnings` and `MayLoseDataOnSave`: the warnings raised while a data file was read, and whether saving could lose data.
- `ChunkDigests` to hash every chunk of a data file without parsing it and to compare two files; `GameMakerChunk.Offset`.
- `ReferenceIndex.FindTileUses` and `ReferenceIndex.ClassifyString`.
- `RepackPlan.Pages` with the size, fill and item count of every planned page.
- `ResourceImportService.Import` overloads that import only named entries.
- `DebugDataGenerator` builds GameMaker 1.4 debugger data (`.yydebug`) from a data file as decompiled text, assembler text or full assembler text; `GameMakerDebugIO.Write` saves it.

### Fixed

- Texture page repack no longer stops with an out of range error for frames at a page edge, and builds each page in one pass.
- Code compiled from a function script now decompiles: the child code entry is placed after its root entry.
- `DecompileSettings.UseSemicolon` now applies to `return`.

## [1.1.0] - 2026-11-01

### Added

- Project format: export a data file to a GameMaker-style folder with a `.g3mproj` file, full or delta, and rebuild it byte-identical; `OpenAsync` returns a resource index without exporting.
- Project export reports progress per resource and can be cancelled, which leaves the project folder unchanged; `G3MProjectService.Update` writes only the named resources into an existing project folder, so a rename does not need a full export.
- `ReadAsync` and `WriteAsync` with progress and cancellation.
- Cancellation tokens for patch create, validate and apply, merge, diff, and resource export, import and hashing.
- Structured compiler diagnostics with severity, code and span, and a public GML tokenizer.
- Decompiler source map and disassembly line map.
- Reverse reference index with find usages and unreferenced assets, and a rename service for resources, variables and functions.
- `ModelLock` and a documented thread rule for the data model.
- `ScriptGlobals` and `ScriptRunner`, shared by every host.
- `TexturePageRepacker` to add or replace sprite frames and repack texture pages, with `RepackOptions` (page size, border, power of two, maximum item size, keep-alone predicate, group key) and a read-only `Plan` step; `PageEditor` for direct page edits.
- Batch forms of resource export and import with per item results, progress and cancellation; `FrameExporter` for PNG frame export with page eviction.
- `AudioGroupFile` to read, add, replace, remove and compact entries of an audio group file.
- `CodeImportGroup.Import` with dry run and stage modes and a result per operation, including line and column of compiler errors.
- Batch rename plans, lazy `FindUsages`, and `FindUnreferenced` for assets, strings, variables, functions, audio, texture items and empty pages.
- `DataLookup` for name lookups, `DataRepair` for code locals and variable counters, and ordered chunk writing.
- Readers and writers for GameMaker `.yy` sprites, sounds, scripts, objects and rooms.
- Unknown chunks are kept and written back unchanged.

### Fixed

- Format gaps in room, sequence, particle emitter, version detection and GEN8 handling; resource ids outside the table are kept.
- Compile and lint now fail on compiler errors.
- The GML compiler accepts `return` before `}`, indexing a call result such as `f()[i]`, and template strings in LTS data.

### Changed

- Full sprite export of a large game takes about 18 s instead of about 52 s.
- Texture pages decode in parallel and frames that share a rectangle are encoded once.
- Sprite import uses the shared texture group packer: new sprites are created in file name order with a 2 pixel border and generated border pixels, so page layouts differ from 1.0.1. Importing a large sprite set is about 39 percent faster.
- Sound import writes each audio group file once per batch instead of once per sound.
- Merge and patch timings match 1.0.1 within measurement noise.
- Numbers are formatted and strings compared with invariant culture and ordinal rules, so results no longer depend on the machine's language.
- Public methods throw `ArgumentNullException` for a null argument they would otherwise dereference.

## [1.0.1] - 2026-09-12

### Added

- Library name and version in patch manifests, separate from the host application.
- Per-operation logging, progress, and xdelta configuration.
- Benchmarks for data I/O, GML, resources, caches, and patches.

### Fixed

- Data and audio-group output recovery when file replacement fails.
- Patch application, sequential merging, and resource path validation.
- ImageMagick resource disposal during texture hashing.
- Buffered chunk boundaries when reading different-sized chunks.

### Changed

- Reduced texture-hashing allocations.
- Smaller read buffers and indexed function lookup during decompilation.
- Removed unused merge code and game-specific debug output.

## [1.0.0] - 2026-09-06

### Added

- Standalone .NET 10 library for GameMaker data files.
- Data reading and writing, GML compilation and decompilation.
- Patching, merging, resource import/export, and legacy script translation.
