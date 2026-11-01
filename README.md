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

Measured on 2026-09-12 (G3MLib 1.0.1) and, for every row except the four direct-comparison GML and header rows, again on 2026-10-26 (pre-release 1.1.0) using the `data.win` of Pizza Tower (92,440,092 bytes), Windows x64, .NET 10 Release, Intel Core Ultra 7 155H, and 31 GiB RAM. Tiered compilation was disabled for both libraries with `DOTNET_TieredCompilation=0`. Times are medians, with minimum–maximum in parentheses. MiB columns measure total managed allocations across all threads, not peak memory or native ImageMagick allocations.

The six directly compared operations use seven measured batches after three warm-up batches, alternating library order each round in the same process. Each batch contains 64 parsing or compilation operations, 16 general-information reads, or one full read, write, or decompilation. Other operations use three measured runs after one warm-up. Garbage collection runs before each measurement, outside the timer. The 2026-10-26 rows come from one `--full` run (three measured runs per operation); the four rows that need `--compare` keep their 2026-09-12 values. Merge and validate rows were rerun alone three times on a quiet machine, 1.0.1 and 1.1.0 side by side: medians of the three runs were 177.6–200.8 ms and 177–185 ms for validation, 909–937 ms and 911–938 ms for merging 2 patches, and 7,313–7,405 ms and 7,476–7,500 ms for merge and apply 8. The differences in the table above are noise except possibly about 2% on merge and apply. On 2026-10-26 1.0.1 and 1.1.0 were also run back to back with the same `--full` set, and four times alternating with the read, write, hash and MD5 set: every row differed by less than 5% except sprite export (45.8 s to 19.5 s, the intended speed-up) and sub-millisecond GML rows and decompilation (10 to 17%, a spread seen between identical runs). The alternating read, write, hash and MD5 medians differed by -0.3, +3.0, +0.8 and +1.8%.


Comparison sources: [UndertaleModLib f43e12c](https://github.com/UnderminersTeam/UndertaleModTool/tree/f43e12c445c37d50dc6244caa12ccab232983f3f) and [Underanalyzer 4ff50a8](https://github.com/UnderminersTeam/Underanalyzer/tree/4ff50a866b4c1a7acee8cebe6a56d6a48709b453). The time ratio is the compared library's median divided by G3MLib's median; above 1 means G3MLib was faster. A dash means no equivalent operation was measured, not that the other library cannot perform it.

| Operation | G3MLib, ms | UndertaleModLib, ms | Underanalyzer, ms | Time ratio | G3MLib, MiB | UndertaleModLib, MiB | Underanalyzer, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Read complete data file | 671.480 (616.698–684.573) | 955.275 (945.134–1,013.547) | — | 1.49× | 237.166 | 250.105 | — |
| Read general information only | 0.894 (0.868–1.065) | 1.533 (1.411–2.507) | — | 1.72× | 0.917 | 12.396 | — |
| Write complete data file to memory | 415.741 (390.843–416.339) | 431.361 (410.292–497.096) | — | 1.03× | 352.154 | 352.232 | — |
| Hash all resources | 1,555.855 (1,501.738–1,615.721) | — | — | — | 6,158.359 | — | — |
| Compute MD5 synchronously | 162.457 (161.941–168.548) | — | — | — | 1.003 | — | — |
| Compute MD5 asynchronously | 167.962 (165.587–171.370) | — | — | — | 1.004 | — | — |
| Build data-info snapshot | 1.448 (1.373–1.468) | — | — | — | 0.849 | — | — |
| Write analysis cache | 24.456 (20.004–27.168) | — | — | — | 0.081 | — | — |
| Read analysis cache | 184.284 (181.093–192.261) | — | — | — | 9.221 | — | — |
| Export all sprite metadata and PNG frames | 18,359.980 (18,214.638–20,230.103) | — | — | — | 13,774.423 | — | — |
| Parse 200-line GML source | 0.145 (0.135–0.176) | — | 0.158 (0.138–0.173) | 1.09× | 0.269 | — | 0.268 |
| Compile 200-line GML source | 0.174 (0.146–0.185) | — | 0.169 (0.147–0.193) | ≈1.00× | 0.323 | — | 0.322 |
| Decompile largest code entry (9,283 instructions) | 11.585 (10.469–12.137) | — | 20.328 (19.702–22.126) | 1.75× | 23.872 | — | 22.376 |
| Validate patch | 185.535 (181.834–188.875) | — | — | — | 1.036 | — | — |
| Create patch | 5,138.426 (5,124.262–5,165.792) | — | — | — | 12,761.905 | — | — |
| Apply patch | 6,636.959 (6,184.126–6,856.011) | — | — | — | 1,072.969 | — | — |
| Create standard diff report | 9,902.653 (9,866.174–9,919.007) | — | — | — | 15,012.817 | — | — |
| Create full diff report | 10,009.991 (9,972.065–10,056.103) | — | — | — | 15,068.463 | — | — |
| Merge 2 independent patches | 1,135.663 (1,097.555–1,148.916) | — | — | — | 302.125 | — | — |
| Merge 4 independent patches | 1,027.896 (895.233–1,073.033) | — | — | — | 304.190 | — | — |
| Merge 8 independent patches | 1,063.303 (950.648–1,063.477) | — | — | — | 308.324 | — | — |
| Merge and apply 8 independent patches | 7,974.408 (7,868.858–8,136.657) | — | — | — | 1,407.305 | — | — |
| Build reference index | 160.194 (147.862–161.010) | — | — | — | 187.597 | — | — |
| Rename 100 sprites (plan, apply, undo) | 58.413 (57.480–58.893) | — | — | — | 1.890 | — | — |
| Plan texture page repack | 20,052.443 (19,906.611–21,065.928) | — | — | — | 29,899.418 | — | — |
| Import all sprites into a new data file | 163,342.798 (163,082.832–164,410.147) | — | — | — | 7,644.218 | — | — |

The four rows after the merge rows were added in 1.1.0 and come from one `--full` run on 2026-10-26 on a quiet machine. The repack row only computes a plan and decodes every texture page of the game; the sprite import row imports the 31,388 exported PNG frames of the sprite export row into an empty data file, including packing and mask generation.

Reads reopen the file with a warm filesystem cache. Writes serialize an already loaded model to memory. The comparison checks room, code, and string counts, plus the selected code entry's instruction count. The upstream partial reader accepts its expected unread-tail warning; other warnings fail the run.

GML input contains 200 lines, from `var value0 = 0 + 1;` through `var value199 = 199 + 1;`, with a fresh mock game context per operation. Parsing includes tokenization and parse-tree post-processing; compilation also generates bytecode, without linking. Decompilation includes a fresh global context and function lookup initialization: G3MLib's context for its analyzer, UndertaleModLib's context for Underanalyzer. These timings include that integration work. The compilation ranges overlap; its small median difference does not establish a consistent speed advantage for either library.

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
