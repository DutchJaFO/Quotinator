using Quotinator.Data.Database;
using Quotinator.Data.Models;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// The reader behind the backup endpoints (#349), tested directly rather than only through HTTP.
/// <para>
/// Each behaviour is checked on both sides: the file that is listed and the folder that has none, the
/// name that opens and the name that does not, usage below the quota and usage above it. A suite that
/// only ever asserted the working case would stay green against a reader that returned everything, and
/// one that only asserted refusals would stay green against a reader that returned nothing.
/// </para>
/// </summary>
[TestClass]
public class DatabaseBackupReaderTests
{
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize() => _backups = Directory.CreateTempSubdirectory("quotinator_349_reader_").FullName;

    [TestCleanup]
    public void TestCleanup()
    {
        try { Directory.Delete(_backups, recursive: true); }
        catch (IOException) { }
    }

    // ── List ─────────────────────────────────────────────────────────────────

    /// <summary>Every backup is reported, with the three facts the endpoints publish.</summary>
    [TestMethod]
    public void List_ReportsEveryFile_WithNameSizeAndTimestamp()
    {
        WriteBackup("quotinatordata_v5_20260101T101010101Z.db", 128);
        WriteBackup("quotinatordata_v5_20260102T101010101Z.db", 256);

        IReadOnlyList<BackupFileInfo> listed = CreateReader().List();

        Assert.HasCount(2, listed);
        Assert.AreEqual(384L, listed.Sum(b => b.SizeBytes));
        Assert.IsTrue(listed.All(b => b.TakenAtUtc != default));
    }

