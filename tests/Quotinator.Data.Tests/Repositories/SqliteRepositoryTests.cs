using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SqliteRepositoryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteRepository<Widget> _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_data_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Widgets (
                Id           TEXT    NOT NULL PRIMARY KEY,
                Label        TEXT    NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            """);

        _repository = new SqliteRepository<Widget>(new SqliteConnectionFactory(_dbPath), NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Contract ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SqliteRepository_ImplementsIRepository()
        => Assert.IsInstanceOfType<IRepository<Widget>>(_repository);

    // ── Insert + GetById ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_ThenGetById_ReturnsRecord()
    {
        var entity = new Widget { Label = "Hello" };

        await _repository.InsertAsync(entity);
        var result = await _repository.GetByIdAsync(entity.Id);

        Assert.IsNotNull(result);
        Assert.AreEqual(entity.Id, result.Id);
        Assert.AreEqual("Hello", result.Label);
        Assert.IsFalse(result.IsDeleted);
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        Assert.IsNull(result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UpdateAsync_PersistsChanges()
    {
        var entity = new Widget { Label = "Before" };
        await _repository.InsertAsync(entity);

        entity.Label = "After";
        await _repository.UpdateAsync(entity);
        var result = await _repository.GetByIdAsync(entity.Id);

        Assert.IsNotNull(result);
        Assert.AreEqual("After", result.Label);
        Assert.IsTrue(result.DateModified.IsValid);
    }

    // ── SoftDelete ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SoftDeleteAsync_HidesRecordFromGetById()
    {
        var entity = new Widget { Label = "ToDelete" };
        await _repository.InsertAsync(entity);

        await _repository.SoftDeleteAsync(entity.Id);

        Assert.IsNull(await _repository.GetByIdAsync(entity.Id));
    }

    [TestMethod]
    public async Task SoftDeleteAsync_SetsAuditColumns()
    {
        var entity = new Widget { Label = "ToDelete" };
        await _repository.InsertAsync(entity);

        await _repository.SoftDeleteAsync(entity.Id);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var raw = conn.QuerySingleOrDefault<Widget>(
            "SELECT * FROM Widgets WHERE Id = @id", new { id = entity.Id.ToString("D").ToUpperInvariant() });

        Assert.IsNotNull(raw);
        Assert.IsTrue(raw.IsDeleted);
        Assert.IsTrue(raw.DateDeleted.IsValid);
        Assert.IsTrue(raw.DateModified.IsValid);
    }

    [TestMethod]
    public async Task SoftDeleteAsync_IsIdempotent()
    {
        var entity = new Widget { Label = "Idempotent" };
        await _repository.InsertAsync(entity);

        await _repository.SoftDeleteAsync(entity.Id);
        await _repository.SoftDeleteAsync(entity.Id);
    }

    [TestMethod]
    public async Task SoftDeleteAsync_NoOp_WhenEntityNotFound()
    {
        await _repository.SoftDeleteAsync(Guid.NewGuid());
    }

    // ── GetPageAsync ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetPageAsync_FirstPage_ReturnsRequestedCountAndTotal()
    {
        for (var i = 0; i < 5; i++)
            await _repository.InsertAsync(new Widget { Label = $"Item {i}" });

        var result = await _repository.GetPageAsync(1, 2);

        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(5, result.TotalCount);
    }

    [TestMethod]
    public async Task GetPageAsync_ExcludesSoftDeletedRows()
    {
        var deleted = new Widget { Label = "Deleted" };
        await _repository.InsertAsync(deleted);
        await _repository.InsertAsync(new Widget { Label = "Active 1" });
        await _repository.InsertAsync(new Widget { Label = "Active 2" });
        await _repository.SoftDeleteAsync(deleted.Id);

        var result = await _repository.GetPageAsync(1, 10);

        Assert.AreEqual(2, result.TotalCount);
        Assert.IsFalse(result.Items.Any(w => w.Id == deleted.Id));
    }

    [TestMethod]
    public async Task GetPageAsync_LastPagePartiallyFull_ReturnsRemainderNotAnError()
    {
        for (var i = 0; i < 5; i++)
            await _repository.InsertAsync(new Widget { Label = $"Item {i}" });

        var result = await _repository.GetPageAsync(3, 2);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(5, result.TotalCount);
    }

    [TestMethod]
    public async Task GetPageAsync_PageSizeExceedsAvailableItems_ReturnsAllOfThem()
    {
        for (var i = 0; i < 3; i++)
            await _repository.InsertAsync(new Widget { Label = $"Item {i}" });

        var result = await _repository.GetPageAsync(1, 100);

        Assert.AreEqual(3, result.Items.Count);
        Assert.AreEqual(3, result.TotalCount);
    }

    [TestMethod]
    public async Task GetPageAsync_PageSizeZero_ReturnsEveryRowAsOnePage()
    {
        for (var i = 0; i < 5; i++)
            await _repository.InsertAsync(new Widget { Label = $"Item {i}" });

        var result = await _repository.GetPageAsync(1, 0);

        Assert.AreEqual(5, result.Items.Count);
        Assert.AreEqual(5, result.TotalCount);
        Assert.AreEqual(5, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetPageAsync_PageBeyondLastPage_ReturnsEmptyItemsWithCorrectTotal()
    {
        for (var i = 0; i < 3; i++)
            await _repository.InsertAsync(new Widget { Label = $"Item {i}" });

        var result = await _repository.GetPageAsync(5, 2);

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(3, result.TotalCount);
    }

    [TestMethod]
    public async Task GetPageAsync_StableOrderAcrossPages_NoRowRepeatedOrSkipped()
    {
        var inserted = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var widget = new Widget { Label = $"Item {i}", DateCreated = SafeDateValue.From(new DateTime(2026, 1, 1).AddMinutes(i)) };
            await _repository.InsertAsync(widget);
            inserted.Add(widget.Id);
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await _repository.GetPageAsync(page, 3);
            seen.AddRange(result.Items.Select(w => w.Id));
        }

        CollectionAssert.AreEquivalent(inserted, seen);
        Assert.AreEqual(inserted.Count, seen.Distinct().Count(), "a row was repeated across pages");
    }

    [TestMethod]
    public async Task GetPageAsync_TotalCountIgnoresPaging_ReportsAllActiveRows()
    {
        for (var i = 0; i < 10; i++)
            await _repository.InsertAsync(new Widget { Label = $"Item {i}" });

        var result = await _repository.GetPageAsync(1, 3);

        Assert.AreEqual(3, result.Items.Count);
        Assert.AreEqual(10, result.TotalCount);
    }

    [TestMethod]
    public async Task GetPageAsync_CustomOrderByColumn_SortsByThatColumn()
    {
        await _repository.InsertAsync(new Widget { Label = "Banana" });
        await _repository.InsertAsync(new Widget { Label = "Apple" });
        await _repository.InsertAsync(new Widget { Label = "Cherry" });

        var result = await _repository.GetPageAsync(1, 10, [new SortColumn("Label")]);

        CollectionAssert.AreEqual(new[] { "Apple", "Banana", "Cherry" }, result.Items.Select(w => w.Label).ToList());
    }

    [TestMethod]
    public async Task GetPageAsync_DescendingOrder_SortsInReverse()
    {
        await _repository.InsertAsync(new Widget { Label = "Banana" });
        await _repository.InsertAsync(new Widget { Label = "Apple" });
        await _repository.InsertAsync(new Widget { Label = "Cherry" });

        var result = await _repository.GetPageAsync(1, 10, [new SortColumn("Label", Descending: true)]);

        CollectionAssert.AreEqual(new[] { "Cherry", "Banana", "Apple" }, result.Items.Select(w => w.Label).ToList());
    }

    [TestMethod]
    public async Task GetPageAsync_MultiColumnOrder_SortsByBothColumnsInOrder()
    {
        var sameOlder = new Widget { Label = "Same", DateCreated = SafeDateValue.From(new DateTime(2026, 1, 1)) };
        var sameNewer = new Widget { Label = "Same", DateCreated = SafeDateValue.From(new DateTime(2026, 1, 2)) };
        var other     = new Widget { Label = "Other", DateCreated = SafeDateValue.From(new DateTime(2026, 1, 1)) };
        await _repository.InsertAsync(sameOlder);
        await _repository.InsertAsync(sameNewer);
        await _repository.InsertAsync(other);

        var result = await _repository.GetPageAsync(
            1, 10, [new SortColumn("Label"), new SortColumn("DateCreated", Descending: true)]);

        CollectionAssert.AreEqual(
            new[] { other.Id, sameNewer.Id, sameOlder.Id },
            result.Items.Select(w => w.Id).ToList());
    }

    [TestMethod]
    public async Task GetPageAsync_UnknownColumn_ThrowsArgumentExceptionNamingTheColumn()
    {
        await _repository.InsertAsync(new Widget { Label = "Item" });

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _repository.GetPageAsync(1, 10, [new SortColumn("NotARealColumn")]));

        StringAssert.Contains(ex.Message, "NotARealColumn");
    }

    [TestMethod]
    public async Task GetPageAsync_SqlInjectionShapedColumn_ThrowsArgumentException()
    {
        await _repository.InsertAsync(new Widget { Label = "Item" });

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _repository.GetPageAsync(1, 10, [new SortColumn("Id; DROP TABLE Widgets;")]));
    }
}
