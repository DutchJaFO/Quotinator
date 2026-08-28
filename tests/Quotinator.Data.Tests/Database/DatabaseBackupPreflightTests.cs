using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #348 — <see cref="IDatabaseInitializer.CheckBackupReadiness"/> as its own subject: can a backup be
/// taken, asked before anything is attempted.
/// <para>
/// The property under test is <strong>agreement</strong>. A pre-flight is only worth having if its
/// answer is the one the attempt would give — a check that says "ready" where the attempt fails is
/// worse than no check at all, because a caller acts on it. So each case here asserts the check and a
/// real <c>CreateBackup</c> against the same directory report the same member, rather than asserting
/// the check's answer in isolation.
/// </para>
/// <para>
/// That is also the gap this class exists to close: the quota tests drive the same method, but they
/// drive it as the quota's implementation detail. Nothing was asking whether the check and the attempt
/// actually agree.
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
    public void CanCreateBackup_WhenNothingObstructsIt_ReportsThatItCan()
    {
        DatabaseInitializer initializer = CreateInitializer();

        Assert.AreEqual(BackupOutcome.Succeeded, initializer.CheckBackupReadiness());

        using SqliteConnection connection = SeededDatabase();
        Assert.AreEqual(
            BackupOutcome.Succeeded, initializer.CreateBackup(connection, fromVersion: 1).Outcome,
            "the attempt must confirm what the check promised, or the check is not worth asking");
    }

    [TestMethod]
    public void CanCreateBackup_WhenBudgetIsAlreadyExhausted_ReportsTheVariantAnAttemptWouldReport()
    {
        DatabaseInitializer initializer = CreateInitializer(maxBackupStorageGb: 0);

        BackupOutcome checkedOutcome = initializer.CheckBackupReadiness();

        using SqliteConnection connection = SeededDatabase();
        BackupOutcome attemptedOutcome = initializer.CreateBackup(connection, fromVersion: 1).Outcome;

        Assert.AreEqual(BackupOutcome.BudgetExceeded, checkedOutcome);
        Assert.AreEqual(
            checkedOutcome, attemptedOutcome,
            "an operator offered a remedy for one obstacle and then hitting a different one has been "
            + "sent after the wrong fault");
    }

    [TestMethod]
    public void CanCreateBackup_WhenTheDestinationIsNotWritable_ReportsTheVariantAnAttemptWouldReport()
    {
        // A file where the backups directory belongs: Directory.CreateDirectory throws IOException,
        // deterministically and identically on Windows and Linux. The same technique #326 uses for the
        // keys/ directory, rather than an ACL that behaves differently per platform.
        File.WriteAllText(_backups, "not a directory");
        DatabaseInitializer initializer = CreateInitializer();

        BackupOutcome checkedOutcome = initializer.CheckBackupReadiness();

        using SqliteConnection connection = SeededDatabase();
        BackupOutcome attemptedOutcome = initializer.CreateBackup(connection, fromVersion: 1).Outcome;

        Assert.AreEqual(BackupOutcome.DestinationDirectoryNotWritable, checkedOutcome);
        Assert.AreEqual(checkedOutcome, attemptedOutcome);
    }

    /// <summary>
    /// The reserve is the caller's to unlock, so the check has to answer differently for the same
    /// directory depending on whether it was asked for. Without this, `allowNoBackup` could silently
    /// stop reaching the reserve and every other test here would still pass.
    /// </summary>
    [TestMethod]
    public void CanCreateBackup_InsideTheReserve_AnswersDifferentlyDependingOnWhetherItIsAllowed()
    {
        Directory.CreateDirectory(_backups);
        using (FileStream filler = new FileStream(Path.Combine(_backups, "filler.db"), FileMode.Create, FileAccess.Write))
        {
            // 95% of a 1 GB ceiling: past the 90% operating quota, below the ceiling itself.
            filler.SetLength(1_073_741_824L * 95 / 100);
        }

        DatabaseInitializer initializer = CreateInitializer();

        Assert.AreEqual(BackupOutcome.BudgetExceeded, initializer.CheckBackupReadiness());
        Assert.AreEqual(BackupOutcome.Succeeded, initializer.CheckBackupReadiness(allowReserve: true));
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

    private DatabaseInitializer CreateInitializer(int maxBackupStorageGb = 1)
    {
        DatabaseOptions options = new DatabaseOptions
        {
            DbPath = _dbPath,
            BackupsPath = _backups,
            MaxBackupStorageGb = maxBackupStorageGb,
        };

        return new DatabaseInitializer(new SqliteConnectionFactory(_dbPath), options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance);
    }
}
