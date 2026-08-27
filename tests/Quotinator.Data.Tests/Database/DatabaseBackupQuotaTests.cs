using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #348 — the backup budget is two levels, not one: an operating quota (default 90% of
/// <see cref="DatabaseOptions.MaxBackupStorageGb"/>) that normal operation stops at, and the absolute
/// ceiling it never crosses.
/// <para>
/// The reserve between them exists because a backup's size cannot be predicted — SQLite copies pages,
/// so the source file's length only approximates the result. Keeping routine operation out of that
/// reserve is what leaves room for the one backup an operator most needs: the one before a Reset, at
/// the moment they have least space left.
/// </para>
/// </summary>
[TestClass]
public class DatabaseBackupQuotaTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quotinator-348q-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        Directory.CreateDirectory(_backups);
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
    public void UsageBelowTheQuota_ReportsThatABackupCanBeTaken()
    {
        FillBackupsTo(percentOfCeiling: 50);

        Assert.AreEqual(BackupOutcome.Succeeded, CreateInitializer().CheckBackupReadiness());
    }

    [TestMethod]
    public void UsageAtTheQuota_IsRefused_WithoutReachingIntoTheReserve()
    {
        // Above 90% but below 100%: inside the reserve, which normal operation must not consume.
        FillBackupsTo(percentOfCeiling: 95);

        Assert.AreEqual(
            BackupOutcome.BudgetExceeded, CreateInitializer().CheckBackupReadiness(),
            "routine operation stops at the quota — if it could spend the reserve, the reserve would "
            + "not be there when a Reset needed it");
    }

    [TestMethod]
    public void UsageAtTheQuota_WithTheReserveAllowed_CanStillTakeABackup()
    {
        FillBackupsTo(percentOfCeiling: 95);

        Assert.AreEqual(
            BackupOutcome.Succeeded, CreateInitializer().CheckBackupReadiness(allowReserve: true),
            "this is the whole point of the reserve: the operator who has run out of quota can still "
            + "take a real backup rather than proceeding with none");
    }

    [TestMethod]
    public void UsageAtTheAbsoluteCeiling_IsRefusedEvenWithTheReserveAllowed()
    {
        FillBackupsTo(percentOfCeiling: 100);

        Assert.AreEqual(
            BackupOutcome.BudgetExceeded, CreateInitializer().CheckBackupReadiness(allowReserve: true),
            "the ceiling is absolute — the reserve is headroom below it, not permission to exceed it");
    }

    [TestMethod]
    public void QuotaPercent_IsConfigurable()
    {
        FillBackupsTo(percentOfCeiling: 60);

        Assert.AreEqual(
            BackupOutcome.Succeeded, CreateInitializer(quotaPercent: 90).CheckBackupReadiness(),
            "60% used is inside a 90% quota");
        Assert.AreEqual(
            BackupOutcome.BudgetExceeded, CreateInitializer(quotaPercent: 50).CheckBackupReadiness(),
            "the same 60% is outside a 50% quota — so the setting is genuinely consulted, not ignored");
    }

    [TestMethod]
    public void QuotaPercent_DefaultsTo90()
    {
        // Reads the property off a real instance rather than comparing the constant to a literal — the
        // latter is const-folded, so it asserts 90 == 90 and can never fail whatever the default becomes.
        Assert.AreEqual(90, new DatabaseOptions { DbPath = _dbPath }.BackupQuotaPercent);
    }

    [TestMethod]
    public void QuotaPercent_OutOfRange_IsReportedAndTheDefaultUsed_NotClampedAndNotFatal()
    {
        // 150% would, if taken at face value, silently raise the quota above the ceiling — the setting
        // failing open. Clamping it to 100 would be just as wrong in the other direction: the operator
        // would never learn their value was ignored. It is reported, and the default is used.
        FillBackupsTo(percentOfCeiling: 95);
        CapturingLogger logger = new CapturingLogger();

        BackupOutcome outcome = CreateInitializer(quotaPercent: 150, logger: logger).CheckBackupReadiness();

        Assert.AreEqual(
            BackupOutcome.BudgetExceeded, outcome,
            "the 90% default applied, so 95% usage is over quota — a clamp to 100% would have allowed it");
        Assert.IsTrue(
            logger.Messages.Exists(m => m.Contains("BackupQuotaPercent", StringComparison.Ordinal)),
            "an ignored configuration value must say so; silently substituting the default is the "
            + "failure mode this test exists to prevent");
    }

    /// <summary>
    /// Writes filler into the backups folder until it occupies the given share of the ceiling. The
    /// ceiling is 1 GB in these tests, so the files are sized from that rather than from any real
    /// database — this fixture is about headroom arithmetic, not about backup content.
    /// </summary>
    private void FillBackupsTo(int percentOfCeiling)
    {
        const long ceilingBytes = 1_073_741_824L;
        long target = ceilingBytes * percentOfCeiling / 100L;
        string filler = Path.Combine(_backups, "filler.db");

        using FileStream stream = new FileStream(filler, FileMode.Create, FileAccess.Write);
        stream.SetLength(target);
    }

    private DatabaseInitializer CreateInitializer(
        int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent, ILogger<DatabaseInitializer>? logger = null)
    {
        SqliteConnectionFactory factory = new SqliteConnectionFactory(_dbPath);
        DatabaseOptions options = new DatabaseOptions
        {
            DbPath = _dbPath,
            BackupsPath = _backups,
            MaxBackupStorageGb = 1,
            BackupQuotaPercent = quotaPercent,
        };

        return new DatabaseInitializer(factory, options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            logger ?? NullLogger<DatabaseInitializer>.Instance);
    }

    /// <summary>
    /// Captures rendered log messages. A plain assertion that "a warning happened" would pass for any
    /// warning at all; this checks the message actually names the setting an operator has to correct.
    /// </summary>
    private sealed class CapturingLogger : ILogger<DatabaseInitializer>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
