using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// #303's payload, exercised through a real <c>Metadata</c> column rather than in memory. The batch,
/// the file and the review workload are what identify the alert, so they have to survive serialization
/// and come back as the same identity — an in-memory comparison would agree with itself without ever
/// proving the round-trip works.
/// </summary>
[TestClass]
public class ImportReviewPendingMetadataTests
{
    public TestContext TestContext { get; set; } = null!;

    private string _tempDir = null!;
    private string _dbPath = null!;
    private NotificationWriter _writer = null!;
    private NotificationReader _reader = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_review_metadata_test_").FullName;
        _dbPath = Path.Combine(_tempDir, "test.db");

        await CurrentSchema.ApplyDataSchemaAsync(_dbPath);

        SqliteConnectionFactory factory = new(_dbPath);
        _writer = new NotificationWriter(factory);
        _reader = TestNotificationReader.Create(factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Every field survives the column round-trip, and the row reads back as the same identity.</summary>
    [TestMethod]
    public async Task Payload_RoundTripsAllFields()
    {
        string batchId = Guid.NewGuid().ToString();

        ImportReviewPendingMetadataDto written = new()
        {
            FileName = "NikhilNamal17_popular-movie-quotes.json",
            Origin   = FileResourceOrigin.System,
            BatchId  = batchId,
            Counts =
            [
                new ImportReviewCountDto { Status = "Pending", Count = 3 },
                new ImportReviewCountDto { Status = "Blocked", Count = 1 },
            ],
            ReleaseState = NotificationReleaseState.NotApplicable,
        };

        await NotificationSeeding.SeedWhileUnresolvedAsync(
            _reader, _writer, NotificationType.ActionRequired, written,
            body: "Four actions await review.", appVersionId: null);

        NotificationEntity stored = (await _reader.GetPagedAsync(1, 0)).Items.Single();

        NotificationMetadataDto? read =
            NotificationMetadataKinds.TryDeserialize(stored.MetadataKind.Parsed, stored.Metadata);

        Assert.IsInstanceOfType<ImportReviewPendingMetadataDto>(read,
            "The row's own MetadataKind must select the payload type, with no knowledge of which producer wrote it.");

        ImportReviewPendingMetadataDto payload = (ImportReviewPendingMetadataDto)read;
        Assert.AreEqual("NikhilNamal17_popular-movie-quotes.json", payload.FileName);
        Assert.AreEqual(FileResourceOrigin.System, payload.Origin);
        Assert.AreEqual(batchId, payload.BatchId, "Dismissal matches on this — a batch id that does not survive storage can never be found again.");
        Assert.HasCount(2, payload.Counts);
        Assert.AreEqual("Pending", payload.Counts[0].Status);
        Assert.AreEqual(3, payload.Counts[0].Count);

        Assert.IsTrue(written.IsSameNotificationAs(payload),
            "A stored payload must read back as the same identity, or dedupe compares against something it can never match.");
    }

    /// <summary>
    /// Two files of the same name from different directories are two files. The same collision #302
    /// found on its own confirmation, which this payload inherits by naming a file the same way.
    /// </summary>
    [TestMethod]
    public void Identity_DiffersByOrigin()
    {
        string batchId = Guid.NewGuid().ToString();

        ImportReviewPendingMetadataDto bundled = Payload("quotinator-curated.json", FileResourceOrigin.System, batchId);
        ImportReviewPendingMetadataDto user    = Payload("quotinator-curated.json", FileResourceOrigin.User,   batchId);

        Assert.IsFalse(bundled.IsSameNotificationAs(user),
            "Same file name, same batch, different directory — two files, so two alerts.");
    }

    /// <summary>
    /// A different batch is a different set of reviews, even for the same file with the same workload.
    /// This is what makes a later reseed raise a fresh alert rather than reusing one that describes
    /// actions which no longer exist.
    /// </summary>
    [TestMethod]
    public void Identity_DiffersByBatch()
    {
        ImportReviewPendingMetadataDto first  = Payload("a.json", FileResourceOrigin.System, Guid.NewGuid().ToString());
        ImportReviewPendingMetadataDto second = Payload("a.json", FileResourceOrigin.System, Guid.NewGuid().ToString());

        Assert.IsFalse(first.IsSameNotificationAs(second),
            "The batch is the set of reviews being reported, so a new batch is a new alert.");
    }

    private static ImportReviewPendingMetadataDto Payload(string fileName, FileResourceOrigin origin, string batchId) => new()
    {
        FileName = fileName,
        Origin   = origin,
        BatchId  = batchId,
        Counts   = [new ImportReviewCountDto { Status = "Pending", Count = 2 }],
        ReleaseState = NotificationReleaseState.NotApplicable,
    };
}
