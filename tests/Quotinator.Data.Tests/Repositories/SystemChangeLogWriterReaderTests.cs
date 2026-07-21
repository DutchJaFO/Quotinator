using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SystemChangeLogWriterReaderTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemChangeLogWriter _writer = null!;
    private SystemChangeLogReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_changelog_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE System_ChangeLog (
                Id               TEXT NOT NULL PRIMARY KEY,
                EntityType       TEXT NOT NULL,
                EntityId         TEXT NOT NULL,
                InitiatedByType  TEXT NOT NULL
                                 CHECK (InitiatedByType IN ('Seed','Import','WriteEndpoint','Enrichment')),
                InitiatedById    TEXT,
                Action           TEXT NOT NULL
                                 CHECK (Action IN ('Created','Modified','SoftDelete','HardDelete')),
                Field            TEXT,
                OldValue         TEXT,
                NewValue         TEXT,
                OccurredAt       TEXT NOT NULL,
                DateCreated      TEXT NOT NULL,
                DateModified     TEXT,
                DateDeleted      TEXT,
                IsDeleted        INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory = new SqliteConnectionFactory(_dbPath);
        _writer  = new SystemChangeLogWriter(_factory);
        _reader  = new SystemChangeLogReader(_factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// EntityId is read back through <c>LOWER(...)</c> (this project's read-time presentation
    /// normalization, independent of what casing was actually written) so a mismatched-case fixture
    /// proves the read side, not just an exact round-trip of the written value — the same gap found
    /// live (#210) that this project's own SqlSelectPresentationGuard and
    /// EntityIdPresentationClassificationTests now catch mechanically. See ADR 012's "read-time
    /// presentation normalization" revision.
    /// </summary>
    [TestMethod]
    public async Task GetHistoryAsync_MixedCaseEntityId_ReturnsLowercase()
    {
        var entry = new SystemChangeLog
        {
            Id              = Guid.NewGuid(),
            EntityType      = "quote",
            EntityId        = "ENTITY-1",
            InitiatedByType = new SafeValue<InitiatorType?>(InitiatorType.Seed.ToString(), InitiatorType.Seed),
            Action          = new SafeValue<ChangeAction?>(ChangeAction.Created.ToString(), ChangeAction.Created),
            OccurredAt      = DateTime.UtcNow,
        };
        await _writer.LogAsync(entry);

        var history = await _reader.GetHistoryAsync("quote", "ENTITY-1");

        Assert.HasCount(1, history);
        Assert.AreEqual("entity-1", history[0].EntityId);
    }

    /// <summary>
    /// <c>InitiatedById</c> is deliberately NOT normalized — it is polymorphic (an import batch UUID,
    /// an HTTP route, or an enrichment provider name), so forcing it lowercase would corrupt
    /// legitimate mixed-case content. Confirms the read side genuinely preserves it as-is.
    /// </summary>
    [TestMethod]
    public async Task GetHistoryAsync_MixedCaseInitiatedById_PreservesCasing()
    {
        var entry = new SystemChangeLog
        {
            Id              = Guid.NewGuid(),
            EntityType      = "quote",
            EntityId        = Guid.NewGuid().ToString("D"),
            InitiatedByType = new SafeValue<InitiatorType?>(InitiatorType.Enrichment.ToString(), InitiatorType.Enrichment),
            InitiatedById   = "TMDb",
            Action          = new SafeValue<ChangeAction?>(ChangeAction.Created.ToString(), ChangeAction.Created),
            OccurredAt      = DateTime.UtcNow,
        };
        await _writer.LogAsync(entry);

        var history = await _reader.GetHistoryAsync("quote", entry.EntityId);

        Assert.HasCount(1, history);
        Assert.AreEqual("TMDb", history[0].InitiatedById);
    }
}
