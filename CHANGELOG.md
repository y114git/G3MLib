# Changelog

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