    /// <summary>Newest first, so the file an operator is most likely to keep is at the top.</summary>
    [TestMethod]
    public void List_ReturnsNewestFirst()
    {
        WriteBackup("older.db", 16);
        File.SetLastWriteTimeUtc(Path.Combine(_backups, "older.db"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteBackup("newer.db", 16);
        File.SetLastWriteTimeUtc(Path.Combine(_backups, "newer.db"), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.AreEqual("newer.db", CreateReader().List()[0].Name);
    }

    /// <summary>An empty folder lists nothing, rather than failing.</summary>
    [TestMethod]
    public void List_EmptyFolder_IsEmpty() => Assert.IsEmpty(CreateReader().List());

    /// <summary>A folder that does not exist yet lists nothing — the state of a fresh install.</summary>
    [TestMethod]
    public void List_MissingFolder_IsEmpty_NotAnError()
    {
        DatabaseBackupReader reader = new DatabaseBackupReader(
            Options(Path.Combine(_backups, "not-created-yet")), NoOpDiskSpaceProvider.Instance);

        Assert.IsEmpty(reader.List());
    }

    /// <summary>
    /// The pre-flight's own writability probe is never reported as a backup.
    /// <para>
    /// <c>CheckBackupReadiness</c> writes a zero-byte probe into this folder and deletes it again. If
    /// the delete fails — or the process stops between the two — the artefact is left behind, and a
    /// reader that enumerated everything would offer it as a restore point: listable, downloadable, and
    /// deletable like a real backup. It is this application's own scratch file, not a backup.
    /// </para>
    /// </summary>
    [TestMethod]
    public void List_ExcludesTheWritabilityProbeArtefact()
    {
        WriteBackup("real-backup.db", 64);
        File.WriteAllBytes(Path.Combine(_backups, BackupFileNames.ProbeFileName), []);

        DatabaseBackupReader reader = CreateReader();
        IReadOnlyList<BackupFileInfo> listed = reader.List();

        Assert.HasCount(1, listed);
        Assert.AreEqual("real-backup.db", listed[0].Name);

        // Consistent across every route, not just the listing: a probe that is invisible in the list
        // but downloadable by name would be a worse answer than either alone.
        Assert.IsFalse(reader.Exists(BackupFileNames.ProbeFileName));
        Assert.IsNull(reader.OpenRead(BackupFileNames.ProbeFileName));
        Assert.IsFalse(new DatabaseBackupWriter(Options(_backups)).Delete(BackupFileNames.ProbeFileName));
        Assert.IsTrue(File.Exists(Path.Combine(_backups, BackupFileNames.ProbeFileName)),
            "refusing to treat it as a backup must not mean deleting it either");
    }

    // ── Usage ────────────────────────────────────────────────────────────────

    /// <summary>Usage reports the files it counted and the quota it measured them against.</summary>
    [TestMethod]
    public void GetUsage_ReportsWhatIsUsed_AndAgainstWhat()
    {
        WriteBackup("one.db", 1000);
        WriteBackup("two.db", 2000);

        BackupStorageUsage usage = CreateReader().GetUsage();

        Assert.AreEqual(3000L, usage.UsedBytes);
        Assert.AreEqual(2, usage.FileCount);
        Assert.AreEqual(BackupStorageBudget.CeilingBytes(Options(_backups)), usage.CeilingBytes);
        Assert.AreEqual(BackupStorageBudget.QuotaBytes(Options(_backups)), usage.QuotaBytes);
        Assert.IsFalse(usage.ReserveInUse, "3 KB is nowhere near the quota");
    }

    /// <summary>Free disk space comes from the provider, not from the quota arithmetic beside it.</summary>
    [TestMethod]
    public void GetUsage_ReportsFreeDiskSpaceFromTheProvider()
    {
        const long FreeBytes = 987_654_321L;
        DatabaseBackupReader reader = new DatabaseBackupReader(Options(_backups), new FixedDiskSpace(FreeBytes));

        Assert.AreEqual(FreeBytes, reader.GetUsage().FreeDiskBytes);
    }

    /// <summary>Above the quota, the reserve is reported as in use and nothing remains against it.</summary>
    [TestMethod]
    public void GetUsage_AboveTheQuota_ReportsTheReserveInUse()
    {
        // 1% of a 1 GB ceiling is ~10.7 MB, so 11 MB crosses it without writing most of a gigabyte.
        WriteBackup("large.db", 11L * 1024 * 1024);
        DatabaseBackupReader reader = new DatabaseBackupReader(Options(_backups, quotaPercent: 1), NoOpDiskSpaceProvider.Instance);

        BackupStorageUsage usage = reader.GetUsage();

        Assert.IsTrue(usage.ReserveInUse);
        Assert.AreEqual(0L, usage.RemainingAgainstQuotaBytes);
        Assert.IsGreaterThan(0L, usage.RemainingAgainstCeilingBytes, "the reserve is in use, not exhausted");
    }

    /// <summary>An empty folder is zero used and zero files, not an error.</summary>
    [TestMethod]
    public void GetUsage_NoBackups_IsZero()
    {
        BackupStorageUsage usage = CreateReader().GetUsage();

        Assert.AreEqual(0L, usage.UsedBytes);
        Assert.AreEqual(0, usage.FileCount);
        Assert.IsFalse(usage.ReserveInUse);
    }

    // ── OpenRead / Exists / IsValidName ──────────────────────────────────────

    /// <summary>An existing backup opens and yields exactly the bytes on disk.</summary>
    [TestMethod]
    public void OpenRead_ExistingBackup_ReturnsItsBytes()
    {
        byte[] written = [1, 2, 3, 4, 5];
        File.WriteAllBytes(Path.Combine(_backups, "present.db"), written);

        using Stream? stream = CreateReader().OpenRead("present.db");

        Assert.IsNotNull(stream);
        using MemoryStream buffer = new MemoryStream();
        stream!.CopyTo(buffer);
        Assert.AreSequenceEqual(written, buffer.ToArray());
    }

    /// <summary>A name with no file behind it opens nothing.</summary>
    [TestMethod]
    public void OpenRead_UnknownName_IsNull() => Assert.IsNull(CreateReader().OpenRead("absent.db"));

    /// <summary>A name that is a path opens nothing, whatever it points at.</summary>
    [TestMethod]
    public void OpenRead_UnsafeName_IsNull() => Assert.IsNull(CreateReader().OpenRead("../escape.db"));

    /// <summary>Exists distinguishes a real backup from one that was never there.</summary>
    [TestMethod]
    public void Exists_TellsAPresentBackupFromAnAbsentOne()
    {
        WriteBackup("present.db", 16);

        Assert.IsTrue(CreateReader().Exists("present.db"));
        Assert.IsFalse(CreateReader().Exists("absent.db"));
    }

    /// <summary>
    /// A valid name and an unsafe one are distinguished without reference to whether the file exists —
    /// which is what lets a caller answer "you may not ask for that" differently from "that is not here".
    /// </summary>
    [TestMethod]
    public void IsValidName_SeparatesAnUnsafeNameFromAMerelyAbsentOne()
    {
        Assert.IsTrue(CreateReader().IsValidName("absent-but-well-formed.db"));
        Assert.IsFalse(CreateReader().IsValidName("../escape.db"));
    }

    private DatabaseBackupReader CreateReader() =>
        new DatabaseBackupReader(Options(_backups), NoOpDiskSpaceProvider.Instance);

    private static DatabaseOptions Options(string backupsPath, int quotaPercent = DatabaseOptions.DefaultBackupQuotaPercent) =>
        new DatabaseOptions
        {
            DbPath             = Path.Combine(backupsPath, "quotinatordata.db"),
            BackupsPath        = backupsPath,
            MaxBackupStorageGb = 1,
            BackupQuotaPercent = quotaPercent,
        };

    private void WriteBackup(string name, long bytes)
    {
        using FileStream stream = File.Create(Path.Combine(_backups, name));
        stream.SetLength(bytes);
    }

    private sealed class FixedDiskSpace(long freeBytes) : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => freeBytes;
    }
}
