using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Real-SQLite tests for <see cref="SeriesUniverseReferenceReader"/> — added by #284 alongside its
/// migration to <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/>
/// (ADR 017). No fake-backed test previously exercised this reader's own SQL.
/// </summary>
[TestClass]
public class SeriesUniverseReferenceReaderTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;
    private SeriesUniverseReferenceReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_surr_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Quotinator_Universe (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE Quotinator_Series (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                UniverseId TEXT REFERENCES Quotinator_Universe(Id),
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            """);

        _reader = new SeriesUniverseReferenceReader(
            new JoinQueryRepository<UniverseReferenceRow>(_factory, new SeriesUniverseReferenceStrategy()),
            new JoinQueryRepository<SeriesUniverseReferenceRow>(_factory, new SeriesUniverseReferencesBatchStrategy()));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void InsertUniverse(Guid id, string name, bool isDeleted = false) =>
        Execute("INSERT INTO Quotinator_Universe (Id, Name, IsDeleted) VALUES (@id, @name, @isDeleted);",
            new { id = id.ToString("D").ToUpperInvariant(), name, isDeleted = isDeleted ? 1 : 0 });

    private void InsertSeries(Guid id, string name, Guid? universeId) =>
        Execute("INSERT INTO Quotinator_Series (Id, Name, UniverseId) VALUES (@id, @name, @universeId);",
            new
            {
                id = id.ToString("D").ToUpperInvariant(),
                name,
                universeId = universeId?.ToString("D").ToUpperInvariant(),
            });

    private void Execute(string sql, object param)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(sql, param);
    }

    [TestMethod]
    public async Task GetUniverseReferenceAsync_SeriesWithUniverse_ReturnsReference()
    {
        var universeId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        InsertUniverse(universeId, "Star Wars Universe");
        InsertSeries(seriesId, "Skywalker Saga", universeId);

        var result = await _reader.GetUniverseReferenceAsync(seriesId);

        Assert.IsNotNull(result);
        Assert.AreEqual(universeId, result.Value.Id);
        Assert.AreEqual("Star Wars Universe", result.Value.Name);
    }

    [TestMethod]
    public async Task GetUniverseReferenceAsync_SeriesWithNoUniverse_ReturnsNull()
    {
        var seriesId = Guid.NewGuid();
        InsertSeries(seriesId, "Standalone Series", universeId: null);

        var result = await _reader.GetUniverseReferenceAsync(seriesId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetUniverseReferenceAsync_UniverseSoftDeleted_ReturnsNull()
    {
        var universeId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        InsertUniverse(universeId, "Removed Universe", isDeleted: true);
        InsertSeries(seriesId, "A Series", universeId);

        var result = await _reader.GetUniverseReferenceAsync(seriesId);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetUniverseReferencesForManyAsync_EmptyIdList_ReturnsEmptyDictionary()
    {
        var result = await _reader.GetUniverseReferencesForManyAsync([]);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetUniverseReferencesForManyAsync_MixedIds_ReturnsOnlySeriesWithActiveUniverse()
    {
        var universeId = Guid.NewGuid();
        var seriesWithUniverse = Guid.NewGuid();
        var seriesWithoutUniverse = Guid.NewGuid();
        InsertUniverse(universeId, "Middle-earth");
        InsertSeries(seriesWithUniverse, "The Lord of the Rings", universeId);
        InsertSeries(seriesWithoutUniverse, "Standalone", universeId: null);

        var result = await _reader.GetUniverseReferencesForManyAsync([seriesWithUniverse, seriesWithoutUniverse]);

        Assert.HasCount(1, result);
        Assert.AreEqual(universeId, result[seriesWithUniverse].Id);
        Assert.AreEqual("Middle-earth", result[seriesWithUniverse].Name);
        Assert.IsFalse(result.ContainsKey(seriesWithoutUniverse));
    }
}
