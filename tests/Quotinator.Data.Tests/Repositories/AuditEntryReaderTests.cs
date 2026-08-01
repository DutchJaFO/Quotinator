using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// Exercises <see cref="AuditEntryReader.GetPagedAsync"/> against a real SQLite schema — in
/// particular #195's <c>pageSize = 0</c> fix, caught live by T2 after the type-only retrofit for
/// <see cref="PagedItems{T}"/> left the underlying <c>LIMIT @pageSize</c> query unchanged.
/// </summary>
[TestClass]
public class AuditEntryReaderTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private AuditEntryReader _reader = null!;
    private AuditEntryWriter _writer = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_audit_reader_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Audit_Entry (
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
        _reader = new AuditEntryReader(factory);
        _writer = new AuditEntryWriter(factory, new CallerContext());
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
            await _writer.WriteAsync(new AuditEntryEntity
            {
                TableName   = "Quotes",
                RecordId    = Guid.NewGuid().ToString("D").ToUpperInvariant(),
                Operation   = AuditOperation.Insert,
                Agent       = "TestRunner/1.0",
                PerformedAt = DateTime.UtcNow,
            });

        var result = await _reader.GetPagedAsync(null, null, 1, 0);

        Assert.HasCount(3, result.Items, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(3, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetPagedAsync_MixedCaseRecordId_ReturnsLowercase()
    {
        // RecordId is read back through LOWER(...) (this project's read-time normalization,
        // independent of what casing was actually written) so a mismatched-case fixture proves the
        // read side, not just an exact round-trip of the written value — see #210's fifth round.
        await _writer.WriteAsync(new AuditEntryEntity
        {
            TableName   = "Quotes",
            RecordId    = "F0000210-0000-4000-8000-000000000210",
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = DateTime.UtcNow,
        });

        var result = await _reader.GetPagedAsync(null, null, 1, 20);

        Assert.AreEqual("f0000210-0000-4000-8000-000000000210", result.Items.Single().RecordId);
    }

    /// <summary>
    /// #216 fix: `?table=quotes` (lowercase — the natural spelling given this endpoint's own JSON
    /// casing conventions) must still match the PascalCase-stored "Quotes" rows, not silently filter
    /// to zero results.
    /// </summary>
    [TestMethod]
    public async Task GetPagedAsync_LowercaseTableFilter_StillMatchesPascalCaseStoredRows()
    {
        await _writer.WriteAsync(new AuditEntryEntity
        {
            TableName   = "Quotes",
            RecordId    = Guid.NewGuid().ToString("D"),
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = DateTime.UtcNow,
        });

        var result = await _reader.GetPagedAsync("quotes", null, 1, 20);

        Assert.HasCount(1, result.Items, "Lowercase ?table=quotes must still match the stored 'Quotes' row");
    }
}
