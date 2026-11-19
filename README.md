# G3MLib

G3MLib reads, writes, analyzes, decompiles, and modifies GameMaker data files.

The `G3MLib.Analyzer` namespaces provide GML compilation, decompilation, bytecode analysis, and game-specific macro resolution.

The `G3MLib.DataFile` namespaces provide the data-file model, I/O, project serialization, and scripting APIs.

The `G3MLib.Modding` namespaces provide patch creation and application, patch merging, diff reports, resource import/export, integrity repair, caching, and xdelta execution. Hosts supply their own UI through the library's log and progress events.

For `ProjectContext` scripts, set `ScriptMessageHandler` and `ScriptWarningHandler` to receive text output. Interactive dialogs and opening URLs require a host implementation of `IScriptInterface`; the default context does not launch applications.

For xdelta operations, configure the executable supplied by your application before invoking patch services:

```csharp
XDeltaService.DefaultExecutablePath = xdeltaPath;
```

For concurrent host operations, use `LogService.BeginScope(messageCallback, progressCallback)` to route each operation's messages separately and `XDeltaService.UseExecutable(xdeltaPath)` to override its executable without changing the process default. Dispose both scopes after the awaited operation completes.

`G3MProjectService.Export` takes an `IProgress<BatchProgress>` (resources done, total, name of the resource just written) and a `CancellationToken`. A cancelled export leaves the project folder as it was. `G3MProjectService.Update` writes only the resources you name into an existing project folder, with the same files a full export writes for them, so a rename needs the renamed resource and the resources that refer to it, not a full export.

Other APIs added in 1.1.0: `GameMakerIO.ReadAsync` and `WriteAsync` with progress and cancellation; `ReferenceIndex` and `RenameService` for find usages, unreferenced assets and renames with undo; `GmlTokenizer` and compiler diagnostics with spans; `CodeImportGroup` to compile code into a data file with a result per operation; `TexturePageRepacker` and `PageEditor` for texture pages; `FrameExporter` and `AudioGroupFile` for frames and audio groups; `DataLookup` and `DataRepair`; `ScriptRunner` and `ScriptGlobals` for scripts; and readers and writers for GameMaker `.yy` files. A data model that several threads touch is guarded with `ModelLock`; the rules are in its documentation.

## Install

G3MLib targets .NET 10.

```shell
dotnet add package G3MLib
```

## Benchmarks

Measured on 2026-11-19 (G3MLib 1.1.4) using the `data.win` of Pizza Tower (92,440,092 bytes), Windows x64, .NET 10 Release, Intel Core Ultra 7 155H, and 31 GiB RAM. Tiered compilation was disabled for both libraries with `DOTNET_TieredCompilation=0`. Times are medians, with minimum–maximum in parentheses. MiB columns measure total managed allocations across all threads, not peak memory or native ImageMagick allocations.

The machine was idle during both runs (no other builds or programs were started), and the tables come from one `--compare` run and one `--full` run. Run-to-run variation on this laptop is still visible in the ranges: judge a row by its minimum–maximum range, not its median alone, and rerun on your own hardware.

The six directly compared operations (complete read, general-information read, write, parse, compile, decompile) come from one `--compare` run: seven measured batches after three warm-up batches, alternating library order each round in the same process. Each batch contains 64 parsing or compilation operations, 16 general-information reads, or one full read, write, or decompilation. Every other row comes from one `--full` run on the same file (three measured runs after one warm-up). Garbage collection runs before each measurement, outside the timer.

