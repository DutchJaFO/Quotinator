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
        var factory = new SqliteConnectionFactory(UniqueConnectionString());
        var keepAlive = new ChangelogConnectionKeepAlive(factory);
        var initializer = new ChangelogDatabaseInitializer(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
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
    /// Writes one <c>Changelog</c> row per release/unreleased entry, with <c>ChangelogLine</c> children
    /// whose <c>SortOrder</c> preserves each list's own original order.
    /// </summary>
    [TestMethod]
    public async Task RefreshAsync_WritesReleasesAndOrderedLines()
    {
        var (factory, keepAlive) = await CreateInitialisedDatabaseAsync();
        using var _ = keepAlive;

        var service = new FakeChangelogService(new Dictionary<string, ChangelogDocument> { ["en"] = OneReleaseDocument() });
        var repository = new ChangelogRepository(factory, NoOpCallerContext.Instance);
        var importer = new ChangelogSystemContentImporter(factory, service, repository, NullLogger<ChangelogSystemContentImporter>.Instance);

        await importer.RefreshAsync();

        using var connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);

        var changelogCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog;");
        Assert.AreEqual(2, changelogCount, "One row for the unreleased entry, one for the 1.0.0 release.");

        var releaseId = await connection.ExecuteScalarAsync<string>(
            "SELECT Id FROM Changelog WHERE Version = '1.0.0';");

        var highlightValues = (await connection.QueryAsync<(string Value, int SortOrder)>(
            "SELECT Value, SortOrder FROM ChangelogLine WHERE ChangelogId = @id AND Kind = 'Highlight' ORDER BY SortOrder;",
            new { id = releaseId })).ToList();

        Assert.AreSequenceEqual(
            ExpectedHighlightOrder,
            [.. highlightValues.Select(h => h.Value)],
            "ChangelogLine rows for Highlight must preserve the source list's original order via SortOrder.");
        Assert.AreSequenceEqual(ExpectedSortOrders, [.. highlightValues.Select(h => h.SortOrder)]);

        var audienceValue = await connection.ExecuteScalarAsync<string>(
            "SELECT Value FROM ChangelogLine WHERE ChangelogId = @id AND Kind = 'AudienceHighlight' AND AudienceKey = 'notification';",
            new { id = releaseId });
        Assert.AreEqual("Notification-only highlight", audienceValue);

        var issueValues = (await connection.QueryAsync<string>(
            "SELECT Value FROM ChangelogLine WHERE ChangelogId = @id AND Kind = 'Issue' ORDER BY SortOrder;",
            new { id = releaseId })).ToList();
        Assert.AreSequenceEqual(ExpectedIssueValues, issueValues, "Issue numbers are stored as their string form.");

        var (quoteText, quoteAttribution, machineTranslated) = await connection.QuerySingleAsync<(string?, string?, bool)>(
            "SELECT QuoteText, QuoteAttribution, MachineTranslated FROM Changelog WHERE Version = '1.0.0';");
        Assert.AreEqual("A quote.", quoteText);
        Assert.AreEqual("Someone", quoteAttribution);
        Assert.IsFalse(machineTranslated);
    }

    /// <summary>Re-running the importer overwrites existing content rather than duplicating or violating the unique (Language, Version) constraint.</summary>
    [TestMethod]
    public async Task RefreshAsync_RunTwice_OverwritesNotDuplicates()
    {
        var (factory, keepAlive) = await CreateInitialisedDatabaseAsync();
        using var _ = keepAlive;

        var service = new FakeChangelogService(new Dictionary<string, ChangelogDocument> { ["en"] = OneReleaseDocument() });
        var repository = new ChangelogRepository(factory, NoOpCallerContext.Instance);
        var importer = new ChangelogSystemContentImporter(factory, service, repository, NullLogger<ChangelogSystemContentImporter>.Instance);

        await importer.RefreshAsync();
        await importer.RefreshAsync();

        using var connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync(TestContext.CancellationToken);

        var changelogCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog;");
        Assert.AreEqual(2, changelogCount, "A second refresh must not duplicate rows.");

        // 2 unreleased highlights + (3 highlights + 1 added + 2 issues + 1 cve + 1 audience highlight) for the release = 10.
        var lineCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ChangelogLine;");
        Assert.AreEqual(10, lineCount, "A second refresh must not duplicate child rows either.");
    }

    public TestContext TestContext { get; set; }
}
