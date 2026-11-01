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
already do that. Each writes the JSON it read to a file (`--body-out`, `--result-out`), and this tool's
`json` command reads that file (`--file`). **Every step is a single command; data passes between
commands through files, never through a shell pipe, here-string or variable** (ADR 010).

## Usage

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- <command> [options]
```

Every command exits `0` when its expectations hold and `1` when one does not, naming the expectation that
failed on stderr. That exit code is what stops a document at the step that failed. A command prints the
values it observed to stdout, so the run records what was seen and not only that it passed.

### `wait-field`: poll a JSON endpoint until a field holds one of the given values

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18408/api/v1/health --field status --in unhealthy,healthy --out .claude/temp/qt-startup-08/health.json
```

Replaces a polling loop. A degraded container answers `503` while it is still starting, with
`status=starting`, so a wait on the status code cannot tell starting from degraded; a wait on the field
can. Prints the JSON body it finally read, and writes it to `--out` when given, so the next step's
`json --file` can assert on it. Gives up after `--timeout` seconds (default 180) and exits `1`.

### `json`: assert on a JSON document read from a file

```bash
dotnet script scripts/testing/http.csx -- --url http://localhost:18408/api/v1/health --expect 503 --body-out .claude/temp/qt-startup-08/health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file .claude/temp/qt-startup-08/health.json --equals status=unhealthy --not-empty reason
```

`--file <path>` names the document. Without it the document is read from stdin, which no document step
uses, since a pipe is shell logic.

| Option | Holds when |
|---|---|
| `--equals <path>=<value>` | the field's value is exactly `<value>` (strings, numbers and booleans compared as their JSON text) |
| `--contains <path>=<text>` | the field is a string containing `<text>`, or an array with an element containing it |
| `--present <path>` | the field exists |
| `--not-empty <path>` | the field exists and is not `null`, an empty string or an empty array |
| `--greater-than <path>=<number>` | the field is a number greater than `<number>` (a seeded count above `0`) |
| `--equals-field <path>=<other path>` | the field's value is exactly the other field's value, for a contract that relates two fields of one response |
| `--length-equals-field <path>=<other path>` | the field is an array with as many elements as the other field's number |
| `--length-equals <path>=<number>` | the field is an array with exactly `<number>` elements |
| `--equals-xml <path>=<file>#<element>` | the field's value is exactly the text of the first `<element>` in the XML file, read now, so a value the repository owns is not copied into a document (`version=Directory.Build.props#Version`) |

Every option is repeatable. A `<path>` is segments joined by dots: a property name, or a zero-based index
into an array (`database.quotes`, `items.0.quote`).

### `file`: set up and check files for a test

| Option | Does |
|---|---|
| `--write-text <path> <text>` | Writes `<text>` to `<path>`, replacing anything there; a missing directory is created, as for `--sparse` |
| `--copy <source> <destination>` | Copies a file, replacing anything at the destination; a missing directory is created. For a fixture a container has to find on disk |
| `--truncate-half <path>` | Cuts the file to half its length and prints both lengths |
| `--sparse <path> <bytes>` | Creates a sparse file of `<bytes>` bytes |
| `--create-directory <path>` | Creates the directory if it is missing; fails when a file is in the way |
| `--delete <path>` | Deletes a file, or a directory with everything in it, if it exists |
| `--expect-file <path>` | Holds when `<path>` is a file |
| `--expect-directory <path>` | Holds when `<path>` is a directory |
| `--expect-empty-directory <path>` | Holds when `<path>` is a directory with nothing in it |
| `--expect-length <path> <bytes>` | Holds when the file is exactly `<bytes>` long |
| `--expect-not-empty <path>` | Holds when `<path>` is a file holding at least one byte |
| `--expect-absent <path>` | Holds when nothing, file or directory, exists at `<path>` |
| `--expect-matches <directory> <pattern> <minimum>` | Holds when at least `<minimum>` files in `<directory>` match `<pattern>` |
| `--expect-match-count <directory> <pattern> <count>` | Holds when exactly `<count>` files in `<directory>` match `<pattern>` |
| `--expect-text <path> <regex>` | Holds when the file's text matches `<regex>`, ignoring case: for a saved body that is not JSON, such as an HTML page |
| `--expect-no-text <path> <regex>` | Holds when the file exists and its text does not match `<regex>`, ignoring case |

### `log`: count a pattern in a container's log

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-05 --pattern "SQLite Error 14" --at-least 1
```

Counts occurrences, not matching lines, in `docker logs <container>` (stdout and stderr both). Takes
`--at-least <n>` or `--exactly <n>`, and prints the count.

**Counting only what was logged since a previous read**, which the suite index requires of a document
that stops or restarts its container:

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\after-restart.mark
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-01-out\after-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
```

`--same-number <regex> <regex>` holds when every occurrence of both patterns captures the same number in its first
group, and each occurs at least once, for two figures that must agree when neither can be predicted.
`--wait-seconds <n>` repeats the check until it holds or `<n>` seconds have passed, for a line that is written after
the application already answers.

`--mark <file>` records the newest line's timestamp; `--since-mark <file>` counts only lines stamped
after it. The timestamps are the Docker daemon's own (`docker logs --timestamps`), so the host clock
plays no part.

### `url`: use a value from a saved response in a later request's URL

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18601/api/v1/import/actions/{actionId}/undo" --value actionId=C:\repos\Quotinator\.claude\temp\qt-import-01-out\pending.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-import-01-out\undo-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-import-01-out\undo-url.txt --expect 204 --status
```

Fills each `{name}` in `--template` and writes the URL to `--out`, printing it. `--value <name>=<file>#<path>` takes a
field from a saved JSON response, with the same path syntax as `json`. `--upper-value` and `--lower-value` do the same
in that case, so a test proving a comparison ignores case sends both forms without assuming which one the server holds. A missing file or field, a value with no placeholder, or a
placeholder with no value exits `1` and writes nothing, so a later request cannot run against a half-built URL.

### `seed-sources`: check a seed's Sources for duplicates and undeclared date variants

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- seed-sources --sources C:\repos\Quotinator\.claude\temp\qt-import-14-out\sources.json --declared data/sources
```

Reads a saved `/masterdata/sources?pageSize=0` response. Fails on an exact duplicate (title, type and date, ignoring
case), a casing duplicate (one title and type under two spellings), a date variant the `sources[]` of no file in
`--declared` names, and an empty list, which would satisfy the other three.

### `seed-noops`: check every no-op resolution is accounted for

```bash
dotnet run --project tools/Quotinator.Tools.TestProbe -- seed-noops --actions C:\repos\Quotinator\.claude\temp\qt-import-14-out\actions.json --rules data/sources
```

Reads a saved `/import/actions?pageSize=0` response. A `ResolvedToExisting` action is accounted for when a
`*conflict-rules.json` file in `--rules` covers its entity, or when the incoming side left empty every field that
differs, an absent field, a `null` and an empty array all counting as empty. Fails on any other, and on there being none
at all.

## Design notes

- Each command is its own class with one job (`Commands/`); `CommandDispatcher` only selects the command
  and maps its result to an exit code, and `Program.cs` only hands it the console.
- The parsing and the expectations are pure functions, unit-tested in
  `tests/Quotinator.Tools.TestProbe.Tests` per `docs/testing-policy.md`, each with a case where it holds
  and a case where it must fail. The I/O around them (an HTTP call, `docker logs`, the file system) is a
  thin shim.
