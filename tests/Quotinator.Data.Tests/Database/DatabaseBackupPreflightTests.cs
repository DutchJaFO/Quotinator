using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
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
    private string _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quotinator-348p-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// The reserve is the caller's to unlock, so the check answers differently for the same directory
    /// depending on whether it was asked for. Without this, the override could silently stop reaching the
    /// reserve. Which answer is which is held by the quota tests.
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_InsideTheReserve_AnswersDifferentlyWithTheReserveAllowed()
    {
        Directory.CreateDirectory(_backups);
        using (FileStream filler = new(Path.Combine(_backups, "filler.db"), FileMode.Create, FileAccess.Write))
        {
            // 95% of a 1 GB ceiling: past the 90% operating quota, below the ceiling itself.
            filler.SetLength(1_073_741_824L * 95 / 100);
        }

        DatabaseInitializer initializer = CreateInitializer();

        Assert.AreNotEqual(initializer.CheckBackupReadiness(allowReserve: false), initializer.CheckBackupReadiness(allowReserve: true));
    }

    private const long OneGigabyte = 1_073_741_824L;

    /// <summary>
    /// #348: the check answers as if an old backup were already gone, so the notification can offer
    /// removing one only when that clears the way. 95% used against a 90% quota; freeing a tenth leaves 85%.
    /// </summary>
    [TestMethod]
    public void CheckBackupReadiness_OverTheQuotaByLessThanWhatIsFreedFirst_ReportsSucceeded()
    {
        FillTheBackupsFolder(OneGigabyte * 95 / 100);

        Assert.AreEqual(BackupOutcome.Succeeded, CreateInitializer().CheckBackupReadiness(bytesFreedFirst: OneGigabyte / 10));
    }

    /// <summary>Freeing a hundredth leaves 94%, still over the quota: removing that backup would delete it for nothing.</summary>
    [TestMethod]
    public void CheckBackupReadiness_OverTheQuotaByMoreThanWhatIsFreedFirst_ReportsBudgetExceeded()
    {
        FillTheBackupsFolder(OneGigabyte * 95 / 100);

        Assert.AreEqual(BackupOutcome.BudgetExceeded, CreateInitializer().CheckBackupReadiness(bytesFreedFirst: OneGigabyte / 100));
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

    private DatabaseInitializer CreateInitializer(int maxBackupStorageGb = 1, IDiskSpaceProvider? diskSpaceProvider = null)
    {
        DatabaseOptions options = new()
        {
            DbPath = _dbPath,
            BackupsPath = _backups,
            MaxBackupStorageGb = maxBackupStorageGb,
        };

        return new DatabaseInitializer(new SqliteConnectionFactory(_dbPath), options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, diskSpaceProvider ?? new DiskSpaceProvider());
    }
}
