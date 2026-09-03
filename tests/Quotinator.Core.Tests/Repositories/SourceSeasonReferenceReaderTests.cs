using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Real-SQLite tests for <see cref="SourceSeasonReferenceReader"/>. Added after #375's own
/// fake-backed <c>SourceEndpointsTests</c> passed while every real request crashed: SQLite's
/// <c>INTEGER</c> affinity reads back as <see cref="long"/> through Microsoft.Data.Sqlite regardless of
/// a column's declared type, and Dapper's record-constructor materialization — used by
/// <see cref="JoinQueryRepository{TResult}"/> — requires an exact type match, unlike the generic
/// repository's property-setter mapping, which narrows implicitly. <c>SourceSeasonReferenceRow.Number</c>
/// and <c>SourceSeasonReferencesBatchRow.Number</c> were declared <c>int</c>; every live call to either
/// query threw <c>InvalidOperationException</c> on both `docker run` invocations this document's own T2
/// verification made (single and batched), confirmed via container logs 2026-09-03. See
/// <c>SourceSeriesReferenceReaderTests</c>' own remarks for the sibling precedent this repeats: a
/// fake-backed test cannot exercise Dapper's own materialization at all.
/// </summary>
[TestClass]
public class SourceSeasonReferenceReaderTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;
    private SourceSeasonReferenceReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_ssnrr_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Quotinator_Season (
                Id TEXT PRIMARY KEY,
                Number INTEGER NOT NULL,
                Title TEXT,
                Subtitle TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE Quotinator_Source (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                SeasonId TEXT REFERENCES Quotinator_Season(Id),
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            """);

        _reader = new SourceSeasonReferenceReader(
            new JoinQueryRepository<SourceSeasonReferenceRow>(_factory, new SourceSeasonReferenceStrategy()),
            new JoinQueryRepository<SourceSeasonReferencesBatchRow>(_factory, new SourceSeasonReferencesBatchStrategy()));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void InsertSeason(Guid id, int number, string? title, string? subtitle, bool isDeleted = false) =>
        Execute("INSERT INTO Quotinator_Season (Id, Number, Title, Subtitle, IsDeleted) VALUES (@id, @number, @title, @subtitle, @isDeleted);",
            new { id = id.ToString("D").ToUpperInvariant(), number, title, subtitle, isDeleted = isDeleted ? 1 : 0 });

    private void InsertSource(Guid id, string title, Guid? seasonId) =>
        Execute("INSERT INTO Quotinator_Source (Id, Title, SeasonId) VALUES (@id, @title, @seasonId);",
            new
            {
                id = id.ToString("D").ToUpperInvariant(),
                title,
                seasonId = seasonId?.ToString("D").ToUpperInvariant(),
            });

    private void Execute(string sql, object param)
    {
        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(sql, param);
    }

    [TestMethod]
    public async Task GetSeasonReferenceAsync_SourceWithSeason_ReturnsReference()
    {
        Guid seasonId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();
        InsertSeason(seasonId, 1, "Book One", "Water");
        InsertSource(sourceId, "The Boy in the Iceberg", seasonId);

        (Guid Id, int Number, string? Title, string? Subtitle)? result = await _reader.GetSeasonReferenceAsync(sourceId);

        Assert.IsNotNull(result);
        Assert.AreEqual(seasonId, result.Value.Id);
        Assert.AreEqual(1, result.Value.Number);
        Assert.AreEqual("Book One", result.Value.Title);
        Assert.AreEqual("Water", result.Value.Subtitle);
    }

    [TestMethod]
    public async Task GetSeasonReferenceAsync_NumberOnlySeason_ReturnsNullTitleAndSubtitle()
    {
        Guid seasonId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();
        InsertSeason(seasonId, 4, title: null, subtitle: null);
        InsertSource(sourceId, "Episode 4", seasonId);

        (Guid Id, int Number, string? Title, string? Subtitle)? result = await _reader.GetSeasonReferenceAsync(sourceId);

        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.Value.Number);
        Assert.IsNull(result.Value.Title);
        Assert.IsNull(result.Value.Subtitle);
    }

    [TestMethod]
    public async Task GetSeasonReferenceAsync_SourceWithNoSeason_ReturnsNull()
    {
        Guid sourceId = Guid.NewGuid();
        InsertSource(sourceId, "Standalone Film", seasonId: null);

        (Guid Id, int Number, string? Title, string? Subtitle)? result = await _reader.GetSeasonReferenceAsync(sourceId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeasonReferenceAsync_SeasonSoftDeleted_ReturnsNull()
    {
        Guid seasonId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();
        InsertSeason(seasonId, 1, "Book One", "Water", isDeleted: true);
        InsertSource(sourceId, "The Boy in the Iceberg", seasonId);

        (Guid Id, int Number, string? Title, string? Subtitle)? result = await _reader.GetSeasonReferenceAsync(sourceId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeasonReferencesForManyAsync_EmptyIdList_ReturnsEmptyDictionary()
    {
        IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)> result =
            await _reader.GetSeasonReferencesForManyAsync([]);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetSeasonReferencesForManyAsync_MixedIds_ReturnsOnlySourcesWithActiveSeason()
    {
        Guid seasonId = Guid.NewGuid();
        Guid sourceWithSeason = Guid.NewGuid();
        Guid sourceWithoutSeason = Guid.NewGuid();
        InsertSeason(seasonId, 1, "Book One", "Water");
        InsertSource(sourceWithSeason, "The Boy in the Iceberg", seasonId);
        InsertSource(sourceWithoutSeason, "Standalone", seasonId: null);

        IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)> result =
            await _reader.GetSeasonReferencesForManyAsync([sourceWithSeason, sourceWithoutSeason]);

        Assert.HasCount(1, result);
        Assert.AreEqual(seasonId, result[sourceWithSeason].Id);
        Assert.AreEqual(1, result[sourceWithSeason].Number);
        Assert.IsFalse(result.ContainsKey(sourceWithoutSeason));
    }
}
