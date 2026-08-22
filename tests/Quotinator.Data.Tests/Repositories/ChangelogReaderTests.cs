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

    /// <summary>
    /// A readiness signal already reporting a completed import — the steady state after startup, and
    /// the right default for any test whose subject is not the import race itself.
    /// </summary>
    private static ChangelogImportReadiness SucceededImport()
    {
        ChangelogImportReadiness readiness = new();
        readiness.MarkSucceeded();
        return readiness;
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
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), SucceededImport(), NullLogger<ChangelogReader>.Instance);

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

    // GetDocumentAsync_DatabaseEmpty_FallsBackToFileService was removed here, not renamed: it asserted
    // that an empty database always falls back, which step 16 established is wrong. Emptiness alone
    // says nothing — during the startup window it means "the import has not written yet", and once the
    // import has succeeded it means "this application genuinely has no changelog", which is an answer
    // rather than a failure. The three tests below replace it, one per state it used to conflate.

    /// <summary>
    /// A successful database-backed read says so in the log. Verification that the database actually
    /// served the changelog previously rested on the *absence* of a fallback warning, which the
    /// silent empty-database path at the time made unsound — an absent message proves nothing about
    /// which source answered. This positive statement is what
    /// docs/automated-testing/notifications-and-changelog/07-changelog-served-from-its-own-database.md
    /// and this issue's own T1/T2 rows assert on instead.
    /// </summary>
    [TestMethod]
    public async Task GetDocumentAsync_DatabasePopulated_LogsThatTheDatabaseServedIt()
    {
        (SqliteConnectionFactory factory, ChangelogConnectionKeepAlive keepAlive) = await CreatePopulatedDatabaseAsync();
        using ChangelogConnectionKeepAlive _ = keepAlive;

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        RecordingLogger logger = new();
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), SucceededImport(), logger);

        await reader.GetDocumentAsync("en");

        Assert.Contains(
            e => e.Level == LogLevel.Information && e.Message.Contains("from the database", StringComparison.Ordinal),
            logger.Entries,
            "A database-backed read must state that the database served it — verification cannot rest on the absence of a fallback warning.");
    }

    /// <summary>
    /// An empty database after a *successful* import is authoritative, not a failure: a new application
    /// legitimately has no changelog yet. It must not fall back to the JSON files and must not warn.
    /// </summary>
    [TestMethod]
    public async Task GetDocumentAsync_DatabaseEmptyAfterSuccessfulImport_DoesNotFallBack()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        ChangelogDocument fallback = new() { Language = "en" };

        ChangelogImportReadiness readiness = new();
        readiness.MarkSucceeded();

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        RecordingLogger logger = new();
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(fallback), readiness, logger);

        ChangelogDocument? document = await reader.GetDocumentAsync("en");

        Assert.IsNull(document, "A successfully-imported but empty changelog is an answer — the JSON fallback must not be consulted.");
        Assert.IsEmpty(logger.Entries.Where(e => e.Level == LogLevel.Warning),
            "An application with no changelog entries yet is not a fault and must not warn.");
    }

    /// <summary>
    /// The startup race, which is what made the fallback the normal path: the import has not concluded
    /// when the read arrives. The reader must wait for it rather than reading emptiness as failure.
    /// </summary>
    [TestMethod]
    public async Task GetDocumentAsync_ImportStillRunning_WaitsForItRatherThanFallingBack()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();

        // Schema exists, no content yet — exactly the state a read hits during the startup window.
        ChangelogImportReadiness readiness = new();
        FakeChangelogService service = new(SourceDocument());
        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        RecordingLogger logger = new();
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(new ChangelogDocument { Language = "en" }), readiness, logger);

        Task<ChangelogDocument?> read = reader.GetDocumentAsync("en");

        // The import runs while the reader is already waiting, then reports — the real sequence.
        ChangelogRepository repository = new(factory, NoOpCallerContext.Instance);
        ChangelogSystemContentImporter importer = new(factory, service, repository, NullLogger<ChangelogSystemContentImporter>.Instance);
        await importer.RefreshAsync();
        readiness.MarkSucceeded();

        ChangelogDocument? document = await read;

        Assert.IsNotNull(document, "The reader must re-query after the import concludes, not fall back on the first empty result.");
        Assert.Contains(e => e.Message.Contains("from the database", StringComparison.Ordinal), logger.Entries);
    }

    /// <summary>
    /// A read must reflect *this* process's import, never a previous run's leftovers. Before #309 the
    /// what's-new producer read the JSON files directly and was therefore always current; moving it
    /// behind a database rebuilt asynchronously at startup reintroduced staleness. Found live
    /// (step 18): a startup read was served the previous run's complete content because the current
    /// import had not committed yet. Identical content on a normal boot — but on an upgrade the old
    /// changelog has none of the new release's highlights, which is exactly what the producer exists to
    /// announce, and which of the two it sees is a race.
    /// </summary>
    [TestMethod]
    public async Task GetDocumentAsync_PreviousRunsContentStillPresent_ReturnsThisImportsContentInstead()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        ChangelogRepository repository = new(factory, NoOpCallerContext.Instance);

        // A previous run's content: complete, committed, and stale.
        ChangelogDocument previousRun = new()
        {
            Language          = "en",
            MachineTranslated = false,
            Releases          = [PreviousRelease()],
        };
        await new ChangelogSystemContentImporter(
            factory, new FakeChangelogService(previousRun), repository, NullLogger<ChangelogSystemContentImporter>.Instance)
            .RefreshAsync();

        ChangelogImportReadiness readiness = new();
        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), readiness, NullLogger<ChangelogReader>.Instance);

        Task<ChangelogDocument?> read = reader.GetDocumentAsync("en");

        // This process's import replaces it while the read is in flight — the upgrade case.
        await new ChangelogSystemContentImporter(
            factory, new FakeChangelogService(SourceDocument()), repository, NullLogger<ChangelogSystemContentImporter>.Instance)
            .RefreshAsync();
        readiness.MarkSucceeded();

        ChangelogDocument? document = await read;

        Assert.IsNotNull(document);
        Assert.AreEqual("1.0.0", document.Releases[0].Version,
            "A read must never be served the previous run's content — it has to wait for this process's own import.");
    }

    /// <summary>A release that only the previous run's content contains, so a stale read is identifiable by version alone.</summary>
    private static ChangelogRelease PreviousRelease() => new()
    {
        Version    = "0.9.0",
        Date       = "2025-12-01",
        Highlights = ["Content from a previous run"],
    };

    /// <summary>A genuinely failed import is the case the JSON fallback exists for — and it says so.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_ImportFailed_FallsBackAndWarns()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        ChangelogDocument fallback = new() { Language = "en" };

        ChangelogImportReadiness readiness = new();
        readiness.MarkFailed();

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        RecordingLogger logger = new();
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(fallback), readiness, logger);

        ChangelogDocument? document = await reader.GetDocumentAsync("en");

        Assert.AreSame(fallback, document);
        Assert.Contains(e => e.Level == LogLevel.Warning && e.Message.Contains("import failed", StringComparison.Ordinal), logger.Entries);
    }

    /// <summary>Giving up waiting is reported as its own condition, never as an import failure.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_WaitTimesOut_FallsBackWithItsOwnMessage()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDatabaseInitializer initializer = new(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        ChangelogDocument fallback = new() { Language = "en" };

        // Never marked — the import task died without reporting. A short budget keeps the test fast;
        // the production value is ChangelogImportReadiness.DefaultWaitBudget.
        ChangelogImportReadiness readiness = new(TimeSpan.FromMilliseconds(50));

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        RecordingLogger logger = new();
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(fallback), readiness, logger);

        ChangelogDocument? document = await reader.GetDocumentAsync("en");

        Assert.AreSame(fallback, document);
        Assert.Contains(e => e.Level == LogLevel.Warning && e.Message.Contains("timed out", StringComparison.Ordinal), logger.Entries);
    }

    /// <summary>A missing Changelog_Entry table falls back to the JSON-backed service instead of throwing.</summary>
    [TestMethod]
    public async Task GetDocumentAsync_TablesMissing_FallsBackToFileService()
    {
        SqliteConnectionFactory factory = new(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new(factory);
        ChangelogDocument fallback = new() { Language = "en" };

        JoinQueryRepository<ChangelogLineRow> joinRepository = new(factory, new ChangelogWithLinesStrategy());
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(fallback), SucceededImport(), NullLogger<ChangelogReader>.Instance);

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
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), SucceededImport(), logger);

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
        ChangelogReader reader = new(joinRepository, new FakeChangelogService(null), SucceededImport(), NullLogger<ChangelogReader>.Instance);

        await Assert.ThrowsExactlyAsync<SqliteException>(() => reader.GetDocumentAsync("en"));
    }

    public TestContext TestContext { get; set; }
}
