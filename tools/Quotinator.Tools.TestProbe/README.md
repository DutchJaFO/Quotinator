# Quotinator.Tools.TestProbe

A developer-only command-line tool that holds the logic `docs/automated-testing/` documents need
between single command invocations: waiting for a state, asserting on a JSON response, setting up and
checking files, and counting a container's log lines. Not part of the shipped product: never referenced
by `src/`, never built into the Docker image.

## Why this exists

[ADR 010](../../docs/architecture-decisions/010-repository-is-csharp-only.md) allows PowerShell only to
invoke a single command; any logic beyond that is C#, and logic that outgrows a single `.csx` becomes a
console project under `tools/`. The startup-degradation documents carried that logic in PowerShell
instead (polling loops, field tests, file counts, regex counts over `docker logs`), which is why their
T2 run was stopped (#327, 2026-10-24). This tool is where that logic lives now, tested like any other C#.

It does **not** make HTTP requests for a document's assertions or drive a browser:
[`http.csx`](../../scripts/testing/http.csx) and [`capture-page.csx`](../../scripts/testing/capture-page.csx)
already do that. Both print JSON to stdout, and this tool's `json` command asserts on it.

## Usage

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- <command> [options]
```

Every command exits `0` when its expectations hold and `1` when one does not, naming the expectation that
failed on stderr. That exit code is what stops a document at the step that failed. A command prints the
values it observed to stdout, so the run records what was seen and not only that it passed.

### `wait-field`: poll a JSON endpoint until a field holds one of the given values

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18408/api/v1/health --field status --in unhealthy,healthy
```

Replaces a polling loop. A degraded container answers `503` while it is still starting, with
`status=starting`, so a wait on the status code cannot tell starting from degraded; a wait on the field
can. Prints the JSON body it finally read, so it can be piped into `json`. Gives up after `--timeout`
seconds (default 180) and exits `1`.

### `json`: assert on a JSON document read from stdin

```bash
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/health --expect 503 | dotnet run --project tools/Quotinator.Tools.TestProbe -- json --equals status=unhealthy --not-empty reason
```

| Option | Holds when |
|---|---|
| `--equals <path>=<value>` | the field's value is exactly `<value>` (strings, numbers and booleans compared as their JSON text) |
| `--contains <path>=<text>` | the field is a string containing `<text>`, or an array with an element containing it |
| `--present <path>` | the field exists |
| `--not-empty <path>` | the field exists and is not `null`, an empty string or an empty array |
| `--greater-than <path>=<number>` | the field is a number greater than `<number>` (a seeded count above `0`) |

Every option is repeatable. A `<path>` is property names joined by dots (`database.quotes`).

### `file`: set up and check files for a test

| Option | Does |
|---|---|
| `--write-text <path> <text>` | Writes `<text>` to `<path>`, replacing anything there |
| `--truncate-half <path>` | Cuts the file to half its length and prints both lengths |
| `--sparse <path> <bytes>` | Creates a sparse file of `<bytes>` bytes |
| `--delete <path>` | Deletes a file, or a directory with everything in it, if it exists |
| `--expect-file <path>` | Holds when `<path>` is a file |
| `--expect-directory <path>` | Holds when `<path>` is a directory |
| `--expect-empty-directory <path>` | Holds when `<path>` is a directory with nothing in it |
| `--expect-length <path> <bytes>` | Holds when the file is exactly `<bytes>` long |
| `--expect-matches <directory> <pattern> <minimum>` | Holds when at least `<minimum>` files in `<directory>` match `<pattern>` |

### `log`: count a pattern in a container's log

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-05 --pattern "SQLite Error 14" --at-least 1
```

Counts occurrences, not matching lines, in `docker logs <container>` (stdout and stderr both). Takes
`--at-least <n>` or `--exactly <n>`, and prints the count.

## Design notes

- Each command is its own class with one job (`Commands/`); `CommandDispatcher` only selects the command
  and maps its result to an exit code, and `Program.cs` only hands it the console.
- The parsing and the expectations are pure functions, unit-tested in
  `tests/Quotinator.Tools.TestProbe.Tests` per `docs/testing-policy.md`, each with a case where it holds
  and a case where it must fail. The I/O around them (an HTTP call, `docker logs`, the file system) is a
  thin shim.
