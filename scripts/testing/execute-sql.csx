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
//   dotnet-script scripts/testing/execute-sql.csx -- --db <path-to-db-file> --sql-file <path>
//
// Options:
//   --db       <path>   Path to the SQLite database file (required)
//   --sql      <text>   One or more semicolon-separated SQL statements to execute
//   --sql-file <path>   Read the statements from a file instead. Required whenever the SQL contains a
//                       double quote — a JSON literal, say. Windows PowerShell 5.1 strips double
//                       quotes out of an argument on its way to a native process, so
//                       --sql "INSERT ... VALUES ('{""a"":1}')" arrives as {a:1} and is stored as
//                       corrupt data rather than failing. Measured, not theoretical.
//
// Exactly one of --sql and --sql-file is given.

#r "nuget: Microsoft.Data.Sqlite, 10.0.10"
using Microsoft.Data.Sqlite;

string? dbArg      = Args.SkipWhile(a => a != "--db").Skip(1).FirstOrDefault();
string? sqlArg     = Args.SkipWhile(a => a != "--sql").Skip(1).FirstOrDefault();
string? sqlFileArg = Args.SkipWhile(a => a != "--sql-file").Skip(1).FirstOrDefault();

if (string.IsNullOrEmpty(dbArg) || string.IsNullOrEmpty(sqlArg) == string.IsNullOrEmpty(sqlFileArg))
{
    Console.Error.WriteLine(
        "Usage: dotnet-script scripts/testing/execute-sql.csx -- --db <path> (--sql \"<statement(s)>\" | --sql-file <path>)");
    Environment.Exit(1);
    return;
}

if (!File.Exists(dbArg))
{
    Console.Error.WriteLine($"Database file not found: {dbArg}");
    Environment.Exit(1);
    return;
}

if (sqlFileArg is not null && !File.Exists(sqlFileArg))
{
    Console.Error.WriteLine($"SQL file not found: {sqlFileArg}");
    Environment.Exit(1);
    return;
}

string sql = sqlFileArg is null ? sqlArg! : File.ReadAllText(sqlFileArg);

using (SqliteConnection connection = new($"Data Source={dbArg}"))
{
    connection.Open();
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = sql;
        try
        {
            int rowsAffected = command.ExecuteNonQuery();
            Console.WriteLine($"OK — {rowsAffected} row(s) affected.");
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine($"SQL error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
