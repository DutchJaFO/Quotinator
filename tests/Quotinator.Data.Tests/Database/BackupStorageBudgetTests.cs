using Quotinator.Data.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// The shared backup storage arithmetic (#349) — the numbers the status endpoint publishes and the
/// numbers a destructive action refuses on, now that both come from here.
/// <para>
/// This class exists separately from <c>DatabaseBackupQuotaTests</c>'s agreement test on purpose, and
/// the distinction is the point: that test proves the reader and the pre-flight agree, which two sides
/// computing the same wrong number would also satisfy. These tests assert the values themselves.
/// </para>
/// <para>
/// Every function is checked on both sides of its boundary — the configured value that is honoured as
/// well as the one that is rejected, the folder that has files as well as the one that does not — so a
/// failure is predictable rather than merely detected.
/// </para>
/// </summary>
[TestClass]
public class BackupStorageBudgetTests
{
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize() => _backups = Directory.CreateTempSubdirectory("quotinator_349_budget_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        try { Directory.Delete(_backups, recursive: true); }
        catch (IOException) { }
    }

    // ── Ceiling ──────────────────────────────────────────────────────────────

    /// <summary>The ceiling is the configured gigabytes, in bytes — and scales with the setting.</summary>
    [TestMethod]
    public void CeilingBytes_IsTheConfiguredGigabytes()
    {
        Assert.AreEqual(1_073_741_824L, BackupStorageBudget.CeilingBytes(Options(maxGb: 1)));
        Assert.AreEqual(4_294_967_296L, BackupStorageBudget.CeilingBytes(Options(maxGb: 4)),
            "the ceiling has to track the setting — a constant would pass the single-gigabyte case alone");
    }

    // A test asserting BytesPerGigabyte == 2^30 was written and removed: both sides are compile-time
    // constants, so the comparison is const-folded and can never fail whatever the constant becomes —
    // MSTEST0032 flags exactly this. The property it was reaching for is that the ceiling is computed in
    // 2^30 units, which CeilingBytes_IsTheConfiguredGigabytes asserts against a real return value and
    // can genuinely fail.

    // ── Quota percentage ─────────────────────────────────────────────────────

    /// <summary>A percentage inside 1–100 is honoured, and is not reported as out of range.</summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(50)]
    [DataRow(90)]
    [DataRow(100)]
    public void EffectiveQuotaPercent_InRange_IsHonoured(int configured)
    {
        int effective = BackupStorageBudget.EffectiveQuotaPercent(Options(quotaPercent: configured), out bool outOfRange);

        Assert.AreEqual(configured, effective);
        Assert.IsFalse(outOfRange, "a value inside the range must not be reported as substituted");
    }

    /// <summary>
    /// A percentage outside the range falls back to the default <em>and</em> says so. Both halves
    /// matter: substituting silently is the failure mode #348 called out, and an operator who is never
    /// told their setting was ignored will keep believing it applies.
    /// </summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    [DataRow(101)]
    [DataRow(150)]
    public void EffectiveQuotaPercent_OutOfRange_FallsBackToTheDefault_AndReportsIt(int configured)
    {
        int effective = BackupStorageBudget.EffectiveQuotaPercent(Options(quotaPercent: configured), out bool outOfRange);

        Assert.AreEqual(DatabaseOptions.DefaultBackupQuotaPercent, effective);
        Assert.IsTrue(outOfRange);
    }

    /// <summary>
    /// An out-of-range value is never clamped into range. 150 clamped to 100 would raise the quota to
    /// the ceiling — the setting failing open, which is worse than it being ignored.
    /// </summary>
    [TestMethod]
    public void EffectiveQuotaPercent_OutOfRange_IsNotClamped()
    {
        int effective = BackupStorageBudget.EffectiveQuotaPercent(Options(quotaPercent: 150), out _);

        Assert.AreNotEqual(100, effective);
        Assert.AreEqual(90, effective);
    }

    // ── Quota and limit ──────────────────────────────────────────────────────

