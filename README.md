# G3MLib

G3MLib reads, writes, analyzes, decompiles, and modifies GameMaker data files.

The `G3MLib.Analyzer` namespaces provide GML compilation, decompilation, bytecode analysis, and game-specific macro resolution.

The `G3MLib.DataFile` namespaces provide the data-file model, I/O, project serialization, and scripting APIs.

The `G3MLib.Modding` namespaces provide patch creation and application, patch merging, diff reports, resource import/export, integrity repair, caching, and xdelta execution. Hosts supply their own UI through the library's log and progress events.

## Install

G3MLib targets .NET 10.

```shell
dotnet add package G3MLib
```

## Benchmarks

The following medians use the `data.win` from Pizza Tower (92,440,092 bytes), .NET 10 Release, an Intel Core Ultra 7 155H, and 31 GiB RAM. Each operation has one warm-up and three measured runs. UndertaleModLib at commit `f43e12c` and Underanalyzer at commit `4ff50a8` were used for comparison where their public APIs provide the same operation.

Patch and report rows start with that `data.win` and a modified copy whose first room has a different width. “Create patch” produces a `.g3mpatch` from that pair; “Apply patch” applies it to the original; both reports compare the same pair. Merge rows use 2, 4, or 8 independently created room-change patches, and “Merge and apply” writes the eight-patch result to a new `data.win`.

An em dash (—) means the compared product has no publicly exposed equivalent operation to measure, usually because that functionality is absent.

| Operation | G3MLib | UndertaleModLib | Underanalyzer |
| --- | ---: | ---: | ---: |
| Read complete data file | **1,100.74 ms** | 2,754.90 ms | — |
| Read general information only | **3.61 ms** | 6.82 ms | — |
| Write complete data file to memory | **713.98 ms** | 1,297.48 ms | — |
| Hash all resources | 2,755.32 ms | — | — |
| Compute MD5 synchronously | 269.94 ms | — | — |
| Compute MD5 asynchronously | 255.58 ms | — | — |
| Build data-info snapshot | 2.07 ms | — | — |
| Write analysis cache | 303.65 ms | — | — |
| Read analysis cache | 404.09 ms | — | — |
| Export all sprite metadata and PNG frames | 55,496.73 ms | — | — |
| Parse the same 200-line GML source | **2.31 ms** | uses Underanalyzer | 3.32 ms |
| Compile the same 200-line GML source | **1.92 ms** | uses Underanalyzer | 3.31 ms |
| Decompile largest code entry (9,283 instructions) | **14.19 ms** | uses Underanalyzer | 22.27 ms |
| Validate patch | 336.07 ms | — | — |
| Create patch | 5,367.93 ms | — | — |
| Apply patch | 4,427.34 ms | — | — |
| Create standard diff report | 9,771.58 ms | — | — |
| Create full diff report | 9,603.93 ms | — | — |
| Merge 2 independent patches | 939.67 ms | — | — |
| Merge 4 independent patches | 951.26 ms | — | — |
| Merge 8 independent patches | 940.86 ms | — | — |
| Merge and apply 8 independent patches | 5,530.92 ms | — | — |

## License

G3MLib is GPL-3.0-only. It includes some UndertaleModLib-derived code under GPL-3.0 and Underanalyzer-derived code under MPL-2.0. See [LICENSE](LICENSE) and the license notices in the repository.