Comparison sources: [UndertaleModLib f43e12c](https://github.com/UnderminersTeam/UndertaleModTool/tree/f43e12c445c37d50dc6244caa12ccab232983f3f) and [Underanalyzer 4ff50a8](https://github.com/UnderminersTeam/Underanalyzer/tree/4ff50a866b4c1a7acee8cebe6a56d6a48709b453). Every ratio uses one rule: the compared library's median divided by G3MLib's median, printed with two decimals. A ratio above 1 means G3MLib's median was lower. The verdict is computed from the ranges: when the minimum–maximum ranges of the two libraries overlap it reads "no clear difference", otherwise it names the library whose whole range is lower. `--compare --markdown` prints these rows. A dash means no equivalent operation was measured, not that the other library cannot perform it.

G3MLib is not faster in every row. Writing a complete data file had a ratio of 0.96 and compiling GML source 0.91, so UndertaleModLib's and Underanalyzer's medians were lower there; parsing (1.08) had a lower G3MLib median. The ranges overlap in all three, so there is no clear difference. G3MLib allocated slightly more than Underanalyzer while decompiling (23.882 MiB against 22.402 MiB). The ranges favor G3MLib for the complete read, the general-information read and decompilation. This is one run on one machine, so treat the size of those gaps as approximate.

| Operation | G3MLib, ms | UndertaleModLib, ms | Underanalyzer, ms | Time ratio | Verdict | G3MLib, MiB | UndertaleModLib, MiB | Underanalyzer, MiB |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: |
| Read complete data file | 551.812 (538.867–586.260) | 879.562 (862.402–926.302) | — | 1.59× | G3MLib faster | 236.615 | 250.662 | — |
| Read general information only | 0.281 (0.265–0.506) | 1.953 (1.895–2.121) | — | 6.94× | G3MLib faster | 0.368 | 12.949 | — |
| Write complete data file to memory | 385.754 (368.631–430.344) | 368.828 (358.207–417.404) | — | 0.96× | no clear difference | 352.153 | 352.232 | — |
| Hash all resources | 1,598.969 (1,524.000–1,610.881) | — | — | — | — | 6,166.202 | — | — |
| Compute MD5 synchronously | 158.603 (155.525–160.464) | — | — | — | — | 1.003 | — | — |
| Compute MD5 asynchronously | 161.784 (161.113–162.585) | — | — | — | — | 1.004 | — | — |
| Build data-info snapshot | 1.265 (1.264–1.400) | — | — | — | — | 0.849 | — | — |
| Write analysis cache | 22.718 (20.059–27.691) | — | — | — | — | 0.081 | — | — |
| Read analysis cache | 174.494 (170.508–178.544) | — | — | — | — | 9.221 | — | — |
| Export all sprite metadata and PNG frames | 25,946.195 (25,246.718–26,906.387) | — | — | — | — | 13,776.253 | — | — |
| Parse 200-line GML source | 0.149 (0.138–0.176) | — | 0.160 (0.134–0.212) | 1.08× | no clear difference | 0.269 | — | 0.268 |
| Compile 200-line GML source | 0.179 (0.153–0.197) | — | 0.163 (0.147–0.194) | 0.91× | no clear difference | 0.323 | — | 0.322 |
| Decompile largest code entry (9,283 instructions) | 11.191 (10.125–12.225) | — | 20.040 (19.072–25.081) | 1.79× | G3MLib faster | 23.882 | — | 22.402 |
| Validate patch | 166.622 (165.027–168.449) | — | — | — | — | 1.051 | — | — |
| Create patch | 4,915.177 (4,750.574–4,979.578) | — | — | — | — | 12,783.300 | — | — |
| Apply patch | 3,117.020 (3,035.676–3,132.424) | — | — | — | — | 857.133 | — | — |
| Create standard diff report | 9,381.283 (9,202.032–9,523.448) | — | — | — | — | 15,019.274 | — | — |
| Create full diff report | 9,643.650 (9,608.761–9,681.787) | — | — | — | — | 15,012.463 | — | — |
| Merge 2 independent patches | 920.908 (918.576–971.917) | — | — | — | — | 303.197 | — | — |
| Merge 4 independent patches | 877.089 (867.404–962.915) | — | — | — | — | 305.274 | — | — |
| Merge 8 independent patches | 855.731 (848.742–887.508) | — | — | — | — | 309.415 | — | — |
| Merge and apply 8 independent patches | 4,040.773 (4,036.043–4,116.770) | — | — | — | — | 1,193.599 | — | — |
| Build reference index | 148.947 (145.170–152.389) | — | — | — | — | 187.618 | — | — |
| Rename 100 sprites (plan, apply, undo) | 58.163 (54.664–58.749) | — | — | — | — | 1.890 | — | — |
| Plan texture page repack | 17,269.700 (16,546.395–18,461.428) | — | — | — | — | 34,291.289 | — | — |
| Import all sprites into a new data file | 100,831.245 (100,091.722–102,810.411) | — | — | — | — | 11,001.759 | — | — |

The repack row only computes a plan and decodes every texture page of the game; the sprite import row imports the 31,388 exported PNG frames of the sprite export row into an empty data file, including packing and mask generation.

Reads reopen the file with a warm filesystem cache. Writes serialize an already loaded model to memory. The comparison checks room, code, and string counts, plus the selected code entry's instruction count. The upstream partial reader accepts its expected unread-tail warning; other warnings fail the run.

GML input contains 200 lines, from `var value0 = 0 + 1;` through `var value199 = 199 + 1;`, with a fresh mock game context per operation. Parsing includes tokenization and parse-tree post-processing; compilation also generates bytecode, without linking. Decompilation includes a fresh global context and function lookup initialization: G3MLib's context for its analyzer, UndertaleModLib's context for Underanalyzer. These timings include that integration work. The ranges overlap for parsing and compilation, so those medians show no speed difference between the libraries.

Hashing covers every resource. Cache writes serialize precomputed hashes, resource names, ordering, and the information snapshot; cache reads include source validation. Sprite export writes metadata and all PNG frames to a temporary directory, reusing output paths after warm-up. The harness checks the exported frame count. Directory cleanup is outside the timer.

Patch creation compares the input with a copy whose first room is one pixel wider. Validation checks that patch against the input; application writes a new data file. Diff reports compare the same pair without a supplied analysis cache. Merge operations combine patches changing 2, 4, or 8 different rooms. Application includes staged output validation and rollback support. The harness reads the applied and merged files and checks the room widths. These rows measure room-only changes.

### Reproduce

Set `DOTNET_TieredCompilation=0` in the benchmark process environment to match these measurements. For read/write, resource hashing, and MD5 across one or more files:

```shell
dotnet run --project G3MLib.Benchmarks -c Release -- path/to/data.win
```

For all operations, supply a data file with at least eight rooms. Sprite export and patch tests require temporary disk space and several minutes:

```shell
dotnet run --project G3MLib.Benchmarks -c Release -- --full path/to/data.win
```

For the direct comparison, check out the UndertaleModTool revision above with its Underanalyzer submodule, then pass its source directory:

```shell
dotnet run --project G3MLib.Benchmarks -c Release -p:UndertaleModToolPath=/path/to/UndertaleModTool -- --compare path/to/data.win
```

The optional upstream reference belongs only to the benchmark project. G3MLib has no dependency on UndertaleModLib or an application host. Game assets are not distributed with the benchmark. Results depend on input, runtime, filesystem cache, native codecs, power settings, and system load.

## Script compatibility

`G3MLib.ScriptCompat` compiles every `.csx` script under a folder against the G3MLib script host and runs those that need no dialog on a copy of a data file, then writes a report:

```shell
dotnet run --project G3MLib.ScriptCompat -c Release -- <scripts folder> <data file> <work folder> <report.md>
```

## License

G3MLib is GPL-3.0-only. It includes some UndertaleModLib-derived code under GPL-3.0 and Underanalyzer-derived code under MPL-2.0. See [LICENSE](LICENSE) and the license notices in the repository.
