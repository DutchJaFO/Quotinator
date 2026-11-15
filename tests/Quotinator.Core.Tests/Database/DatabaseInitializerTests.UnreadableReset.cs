using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Database;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Paths;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// #482: a Reset recreates the database whatever state the old file is in. A file SQLite cannot read is
/// never treated as a backup: it is set aside in a reserved folder inside the backup folder, with its
/// sidecars, and a new database takes its place. A readable database keeps its backup, per ADR 025.
/// </summary>
public partial class DatabaseInitializerTests
{
    /// <summary>
    /// The reserved folder's name, written out rather than read from the production constant: it is a
    /// contract with the user and with #481, so a rename has to fail a test rather than follow silently.
    /// </summary>
    private const string SetAsideFolderName = "unreadable";

    private const string NotADatabase = "this file is not a SQLite database";

    private static readonly string[] TwoContents = ["first", "second"];

    private string SetAsideFolder => Path.Combine(_backups, SetAsideFolderName);

    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_RebuildsAnEmptyDatabase()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"the Reset refused ({result.BackupObstacle})");
        Assert.AreEqual(0, await CountQuotesInTheDatabaseFileAsync());
    }

    [TestMethod]
    public async Task ResetAsync_TruncatedDatabase_RebuildsAnEmptyDatabase()
    {
        await TruncateASeededDatabaseAsync();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"the Reset refused ({result.BackupObstacle})");
        Assert.AreEqual(0, await CountQuotesInTheDatabaseFileAsync());
    }

    /// <summary>
    /// A file whose first page reads but whose later pages are damaged. SQLite's page copy does not
    /// validate what it copies, so a check that only opens the file would let this through as a backup.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_DatabaseCorruptBeyondItsHeader_SetsItAsideAndKeepsNoBackup()
    {
        await CorruptASeededDatabaseBeyondItsHeaderAsync();

        await CreateInitializer([]).ResetAsync();

        Assert.HasCount(1, MainSetAsideFiles(), "the damaged file was not set aside");
        Assert.AreEqual(0, BackupFileCount(), "a damaged file was kept as a backup");
    }

    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_MovesTheFileToTheReservedFolder()
    {
        WriteNotADatabase();

        await CreateInitializer([]).ResetAsync();

        string setAside = Assert.ContainsSingle(MainSetAsideFiles(), "the file was not set aside");
        Assert.AreEqual(NotADatabase, File.ReadAllText(setAside), "the set-aside file is not the original");
    }

    /// <summary>
    /// #348: an unreadable file is never a backup, so the caller is told where the old file went instead:
    /// that is what it has to restore from, if anything can be recovered from it.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_NamesTheFileItSetAside()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        string setAside = Assert.ContainsSingle(MainSetAsideFiles(), "precondition: the file was set aside");
        Assert.AreEqual(Path.GetFileName(setAside), result.RestorePoint?.FileName);
    }

    /// <summary>
    /// #490: the restore point says why the file was set aside: the check layer that showed it was not a
    /// sound copy, exactly as the check reported it, and no backup error.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_RestorePointNamesTheLayerThatShowedIt()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        DatabaseCheckLayerResult expected = FirstLayerThatDidNotPass(result);
        Assert.AreSame(expected, result.RestorePoint?.SetAsideReason?.CheckLayer);
        Assert.IsNull(result.RestorePoint?.SetAsideReason?.BackupError, "a file the check set aside carries a backup error");
    }

    /// <summary>
    /// #490: the notification records the same layer, status and finding, so an operator reading it later
    /// can see why the file was set aside without the log.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_NotificationRecordsTheLayerThatShowedIt()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        DatabaseCheckLayerResult expected = FirstLayerThatDidNotPass(result);
        UnreadableDatabaseSetAsideMetadataDto payload = await SetAsidePayloadAsync();
        Assert.AreEqual(expected.Layer, payload.CheckLayer);
        Assert.AreEqual(expected.Status, payload.CheckStatus);
        Assert.AreEqual(expected.Finding, payload.CheckFinding);
        Assert.IsNull(payload.BackupError, "a file the check set aside was recorded with a backup error");
    }

    /// <summary>
    /// #490: a file the check passes but the backup copy cannot read is set aside for the backup's reason,
    /// and the restore point carries that error rather than a layer. A check that passes everything stands
    /// in for damage the check cannot see.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_FileOnlyTheBackupFindsUnreadable_RestorePointCarriesTheBackupError()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([], databaseCheck: new NoOpDatabaseCheck("main")).ResetAsync();

        Assert.AreEqual(RestorePointOutcome.SetAside, result.RestorePoint?.Outcome, "precondition: the backup set the file aside");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.RestorePoint?.SetAsideReason?.BackupError), "the restore point carries no backup error");
        Assert.IsNull(result.RestorePoint?.SetAsideReason?.CheckLayer, "a file the backup set aside carries a check layer");
    }

    /// <summary>#490: the notification records the backup's error, and no layer, for a file only the backup found unreadable.</summary>
    [TestMethod]
    public async Task ResetAsync_FileOnlyTheBackupFindsUnreadable_NotificationRecordsTheBackupError()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([], databaseCheck: new NoOpDatabaseCheck("main")).ResetAsync();

        UnreadableDatabaseSetAsideMetadataDto payload = await SetAsidePayloadAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(payload.BackupError), "the notification records no backup error");
        Assert.AreEqual(result.RestorePoint?.SetAsideReason?.BackupError, payload.BackupError);
        Assert.IsNull(payload.CheckLayer, "a file the backup set aside was recorded with a check layer");
    }

    /// <summary>
    /// #490: a Reset refused because the file could not be set aside says why the file needed setting aside
    /// as well as what stopped the move: the check layer that showed it was not a sound copy.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabaseThatCannotBeSetAside_RefusalSaysWhichLayerShowedIt()
    {
        WriteNotADatabase();
        BlockTheSetAsideFolder();
        // A refusal carries no check of its own, so the expected layer comes from checking the file first.
        DatabaseCheckResult before = await TestDatabaseChecks.ForMain(_dbPath, QuotinatorMigrations.All, QuotinatorMigrations.Baseline)
            .CheckAsync(DatabaseCheckDepth.Full);
        DatabaseCheckLayerResult expected = before.Layers.First(l => l.Status is DatabaseCheckLayerStatus.Failed or DatabaseCheckLayerStatus.Inconclusive);

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.AreEqual(ResetObstacle.SetAsideFolderNotWritable, result.ResetObstacle, "precondition: the Reset refused for the set-aside folder");
        Assert.IsNotNull(result.SetAsideReason?.CheckLayer, "the refusal does not say which layer showed the file was not a sound copy");
        Assert.AreEqual(expected.Layer, result.SetAsideReason.CheckLayer.Layer);
        Assert.AreEqual(expected.Status, result.SetAsideReason.CheckLayer.Status);
        Assert.AreEqual(expected.Finding, result.SetAsideReason.CheckLayer.Finding);
    }

    /// <summary>#490: the same refusal for a file only the backup copy found unreadable carries the backup's error.</summary>
    [TestMethod]
    public async Task ResetAsync_FileOnlyTheBackupFindsUnreadableThatCannotBeSetAside_RefusalCarriesTheBackupError()
    {
        WriteNotADatabase();
        BlockTheSetAsideFolder();

        DatabaseOperationResult result = await CreateInitializer([], databaseCheck: new NoOpDatabaseCheck("main")).ResetAsync();

        Assert.AreEqual(ResetObstacle.SetAsideFolderNotWritable, result.ResetObstacle, "precondition: the Reset refused for the set-aside folder");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.SetAsideReason?.BackupError), "the refusal carries no backup error");
        Assert.IsNull(result.SetAsideReason?.CheckLayer, "a file the backup found unreadable carries a check layer");
    }

    /// <summary>
    /// A file where the reserved folder belongs, so creating it throws, deterministically on every platform:
    /// the technique the Api tests' corrupt-database factory uses.
    /// </summary>
    private void BlockTheSetAsideFolder()
    {
        Directory.CreateDirectory(_backups);
        File.WriteAllText(SetAsideFolder, "not a directory");
    }

    /// <summary>The first layer of a Reset's own check that did not pass: the one that sent the file aside.</summary>
    private static DatabaseCheckLayerResult FirstLayerThatDidNotPass(DatabaseOperationResult result)
    {
        Assert.IsNotNull(result.Check, "the Reset returned no check");
        return result.Check.Layers.First(l => l.Status is DatabaseCheckLayerStatus.Failed or DatabaseCheckLayerStatus.Inconclusive);
    }

    /// <summary>The single set-aside notification's payload.</summary>
    private async Task<UnreadableDatabaseSetAsideMetadataDto> SetAsidePayloadAsync()
    {
        NotificationEntity notification = Assert.ContainsSingle(
            (await NotificationsAsync()).Where(n => n.MetadataKind.Parsed == NotificationMetadataKind.UnreadableDatabaseSetAside),
            "no set-aside notification was written");
        return (UnreadableDatabaseSetAsideMetadataDto)NotificationMetadataKinds.TryDeserialize(
            NotificationMetadataKind.UnreadableDatabaseSetAside, notification.Metadata)!;
    }

    /// <summary>The same Reset is not reported as a backup taken: the set-aside file was never one.</summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_ReturnsASetAsideRestorePoint()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync();

        Assert.AreEqual(RestorePointOutcome.SetAside, result.RestorePoint?.Outcome);
    }

    [TestMethod]
    public async Task ResetAsync_CorruptDatabaseWithSidecars_MovesTheSidecarsWithIt()
    {
        WriteNotADatabase();
        File.WriteAllText(_dbPath + "-wal", "wal");
        File.WriteAllText(_dbPath + "-shm", "shm");

        await CreateInitializer([]).ResetAsync();

        string main = Assert.ContainsSingle(MainSetAsideFiles(), "the file was not set aside");
        Assert.AreEqual("wal", File.ReadAllText(main + "-wal"));
        Assert.AreEqual("shm", File.ReadAllText(main + "-shm"));
    }

    /// <summary>
    /// Restoring a damaged file would put a broken database back in place, so nothing a Reset of one
    /// writes to the backup folder's top level may look like a backup, a partial copy included.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_TakesNoBackup()
    {
        WriteNotADatabase();

        await CreateInitializer([]).ResetAsync();

        Assert.AreEqual(0, BackupFileCount(), "something a backup listing would offer was written");
    }

    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_SetAsideFileIsNotListedAsABackup()
    {
        WriteNotADatabase();

        await CreateInitializer([]).ResetAsync();

        IReadOnlyList<BackupFileInfo> listed = new DatabaseBackupReader(BackupOptions(), NoOpDiskSpaceProvider.Instance).List();
        Assert.IsEmpty(listed, "the backup listing offers: " + string.Join(", ", listed.Select(b => b.Name)));
    }

    /// <summary>Control: green before #482 and after. A readable database keeps its backup (ADR 025).</summary>
    [TestMethod]
    public async Task ResetAsync_HealthyDatabase_StillTakesItsBackup()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([SimpleQuoteBatch()]);
        await db.InitialiseAsync();

        await db.ResetAsync();

        Assert.AreEqual(1, BackupFileCount());
    }

    /// <summary>Control: green before #482 and after. Only an unreadable file is set aside.</summary>
    [TestMethod]
    public async Task ResetAsync_HealthyDatabase_LeavesNoFileInTheReservedFolder()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([SimpleQuoteBatch()]);
        await db.InitialiseAsync();

        await db.ResetAsync();

        Assert.IsEmpty(SetAsideFiles());
    }

    /// <summary>
    /// Control: green before #482 and after. A readable database is replaced in place, never swapped
    /// under a connection already open on it, which would go on reading or writing the old file.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_HealthyDatabase_AConnectionOpenedBeforeTheResetSeesTheRebuiltDatabase()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([SimpleQuoteBatch()]);
        await db.InitialiseAsync();
        using SqliteConnection openBefore = new($"Data Source={_dbPath}");
        await openBefore.OpenAsync(TestContext.CancellationToken);
        Assert.IsGreaterThan(0, await openBefore.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote;"), "precondition: the seed loaded quotes");

        await db.ResetAsync();

        Assert.AreEqual(0, await openBefore.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote;"));
    }

    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_SetAsideFileCountsTowardsBackupStorage()
    {
        WriteNotADatabase();

        await CreateInitializer([]).ResetAsync();

        Assert.IsGreaterThanOrEqualTo(NotADatabase.Length, BackupStorageBudget.UsedBytes(_backups));
    }

    /// <summary>The ceiling is zero, so no backup could be taken; an unreadable file needs none.</summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabaseWithAFullBackupFolder_StillRebuilds()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([], maxBackupStorageGb: 0).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"the Reset refused ({result.BackupObstacle})");
    }

    /// <summary>Moving a file within one volume needs no space, so a full disk does not stop it.</summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabaseWithNoFreeDiskSpace_StillRebuilds()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([], diskSpaceProvider: new NoFreeDiskSpace()).ResetAsync();

        Assert.IsTrue(result.Succeeded, $"the Reset refused ({result.BackupObstacle})");
    }

    [TestMethod]
    public async Task ResetAsync_TwoCorruptDatabases_KeepsBothFiles()
    {
        WriteNotADatabase("first");
        await CreateInitializer([]).ResetAsync();
        SqliteConnection.ClearAllPools();
        WriteNotADatabase("second");

        await CreateInitializer([]).ResetAsync();

        Assert.AreSequenceEqual(
            TwoContents,
            [.. SetAsideFiles().Select(File.ReadAllText).Order(StringComparer.Ordinal)]);
    }

    /// <summary>The old file's version rows cannot be read, so the baseline's own are what remains.</summary>
    [TestMethod]
    public async Task ResetAsync_CorruptDatabase_PreserveSchemaVersion_ProceedsWithTheBaselineVersionRows()
    {
        WriteNotADatabase();

        DatabaseOperationResult result = await CreateInitializer([]).ResetAsync(preserveSchemaVersion: true);

        Assert.IsTrue(result.Succeeded, $"the Reset refused ({result.BackupObstacle})");
        using SqliteConnection connection = new($"Data Source={_dbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);
        Assert.IsGreaterThan(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ConsumerSchemaVersion;"));
    }

    private void WriteNotADatabase(string content = NotADatabase)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");
        File.WriteAllText(_dbPath, content);
    }

    private async Task SeedAndCloseAsync()
    {
        await CreateInitializer([SimpleQuoteBatch()]).InitialiseAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");
    }

    private async Task TruncateASeededDatabaseAsync()
    {
        await SeedAndCloseAsync();
        using FileStream file = new(_dbPath, FileMode.Open, FileAccess.Write);
        file.SetLength(file.Length / 2);
    }

    /// <summary>Overwrites every page after the first, so the header and the schema page still read.</summary>
    private async Task CorruptASeededDatabaseBeyondItsHeaderAsync()
    {
        await SeedAndCloseAsync();
        const int pageSize = 4096;
        using FileStream file = new(_dbPath, FileMode.Open, FileAccess.Write);
        byte[] garbage = [.. Enumerable.Repeat((byte)0xA5, (int)file.Length - pageSize)];
        file.Position = pageSize;
        file.Write(garbage);
    }

    private async Task<int> CountQuotesInTheDatabaseFileAsync()
    {
        using SqliteConnection connection = new($"Data Source={_dbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote;");
    }

    private string[] SetAsideFiles() =>
        Directory.Exists(SetAsideFolder) ? Directory.GetFiles(SetAsideFolder) : [];

    private string[] MainSetAsideFiles() =>
        [.. SetAsideFiles().Where(f => !f.EndsWith("-wal", StringComparison.Ordinal) && !f.EndsWith("-shm", StringComparison.Ordinal))];

    private DatabaseOptions BackupOptions() => new() { DbPath = _dbPath, BackupsPath = _backups };

    private sealed class NoFreeDiskSpace : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => 0L;
    }
}
