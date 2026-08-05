using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Core.Repositories;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Exercises <see cref="SeriesNameResolver"/>/<see cref="UniverseNameResolver"/> against a real,
/// freshly-migrated SQLite schema — proves #216's case-insensitivity fix
/// (<c>Sql.Series.SelectIdByName</c>/<c>Sql.Universe.SelectIdByName</c>) end to end through the same
/// production classes <c>EntityFilterParsing.ResolveAsync</c> calls for the <c>series=</c>/<c>universe=</c>
/// query filters — not just the raw SQL constant.
/// </summary>
[TestClass]
public class SeriesUniverseNameResolverTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_resolver_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = Path.Combine(_tempDir, "backups") };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new ImportActionReader(_factory);
        var actionWriter  = new ImportActionWriter(_factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<Quotinator.Core.Entities.QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Quotinator.Core.Entities.SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Quotinator.Core.Entities.CharacterEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Quotinator.Core.Entities.PersonEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Quotinator.Core.Entities.ConversationEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Quotinator.Core.Entities.StageDirectionEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Quotinator.Core.Entities.SoundCueEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory);
        var db = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService, actionWriter, NoOpAuditEntryWriter.Instance,
            NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
            autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance, NoOpFileResourceRepository.Instance, QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task SeedSeriesAsync(string name)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Series (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, @Name, 'Incomplete', @now)",
            new { Id = Guid.NewGuid().ToString("D"), Name = name, now });
    }

    private async Task SeedUniverseAsync(string name)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Universe (Id, Name, CompletenessStatus, DateCreated) VALUES (@Id, @Name, 'Incomplete', @now)",
            new { Id = Guid.NewGuid().ToString("D"), Name = name, now });
    }

    [TestMethod]
    public async Task SeriesNameResolver_ExactCasing_ResolvesId()
    {
        await SeedSeriesAsync("The Lord of the Rings");
        var resolver = new SeriesNameResolver(_factory);

        var id = await resolver.ResolveIdByNameAsync("The Lord of the Rings");

        Assert.IsNotNull(id);
    }

    /// <summary>#216 fix: a mixed-case `series=` query filter must still resolve to the stored row.</summary>
    [TestMethod]
    public async Task SeriesNameResolver_DifferingCasing_StillResolvesId()
    {
        await SeedSeriesAsync("The Lord of the Rings");
        var resolver = new SeriesNameResolver(_factory);

        var id = await resolver.ResolveIdByNameAsync("the lord of the rings");

        Assert.IsNotNull(id, "?series=the lord of the rings (lowercase) must still match the stored 'The Lord of the Rings' row");
    }

    [TestMethod]
    public async Task SeriesNameResolver_NoMatch_ReturnsNull()
    {
        var resolver = new SeriesNameResolver(_factory);

        var id = await resolver.ResolveIdByNameAsync("Does Not Exist");

        Assert.IsNull(id);
    }

    [TestMethod]
    public async Task UniverseNameResolver_ExactCasing_ResolvesId()
    {
        await SeedUniverseAsync("Middle Earth");
        var resolver = new UniverseNameResolver(_factory);

        var id = await resolver.ResolveIdByNameAsync("Middle Earth");

        Assert.IsNotNull(id);
    }

    /// <summary>#216 fix: a mixed-case `universe=` query filter must still resolve to the stored row.</summary>
    [TestMethod]
    public async Task UniverseNameResolver_DifferingCasing_StillResolvesId()
    {
        await SeedUniverseAsync("Middle Earth");
        var resolver = new UniverseNameResolver(_factory);

        var id = await resolver.ResolveIdByNameAsync("MIDDLE EARTH");

        Assert.IsNotNull(id, "?universe=MIDDLE EARTH (uppercase) must still match the stored 'Middle Earth' row");
    }

    [TestMethod]
    public async Task UniverseNameResolver_NoMatch_ReturnsNull()
    {
        var resolver = new UniverseNameResolver(_factory);

        var id = await resolver.ResolveIdByNameAsync("Does Not Exist");

        Assert.IsNull(id);
    }
}
