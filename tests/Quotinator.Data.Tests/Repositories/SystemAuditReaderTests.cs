using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// Exercises <see cref="SystemAuditReader.GetPagedAsync"/> against a real SQLite schema — in
/// particular #195's <c>pageSize = 0</c> fix, caught live by T2 after the type-only retrofit for
/// <see cref="PagedItems{T}"/> left the underlying <c>LIMIT @pageSize</c> query unchanged.
/// </summary>
[TestClass]
public class SystemAuditReaderTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SystemAuditReader _reader = null!;
    private SystemAuditWriter _writer = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_audit_reader_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE System_AuditEntries (
                Id           TEXT    NOT NULL PRIMARY KEY,
                TableName    TEXT    NOT NULL,
                RecordId     TEXT,
                Operation    TEXT    NOT NULL,
                Agent        TEXT,
                PerformedAt  TEXT    NOT NULL,
                DateCreated  TEXT    NOT NULL,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            """);

        var factory = new SqliteConnectionFactory(_dbPath);
        _reader = new SystemAuditReader(factory);
        _writer = new SystemAuditWriter(factory, new CallerContext());
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task GetPagedAsync_PageSizeZero_ReturnsEveryRowNotZeroRows()
    {
        for (var i = 0; i < 3; i++)
            await _writer.WriteAsync(new SystemAuditEntry
            {
                TableName   = "Quotes",
                RecordId    = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                Operation   = AuditOperation.Insert,
                Agent       = "TestRunner/1.0",
                PerformedAt = DateTime.UtcNow,
            });

        var result = await _reader.GetPagedAsync(null, null, 1, 0);

        Assert.AreEqual(3, result.Items.Count, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(3, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }
}
