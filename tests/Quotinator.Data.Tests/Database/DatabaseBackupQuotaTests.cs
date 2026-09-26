using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Fakes;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// #348: the backup budget is two levels, not one: an operating quota (default 90% of
/// <see cref="DatabaseOptions.MaxBackupStorageGb"/>) above which a backup is still taken and a warning
/// raised, and the absolute ceiling past which a backup is refused.
/// <para>
/// The reserve between them exists because a backup's size cannot be predicted: SQLite copies pages,
/// so the source file's length only approximates the result. The reserve is what lets a backup still be
/// taken while the user is warned, so they can remove older backups or raise the quota before the
/// ceiling refuses one (developer, 2026-09-26).
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

        // A real database, so every check weighs a backup of real size: whether a backup would pass the
        // ceiling depends on what it adds, and an absent file adds nothing.
        CreateSeededDatabaseAsync().GetAwaiter().GetResult();
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
    /// Above 90% but below 100%: inside the reserve, where a backup the user asks for is still taken. The
    /// warning that says so is the notification's concern, not this one's.
    /// </summary>
    [TestMethod]
    public async Task CreateBackupAsync_InsideTheReserve_TakesTheBackup()
    {
        FillBackupsTo(percentOfCeiling: 95);

        DatabaseBackupResult result = await CreateInitializer().CreateBackupAsync();

        Assert.AreEqual(BackupOutcome.Succeeded, result.Outcome);
    }

    /// <summary>
    /// A Reset inside the reserve takes its backup and runs, rather than refusing: only a backup that would
    /// pass the ceiling stops it.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_InsideTheReserve_ReachesTheDestructiveStep()
    {
        FillBackupsTo(percentOfCeiling: 95);
        RecordingInitializer initializer = new(NewOptions(), _dbPath);

        await initializer.ResetAsync();

        Assert.IsTrue(initializer.ResetHookRan, "a Reset inside the reserve is taken, not refused");
    }

    /// <summary>
    /// Checked against the real <see cref="DatabaseInitializer.ResetAsync"/>, where <c>OnResetAsync</c> is
    /// the actual destructive step, rather than at the endpoint, where only a spy's bookkeeping could say.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_WhenNoBackupCanBeTaken_Refuses()
    {
        FillBackupsTo(percentOfCeiling: 100);

        DatabaseOperationResult result = await new RecordingInitializer(NewOptions(), _dbPath).ResetAsync();

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ResetAsync_WhenNoBackupCanBeTaken_NamesTheObstacle()
    {
        FillBackupsTo(percentOfCeiling: 100);

        DatabaseOperationResult result = await new RecordingInitializer(NewOptions(), _dbPath).ResetAsync();

        Assert.AreEqual(BackupOutcome.BudgetExceeded, result.BackupObstacle);
    }

    [TestMethod]
    public async Task ResetAsync_WhenNoBackupCanBeTaken_NamesTheResetStep()
    {
        FillBackupsTo(percentOfCeiling: 100);

        DatabaseOperationResult result = await new RecordingInitializer(NewOptions(), _dbPath).ResetAsync();

        Assert.AreEqual(BackupGuardedStep.Reset, result.RefusedStep);
    }

    /// <summary>
    /// A refusal that still wiped the database would be strictly worse than the unhandled 500 it replaced.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_WhenNoBackupCanBeTaken_NeverReachesTheDestructiveStep()
    {
        FillBackupsTo(percentOfCeiling: 100);
        RecordingInitializer initializer = new(NewOptions(), _dbPath);

        await initializer.ResetAsync();

        Assert.IsFalse(initializer.ResetHookRan, "refusing has to mean the tables were never dropped");
    }

    /// <summary>The caller has to be told the reset ran without a backup, or the audit trail above it has nothing to record.</summary>
    [TestMethod]
    public async Task ResetAsync_WithTheOverride_ReportsThatTheBackupWasSkipped()
    {
        FillBackupsTo(percentOfCeiling: 100);

        DatabaseOperationResult result = await new RecordingInitializer(NewOptions(), _dbPath).ResetAsync(allowNoBackup: true);

        Assert.IsTrue(result.BackupSkippedByOverride);
    }

    private const string SkippedBackupLine = "reset proceeding WITHOUT a backup";

    /// <summary>
    /// #348 requirement 5 asks for the skip in the log as well as the audit trail: the log is where an
    /// operator reading a failed restore looks first.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_WithTheOverride_LogsTheSkippedBackup()
    {
        FillBackupsTo(percentOfCeiling: 100);
        CapturingLogger<DatabaseInitializer> logger = new();

        await new RecordingInitializer(NewOptions(), _dbPath, logger).ResetAsync(allowNoBackup: true);

        Assert.Contains(m => m.Contains($"{SkippedBackupLine} (BudgetExceeded)", StringComparison.Ordinal), logger.Messages);
    }

    /// <summary>A reset that did take its backup must not claim otherwise, or the line stops meaning anything.</summary>
    [TestMethod]
    public async Task ResetAsync_WhenTheBackupSucceeds_LogsNoSkippedBackup()
    {
        CapturingLogger<DatabaseInitializer> logger = new();

        await new RecordingInitializer(NewOptions(), _dbPath, logger).ResetAsync();

        Assert.DoesNotContain(m => m.Contains(SkippedBackupLine, StringComparison.Ordinal), logger.Messages);
    }

    /// <summary>
    /// Read off a real instance rather than comparing the constant to a literal, which is const-folded and
    /// could never fail whatever the default became.
    /// </summary>
    [TestMethod]
    public void QuotaPercent_DefaultsTo90()
    {
        Assert.AreEqual(90, new DatabaseOptions { DbPath = _dbPath }.BackupQuotaPercent);
    }


    /// <summary>An ignored configuration value says so; silently substituting the default is the failure this prevents.</summary>
    [TestMethod]
    public void QuotaPercent_OutOfRange_IsReported()
    {
        CapturingLogger<DatabaseInitializer> logger = new();

        CreateInitializer(quotaPercent: 150, logger: logger).CheckBackupReadiness();

        Assert.IsTrue(logger.Messages.Exists(m => m.Contains("BackupQuotaPercent", StringComparison.Ordinal)));
    }

    /// <summary>
    /// #349: the figures the status endpoint publishes and the limit a backup is refused on are computed by
    /// the same code, so they cannot drift apart: the check refuses exactly when the published room left
    /// under the ceiling is smaller than the backup.
    /// <para>
    /// Checked across both limits rather than at one point, since agreement that holds only where nothing
    /// is near a limit is not agreement: below the quota, inside the reserve, one byte short of the
    /// ceiling, and at it.
    /// </para>
    /// </summary>
    [TestMethod]
    public void PublishedUsage_AgreesWithTheCeilingAReadinessCheckRefusesOn()
    {
        const long ceilingBytes = 1_073_741_824L;
        long backupBytes = new FileInfo(_dbPath).Length;
        List<string> disagreements = [];

        foreach (long used in (long[])[ceilingBytes / 2, ceilingBytes * 95 / 100, ceilingBytes - 1, ceilingBytes])
        {
            FillBackupsToBytes(used);

            Quotinator.Data.Models.BackupStorageUsage usage = new DatabaseBackupReader(NewOptions(), NoOpDiskSpaceProvider.Instance).GetUsage();
            bool publishedNoRoom  = usage.RemainingAgainstCeilingBytes < backupBytes;
            bool refusedForBudget = CreateInitializer().CheckBackupReadiness() == BackupOutcome.BudgetExceeded;

            if (publishedNoRoom != refusedForBudget)
                disagreements.Add($"{used} bytes used: published no room={publishedNoRoom}, refused={refusedForBudget}");
        }

        Assert.IsEmpty(disagreements, "the operator would be told one thing and get another: " + string.Join("; ", disagreements));
    }

    /// <summary>
    /// A backup that has just been taken is immediately readable and removable: no handle is retained.
    /// <para>
    /// Found live in T1 (#349, 2026-08-29): downloading a backup created moments earlier answered an
    /// unhandled <c>500</c>, "the process cannot access the file because it is being used by another
    /// process". The other process was this one. <c>Microsoft.Data.Sqlite</c> pools connections by
    /// default, so disposing the destination connection returns it to the pool and keeps its file
    /// handle open for the life of the process: every backup ever taken stayed locked.
    /// </para>
    /// <para>
    /// This assertion can only <em>fail</em> on Windows: on Unix a retained handle does not prevent
    /// another open or an unlink, so the same leak is invisible there. Recorded rather than hidden:
    /// the guarantee is the same on both, and the leak was real on both.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task CreateBackupAsync_LeavesNoHandleOnTheFileItWrote()
    {
        RecordingInitializer initializer = new(NewOptions(), _dbPath);
        await CreateSeededDatabaseAsync();

        DatabaseBackupResult result = await initializer.CreateBackupAsync();

        Assert.AreEqual(BackupOutcome.Succeeded, result.Outcome);

        using (FileStream stream = new(result.Path!, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.IsGreaterThan(0L, stream.Length);

        File.Delete(result.Path!);
        Assert.IsFalse(File.Exists(result.Path!), "a backup nothing holds open can be removed");
    }

    private async Task CreateSeededDatabaseAsync()
    {
        using SqliteConnection connection = new($"Data Source={_dbPath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER PRIMARY KEY)";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Writes filler into the backups folder until it occupies the given share of the ceiling. The
    /// ceiling is 1 GB in these tests, so the files are sized from that rather than from any real
    /// database: this fixture is about headroom arithmetic, not about backup content.
    /// </summary>
    private void FillBackupsTo(int percentOfCeiling) => FillBackupsToBytes(1_073_741_824L * percentOfCeiling / 100L);

    private void FillBackupsToBytes(long bytes)
    {
        string filler = Path.Combine(_backups, "filler.db");

        using FileStream stream = new(filler, FileMode.Create, FileAccess.Write);
        stream.SetLength(bytes);
    }

    private DatabaseOptions NewOptions(int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent) =>
        new()
        {
            DbPath = _dbPath,
            BackupsPath = _backups,
            MaxBackupStorageGb = 1,
            BackupQuotaPercent = quotaPercent,
        };

    private DatabaseInitializer CreateInitializer(
        int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent, ILogger<DatabaseInitializer>? logger = null)
        => new(new SqliteConnectionFactory(_dbPath), NewOptions(quotaPercent), [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            logger ?? NullLogger<DatabaseInitializer>.Instance, new DiskSpaceProvider());

    /// <summary>
    /// Records whether the destructive reset hook actually ran, which is the only way to tell a refusal
    /// apart from a reset that wiped the database and then reported failure.
    /// </summary>
    private sealed class RecordingInitializer(DatabaseOptions options, string dbPath, ILogger<DatabaseInitializer>? logger = null)
        : DatabaseInitializer(new SqliteConnectionFactory(dbPath), options, [],
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            logger ?? NullLogger<DatabaseInitializer>.Instance, new DiskSpaceProvider())
    {
        public bool ResetHookRan { get; private set; }

        protected override Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
        {
            ResetHookRan = true;
            return Task.CompletedTask;
        }
    }
}
