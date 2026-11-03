# Changelog

## [1.1.2] - 2026-11-03

### Removed

- `ModConfig` and `ModPatchOperation`, added in 1.1.1. They read the manifest of one mod manager, which does not belong in a general library; an application that needs it reads the manifest itself.

### Security

- Patch archives are refused when an entry name is rooted, holds a drive or stream colon, or has a `..` segment, or when the entries declare more than 4 GiB in total (`PatchFileSystem.MaxTotalPayloadBytes`, settable, `long.MaxValue` removes the limit). Embedded texture paths are joined with a containment check. `g3mpatch.json` larger than 64 MiB is refused.
- Resource names `.`, `..`, trailing dots or spaces and Windows device names are made safe when files are written.
- Recursion guards in the GML parser, UI node and sequence readers, so deeply nested input raises an error instead of a stack overflow.
- Allocation caps: image decompression size hint, tile layer size; `MurmurHash` no longer stack-allocates by input length.
- Patch JSON is validated with a streaming reader instead of a DOM. Merge reference scans stop after 2 seconds per pattern. Absolute paths are passed to xdelta.

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
