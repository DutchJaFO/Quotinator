#r "nuget: Microsoft.Data.Sqlite, 10.0.10"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

// Probes how SQLite behaves when the storage underneath it is constrained: an unwritable directory,
// a read-only file, a database path that is not a file at all. Written for #326, kept because the
// degraded and read-only scenarios in docs/automated-testing/startup-and-degradation/ need a way to
// establish what the storage layer actually does before asserting what the application does on top
// of it.
//
// Run:  dotnet script scripts/testing/sqlite-storage-probe.csx
//
// Every case is self-contained: it builds its own database under a temp root, constrains it, probes,
// and restores permissions in a finally block so nothing is left locked. Windows constrains via an
// icacls deny ACE; everything else via chmod. Both are removed again before the run ends.
//
// What the cases establish (measured 2026-08-20, Microsoft.Data.Sqlite 10.0.10) is recorded in
// docs/milestones/notification-system/326-startup-degrades-on-unwritable-data-directory-plan.md,
// step 1. Re-run it rather than trusting that record if the SQLite or Microsoft.Data.Sqlite version
// changes: it is the measurement, not the conclusion, that is authoritative.

static readonly string Root = Path.Combine(Path.GetTempPath(), "quotinator-sqlite-storage-probe");
static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
static readonly string WindowsUser = Environment.UserDomainName + "\\" + Environment.UserName;

static void Run(string fileName, string args)
{
    using Process process = Process.Start(new ProcessStartInfo(fileName, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
    });
    process.WaitForExit();
}

static void BlockDirectoryWrites(string directory)
{
    if (IsWindows) Run("icacls", $"\"{directory}\" /deny \"{WindowsUser}\":(W)");
    else Run("chmod", $"a-w \"{directory}\"");
}

static void RestoreDirectoryWrites(string directory)
{
    if (IsWindows) Run("icacls", $"\"{directory}\" /remove:d \"{WindowsUser}\"");
    else Run("chmod", $"u+w \"{directory}\"");
}

// Mirrors SqliteConnectionFactory.CreateConnection (temp_store=MEMORY, #294) followed by
// DatabaseInitializer.InitialiseAsync's opening PRAGMA — the exact sequence #326's crash came from.
static void OpenAndEnableWal(string databasePath)
{
    using SqliteConnection connection = new($"Data Source={databasePath}");
    connection.Open();
    Execute(connection, "PRAGMA temp_store=MEMORY;");
    Execute(connection, "PRAGMA journal_mode=WAL;");
}

static void OpenAndSelect(string databasePath)
{
    using SqliteConnection connection = new($"Data Source={databasePath}");
    connection.Open();
    Execute(connection, "SELECT COUNT(*) FROM probe;");
}

static void OpenAndInsert(string databasePath)
{
    using SqliteConnection connection = new($"Data Source={databasePath}");
    connection.Open();
    Execute(connection, "INSERT INTO probe(value) VALUES ('written');");
}

static void Execute(SqliteConnection connection, string sql)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static void Seed(string databasePath, string journalMode)
{
    using (SqliteConnection connection = new($"Data Source={databasePath}"))
    {
        connection.Open();
        Execute(connection, $"PRAGMA journal_mode={journalMode};");
        Execute(connection, "CREATE TABLE IF NOT EXISTS probe(value TEXT);");
        Execute(connection, "INSERT INTO probe(value) VALUES ('seed');");
    }

    // Without this the pooled connection stays open and its sidecars stay on disk, which is the
    // difference between two of the cases below — so it must be explicit, not incidental.
    SqliteConnection.ClearAllPools();
}

static void Probe(string label, Action action)
{
    try
    {
        action();
        Console.WriteLine($"  {label,-46} OK");
    }
    catch (SqliteException ex)
    {
        Console.WriteLine($"  {label,-46} SqliteException code={ex.SqliteErrorCode} extended={ex.SqliteExtendedErrorCode} :: {ex.Message.Split('\n')[0]}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {label,-46} {ex.GetType().Name} :: {ex.Message.Split('\n')[0]}");
    }
}

static string Sidecars(string directory)
{
    List<string> names = [];
    foreach (string path in Directory.GetFiles(directory))
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith("-wal") || name.EndsWith("-shm")) names.Add(name);
    }
    return names.Count == 0 ? "(none)" : string.Join(", ", names);
}

static string NewCase(string name)
{
    string directory = Path.Combine(Root, name);
    Directory.CreateDirectory(directory);
    return directory;
}

// A previous interrupted run can leave a deny ACE behind, which would make the cleanup below fail.
if (Directory.Exists(Root))
{
    foreach (string directory in Directory.GetDirectories(Root)) RestoreDirectoryWrites(directory);
    Directory.Delete(Root, recursive: true);
}
Directory.CreateDirectory(Root);

