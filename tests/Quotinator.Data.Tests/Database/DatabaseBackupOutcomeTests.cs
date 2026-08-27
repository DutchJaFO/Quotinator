using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #348 — a backup exists to make a startup or a destructive action safe, so a backup that cannot be
/// taken is a failure to report with options attached. There are five ways it can fail and five
/// different remedies, so every caller has to be told <em>which</em> one it hit.
/// <para>
/// Before this issue, the five arrived as two shapes: a <see langword="null"/> meaning "budget",
/// "insufficient disk space" and "no backup attempted" all at once, and one
/// <c>catch (Exception)</c> covering three faults with three different remedies. These tests hold each
/// variant to its own name.
/// </para>
/// <para>
/// They call <c>CreateBackup</c> directly rather than driving five different failure states through a
/// full initialisation. Attribution is the unit under test here; the behaviour each outcome then
/// produces — degrading, refusing, overriding — is covered separately through the public paths.
/// </para>
/// </summary>
[TestClass]
public class DatabaseBackupOutcomeTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quotinator-348-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void BudgetExceeded_IsReportedAsBudgetExceeded()
    {
        // A budget of 0 GB cannot accommodate any backup at all, so the pre-flight rejects before
        // anything is written — deterministic, and independent of how large the database happens to be.
        using SqliteConnection connection = SeededDatabase();
        DatabaseInitializer initializer = CreateInitializer(maxBackupStorageGb: 0);

        DatabaseBackupResult result = initializer.CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.BudgetExceeded, result.Outcome);
        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Path, "no file was written, so there is no path to report");
    }

    [TestMethod]
    public void InsufficientDiskSpace_IsReportedAsInsufficientDiskSpace()
    {
        using SqliteConnection connection = SeededDatabase();
        DatabaseInitializer initializer = CreateInitializer(diskSpaceProvider: new ZeroFreeSpaceProvider());

        DatabaseBackupResult result = initializer.CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.InsufficientDiskSpace, result.Outcome);
        Assert.IsNull(result.Path);
    }

    [TestMethod]
    public void UnwritableBackupsDirectory_IsReportedAsDestinationDirectoryNotWritable()
    {
        // A file sitting where the backups directory belongs: Directory.CreateDirectory throws
        // IOException, deterministically and identically on Windows and Linux — the same technique
        // #326 uses for the keys/ directory, rather than an ACL that behaves differently per platform.
        using SqliteConnection connection = SeededDatabase();
        File.WriteAllText(_backups, "not a directory");
        DatabaseInitializer initializer = CreateInitializer();

        DatabaseBackupResult result = initializer.CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.DestinationDirectoryNotWritable, result.Outcome);
        Assert.IsNotNull(result.Error, "the underlying failure is carried, not swallowed");
    }

    [TestMethod]
    public void CorruptSourceDatabase_IsReportedAsSourceUnreadable()
    {
        // The destination is fine here — it is the source that cannot be read. That distinction is the
        // whole point: before #348 this arrived identical to an unwritable destination, and the two
        // have opposite remedies.
        string corruptPath = Path.Combine(_tempDir, "corrupt.db");
        File.WriteAllText(corruptPath, "this file is not a SQLite database");

        using SqliteConnection connection = new SqliteConnection($"Data Source={corruptPath}");
        connection.Open();
        DatabaseInitializer initializer = CreateInitializer(dbPath: corruptPath);

        DatabaseBackupResult result = initializer.CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.SourceUnreadable, result.Outcome);
        Assert.IsNull(result.Path);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void SucceedingBackup_ReportsSucceededAndTheFileItWrote()
    {
        // The control. Every assertion above is about a failure being named correctly; this one proves
        // the happy path still reports success and a real path, so a test suite that only ever saw
        // failures could not pass by accident.
        using SqliteConnection connection = SeededDatabase();
        DatabaseInitializer initializer = CreateInitializer();

        DatabaseBackupResult result = initializer.CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.Succeeded, result.Outcome);
        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Path);
        Assert.IsTrue(File.Exists(result.Path), "a succeeded outcome must mean a file actually exists");
        Assert.IsNull(result.Error);
    }

    private SqliteConnection SeededDatabase()
    {
        SqliteConnection connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
        return connection;
    }

    private DatabaseInitializer CreateInitializer(
        int maxBackupStorageGb = 1, IDiskSpaceProvider? diskSpaceProvider = null, string? dbPath = null)
    {
        string path = dbPath ?? _dbPath;
        SqliteConnectionFactory factory = new SqliteConnectionFactory(path);
        DatabaseOptions options = new DatabaseOptions
        {
            DbPath = path,
            BackupsPath = _backups,
            MaxBackupStorageGb = maxBackupStorageGb,
        };

        return new DatabaseInitializer(factory, options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, baseline: null, diskSpaceProvider);
    }

    private sealed class ZeroFreeSpaceProvider : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => 0L;
    }
}