    /// <summary>The quota is that share of the ceiling, computed rather than assumed to be 90%.</summary>
    [TestMethod]
    public void QuotaBytes_IsTheConfiguredShareOfTheCeiling()
    {
        Assert.AreEqual(1_073_741_824L * 90 / 100, BackupStorageBudget.QuotaBytes(Options(quotaPercent: 90)));
        Assert.AreEqual(1_073_741_824L * 25 / 100, BackupStorageBudget.QuotaBytes(Options(quotaPercent: 25)));
    }

    /// <summary>An out-of-range percentage produces the default's quota, not a nonsensical one.</summary>
    [TestMethod]
    public void QuotaBytes_OutOfRangePercentage_UsesTheDefaultShare()
    {
        Assert.AreEqual(
            BackupStorageBudget.QuotaBytes(Options(quotaPercent: DatabaseOptions.DefaultBackupQuotaPercent)),
            BackupStorageBudget.QuotaBytes(Options(quotaPercent: 150)));
    }

    /// <summary>Routine operation is measured against the quota; only an explicit caller reaches the ceiling.</summary>
    [TestMethod]
    public void LimitBytes_StopsAtTheQuota_UnlessTheReserveIsAllowed()
    {
        DatabaseOptions options = Options(quotaPercent: 90);

        Assert.AreEqual(BackupStorageBudget.QuotaBytes(options),   BackupStorageBudget.LimitBytes(options, allowReserve: false));
        Assert.AreEqual(BackupStorageBudget.CeilingBytes(options), BackupStorageBudget.LimitBytes(options, allowReserve: true));
    }

    /// <summary>
    /// The reserve is real headroom, not a relabelling: the routine limit is strictly below the
    /// ceiling. A quota of 100% would make the two equal and the reserve would silently not exist.
    /// </summary>
    [TestMethod]
    public void LimitBytes_TheReserveIsNonEmptyAtTheDefaultQuota()
    {
        DatabaseOptions options = Options();

        Assert.IsLessThan(
            BackupStorageBudget.LimitBytes(options, allowReserve: true),
            BackupStorageBudget.LimitBytes(options, allowReserve: false));
    }

    // ── Used bytes ───────────────────────────────────────────────────────────

    /// <summary>Files in the folder are summed — the positive case.</summary>
    [TestMethod]
    public void UsedBytes_SumsEveryFileInTheFolder()
    {
        WriteFile("one.db", 100);
        WriteFile("two.db", 250);

        Assert.AreEqual(350L, BackupStorageBudget.UsedBytes(_backups));
    }

    /// <summary>An empty folder is zero used, not an error.</summary>
    [TestMethod]
    public void UsedBytes_EmptyFolder_IsZero() => Assert.AreEqual(0L, BackupStorageBudget.UsedBytes(_backups));

    /// <summary>
    /// A folder that does not exist yet is zero used, not an exception — this runs before the first
    /// backup has ever been taken, and on a status call against a fresh install.
    /// </summary>
    [TestMethod]
    public void UsedBytes_MissingFolder_IsZero_NotAnError()
    {
        string missing = Path.Combine(_backups, "does-not-exist");

        Assert.AreEqual(0L, BackupStorageBudget.UsedBytes(missing));
    }

    /// <summary>
    /// Only the folder's own files count; a subdirectory's contents do not. Asserted rather than left
    /// to whichever enumeration overload was reached for — the quota is a claim about this folder, and
    /// which files it covers has to be a decision rather than an accident.
    /// </summary>
    [TestMethod]
    public void UsedBytes_IgnoresFilesInSubdirectories()
    {
        WriteFile("one.db", 100);
        string nested = Directory.CreateDirectory(Path.Combine(_backups, "nested")).FullName;
        using (FileStream stream = File.Create(Path.Combine(nested, "deep.db")))
            stream.SetLength(500);

        Assert.AreEqual(100L, BackupStorageBudget.UsedBytes(_backups));
    }

    private static DatabaseOptions Options(int maxGb = 1, int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent) =>
        new DatabaseOptions
        {
            DbPath             = "unused.db",
            BackupsPath        = "unused",
            MaxBackupStorageGb = maxGb,
            BackupQuotaPercent = quotaPercent,
        };

    private void WriteFile(string name, long bytes)
    {
        using FileStream stream = File.Create(Path.Combine(_backups, name));
        stream.SetLength(bytes);
    }
}
