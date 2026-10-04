using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #348: <see cref="IDatabaseInitializer.CheckBackupReadiness"/> as its own subject: can a backup be taken,
/// asked before anything is attempted.
/// <para>
/// The property under test is <strong>agreement</strong>. A pre-flight is only worth having if its answer
/// is the one the attempt would give; a check that says "ready" where the attempt fails is worse than no
/// check, because a caller acts on it. So each obstacle has two tests: the check names it, and a real
/// <c>CreateBackup</c> against the same directory reports the same member.
/// </para>
/// </summary>
[TestClass]
public class DatabaseBackupPreflightTests
{
    private TempDirectory _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = new TempDirectory("quotinator_348_preflight_");
        _dbPath = Path.Combine(_tempDir.Path, "test.db");
        _backups = Path.Combine(_tempDir.Path, "backups");

        // A real database, so every check weighs a backup of real size: whether a backup would pass the
        // ceiling depends on what it adds, and an absent file adds nothing.
        using SqliteConnection connection = SeededDatabase();
    }

    [TestCleanup]
    public void TestCleanup() => _tempDir.Dispose();

    [TestMethod]
    public void CheckBackupReadiness_WhenTheBudgetIsExhausted_ReportsBudgetExceeded()
    {
        Assert.AreEqual(BackupOutcome.BudgetExceeded, CreateInitializer(maxBackupStorageGb: 0).CheckBackupReadiness());
    }

    /// <summary>
    /// An operator offered a remedy for one obstacle and then hitting a different one has been sent after
    /// the wrong fault.
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_WhenTheBudgetIsExhausted_AgreesWithTheAttempt()
    {
        DatabaseInitializer initializer = CreateInitializer(maxBackupStorageGb: 0);
        using SqliteConnection connection = SeededDatabase();

        Assert.AreEqual(initializer.CreateBackup(connection, fromVersion: 1).Outcome, initializer.CheckBackupReadiness());
    }

    [TestMethod]
    public void CheckBackupReadiness_WhenTheDestinationIsNotWritable_ReportsDestinationDirectoryNotWritable()
    {
        BlockTheBackupsDirectory();

        Assert.AreEqual(BackupOutcome.DestinationDirectoryNotWritable, CreateInitializer().CheckBackupReadiness());
    }

    [TestMethod]
    public void CheckBackupReadiness_WhenTheDestinationIsNotWritable_AgreesWithTheAttempt()
    {
        BlockTheBackupsDirectory();
        DatabaseInitializer initializer = CreateInitializer();
        using SqliteConnection connection = SeededDatabase();

        Assert.AreEqual(initializer.CreateBackup(connection, fromVersion: 1).Outcome, initializer.CheckBackupReadiness());
    }

    private const long OneGigabyte = 1_073_741_824L;

    /// <summary>
    /// 95% of a 1 GB ceiling: past the 90% operating quota, inside the reserve. The reserve is what lets a
    /// backup still be taken there, with a warning raised, rather than refused (#348, developer
    /// 2026-09-26).
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_InsideTheReserve_ReportsSucceeded()
    {
        FillTheBackupsFolder(OneGigabyte * 95 / 100);

        Assert.AreEqual(BackupOutcome.Succeeded, CreateInitializer().CheckBackupReadiness());
    }

    /// <summary>
    /// One byte short of the ceiling, so only the backup itself would take the folder past it. The quota is
    /// set to 100% so the ceiling is the only limit in play: what refuses here can only be the backup's
    /// own size measured against it.
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_WhenTheBackupWouldPassTheCeiling_ReportsBudgetExceeded()
    {
        FillTheBackupsFolder(OneGigabyte - 1);

        Assert.AreEqual(BackupOutcome.BudgetExceeded, CreateInitializer(quotaPercent: 100).CheckBackupReadiness());
    }

    [TestMethod]
    public void CheckBackupReadiness_WhenTheBackupWouldPassTheCeiling_AgreesWithTheAttempt()
    {
        FillTheBackupsFolder(OneGigabyte - 1);
        DatabaseInitializer initializer = CreateInitializer(quotaPercent: 100);
        using SqliteConnection connection = SeededDatabase();

        Assert.AreEqual(initializer.CreateBackup(connection, fromVersion: 1).Outcome, initializer.CheckBackupReadiness());
    }

    /// <summary>
    /// #348: the check answers as if an old backup were already gone, so the notification can offer
    /// removing one only when that clears the way. The folder is full to the ceiling; freeing a megabyte
    /// leaves room for a backup of a few kilobytes.
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_PastTheCeilingByLessThanWhatIsFreedFirst_ReportsSucceeded()
    {
        FillTheBackupsFolder(OneGigabyte);

        Assert.AreEqual(BackupOutcome.Succeeded, CreateInitializer().CheckBackupReadiness(bytesFreedFirst: 1_048_576));
    }

    /// <summary>
    /// Freeing one byte of a full folder leaves no room for the backup: removing that one would delete it
    /// for nothing. The quota at 100% keeps the ceiling the only limit in play.
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_PastTheCeilingByMoreThanWhatIsFreedFirst_ReportsBudgetExceeded()
    {
        FillTheBackupsFolder(OneGigabyte);

        Assert.AreEqual(BackupOutcome.BudgetExceeded, CreateInitializer(quotaPercent: 100).CheckBackupReadiness(bytesFreedFirst: 1));
    }

    /// <summary>A full volume is cleared the same way: the space a removed backup occupied becomes free.</summary>
    [TestMethod]
    public void CheckBackupReadiness_WithNoFreeDiskSpaceAndSpaceFreedFirst_ReportsSucceeded()
    {
        Assert.AreEqual(BackupOutcome.Succeeded,
            CreateInitializer(diskSpaceProvider: new FixedDiskSpaceProvider(0)).CheckBackupReadiness(bytesFreedFirst: 1_000));
    }

    private void FillTheBackupsFolder(long bytes)
    {
        Directory.CreateDirectory(_backups);
        using FileStream filler = new(Path.Combine(_backups, "filler.db"), FileMode.Create, FileAccess.Write);
        filler.SetLength(bytes);
    }

    private sealed class FixedDiskSpaceProvider(long availableBytes) : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
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

    // A file where the backups directory belongs: Directory.CreateDirectory throws IOException,
    // deterministically and identically on Windows and Linux, which an ACL would not.
    private void BlockTheBackupsDirectory() => File.WriteAllText(_backups, "not a directory");

    private DatabaseInitializer CreateInitializer(
        int maxBackupStorageGb = 1, IDiskSpaceProvider? diskSpaceProvider = null,
        int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent)
    {
        DatabaseOptions options = new()
        {
            DbPath = _dbPath,
            BackupsPath = _backups,
            MaxBackupStorageGb = maxBackupStorageGb,
            BackupQuotaPercent = quotaPercent,
        };

        return new DatabaseInitializer(new SqliteConnectionFactory(_dbPath), options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, diskSpaceProvider ?? new DiskSpaceProvider());
    }
}
