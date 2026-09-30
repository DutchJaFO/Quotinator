using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #348: a backup exists to make a startup or a destructive action safe, so a backup that cannot be taken
/// is a failure to report with options attached. There are several ways it can fail, each with its own
/// remedy, so every caller has to be told <em>which</em> one it hit.
/// <para>
/// Before this issue the obstacles arrived as two shapes: a <see langword="null"/> meaning "budget",
/// "insufficient disk space" and "no backup attempted" all at once, and one <c>catch (Exception)</c>
/// covering three faults with three different remedies. These tests hold each obstacle to its own name,
/// one statement per test.
/// </para>
/// <para>
/// They call <c>CreateBackup</c> directly rather than driving each failure state through a full
/// initialisation: attribution is the unit under test here, and the behaviour each outcome then produces
/// (degrading, refusing, overriding) is covered through the public paths.
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

    /// <summary>
    /// A budget of 0 GB cannot hold any backup, so the check rejects before anything is written:
    /// deterministic, and independent of how large the database happens to be.
    /// </summary>
    [TestMethod]
    public void BudgetExceeded_IsReportedAsBudgetExceeded()
    {
        using SqliteConnection connection = SeededDatabase();

        DatabaseBackupResult result = CreateInitializer(maxBackupStorageGb: 0).CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.BudgetExceeded, result.Outcome);
    }

    [TestMethod]
    public void InsufficientDiskSpace_IsReportedAsInsufficientDiskSpace()
    {
        using SqliteConnection connection = SeededDatabase();

        DatabaseBackupResult result = CreateInitializer(diskSpaceProvider: new ZeroFreeSpaceProvider()).CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.InsufficientDiskSpace, result.Outcome);
    }

    [TestMethod]
    public void UnwritableBackupsDirectory_IsReportedAsDestinationDirectoryNotWritable()
    {
        using SqliteConnection connection = SeededDatabase();
        BlockTheBackupsDirectory();

        DatabaseBackupResult result = CreateInitializer().CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.DestinationDirectoryNotWritable, result.Outcome);
    }

    [TestMethod]
    public void UnwritableBackupsDirectory_CarriesTheUnderlyingError()
    {
        using SqliteConnection connection = SeededDatabase();
        BlockTheBackupsDirectory();

        DatabaseBackupResult result = CreateInitializer().CreateBackup(connection, fromVersion: 1);

        Assert.IsNotNull(result.Error, "the underlying failure is carried, not swallowed");
    }

    /// <summary>
    /// The destination is fine here; it is the source that cannot be read. Before #348 this arrived
    /// identical to an unwritable destination, and the two have opposite remedies.
    /// </summary>
    [TestMethod]
    public void CorruptSourceDatabase_IsReportedAsSourceUnreadable()
    {
        using SqliteConnection connection = CorruptDatabase(out string corruptPath);

        DatabaseBackupResult result = CreateInitializer(dbPath: corruptPath).CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.SourceUnreadable, result.Outcome);
    }

    [TestMethod]
    public void CorruptSourceDatabase_CarriesTheUnderlyingError()
    {
        using SqliteConnection connection = CorruptDatabase(out string corruptPath);

        DatabaseBackupResult result = CreateInitializer(dbPath: corruptPath).CreateBackup(connection, fromVersion: 1);

        Assert.IsNotNull(result.Error, "the SQLite error naming the corruption is carried, not swallowed");
    }

    /// <summary>
    /// A closed source connection: the copy cannot even begin, and the failure is not a
    /// <see cref="SqliteException"/>. A failure the classifier has no name for is reported as unnamed
    /// rather than as whichever named obstacle looks closest.
    /// </summary>
    [TestMethod]
    public void CopyFailureThatIsNotASqliteError_IsReportedAsUnclassified()
    {
        using SqliteConnection connection = SeededDatabase();
        connection.Close();

        DatabaseBackupResult result = CreateInitializer().CreateBackup(connection, fromVersion: 1);

        Assert.AreEqual(BackupOutcome.Unclassified, result.Outcome);
    }

    [TestMethod]
    public void CopyFailureThatIsNotASqliteError_CarriesTheUnderlyingError()
    {
        using SqliteConnection connection = SeededDatabase();
        connection.Close();

        DatabaseBackupResult result = CreateInitializer().CreateBackup(connection, fromVersion: 1);

        Assert.IsNotNull(result.Error, "an unnamed obstacle is only actionable if it carries the real error");
    }

    [TestMethod]
    public void SucceedingBackup_ReportsWhereItWrote()
    {
        using SqliteConnection connection = SeededDatabase();

        DatabaseBackupResult result = CreateInitializer().CreateBackup(connection, fromVersion: 1);

        Assert.IsNotNull(result.Path);
    }

    [TestMethod]
    public void SucceedingBackup_WritesTheFileItReports()
    {
        using SqliteConnection connection = SeededDatabase();

        DatabaseBackupResult result = CreateInitializer().CreateBackup(connection, fromVersion: 1);

        Assert.IsTrue(File.Exists(result.Path), "a backup reported as taken must exist on disk");
    }

    private SqliteConnection SeededDatabase()
    {
        SqliteConnection connection = new($"Data Source={_dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
        return connection;
    }

    private SqliteConnection CorruptDatabase(out string corruptPath)
    {
        corruptPath = Path.Combine(_tempDir, "corrupt.db");
        File.WriteAllText(corruptPath, "this file is not a SQLite database");

        SqliteConnection connection = new($"Data Source={corruptPath}");
        connection.Open();
        return connection;
    }

    // A file sitting where the backups directory belongs: Directory.CreateDirectory throws IOException,
    // deterministically and identically on Windows and Linux, which an ACL would not.
    private void BlockTheBackupsDirectory() => File.WriteAllText(_backups, "not a directory");

    private DatabaseInitializer CreateInitializer(
        int maxBackupStorageGb = 1, IDiskSpaceProvider? diskSpaceProvider = null, string? dbPath = null)
    {
        string path = dbPath ?? _dbPath;
        SqliteConnectionFactory factory = new(path);
        DatabaseOptions options = new()
        {
            DbPath = path,
            BackupsPath = _backups,
            MaxBackupStorageGb = maxBackupStorageGb,
        };

        return new DatabaseInitializer(factory, options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, diskSpaceProvider ?? new DiskSpaceProvider());
    }

    private sealed class ZeroFreeSpaceProvider : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => 0L;
    }
}
