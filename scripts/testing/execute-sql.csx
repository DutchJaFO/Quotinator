#!/usr/bin/env dotnet-script
#nullable enable
// Runs arbitrary, non-parameterised SQL against a Quotinator SQLite database file with a normal
// (writable) connection — the deliberate counterpart to tools/Quotinator.Tools.DbInspector, which
// opens read-only by design and therefore cannot run DDL/DML. Exists specifically to break/repair a
// database file on the host side during manual verification (e.g.
// docs/automated-testing/startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md,
// which needs to drop a table to reproduce a schema/version mismatch against a running Docker
// container's bind-mounted data directory) — never referenced by src/Quotinator.Api, never built into
// the Docker image, developer-only like DbInspector.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/execute-sql.csx -- --db <path-to-db-file> --sql "<statement(s)>"
//
// Options:
//   --db   <path>   Path to the SQLite database file (required)
//   --sql  <text>   One or more semicolon-separated SQL statements to execute (required)

#r "nuget: Microsoft.Data.Sqlite, 10.0.10"
using Microsoft.Data.Sqlite;

var dbArg  = Args.SkipWhile(a => a != "--db").Skip(1).FirstOrDefault();
var sqlArg = Args.SkipWhile(a => a != "--sql").Skip(1).FirstOrDefault();

if (string.IsNullOrEmpty(dbArg) || string.IsNullOrEmpty(sqlArg))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/execute-sql.csx -- --db <path> --sql \"<statement(s)>\"");
    Environment.Exit(1);
    return;
}

if (!File.Exists(dbArg))
{
    Console.Error.WriteLine($"Database file not found: {dbArg}");
    Environment.Exit(1);
    return;
}

using (var connection = new SqliteConnection($"Data Source={dbArg}"))
{
    connection.Open();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = sqlArg;
        try
        {
            var rowsAffected = command.ExecuteNonQuery();
            Console.WriteLine($"OK — {rowsAffected} row(s) affected.");
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine($"SQL error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
