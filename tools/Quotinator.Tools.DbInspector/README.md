# Quotinator.Tools.DbInspector

A developer-only command-line tool for running an arbitrary SQL query against a Quotinator SQLite database file and printing the result as an aligned table. Not part of the shipped product — never referenced by `src/Quotinator.Api` and never built into the Docker image.

## Why this exists

Quotinator's runtime data directory (`data/quotinatordata.db` in VS, `/app/data/quotinatordata.db` or `/data/quotinatordata.db` in Docker/HA) is a plain SQLite file. Inspecting it during development — confirming a migration applied correctly, checking `ImportBatches` provenance, spot-checking seeded data — otherwise means installing a separate SQLite browser or fighting `dotnet-script`'s native-library resolution (it doesn't reliably locate the native `e_sqlite3` library, since it isn't an SDK-style project that manages runtime asset copying). This tool is a real `dotnet run` console project, so native asset resolution works the same way it does for every other project in this solution.

## Usage

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db <path-to-db-file> --sql "<query>"
```

Both `--db` and `--sql` are required; the tool prints a usage message and exits with code 1 if either is missing. On a SQL error, the message is printed to stderr and the tool exits with code 1.

### Examples

Check `ImportBatches` provenance (the most common use case — confirming `System`/`Seed`/`UserSeed`/`Import` classification):

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- \
  --db "C:/repos/Quotinator/src/Quotinator.Api/bin/Debug/net10.0/data/quotinatordata.db" \
  --sql "SELECT Name, Type, Url FROM ImportBatches WHERE IsDeleted = 0 ORDER BY ImportedAt"
```

Inspecting a database file produced by a Docker container: if the container was run with a bind-mounted volume (`-v <host-path>:/data`), the database file is directly accessible on the host filesystem at `<host-path>/quotinatordata.db` — point `--db` at that path directly; the container doesn't need to be running.

Any table can be queried this way — `Quotes`, `Sources`, `AuditEntries`, `SchemaVersion`, etc.

## Design notes

- `ArgsParser` and `TableFormatter` are the only two pieces of logic — both pure functions, both unit-tested in `tests/Quotinator.Tools.DbInspector.Tests`. `Program.cs` itself (argument-to-exit-code wiring, the actual `SqliteConnection`/Dapper call) is intentionally left untested — it's a thin I/O shim with no independent logic worth covering.
- Uses Dapper's dynamic `Query` — there's no fixed result shape to model, since the whole point is running arbitrary queries.
- No `appsettings.json`, no DI container, no config — just two required CLI arguments. Keep it that way; if this tool starts growing features, that's a signal it should become a real admin endpoint instead (see `AdminEndpoints.cs`), not a bigger CLI tool.
