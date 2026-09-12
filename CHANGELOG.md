# Changelog

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
