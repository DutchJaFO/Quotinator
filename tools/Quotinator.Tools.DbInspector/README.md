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

## Read-only by design

The connection opens with `Mode=ReadOnly` (`ConnectionStrings.BuildReadOnly`) — no query, however it's crafted, can `INSERT`/`UPDATE`/`DELETE`/`DROP`. This is deliberate, and is this tool's equivalent of the project's mandatory SQL policy (`CLAUDE.md` → SQL injection policy): the rest of the codebase enforces safety by parameterising every query, but this tool's entire purpose is running arbitrary, non-parameterisable query *text* supplied directly by the developer — there's no fixed structure to bind parameters into. Read-only mode is the correct analogous protection: it can't do damage regardless of what's typed, and it also means this tool won't read as a SQL-injection-shaped pattern to security scanners the way an unguarded `connection.Query(freeformSql)` normally would, since there's no write capability to exploit even if the "injection" succeeded.

Covered by `ConnectionStringsTests` (`tests/Quotinator.Tools.DbInspector.Tests`) — real SQLite file, not mocked: confirms `SELECT` works, confirms `UPDATE`/`INSERT` throw, and confirms data is provably unchanged after a rejected write attempt.

`Mode=ReadOnly` is documented, supported Microsoft.Data.Sqlite syntax — see the [official connection strings reference](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings), whose own example for this exact scenario is `Data Source=Reference.db;Mode=ReadOnly`, described as "a read-only database that cannot be modified by the app."

This also means the tool can never create a database file that doesn't already exist — SQLite rejects the open outright in read-only mode if the path is missing.

**WAL-mode caveat:** Quotinator's real databases run in [WAL mode](https://sqlite.org/wal.html) (`DatabaseInitializer.EnableWal`), and SQLite's own WAL documentation notes a read-only connection can only open a WAL-mode database if the `-shm`/`-wal` files already exist (or can be created) — since SQLite 3.22.0 (2018), this is the normal case for any database that's been written to at least once, which in practice is every Quotinator database you'd ever want to inspect. If you ever point this tool at a WAL-mode `.db` file with no accompanying `-shm`/`-wal` files present and no write access to the containing directory, the open itself may fail (not a security concern — just a "can't open" error, never a partial or unsafe open). Verified against this exact scenario in `BuildReadOnly_WalModeDatabaseWithExistingShmWalFiles_OpensAndStaysReadOnly`.

## Design notes

- `ArgsParser`, `TableFormatter`, and `ConnectionStrings` are the only pieces of logic — all pure functions, all unit-tested in `tests/Quotinator.Tools.DbInspector.Tests`. `Program.cs` itself (argument-to-exit-code wiring, the actual `SqliteConnection`/Dapper call) is intentionally left untested — it's a thin I/O shim with no independent logic worth covering.
- Uses Dapper's dynamic `Query` — there's no fixed result shape to model, since the whole point is running arbitrary queries.
- No `appsettings.json`, no DI container, no config — just two required CLI arguments. Keep it that way; if this tool starts growing features, that's a signal it should become a real admin endpoint instead (see `AdminEndpoints.cs`), not a bigger CLI tool.
