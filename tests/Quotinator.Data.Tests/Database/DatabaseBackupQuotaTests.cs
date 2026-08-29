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

    /// <summary>
    /// The substantive half of "a refusal does not rebuild". Asserting this at the endpoint layer only
    /// proves the spy refused, since a stub's own bookkeeping is what records whether a reset ran — so
    /// the guarantee is checked here, against the real <see cref="DatabaseInitializer.ResetAsync"/>,
    /// where <c>OnResetAsync</c> is the actual destructive step.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_WhenNoBackupCanBeTaken_NeverReachesTheDestructiveStep()
    {
        FillBackupsTo(percentOfCeiling: 100);
        RecordingInitializer initializer = new RecordingInitializer(NewOptions(), _dbPath);

        DatabaseOperationResult result = await initializer.ResetAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(BackupOutcome.BudgetExceeded, result.BackupObstacle);
        Assert.IsFalse(
            initializer.ResetHookRan,
            "refusing has to mean the tables were never dropped — a refusal that still wiped the "
            + "database would be strictly worse than the unhandled 500 it replaced");
    }

    [TestMethod]
    public async Task ResetAsync_WithTheOverride_ReachesTheDestructiveStepAndReportsTheSkip()
    {
        FillBackupsTo(percentOfCeiling: 100);
        RecordingInitializer initializer = new RecordingInitializer(NewOptions(), _dbPath);

        DatabaseOperationResult result = await initializer.ResetAsync(allowNoBackup: true);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(initializer.ResetHookRan);
        Assert.IsTrue(
            result.BackupSkippedByOverride,
            "the caller has to be told it ran without a backup, or the audit trail above it has nothing "
            + "to record");
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
    /// #349 — the figures the status endpoint publishes and the limit a destructive action refuses on
    /// are computed by the same code, so they cannot drift apart.
    /// <para>
    /// Checked across the quota boundary in both directions rather than at one point: agreement that
    /// holds only where nothing is near a limit is not agreement. This is the same "check and attempt
    /// agree" property #348 found was worth its own test, applied to the reader that now reports it.
    /// </para>
    /// </summary>
    [TestMethod]
    public void PublishedUsage_AgreesWithTheLimitAReadinessCheckRefusesOn()
    {
        foreach (int percent in (int[])[50, 89, 95, 100])
        {
            FillBackupsTo(percentOfCeiling: percent);

            DatabaseBackupReader reader = new DatabaseBackupReader(NewOptions(), NoOpDiskSpaceProvider.Instance);
            Quotinator.Data.Models.BackupStorageUsage usage = reader.GetUsage();
            BackupOutcome readiness = CreateInitializer().CheckBackupReadiness();

            bool reportedOverQuota = usage.UsedBytes >= usage.QuotaBytes;
            bool refusedForBudget  = readiness == BackupOutcome.BudgetExceeded;

            Assert.AreEqual(reportedOverQuota, refusedForBudget,
                $"at {percent}% of the ceiling the reader reported reserveInUse={usage.ReserveInUse} while the "
                + $"readiness check said {readiness} — the operator would be told one thing and get another");
            Assert.AreEqual(reportedOverQuota, usage.ReserveInUse);
        }
    }

    /// <summary>
    /// A backup that has just been taken is immediately readable and removable — no handle is retained.
    /// <para>
    /// Found live in T1 (#349, 2026-08-29): downloading a backup created moments earlier answered an
    /// unhandled <c>500</c>, "the process cannot access the file because it is being used by another
    /// process". The other process was this one. <c>Microsoft.Data.Sqlite</c> pools connections by
    /// default, so disposing the destination connection returns it to the pool and keeps its file
    /// handle open for the life of the process — every backup ever taken stayed locked.
    /// </para>
    /// <para>
    /// This assertion can only <em>fail</em> on Windows: on Unix a retained handle does not prevent
    /// another open or an unlink, so the same leak is invisible there. Recorded rather than hidden —
    /// the guarantee is the same on both, and the leak was real on both.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task CreateBackupAsync_LeavesNoHandleOnTheFileItWrote()
    {
        RecordingInitializer initializer = new RecordingInitializer(NewOptions(), _dbPath);
        await CreateSeededDatabaseAsync();

        DatabaseBackupResult result = await initializer.CreateBackupAsync();

        Assert.AreEqual(BackupOutcome.Succeeded, result.Outcome);

        using (FileStream stream = new FileStream(result.Path!, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.IsGreaterThan(0L, stream.Length);

        File.Delete(result.Path!);
        Assert.IsFalse(File.Exists(result.Path!), "a backup nothing holds open can be removed");
    }

    private async Task CreateSeededDatabaseAsync()
    {
        using SqliteConnection connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER PRIMARY KEY)";
        await command.ExecuteNonQueryAsync();
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

    private DatabaseOptions NewOptions(int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent) =>
        new DatabaseOptions
        {
            DbPath = _dbPath,
            BackupsPath = _backups,
            MaxBackupStorageGb = 1,
            BackupQuotaPercent = quotaPercent,
        };

    private DatabaseInitializer CreateInitializer(
        int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent, ILogger<DatabaseInitializer>? logger = null)
        => new DatabaseInitializer(new SqliteConnectionFactory(_dbPath), NewOptions(quotaPercent), [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            logger ?? NullLogger<DatabaseInitializer>.Instance);

    /// <summary>
    /// Records whether the destructive reset hook actually ran, which is the only way to tell a refusal
    /// apart from a reset that wiped the database and then reported failure.
    /// </summary>
    private sealed class RecordingInitializer(DatabaseOptions options, string dbPath)
        : DatabaseInitializer(new SqliteConnectionFactory(dbPath), options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance)
    {
        public bool ResetHookRan { get; private set; }

        protected override Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
        {
            ResetHookRan = true;
            return Task.CompletedTask;
        }
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
