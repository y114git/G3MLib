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

## Install

G3MLib targets .NET 10.

```shell
dotnet add package G3MLib
```

## Benchmarks

Measured on 2026-09-12 using the `data.win` of Pizza Tower (92,440,092 bytes), Windows x64, .NET 10 Release, Intel Core Ultra 7 155H, and 31 GiB RAM. Tiered compilation was disabled for both libraries with `DOTNET_TieredCompilation=0`. Times are medians, with minimum–maximum in parentheses. MiB columns measure total managed allocations across all threads, not peak memory or native ImageMagick allocations.

The six directly compared operations use seven measured batches after three warm-up batches, alternating library order each round in the same process. Each batch contains 64 parsing or compilation operations, 16 general-information reads, or one full read, write, or decompilation. Other operations use three measured runs after one warm-up. Garbage collection runs before each measurement, outside the timer.

Comparison sources: [UndertaleModLib f43e12c](https://github.com/UnderminersTeam/UndertaleModTool/tree/f43e12c445c37d50dc6244caa12ccab232983f3f) and [Underanalyzer 4ff50a8](https://github.com/UnderminersTeam/Underanalyzer/tree/4ff50a866b4c1a7acee8cebe6a56d6a48709b453). The time ratio is the compared library's median divided by G3MLib's median; above 1 means G3MLib was faster. A dash means no equivalent operation was measured, not that the other library cannot perform it.

| Operation | G3MLib, ms | UndertaleModLib, ms | Underanalyzer, ms | Time ratio | G3MLib, MiB | UndertaleModLib, MiB | Underanalyzer, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Read complete data file | 641.725 (609.285–675.944) | 955.275 (945.134–1,013.547) | — | 1.49× | 237.162 | 250.105 | — |
| Read general information only | 0.894 (0.868–1.065) | 1.533 (1.411–2.507) | — | 1.72× | 0.917 | 12.396 | — |
| Write complete data file to memory | 417.663 (407.387–504.439) | 431.361 (410.292–497.096) | — | 1.03× | 352.152 | 352.232 | — |
| Hash all resources | 1,695.522 (1,654.236–1,732.380) | — | — | — | 6,174.594 | — | — |
| Compute MD5 synchronously | 173.480 (170.935–181.608) | — | — | — | 1.003 | — | — |
| Compute MD5 asynchronously | 183.517 (173.080–191.554) | — | — | — | 1.004 | — | — |
| Build data-info snapshot | 1.503 (1.397–1.792) | — | — | — | 0.849 | — | — |
| Write analysis cache | 20.939 (20.889–21.106) | — | — | — | 0.081 | — | — |
| Read analysis cache | 184.587 (183.880–189.482) | — | — | — | 9.221 | — | — |
| Export all sprite metadata and PNG frames | 51,557.141 (51,309.323–51,602.537) | — | — | — | 6,004.112 | — | — |
| Parse 200-line GML source | 0.145 (0.135–0.176) | — | 0.158 (0.138–0.173) | 1.09× | 0.269 | — | 0.268 |
| Compile 200-line GML source | 0.174 (0.146–0.185) | — | 0.169 (0.147–0.193) | ≈1.00× | 0.323 | — | 0.322 |
| Decompile largest code entry (9,283 instructions) | 11.585 (10.469–12.137) | — | 20.328 (19.702–22.126) | 1.75× | 23.872 | — | 22.376 |
| Validate patch | 167.090 (165.492–172.361) | — | — | — | 1.036 | — | — |
| Create patch | 5,063.311 (4,879.206–5,152.304) | — | — | — | 12,799.328 | — | — |
| Apply patch | 6,340.022 (6,227.197–6,353.047) | — | — | — | 1,072.970 | — | — |
| Create standard diff report | 9,504.684 (9,398.372–9,603.097) | — | — | — | 15,046.908 | — | — |
| Create full diff report | 9,551.802 (9,445.443–9,654.097) | — | — | — | 15,040.595 | — | — |
| Merge 2 independent patches | 942.803 (932.947–974.906) | — | — | — | 302.128 | — | — |
| Merge 4 independent patches | 876.634 (876.071–895.653) | — | — | — | 304.201 | — | — |
| Merge 8 independent patches | 884.474 (856.906–885.603) | — | — | — | 308.347 | — | — |
| Merge and apply 8 independent patches | 7,116.002 (7,097.161–7,274.044) | — | — | — | 1,406.793 | — | — |

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

## License

G3MLib is GPL-3.0-only. It includes some UndertaleModLib-derived code under GPL-3.0 and Underanalyzer-derived code under MPL-2.0. See [LICENSE](LICENSE) and the license notices in the repository.
