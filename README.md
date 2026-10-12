# tool-release-checker

**Catch Debug builds before they ship.** Point `relcheck` at a release folder
and it tells you, for every `.dll` and `.exe`, whether it was built in Debug or
Release and for which .NET version. In big solutions with dozens of projects
it's easy for one assembly to slip into a release drop as a Debug build, slower
and with debug-only code still inside. `relcheck` finds it, and in CI it fails
the build (exit code `1`).

```
relcheck .\publish
```

## What it reports

A small .NET 9 command-line tool that scans a directory for `.dll` and `.exe`
files and reports the build characteristics of each managed assembly. Instead
of a single "Debug/Release" verdict, it shows the target framework plus
**three independent build flags**, because "Debug" actually conflates two
unrelated build switches (plus a useful heuristic):

| Flag          | What it means                                                        | Source |
| ------------- | -------------------------------------------------------------------- | ------ |
| **FRAMEWORK** | Target framework the assembly was built for (e.g. `.NET 8.0`, `.NET Framework 4.7.2`). `unknown` for assemblies targeting < .NET 4.0. | `TargetFrameworkAttribute`. |
| **OPTIMIZED** | JIT optimizer enabled (the `<Optimize>` switch).                     | `DebuggableAttribute` optimizer bit — reliable. |
| **DBG INFO**  | JIT tracking / debuggable codegen (sequence points). Informational — Release builds often ship this too. | `DebuggableAttribute` JIT-tracking bit. |
| **DBG CODE**  | DEBUG-conditional code (e.g. `Debug.Assert`) appears to have survived compilation. | Heuristic — see below. |

It's meant for gating release pipelines: `relcheck` exits with code `1` if any
assembly **looks like a Debug build** (unoptimized *or* contains DEBUG code), so
a build step can fail when a Debug binary slips into a release drop.

## Why three flags instead of "Debug/Release"

The Visual Studio "Debug" and "Release" presets flip several switches at once,
but those switches are independent and leave **different traces** in the
compiled DLL:

- **`<Optimize>`** (the optimizer) is recorded in `DebuggableAttribute` and can
  be read back exactly.
- **`DEFINE DEBUG`** (the `DEBUG` conditional-compilation constant) leaves **no
  dedicated marker**. When `DEBUG` is undefined the compiler simply *omits*
  `#if DEBUG` blocks and `[Conditional("DEBUG")]` calls — there is no flag saying
  "DEBUG was off".

So unchecking "Define DEBUG" but leaving optimizations off still produces an
unoptimized assembly, which is why a single label is misleading.

## How it works

Each file is inspected by reading its metadata only — assemblies are **never
executed**:

- **FRAMEWORK** is read from the assembly's `TargetFrameworkAttribute` and
  rendered as a friendly name. This records the *target* the assembly was built
  for, which is what distinguishes 4.7.2 vs 4.8 and .NET 6/7/8/9. Assemblies
  targeting < .NET 4.0 don't carry the attribute and show `unknown`.
- **OPTIMIZED / DBG INFO** are read from the assembly's `DebuggableAttribute`
  via `MetadataLoadContext`.
- **DBG CODE** is a heuristic: every method on `System.Diagnostics.Debug` is
  `[Conditional("DEBUG")]`, so when `DEBUG` is undefined the compiler strips
  those calls and the reference to the `Debug` type disappears from the
  metadata. A surviving reference therefore indicates the assembly was compiled
  with `DEBUG` defined.

  *Limitation:* this catches the BCL `Debug` class only, not your own
  `[Conditional("DEBUG")]` methods (detecting those would require walking IL).

Non-managed DLLs/EXEs are reported as **native**; files that fail to load as
**error**.

Note: for .NET Core / .NET 5+ apps, `MyApp.exe` is a native *apphost*
launcher and shows as **native** — the actual managed code lives in
`MyApp.dll` next to it, which is inspected as usual. .NET Framework `.exe`
files are managed assemblies and get the full set of flags.

## Build

```
dotnet build -c Release
```

## Usage

```
relcheck [directory] [options]
```

### Arguments

| Argument    | Description                            |
| ----------- | -------------------------------------- |
| `directory` | Folder to scan (default: current dir). |

### Options

| Option                 | Description                                                        |
| ---------------------- | ------------------------------------------------------------------ |
| `-r`, `--recurse`      | Scan subdirectories too.                                           |
| `-d`, `--debug-only`   | Show only assemblies that look like Debug builds (unoptimized or with DEBUG code). |
| `-m`, `--managed-only` | Hide native / non-.NET files.                                      |
| `-h`, `--help`         | Show help.                                                         |

### Exit codes

| Code | Meaning                                                            |
| ---- | ------------------------------------------------------------------ |
| `0`  | Completed, no Debug-looking assemblies found.                      |
| `1`  | Completed, at least one Debug or unoptimized assembly found.       |
| `2`  | Invalid directory or enumeration error.                            |

## Examples

Scan the current directory:

```
relcheck
```

Recursively scan a build output folder, showing only Debug-looking assemblies:

```
relcheck ./bin -r -d
```

Gate a release in a pipeline (non-zero exit fails the step):

```
relcheck ./publish -r || echo "Debug assemblies found!"
```

## Output

Results are printed as a color-coded table:

```
FILE     FRAMEWORK  OPTIMIZED  DBG INFO   DBG CODE   SUMMARY
-------  ---------  ---------  ---------  ---------  -------
lib.dll  .NET 8.0   No         Yes        No         Debug build (unoptimized + debug info)

Total: 1   Managed: 1   Debug: 1   Unoptimized: 0   Release: 0   Native/Other: 0
```

`OPTIMIZED` is green when enabled / yellow when disabled, `DBG CODE` is yellow
when present / green when absent, `DBG INFO` is neutral, and native/error rows
are gray/red. The `SUMMARY` column rolls the flags up into one of three
verdicts:

- **Debug** (red) — DEBUG code survived, *or* the optimizer is off while debug
  info (JIT tracking) is on — i.e. the classic Debug configuration.
- **unoptimized** (yellow) — optimizer off, but without the other Debug markers.
- **Release** (green) — optimized, no DEBUG code.

For native/error rows the column instead carries the underlying note. A totals
line follows the table.

## Requirements

- .NET 9 SDK / runtime

## License

Free to use, including commercially and in CI pipelines, and free to
redistribute unmodified. Modifying the code or creating derivative works is not
permitted. See [LICENSE](LICENSE) for the full terms.
