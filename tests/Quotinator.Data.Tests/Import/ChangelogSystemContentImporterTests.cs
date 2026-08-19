using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Changelog.Models;
using Quotinator.Changelog.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Import;

/// <summary>Exercises <see cref="ChangelogSystemContentImporter"/>'s flatten-and-write behaviour (#309).</summary>
[TestClass]
public class ChangelogSystemContentImporterTests
{
    [TestCleanup]
    public void TestCleanup() => SqliteConnection.ClearAllPools();

    private static readonly string[] ExpectedHighlightOrder = ["First highlight", "Second highlight", "Third highlight"];
    private static readonly int[] ExpectedSortOrders = [0, 1, 2];
    private static readonly string[] ExpectedIssueValues = ["100", "200"];

    private static string UniqueConnectionString() =>
        $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

    private sealed class FakeChangelogService(IReadOnlyDictionary<string, ChangelogDocument> documents) : IChangelogService
    {
        public IReadOnlyList<string> AvailableLanguages { get; } = [.. documents.Keys];
        public ChangelogDocument? GetForCulture(string? culture) => documents.GetValueOrDefault(culture ?? "en");
    }

    private static async Task<(SqliteConnectionFactory Factory, ChangelogConnectionKeepAlive KeepAlive)> CreateInitialisedDatabaseAsync()
    {
        SqliteConnectionFactory factory = new SqliteConnectionFactory(UniqueConnectionString());
        ChangelogConnectionKeepAlive keepAlive = new ChangelogConnectionKeepAlive(factory);
        ChangelogDatabaseInitializer initializer = new ChangelogDatabaseInitializer(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        return (factory, keepAlive);
    }

    private static ChangelogDocument OneReleaseDocument() => new()
    {
        Language          = "en",
        MachineTranslated = false,
        Unreleased        = new ChangelogUnreleased
        {
            Highlights = ["Unreleased highlight one", "Unreleased highlight two"],
        },
        Releases =
        [
            new ChangelogRelease
            {
                Version    = "1.0.0",
                Date       = "2026-01-01",
                Highlights = ["First highlight", "Second highlight", "Third highlight"],
                Added      = ["Added one"],
                Issues     = [100, 200],
                Cves       = ["CVE-2026-00001"],
                AudienceHighlights = new Dictionary<string, List<string>>
                {
                    ["notification"] = ["Notification-only highlight"],
                },
                Quote = new ChangelogQuote { Text = "A quote.", Attribution = "Someone" },
            },
        ],
    };

    /// <summary>
    /// Writes one <c>Changelog_Entry</c> row per release/unreleased entry, with <c>Changelog_Line</c> children
    /// whose <c>SortOrder</c> preserves each list's own original order.
    /// </summary>
    [TestMethod]
    public async Task RefreshAsync_WritesReleasesAndOrderedLines()
    {
        (SqliteConnectionFactory? factory, ChangelogConnectionKeepAlive? keepAlive) = await CreateInitialisedDatabaseAsync();
        using ChangelogConnectionKeepAlive _ = keepAlive;

        FakeChangelogService service = new FakeChangelogService(new Dictionary<string, ChangelogDocument> { ["en"] = OneReleaseDocument() });
        ChangelogRepository repository = new ChangelogRepository(factory, NoOpCallerContext.Instance);
        ChangelogSystemContentImporter importer = new ChangelogSystemContentImporter(factory, service, repository, NullLogger<ChangelogSystemContentImporter>.Instance);

        await importer.RefreshAsync();

        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);

        int changelogCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Entry;");
        Assert.AreEqual(2, changelogCount, "One row for the unreleased entry, one for the 1.0.0 release.");

        string? releaseId = await connection.ExecuteScalarAsync<string>(
            "SELECT Id FROM Changelog_Entry WHERE Version = '1.0.0';");

        List<(string Value, int SortOrder)> highlightValues =
        [
            .. await connection.QueryAsync<(string Value, int SortOrder)>(
                "SELECT Value, SortOrder FROM Changelog_Line WHERE ChangelogEntryId = @id AND Kind = 'Highlight' ORDER BY SortOrder;",
                new { id = releaseId })
        ];

        Assert.AreSequenceEqual(
            ExpectedHighlightOrder,
            [.. highlightValues.Select(h => h.Value)],
            "ChangelogLine rows for Highlight must preserve the source list's original order via SortOrder.");
        Assert.AreSequenceEqual(ExpectedSortOrders, [.. highlightValues.Select(h => h.SortOrder)]);

        string? audienceValue = await connection.ExecuteScalarAsync<string>(
            "SELECT Value FROM Changelog_Line WHERE ChangelogEntryId = @id AND Kind = 'AudienceHighlight' AND AudienceKey = 'notification';",
            new { id = releaseId });
        Assert.AreEqual("Notification-only highlight", audienceValue);

        List<string> issueValues =
        [
            .. await connection.QueryAsync<string>(
                "SELECT Value FROM Changelog_Line WHERE ChangelogEntryId = @id AND Kind = 'Issue' ORDER BY SortOrder;",
                new { id = releaseId })
        ];
        Assert.AreSequenceEqual(ExpectedIssueValues, issueValues, "Issue numbers are stored as their string form.");

        (string? quoteText, string? quoteAttribution, bool machineTranslated) = await connection.QuerySingleAsync<(string?, string?, bool)>(
            "SELECT QuoteText, QuoteAttribution, MachineTranslated FROM Changelog_Entry WHERE Version = '1.0.0';");
        Assert.AreEqual("A quote.", quoteText);
        Assert.AreEqual("Someone", quoteAttribution);
        Assert.IsFalse(machineTranslated);
    }

