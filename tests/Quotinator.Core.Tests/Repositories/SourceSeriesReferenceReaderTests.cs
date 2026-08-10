using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Real-SQLite tests for <see cref="SourceSeriesReferenceReader"/> — added by #284 alongside its
/// migration to <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/>
/// (ADR 017). No fake-backed test previously exercised this reader's own SQL — see
/// <c>ConversationLineCountReaderTests</c> for the sibling reader whose equivalent real-SQLite test
/// found two genuine bugs no fake could have caught.
/// </summary>
[TestClass]
public class SourceSeriesReferenceReaderTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;
    private SourceSeriesReferenceReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_ssrr_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Quotinator_Series (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE Quotinator_Source (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                SeriesId TEXT REFERENCES Quotinator_Series(Id),
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            """);

        _reader = new SourceSeriesReferenceReader(
            new JoinQueryRepository<SeriesReferenceRow>(_factory, new SourceSeriesReferenceStrategy()),
            new JoinQueryRepository<SourceSeriesReferenceRow>(_factory, new SourceSeriesReferencesBatchStrategy()));
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

    private void InsertSource(Guid id, string title, Guid? seriesId) =>
        Execute("INSERT INTO Quotinator_Source (Id, Title, SeriesId) VALUES (@id, @title, @seriesId);",
            new
            {
                id = id.ToString("D").ToUpperInvariant(),
                title,
                seriesId = seriesId?.ToString("D").ToUpperInvariant(),
            });

    private void Execute(string sql, object param)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(sql, param);
    }

    [TestMethod]
    public async Task GetSeriesReferenceAsync_SourceWithSeries_ReturnsReference()
    {
        var seriesId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        InsertSeries(seriesId, "Star Wars");
        InsertSource(sourceId, "A New Hope", seriesId);

        var result = await _reader.GetSeriesReferenceAsync(sourceId);

        Assert.IsNotNull(result);
        Assert.AreEqual(seriesId, result.Value.Id);
        Assert.AreEqual("Star Wars", result.Value.Name);
    }

    [TestMethod]
    public async Task GetSeriesReferenceAsync_SourceWithNoSeries_ReturnsNull()
    {
        var sourceId = Guid.NewGuid();
        InsertSource(sourceId, "Standalone Film", seriesId: null);

        var result = await _reader.GetSeriesReferenceAsync(sourceId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeriesReferenceAsync_SeriesSoftDeleted_ReturnsNull()
    {
        var seriesId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        InsertSeries(seriesId, "Removed Series", isDeleted: true);
        InsertSource(sourceId, "A Film", seriesId);

        var result = await _reader.GetSeriesReferenceAsync(sourceId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeriesReferencesForManyAsync_EmptyIdList_ReturnsEmptyDictionary()
    {
        var result = await _reader.GetSeriesReferencesForManyAsync([]);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetSeriesReferencesForManyAsync_MixedIds_ReturnsOnlySourcesWithActiveSeries()
    {
        var seriesId = Guid.NewGuid();
        var sourceWithSeries = Guid.NewGuid();
        var sourceWithoutSeries = Guid.NewGuid();
        InsertSeries(seriesId, "Middle-earth");
        InsertSource(sourceWithSeries, "The Fellowship of the Ring", seriesId);
        InsertSource(sourceWithoutSeries, "Standalone", seriesId: null);

        var result = await _reader.GetSeriesReferencesForManyAsync([sourceWithSeries, sourceWithoutSeries]);

        Assert.HasCount(1, result);
        Assert.AreEqual(seriesId, result[sourceWithSeries].Id);
        Assert.AreEqual("Middle-earth", result[sourceWithSeries].Name);
        Assert.IsFalse(result.ContainsKey(sourceWithoutSeries));
    }
}
