using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Repositories;

/// <summary>
/// Real-SQLite tests for <see cref="CharacterSourceLinkReader"/> — added by #284 alongside its
/// migration to <see cref="JoinQueryRepository{TResult}"/>/<see cref="Quotinator.Data.Queries.IJoinStrategy{TResult}"/>
/// (ADR 017). No fake-backed test previously exercised this reader's own SQL.
/// </summary>
[TestClass]
public class CharacterSourceLinkReaderTests
{
    private string _tempDir = null!;
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;
    private CharacterSourceLinkReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_csl_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Quotinator_Source (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE Quotinator_CharacterSource (
                Id TEXT PRIMARY KEY,
                CharacterId TEXT NOT NULL,
                SourceId TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );
            """);

        _reader = new CharacterSourceLinkReader(
            new JoinQueryRepository<SourceRow>(_factory, new CharacterSourceReferenceStrategy()),
            new JoinQueryRepository<LinkRow>(_factory, new CharacterSourceReferencesBatchStrategy()));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void InsertSource(Guid id, string title, bool isDeleted = false) =>
        Execute("INSERT INTO Quotinator_Source (Id, Title, IsDeleted) VALUES (@id, @title, @isDeleted);",
            new { id = id.ToString("D").ToUpperInvariant(), title, isDeleted = isDeleted ? 1 : 0 });

    private void InsertLink(Guid characterId, Guid sourceId, bool isDeleted = false) =>
        Execute(
            "INSERT INTO Quotinator_CharacterSource (Id, CharacterId, SourceId, IsDeleted) VALUES (@id, @characterId, @sourceId, @isDeleted);",
            new
            {
                id = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                characterId = characterId.ToString("D").ToUpperInvariant(),
                sourceId = sourceId.ToString("D").ToUpperInvariant(),
                isDeleted = isDeleted ? 1 : 0,
            });

    private void Execute(string sql, object param)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute(sql, param);
    }

    [TestMethod]
    public async Task GetSourceReferencesAsync_CharacterWithLinkedSources_ReturnsReferences()
    {
        var characterId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        InsertSource(sourceId, "Casablanca");
        InsertLink(characterId, sourceId);

        var result = await _reader.GetSourceReferencesAsync(characterId);

        Assert.HasCount(1, result);
        Assert.AreEqual(sourceId, result[0].Id);
        Assert.AreEqual("Casablanca", result[0].Name);
    }

    [TestMethod]
    public async Task GetSourceReferencesAsync_CharacterWithNoLinks_ReturnsEmpty()
    {
        var characterId = Guid.NewGuid();

        var result = await _reader.GetSourceReferencesAsync(characterId);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetSourceReferencesAsync_LinkedSourceSoftDeleted_ExcludedFromResult()
    {
        var characterId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        InsertSource(sourceId, "Removed Film", isDeleted: true);
        InsertLink(characterId, sourceId);

        var result = await _reader.GetSourceReferencesAsync(characterId);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetSourceReferencesForManyAsync_EmptyIdList_ReturnsEmptyDictionary()
    {
        var result = await _reader.GetSourceReferencesForManyAsync([]);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetSourceReferencesForManyAsync_MultipleCharacters_EachGroupedIndependently()
    {
        var characterA = Guid.NewGuid();
        var characterB = Guid.NewGuid();
        var sourceA = Guid.NewGuid();
        var sourceB1 = Guid.NewGuid();
        var sourceB2 = Guid.NewGuid();
        InsertSource(sourceA, "Source A");
        InsertSource(sourceB1, "Source B1");
        InsertSource(sourceB2, "Source B2");
        InsertLink(characterA, sourceA);
        InsertLink(characterB, sourceB1);
        InsertLink(characterB, sourceB2);

        var result = await _reader.GetSourceReferencesForManyAsync([characterA, characterB]);

        Assert.HasCount(1, result[characterA]);
        Assert.HasCount(2, result[characterB]);
    }
}
