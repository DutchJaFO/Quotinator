using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Real-SQLite tests for <see cref="SeasonSeriesReferenceReader"/> (#375), mirroring
/// <see cref="SeriesUniverseReferenceReaderTests"/> — no fake-backed test exercises this reader's own
/// SQL, so a mistake in <c>Sql.Season.SelectSeriesReferenceForSeason</c>/
/// <c>SelectSeriesReferencesForSeasons</c> would otherwise pass every endpoint test.
/// </summary>
[TestClass]
public class SeasonSeriesReferenceReaderTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;
    private SeasonSeriesReferenceReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_ssrr_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Quotinator_Series (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE Quotinator_Season (
                Id TEXT PRIMARY KEY,
                Number INTEGER NOT NULL,
                SeriesId TEXT REFERENCES Quotinator_Series(Id),
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            """);

        _reader = new SeasonSeriesReferenceReader(
            new JoinQueryRepository<SeasonSeriesReferenceRow>(_factory, new SeasonSeriesReferenceStrategy()),
            new JoinQueryRepository<SeasonSeriesReferencesBatchRow>(_factory, new SeasonSeriesReferencesBatchStrategy()));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void InsertSeries(Guid id, string name, bool isDeleted = false) =>
        Execute("INSERT INTO Quotinator_Series (Id, Name, IsDeleted) VALUES (@id, @name, @isDeleted);",
            new { id = id.ToString("D").ToUpperInvariant(), name, isDeleted = isDeleted ? 1 : 0 });

    private void InsertSeason(Guid id, int number, Guid? seriesId) =>
        Execute("INSERT INTO Quotinator_Season (Id, Number, SeriesId) VALUES (@id, @number, @seriesId);",
            new
            {
                id = id.ToString("D").ToUpperInvariant(),
                number,
                seriesId = seriesId?.ToString("D").ToUpperInvariant(),
            });

    private void Execute(string sql, object param)
    {
        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(sql, param);
    }

    [TestMethod]
    public async Task GetSeriesReferenceAsync_SeasonWithSeries_ReturnsReference()
    {
        Guid seriesId = Guid.NewGuid();
        Guid seasonId = Guid.NewGuid();
        InsertSeries(seriesId, "Avatar: The Last Airbender");
        InsertSeason(seasonId, 1, seriesId);

        (Guid Id, string Name)? result = await _reader.GetSeriesReferenceAsync(seasonId);

        Assert.IsNotNull(result);
        Assert.AreEqual(seriesId, result.Value.Id);
        Assert.AreEqual("Avatar: The Last Airbender", result.Value.Name);
    }

    [TestMethod]
    public async Task GetSeriesReferenceAsync_SeasonWithNoSeries_ReturnsNull()
    {
        Guid seasonId = Guid.NewGuid();
        InsertSeason(seasonId, 1, seriesId: null);

        (Guid Id, string Name)? result = await _reader.GetSeriesReferenceAsync(seasonId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeriesReferenceAsync_SeriesSoftDeleted_ReturnsNull()
    {
        Guid seriesId = Guid.NewGuid();
        Guid seasonId = Guid.NewGuid();
        InsertSeries(seriesId, "Removed Series", isDeleted: true);
        InsertSeason(seasonId, 1, seriesId);

        (Guid Id, string Name)? result = await _reader.GetSeriesReferenceAsync(seasonId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeriesReferencesForManyAsync_EmptyIdList_ReturnsEmptyDictionary()
    {
        IReadOnlyDictionary<Guid, (Guid Id, string Name)> result = await _reader.GetSeriesReferencesForManyAsync([]);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetSeriesReferencesForManyAsync_MixedIds_ReturnsOnlySeasonsWithActiveSeries()
    {
        Guid seriesId = Guid.NewGuid();
        Guid seasonWithSeries = Guid.NewGuid();
        Guid seasonWithoutSeries = Guid.NewGuid();
        InsertSeries(seriesId, "Avatar: The Last Airbender");
        InsertSeason(seasonWithSeries, 1, seriesId);
        InsertSeason(seasonWithoutSeries, 2, seriesId: null);

        IReadOnlyDictionary<Guid, (Guid Id, string Name)> result =
            await _reader.GetSeriesReferencesForManyAsync([seasonWithSeries, seasonWithoutSeries]);

        Assert.HasCount(1, result);
        Assert.AreEqual(seriesId, result[seasonWithSeries].Id);
        Assert.AreEqual("Avatar: The Last Airbender", result[seasonWithSeries].Name);
        Assert.IsFalse(result.ContainsKey(seasonWithoutSeries));
    }
}
