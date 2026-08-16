using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Changelog.Models;
using Quotinator.Changelog.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>Exercises <see cref="ChangelogReader"/>'s DB-first, JSON-fallback behaviour (#309).</summary>
[TestClass]
public class ChangelogReaderTests
{
    [TestCleanup]
    public void TestCleanup() => SqliteConnection.ClearAllPools();

    private static readonly string[] ExpectedUnreleasedHighlights = ["Unreleased highlight"];
    private static readonly string[] ExpectedReleaseHighlights = ["First highlight", "Second highlight"];
    private static readonly int[] ExpectedIssues = [100];
    private static readonly string[] ExpectedCves = ["CVE-2026-00001"];
    private static readonly string[] ExpectedNotificationHighlights = ["Notification-only highlight"];

    private static string UniqueConnectionString() =>
        $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

    private sealed class FakeChangelogService(ChangelogDocument? fallbackDocument) : IChangelogService
    {
        public IReadOnlyList<string> AvailableLanguages { get; } = fallbackDocument is null ? [] : [fallbackDocument.Language];
        public ChangelogDocument? GetForCulture(string? culture) => fallbackDocument;
    }

    private sealed class RecordingLogger : ILogger<ChangelogReader>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class BrokenSqlStrategy : IJoinStrategy<ChangelogLineRow>
    {
        public string BuildSql() => "SELCT * FROM Changelog_Entry;";
    }

    private static ChangelogDocument SourceDocument() => new()
    {
        Language          = "en",
        MachineTranslated = true,
        Unreleased        = new ChangelogUnreleased { Highlights = ["Unreleased highlight"] },
        Releases =
        [
            new ChangelogRelease
            {
                Version    = "1.0.0",
                Date       = "2026-01-01",
                Highlights = ["First highlight", "Second highlight"],
                Issues     = [100],
                Cves       = ["CVE-2026-00001"],
                AudienceHighlights = new Dictionary<string, List<string>>
                {
                    ["notification"] = ["Notification-only highlight"],
                },
                Quote = new ChangelogQuote { Text = "A quote.", Attribution = "Someone" },
            },
        ],
    };

    private static async Task<(SqliteConnectionFactory Factory, ChangelogConnectionKeepAlive KeepAlive)> CreatePopulatedDatabaseAsync()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();

        FakeChangelogService service = new(SourceDocument());
        ChangelogRepository repository = new(factory, NoOpCallerContext.Instance);
        ChangelogSystemContentImporter importer = new(factory, service, repository, NullLogger<ChangelogSystemContentImporter>.Instance);
        await importer.RefreshAsync();

        return (factory, keepAlive);
    }

    /// <summary>DB-backed content reassembles into a <see cref="ChangelogDocument"/> matching the imported source, including audience highlights and issue-number parsing.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_DatabasePopulated_ReturnsReassembledContent()
    {
        (SqliteConnectionFactory factory, ChangelogConnectionKeepAlive keepAlive) = await CreatePopulatedDatabaseAsync();
        using ChangelogConnectionKeepAlive _ = keepAlive;

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), NullLogger<ChangelogReader>.Instance);

        ChangelogDocument? document = await reader.GetDocumentAsync("en");

        Assert.IsNotNull(document);
        Assert.AreEqual("en", document.Language);
        Assert.IsTrue(document.MachineTranslated);
        Assert.IsNotNull(document.Unreleased);
        Assert.AreSequenceEqual(ExpectedUnreleasedHighlights, [.. document.Unreleased.Highlights]);

        Assert.HasCount(1, document.Releases);
        ChangelogRelease release = document.Releases[0];
        Assert.AreEqual("1.0.0", release.Version);
        Assert.AreEqual("2026-01-01", release.Date);
        Assert.AreSequenceEqual(ExpectedReleaseHighlights, [.. release.Highlights]);
        Assert.AreSequenceEqual(ExpectedIssues, [.. release.Issues]);
        Assert.AreSequenceEqual(ExpectedCves, [.. release.Cves]);
        Assert.IsNotNull(release.Quote);
        Assert.AreEqual("A quote.", release.Quote.Text);
        Assert.AreEqual("Someone", release.Quote.Attribution);
        Assert.AreSequenceEqual(
            ExpectedNotificationHighlights,
            [.. release.GetHighlightsFor(Quotinator.Changelog.Enums.ChangelogReservedAudience.Notification)]);
    }

    /// <summary>
    /// A genuinely empty database (schema created, but the background import from Step 6 hasn't
    /// written any rows yet — the real race window #309's non-blocking Program.cs wiring accepts)
    /// falls back the same way a missing table does, rather than returning an empty/null document.
    /// </summary>
    [TestMethod]
    public async Task GetDocumentAsync_DatabaseEmpty_FallsBackToFileService()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        ChangelogDocument fallback = new() { Language = "en" };

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(fallback), NullLogger<ChangelogReader>.Instance);

        ChangelogDocument? document = await reader.GetDocumentAsync("en");

        Assert.AreSame(fallback, document);
    }

    /// <summary>A missing Changelog_Entry table falls back to the JSON-backed service instead of throwing.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_TablesMissing_FallsBackToFileService()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDocument fallback = new() { Language = "en" };

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(fallback), NullLogger<ChangelogReader>.Instance);

        ChangelogDocument? document = await reader.GetDocumentAsync("en");

        Assert.AreSame(fallback, document);
    }

    /// <summary>The missing-table fallback logs a warning, matching #293's NotificationReader precedent — not a silent swallow.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_TablesMissing_LogsWarning()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        RecordingLogger logger = new();
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), logger);

        await reader.GetDocumentAsync("en");

        Assert.Contains(e => e.Level == LogLevel.Warning, logger.Entries,
            "A missing Changelog_Entry table must log a warning, not fail silently.");
    }

    /// <summary>A genuinely different SQL error (not "table missing") propagates rather than being swallowed by the narrow fallback filter.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_UnrelatedSqlError_Propagates()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new BrokenSqlStrategy());
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), NullLogger<ChangelogReader>.Instance);

        await Assert.ThrowsExactlyAsync<SqliteException>(() => reader.GetDocumentAsync("en"));
    }

    public TestContext TestContext { get; set; }
}