Console.WriteLine();
Console.WriteLine("A. database path is a DIRECTORY");
Console.WriteLine("   The portable, ACL-free way to make SQLite fail to open at the same throw site as the live");
Console.WriteLine("   #326 crash. Used by StartupResilienceTests.");
{
    string directory = NewCase("directory-at-database-path");
    string databasePath = Path.Combine(directory, "quotinatordata.db");
    Directory.CreateDirectory(databasePath);
    Probe("open + PRAGMA journal_mode=WAL", () => OpenAndEnableWal(databasePath));
}

Console.WriteLine();
Console.WriteLine("B. WAL database, sidecars ABSENT, directory unwritable");
Console.WriteLine("   What a cleanly stopped container leaves behind. This is #326's live failure.");
{
    string directory = NewCase("wal-no-sidecars");
    string databasePath = Path.Combine(directory, "quotinatordata.db");
    Seed(databasePath, "WAL");
    Console.WriteLine($"  sidecars on disk:                              {Sidecars(directory)}");
    BlockDirectoryWrites(directory);
    try
    {
        Probe("open + PRAGMA journal_mode=WAL", () => OpenAndEnableWal(databasePath));
        Probe("open + SELECT", () => OpenAndSelect(databasePath));
        Probe("open + INSERT", () => OpenAndInsert(databasePath));
    }
    finally { RestoreDirectoryWrites(directory); }
}

Console.WriteLine();
Console.WriteLine("C. WAL database, sidecars PRESENT, directory unwritable");
Console.WriteLine("   What an abruptly stopped container leaves behind — and why #326's control run passed.");
{
    string directory = NewCase("wal-with-sidecars");
    string databasePath = Path.Combine(directory, "quotinatordata.db");
    Seed(databasePath, "WAL");

    using SqliteConnection holder = new($"Data Source={databasePath}");
    holder.Open();
    Execute(holder, "INSERT INTO probe(value) VALUES ('held open');");
    Console.WriteLine($"  sidecars on disk:                              {Sidecars(directory)}");

    BlockDirectoryWrites(directory);
    try
    {
        Probe("open + PRAGMA journal_mode=WAL", () => OpenAndEnableWal(databasePath));
        Probe("open + SELECT", () => OpenAndSelect(databasePath));
    }
    finally { RestoreDirectoryWrites(directory); }
}

Console.WriteLine();
Console.WriteLine("D. DELETE-mode database, directory unwritable");
Console.WriteLine("   Reads work; only the WAL switch fails. The basis of #332's read-only mode and #335's artefact.");
{
    string directory = NewCase("delete-mode");
    string databasePath = Path.Combine(directory, "quotinatordata.db");
    Seed(databasePath, "DELETE");
    BlockDirectoryWrites(directory);
    try
    {
        Probe("open + PRAGMA journal_mode=WAL", () => OpenAndEnableWal(databasePath));
        Probe("open + SELECT", () => OpenAndSelect(databasePath));
    }
    finally { RestoreDirectoryWrites(directory); }
}

Console.WriteLine();
Console.WriteLine("E. writable directory, database FILE read-only");
Console.WriteLine("   The other error code: 8 (SQLITE_READONLY), not 14 (SQLITE_CANTOPEN).");
{
    string directory = NewCase("readonly-file");
    string databasePath = Path.Combine(directory, "quotinatordata.db");
    Seed(databasePath, "DELETE");
    if (IsWindows) File.SetAttributes(databasePath, FileAttributes.ReadOnly);
    else Run("chmod", $"a-w \"{databasePath}\"");
    try
    {
        Probe("open + SELECT", () => OpenAndSelect(databasePath));
        Probe("open + INSERT", () => OpenAndInsert(databasePath));
        Probe("open + PRAGMA journal_mode=WAL", () => OpenAndEnableWal(databasePath));
    }
    finally
    {
        if (IsWindows) File.SetAttributes(databasePath, FileAttributes.Normal);
        else Run("chmod", $"u+w \"{databasePath}\"");
    }
}

Console.WriteLine();
Console.WriteLine("F. keys/ directory cannot be created (a FILE of that name is in the way)");
Console.WriteLine("   The portable sabotage for #326's pre-Kestrel Directory.CreateDirectory crash.");
{
    string directory = NewCase("keys-blocked");
    File.WriteAllText(Path.Combine(directory, "keys"), "not a directory");
    Probe("Directory.CreateDirectory(dataDir/keys)", () => Directory.CreateDirectory(Path.Combine(directory, "keys")));
}

Console.WriteLine();