    /// <summary>Re-running the importer overwrites existing content rather than duplicating or violating the unique (Language, Version) constraint.</summary>
    [TestMethod]
    public async Task RefreshAsync_RunTwice_OverwritesNotDuplicates()
    {
        (SqliteConnectionFactory? factory, ChangelogConnectionKeepAlive? keepAlive) = await CreateInitialisedDatabaseAsync();
        using ChangelogConnectionKeepAlive _ = keepAlive;

        FakeChangelogService service = new FakeChangelogService(new Dictionary<string, ChangelogDocument> { ["en"] = OneReleaseDocument() });
        ChangelogRepository repository = new ChangelogRepository(factory, NoOpCallerContext.Instance);
        ChangelogSystemContentImporter importer = new ChangelogSystemContentImporter(factory, service, repository, NullLogger<ChangelogSystemContentImporter>.Instance);

        await importer.RefreshAsync();
        await importer.RefreshAsync();

        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);

        int changelogCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Entry;");
        Assert.AreEqual(2, changelogCount, "A second refresh must not duplicate rows.");

        // 2 unreleased highlights + (3 highlights + 1 added + 2 issues + 1 cve + 1 audience highlight) for the release = 10.
        int lineCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Line;");
        Assert.AreEqual(10, lineCount, "A second refresh must not duplicate child rows either.");
    }

    /// <summary>
    /// A refresh clears the tables and rebuilds them. If that is not one transaction, a failure partway
    /// through leaves the database holding neither the old content nor the new — and, worse, a
    /// concurrent reader observes the half-built state as if it were real. Found live (#309, step 17):
    /// a startup read logged <c>served 615 row(s)</c> mid-rebuild where a complete set is 2292, and the
    /// what's-new producer chose which highlights to announce from that partial content.
    /// </summary>
    [TestMethod]
    public async Task RefreshAsync_FailsPartway_LeavesThePreviousContentIntact()
    {
        (SqliteConnectionFactory? factory, ChangelogConnectionKeepAlive? keepAlive) = await CreateInitialisedDatabaseAsync();
        using ChangelogConnectionKeepAlive _ = keepAlive;

        ChangelogRepository repository = new ChangelogRepository(factory, NoOpCallerContext.Instance);

        // Each document must carry its own Language: the importer writes document.Language, not the
        // dictionary key, and (Language, Version) is unique.
        FakeChangelogService healthy = new FakeChangelogService(new Dictionary<string, ChangelogDocument>
        {
            ["en"] = OneReleaseDocument(),
            ["nl"] = DocumentInLanguage("nl"),
        });
        await new ChangelogSystemContentImporter(factory, healthy, repository, NullLogger<ChangelogSystemContentImporter>.Instance)
            .RefreshAsync();

        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);
        int entriesBefore = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Entry;");
        int linesBefore   = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Line;");
        Assert.AreNotEqual(0, entriesBefore, "Guard: the first import must have written something for this test to mean anything.");

        // Second language throws, so the refresh dies after the clear and after "en" has been written.
        FakeThrowingChangelogService failing = new FakeThrowingChangelogService(OneReleaseDocument(), failOn: "nl");
        ChangelogSystemContentImporter importer =
            new ChangelogSystemContentImporter(factory, failing, repository, NullLogger<ChangelogSystemContentImporter>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => importer.RefreshAsync());

        int entriesAfter = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Entry;");
        int linesAfter   = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_Line;");

        Assert.AreEqual(entriesBefore, entriesAfter,
            "A failed refresh must roll back to the previous content — never leave the tables cleared or half-rebuilt.");
        Assert.AreEqual(linesBefore, linesAfter,
            "Child rows must roll back with their parents.");
    }

    /// <summary>The same shape as <see cref="OneReleaseDocument"/>, in a different language.</summary>
    private static ChangelogDocument DocumentInLanguage(string language)
    {
        ChangelogDocument source = OneReleaseDocument();
        return new ChangelogDocument
        {
            Language          = language,
            MachineTranslated = source.MachineTranslated,
            Unreleased        = source.Unreleased,
            Releases          = source.Releases,
        };
    }

    private sealed class FakeThrowingChangelogService(ChangelogDocument document, string failOn) : IChangelogService
    {
        public IReadOnlyList<string> AvailableLanguages { get; } = ["en", failOn];

        public ChangelogDocument? GetForCulture(string? culture) =>
            culture == failOn
                ? throw new InvalidOperationException($"Simulated source failure for '{culture}'.")
                : document;
    }

    public TestContext TestContext { get; set; }
}
