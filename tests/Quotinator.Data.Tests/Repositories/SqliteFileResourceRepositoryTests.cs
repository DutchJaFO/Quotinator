using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SqliteFileResourceRepositoryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private SqliteFileResourceRepository _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_fileresource_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS Import_Batch (
                Id TEXT NOT NULL PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS Import_FileResource (
                Id                      TEXT    NOT NULL PRIMARY KEY,
                FileName                TEXT    NOT NULL,
                OriginalFolderPath      TEXT,
                Origin                  TEXT    NOT NULL
                                        CHECK (Origin IN ('System', 'User', 'Upload')),
                HomeDirectoryKey        TEXT,
                ContentHash             TEXT    NOT NULL,
                LineEnding              TEXT    NOT NULL
                                        CHECK (LineEnding IN ('LF', 'CRLF', 'CR')),
                EndsWithTrailingNewline INTEGER NOT NULL,
                Converter               TEXT,
                ConverterOptions        TEXT,
                FirstSeenAtUtc          TEXT    NOT NULL,
                LastSeenAtUtc           TEXT    NOT NULL,
                DateCreated             TEXT    NOT NULL,
                DateModified            TEXT,
                DateDeleted             TEXT,
                IsDeleted               INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_FileResource_ContentHash ON Import_FileResource (ContentHash);
            CREATE INDEX IF NOT EXISTS IX_Import_FileResource_FileName ON Import_FileResource (FileName);

            CREATE TABLE IF NOT EXISTS Import_FileResourceLine (
                Id             TEXT    NOT NULL PRIMARY KEY,
                FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
                LineNumber     INTEGER NOT NULL,
                Text           TEXT    NOT NULL,
                DateCreated    TEXT    NOT NULL,
                DateModified   TEXT,
                DateDeleted    TEXT,
                IsDeleted      INTEGER NOT NULL DEFAULT 0,
                UNIQUE (FileResourceId, LineNumber)
            );

            CREATE TABLE IF NOT EXISTS Import_FileResourceBatch (
                Id             TEXT    NOT NULL PRIMARY KEY,
                FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
                ImportBatchId  TEXT    NOT NULL REFERENCES Import_Batch(Id),
                ImportedAt     TEXT    NOT NULL,
                DateCreated    TEXT    NOT NULL,
                DateModified   TEXT,
                DateDeleted    TEXT,
                IsDeleted      INTEGER NOT NULL DEFAULT 0,
                UNIQUE (FileResourceId, ImportBatchId)
            );
            """);

        _repository = new SqliteFileResourceRepository(new SqliteConnectionFactory(_dbPath));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Inserts a real Import_Batch row and returns its id — Microsoft.Data.Sqlite enforces foreign keys by default, so every WriteAsync call needs a real batch to link to.</summary>
    private async Task<Guid> InsertImportBatchAsync()
    {
        var id = Guid.NewGuid();
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await conn.ExecuteAsync("INSERT INTO Import_Batch (Id) VALUES (@id);", new { id = id.ToString() });
        return id;
    }

    // ── WriteAsync — dedup ───────────────────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_UnchangedFileContent_DoesNotDuplicateRow()
    {
        var firstId = await _repository.WriteAsync(
            "quotinator-curated.json", "sources", FileResourceOrigin.System, "[\"a\",\"b\"]",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var secondId = await _repository.WriteAsync(
            "quotinator-curated.json", "sources", FileResourceOrigin.System, "[\"a\",\"b\"]",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(firstId, secondId, "Re-capturing unchanged content must reuse the same FileResource row, not insert a new one");
    }

    [TestMethod]
    public async Task WriteAsync_ChangedFileContent_CreatesNewRow()
    {
        var firstId = await _repository.WriteAsync(
            "quotinator-curated.json", "sources", FileResourceOrigin.System, "[\"a\",\"b\"]",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var secondId = await _repository.WriteAsync(
            "quotinator-curated.json", "sources", FileResourceOrigin.System, "[\"a\",\"b\",\"c\"]",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        Assert.AreNotEqual(firstId, secondId, "Changed content must produce a new FileResource row");
    }

    // ── WriteAsync — batch linkage ───────────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_LinksFileResourceToImportBatch()
    {
        var batchId = await InsertImportBatchAsync();

        var fileResourceId = await _repository.WriteAsync(
            "quotinator-curated.json", "sources", FileResourceOrigin.System, "content",
            batchId, cancellationToken: TestContext.CancellationToken);

        using var checkConn = new SqliteConnection($"Data Source={_dbPath}");
        checkConn.Open();
        var linkedBatchId = await checkConn.QuerySingleAsync<string>(
            "SELECT ImportBatchId FROM Import_FileResourceBatch WHERE FileResourceId = @id;",
            new { id = fileResourceId.ToString() });

        Assert.AreEqual(batchId.ToString().ToLowerInvariant(), linkedBatchId.ToLowerInvariant());
    }

    // ── WriteAsync — line decomposition ──────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_SplitsContentIntoOrderedFileResourceLineRows()
    {
        var fileResourceId = await _repository.WriteAsync(
            "three-lines.txt", null, FileResourceOrigin.Upload, "one\ntwo\nthree",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var lines = await _repository.GetLinesAsync(fileResourceId, TestContext.CancellationToken);

        Assert.HasCount(3, lines);
        Assert.AreEqual("one", lines[0].Text);
        Assert.AreEqual(1, lines[0].LineNumber);
        Assert.AreEqual("two", lines[1].Text);
        Assert.AreEqual(2, lines[1].LineNumber);
        Assert.AreEqual("three", lines[2].Text);
        Assert.AreEqual(3, lines[2].LineNumber);
    }

    [TestMethod]
    public async Task WriteAsync_DetectsCrlfLineEndingAndTrailingNewline()
    {
        var fileResourceId = await _repository.WriteAsync(
            "windows-file.txt", null, FileResourceOrigin.Upload, "one\r\ntwo\r\n",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.AreEqual(LineEndingStyle.CRLF, found!.LineEnding.Parsed);
        Assert.IsTrue(found.EndsWithTrailingNewline);
    }

    [TestMethod]
    public async Task WriteAsync_DetectsLfLineEndingNoTrailingNewline()
    {
        var fileResourceId = await _repository.WriteAsync(
            "unix-file.txt", null, FileResourceOrigin.Upload, "one\ntwo",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.AreEqual(LineEndingStyle.LF, found!.LineEnding.Parsed);
        Assert.IsFalse(found.EndsWithTrailingNewline);
    }

    // ── WriteAsync — origin/folder ────────────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_UploadOrigin_StoresNullOriginalFolderPath()
    {
        var fileResourceId = await _repository.WriteAsync(
            "uploaded.json", null, FileResourceOrigin.Upload, "content",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.AreEqual(FileResourceOrigin.Upload, found!.Origin.Parsed);
        Assert.IsNull(found.OriginalFolderPath);
    }

    [TestMethod]
    public async Task WriteAsync_UploadOrigin_StoresNullHomeDirectoryKey()
    {
        var fileResourceId = await _repository.WriteAsync(
            "uploaded2.json", null, FileResourceOrigin.Upload, "content2",
            await InsertImportBatchAsync(), homeDirectoryKey: null, cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.IsNull(found!.HomeDirectoryKey);
    }

    [TestMethod]
    public async Task WriteAsync_SystemOrigin_StoresSuppliedHomeDirectoryKey()
    {
        var fileResourceId = await _repository.WriteAsync(
            "quotinator-curated.json", "sources", FileResourceOrigin.System, "content3",
            await InsertImportBatchAsync(), homeDirectoryKey: "sources", cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.AreEqual("sources", found!.HomeDirectoryKey);
    }

    // ── WriteAsync — converter/converterOptions ──────────────────────────────

    [TestMethod]
    public async Task WriteAsync_ConverterAndConverterOptionsSupplied_AreStoredOnTheRow()
    {
        var fileResourceId = await _repository.WriteAsync(
            "raw-upstream.csv", null, FileResourceOrigin.Upload, "a,b,c",
            await InsertImportBatchAsync(), converter: "csv", converterOptions: "{\"delimiter\":\",\"}",
            cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.AreEqual("csv", found!.Converter);
        Assert.AreEqual("{\"delimiter\":\",\"}", found.ConverterOptions);
    }

    [TestMethod]
    public async Task WriteAsync_NoConverterSupplied_LeavesConverterColumnsNull()
    {
        var fileResourceId = await _repository.WriteAsync(
            "already-canonical.json", null, FileResourceOrigin.System, "[]",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var found = await _repository.FindAsync(fileResourceId, TestContext.CancellationToken);

        Assert.IsNotNull(found);
        Assert.IsNull(found!.Converter);
        Assert.IsNull(found.ConverterOptions);
    }

    [TestMethod]
    public async Task WriteAsync_DedupHitWithDifferentConverter_OverwritesWithTheLatestValues()
    {
        var firstId = await _repository.WriteAsync(
            "same-content.csv", null, FileResourceOrigin.Upload, "a,b,c",
            await InsertImportBatchAsync(), converter: "csv", converterOptions: "{\"delimiter\":\",\"}",
            cancellationToken: TestContext.CancellationToken);

        var secondId = await _repository.WriteAsync(
            "same-content.csv", null, FileResourceOrigin.Upload, "a,b,c",
            await InsertImportBatchAsync(), converter: "csv", converterOptions: "{\"delimiter\":\";\"}",
            cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(firstId, secondId, "Unchanged content must still reuse the same row");

        var found = await _repository.FindAsync(firstId, TestContext.CancellationToken);
        Assert.IsNotNull(found);
        Assert.AreEqual("{\"delimiter\":\";\"}", found!.ConverterOptions,
            "A dedup hit must overwrite Converter/ConverterOptions with the latest capture's values, not keep the first");
    }

    // ── GetPageAsync (#251) ──────────────────────────────────────────────────

    [TestMethod]
    public async Task GetPageAsync_NoFilters_ReturnsAllRows()
    {
        await _repository.WriteAsync("a.json", null, FileResourceOrigin.System, "a", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync("b.json", null, FileResourceOrigin.Upload, "b", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var result = await _repository.GetPageAsync(fileName: null, origin: null, page: 1, pageSize: 20, TestContext.CancellationToken);

        Assert.AreEqual(2, result.TotalCount);
        Assert.HasCount(2, result.Items);
    }

    [TestMethod]
    public async Task GetPageAsync_FilterByFileName_ReturnsOnlyMatchingFile()
    {
        await _repository.WriteAsync("target.json", null, FileResourceOrigin.System, "a", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync("other.json", null, FileResourceOrigin.System, "b", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var result = await _repository.GetPageAsync("target.json", origin: null, page: 1, pageSize: 20, TestContext.CancellationToken);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual("target.json", result.Items.Single().FileName);
    }

    [TestMethod]
    public async Task GetPageAsync_FilterByFileNameDifferentCase_StillMatches()
    {
        await _repository.WriteAsync("Target.json", null, FileResourceOrigin.System, "a", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var result = await _repository.GetPageAsync("target.JSON", origin: null, page: 1, pageSize: 20, TestContext.CancellationToken);

        Assert.AreEqual(1, result.TotalCount);
    }

    [TestMethod]
    public async Task GetPageAsync_FilterByOrigin_ReturnsOnlyMatchingOrigin()
    {
        await _repository.WriteAsync("a.json", null, FileResourceOrigin.System, "a", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync("b.json", null, FileResourceOrigin.Upload, "b", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var result = await _repository.GetPageAsync(fileName: null, FileResourceOrigin.Upload, page: 1, pageSize: 20, TestContext.CancellationToken);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual("b.json", result.Items.Single().FileName);
    }

    [TestMethod]
    public async Task GetPageAsync_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        for (var i = 0; i < 3; i++)
            await _repository.WriteAsync($"file-{i}.json", null, FileResourceOrigin.System, $"content-{i}", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var result = await _repository.GetPageAsync(fileName: null, origin: null, page: 1, pageSize: 0, TestContext.CancellationToken);

        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(3, result.PageSize);
        Assert.HasCount(3, result.Items);
    }

    [TestMethod]
    public async Task GetPageAsync_FileLinkedToMultipleBatches_ReportsCorrectLinkedBatchCount()
    {
        await _repository.WriteAsync("shared.json", null, FileResourceOrigin.System, "content", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync("shared.json", null, FileResourceOrigin.System, "content", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync("shared.json", null, FileResourceOrigin.System, "content", await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        var result = await _repository.GetPageAsync("shared.json", origin: null, page: 1, pageSize: 20, TestContext.CancellationToken);

        Assert.AreEqual(3, result.Items.Single().LinkedBatchCount,
            "Unchanged content re-captured across three batches must report all three links, not just the most recent");
    }

    // ── GetBatchIdsAsync (#251) ──────────────────────────────────────────────

    [TestMethod]
    public async Task GetBatchIdsAsync_FileLinkedToMultipleBatches_ReturnsAllLinkedBatchIds()
    {
        var firstBatch  = await InsertImportBatchAsync();
        var secondBatch = await InsertImportBatchAsync();
        var fileResourceId = await _repository.WriteAsync(
            "shared.json", null, FileResourceOrigin.System, "content", firstBatch, cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync(
            "shared.json", null, FileResourceOrigin.System, "content", secondBatch, cancellationToken: TestContext.CancellationToken);

        var batchIds = await _repository.GetBatchIdsAsync(fileResourceId, TestContext.CancellationToken);

        Assert.HasCount(2, batchIds);
        Assert.Contains(firstBatch, batchIds);
        Assert.Contains(secondBatch, batchIds);
    }

    [TestMethod]
    public async Task GetBatchIdsAsync_UnknownFileResourceId_ReturnsEmpty()
    {
        var batchIds = await _repository.GetBatchIdsAsync(Guid.NewGuid(), TestContext.CancellationToken);

        Assert.IsEmpty(batchIds);
    }

    // ── PruneAsync ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PruneAsync_KeepsOnlyKeepPerFileMostRecentRowsPerFileName()
    {
        for (var i = 0; i < 5; i++)
        {
            await _repository.WriteAsync(
                "same-name.json", null, FileResourceOrigin.System, $"content-v{i}",
                await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        }

        var prunedCount = await _repository.PruneAsync(keepPerFile: 2, TestContext.CancellationToken);

        Assert.AreEqual(3, prunedCount);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var remaining = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResource WHERE FileName = 'same-name.json' AND IsDeleted = 0;");
        Assert.AreEqual(2, remaining);
    }

    [TestMethod]
    public async Task PruneAsync_CascadesDeleteToFileResourceLineAndBatchLinks()
    {
        var fileResourceId = await _repository.WriteAsync(
            "solo.json", null, FileResourceOrigin.System, "one\ntwo",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);
        await _repository.WriteAsync(
            "solo.json", null, FileResourceOrigin.System, "changed content, keeps most recent",
            await InsertImportBatchAsync(), cancellationToken: TestContext.CancellationToken);

        await _repository.PruneAsync(keepPerFile: 1, TestContext.CancellationToken);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var remainingLines = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResourceLine WHERE FileResourceId = @id;",
            new { id = fileResourceId.ToString() });
        var remainingBatchLinks = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResourceBatch WHERE FileResourceId = @id;",
            new { id = fileResourceId.ToString() });

        Assert.AreEqual(0, remainingLines, "Pruning the parent row must cascade-delete its lines");
        Assert.AreEqual(0, remainingBatchLinks, "Pruning the parent row must cascade-delete its batch links");
    }

    public TestContext TestContext { get; set; }
}
