using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Paths;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Database;

[TestClass]
public class DatabaseInitializerTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string SourcesDir = Path.Combine(RepoRoot, "data", "sources");

    private static string CuratedFile       => Path.Combine(SourcesDir, "quotinator-curated.json");
    private static string VilaboimFile      => Path.Combine(SourcesDir, "vilaboim_movie-quotes.json");
    private static string NikhilNamal17File => Path.Combine(SourcesDir, "NikhilNamal17_popular-movie-quotes.json");

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private QuotinatorDatabaseInitializer CreateInitializer(
        IReadOnlyList<SeedBatch> batches, bool useBaseline = true,
        IRuleFileOverridePathResolver? ruleFileOverridePathResolver = null, ISourceFileOverrideRegistry? sourceFileOverrideRegistry = null)
        => CreateInitializer(batches, QuotinatorMigrations.All, useBaseline, ruleFileOverridePathResolver, sourceFileOverrideRegistry);

    private QuotinatorDatabaseInitializer CreateInitializer(
        IReadOnlyList<SeedBatch> batches, IReadOnlyList<SchemaMigration> migrations, bool useBaseline,
        IRuleFileOverridePathResolver? ruleFileOverridePathResolver = null, ISourceFileOverrideRegistry? sourceFileOverrideRegistry = null)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var actionReader   = new SystemImportActionReader(factory);
        var actionWriter   = new SystemImportActionWriter(factory);
        var coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService  = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        return new QuotinatorDatabaseInitializer(factory, options, migrations, batches, importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, logger,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            ruleFileOverridePathResolver ?? NoOpRuleFileOverridePathResolver.Instance,
            sourceFileOverrideRegistry ?? NoOpSourceFileOverrideRegistry.Instance,
            useBaseline ? QuotinatorMigrations.Baseline : null);
    }

    private static SeedBatch AllFilesBatch() => new(
        [
            new SeedFile(CuratedFile,        null),
            new SeedFile(VilaboimFile,        "https://github.com/vilaboim/movie-quotes"),
            new SeedFile(NikhilNamal17File,   "https://github.com/NikhilNamal17/popular-movie-quotes")
        ],
        ManifestPolicy.HardcodedDefault,
        "bundled sources");

    /// <summary>#153: mirrors production's manifest-driven wiring — NikhilNamal17 seeded under its own
    /// Review policy with its real ruleFile, matching what ManifestSeedPlanner actually builds (unlike
    /// <see cref="AllFilesBatch"/>, which never wires a rule file at all).</summary>
    private static SeedBatch NikhilNamal17WithRuleFileBatch() => new(
        [
            new SeedFile(
                NikhilNamal17File,
                "https://github.com/NikhilNamal17/popular-movie-quotes",
                Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review),
                RuleFilePath: Path.Combine(SourcesDir, "nikhilnamal17-conflict-rules.json"),
                SourceAliasFilePath: Path.Combine(SourcesDir, "nikhilnamal17-source-aliases.json"))
        ],
        ManifestPolicy.HardcodedDefault,
        "bundled sources");

    /// <summary>#153: seeding NikhilNamal17 alone, under its real Review policy and real rule file, must
    /// produce zero Pending/Stale/Blocked actions — the same "no file left staged awaiting review"
    /// invariant CLAUDE.md's own live T2 checklist already asserts against the full bundled dataset.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NikhilNamal17WithRealRuleFile_ProducesNoUnresolvedActions()
    {
        var db = CreateInitializer([NikhilNamal17WithRuleFileBatch()]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var unresolved = (await conn.QueryAsync<(string Id, string EntityId, string Status, string? ExistingValue, string? IncomingValue)>(
            "SELECT Id, EntityId, Status, ExistingValue, IncomingValue FROM System_ImportActions WHERE Status NOT IN ('Decided', 'Applied') AND IsDeleted = 0;")).ToList();

        Assert.IsEmpty(unresolved,
            $"Every action must auto-resolve under Review with the real rule file — found: {string.Join(" | ", unresolved.Select(u => $"{u.EntityId}:{u.Status} existing={u.ExistingValue} incoming={u.IncomingValue}"))}");
    }

    /// <summary>#153: the Galadriel Custom rule (nikhilnamal17-conflict-rules.json) must correct the
    /// character field on this quote's very first (Add) encounter, not only on a later Modify.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NikhilNamal17WithRealRuleFile_GaladrielQuoteGetsCharacterOnAdd()
    {
        var db = CreateInitializer([NikhilNamal17WithRuleFileBatch()]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var character = await conn.ExecuteScalarAsync<string?>(
            "SELECT c.Name FROM Quotes q JOIN Characters c ON c.Id = q.CharacterId " +
            "WHERE q.Id = 'c124e692-04fc-7b49-af53-b6bcc0692dbe' AND q.IsDeleted = 0;");

        Assert.AreEqual("Galadriel", character);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    /// <summary>Seeding all three bundled source files produces the expected quote/source/character counts.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_SeedsExpectedCounts()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        Assert.AreEqual(799, db.QuoteCount,     "Unique quotes");
        Assert.AreEqual(482, db.SourceCount,    "Sources");
        Assert.AreEqual(7,   db.CharacterCount, "Characters");
        Assert.AreEqual(3,   db.PeopleCount,    "People (Winston Churchill, Neil Armstrong, Martin Luther King Jr. — curated)");
    }

    /// <summary>#221: cross-file duplicates between vilaboim and NikhilNamal17 show up as "modified" Quote
    /// actions in the per-file report (AllFilesBatch() uses ManifestPolicy.HardcodedDefault, i.e.
    /// NewestWins, bypassing the bundled manifest.json's own "skip" override) — none pending or blocked,
    /// since NewestWins always resolves deterministically.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var modified = db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Modified ?? 0);
        var pending  = db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Pending ?? 0);
        var blocked  = db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Blocked ?? 0);

        Assert.AreEqual(45, modified, "Cross-file duplicates, resolved as modified Quote actions");
        Assert.AreEqual(0, pending, "NewestWins always resolves deterministically — nothing pending");
        Assert.AreEqual(0, blocked, "NewestWins never blocks — no Complete rows exist yet to block against");
    }

    /// <summary>Seeding only the curated file correctly wires up the FK chain: Source → Character → Quote.</summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsFkChainCorrectly()
    {
        var batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        var db    = CreateInitializer([batch]);
        await db.InitialiseAsync();

        Assert.AreEqual(13, db.QuoteCount,     "10 movie/tv quotes (Airplane!, Holy Grail, Princess Bride, Star Wars) + 3 person quotes (Churchill, Armstrong, MLK)");
        Assert.AreEqual(7,  db.SourceCount,    "4 movie sources + 3 person speech occasions");
        Assert.AreEqual(7,  db.CharacterCount, "7 characters across the four movie sources");
        Assert.AreEqual(3,  db.PeopleCount,    "Winston Churchill, Neil Armstrong, Martin Luther King Jr.");
        Assert.AreEqual(0,  db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Modified ?? 0), "A single file has no cross-file duplicates");
    }

    /// <summary>
    /// The curated file's explicit <c>people[]</c> entries (Winston Churchill, Neil Armstrong, Martin
    /// Luther King Jr.) carry real dateOfBirth/dateOfDeath — added specifically to exercise the Person
    /// Add write path with real data, after a live T2 pass found it silently dropped both fields on a
    /// brand-new Person (see <c>SqliteImportActionServiceTests.ApplyBatchAsync_PersonAdd_WritesDateOfBirthAndDateOfDeath</c>
    /// for the isolated regression test; this is the end-to-end seeding equivalent).
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsPersonDatesFromExplicitEntries()
    {
        var batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        var db    = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var people = (await conn.QueryAsync<(string Name, string? DateOfBirth, string? DateOfDeath)>(
            "SELECT Name, DateOfBirth, DateOfDeath FROM People WHERE IsDeleted = 0;")).ToList();

        Assert.HasCount(3, people);
        Assert.IsTrue(people.Any(p => p is { Name: "Winston Churchill", DateOfBirth: "1874-11-30", DateOfDeath: "1965-01-24" }));
        Assert.IsTrue(people.Any(p => p is { Name: "Neil Armstrong", DateOfBirth: "1930-08-05", DateOfDeath: "2012-08-25" }));
        Assert.IsTrue(people.Any(p => p is { Name: "Martin Luther King Jr.", DateOfBirth: "1929-01-15", DateOfDeath: "1968-04-04" }));
    }

    /// <summary>#191: a Source discovered implicitly from a quote (never named in a sources[] section) still carries that quote's own Date once seeded — the curated file's own Airplane!/1980 entries are the fixture.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_SeedsSourceDatesFromQuotes()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var airplaneDate = await conn.ExecuteScalarAsync<string?>(
            "SELECT Date FROM Sources WHERE Title = 'Airplane!' AND Type = 'Movie' AND IsDeleted = 0;");
        Assert.AreEqual("1980", airplaneDate, "Sources.Date must be populated from the resolving quote's own Date");

        var datedSourceCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Sources WHERE Date IS NOT NULL AND IsDeleted = 0;");
        Assert.IsGreaterThan(0, datedSourceCount, "At least some seeded Sources must now carry a Date — today every one of them is null");
    }

    /// <summary>
    /// #68: seeding the curated file writes its four conversations (Airplane!, Holy Grail, Princess
    /// Bride, Empire Strikes Back) into Conversations/ConversationLines/StageDirections/SoundCues,
    /// all sharing the file's own ImportBatchId, staged through System_ImportActions like every
    /// other entity type.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsConversationsStageDirectionsAndSoundCues()
    {
        var batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        var db    = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var conversationCount    = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Conversations WHERE IsDeleted = 0;");
        var stageDirectionCount  = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StageDirections WHERE IsDeleted = 0;");
        var soundCueCount        = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SoundCues WHERE IsDeleted = 0;");
        var conversationLineCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ConversationLines WHERE IsDeleted = 0;");

        Assert.AreEqual(4, conversationCount,     "4 conversations (Airplane!, Holy Grail, Princess Bride, Empire Strikes Back)");
        Assert.AreEqual(2, stageDirectionCount,   "2 stage directions (Princess Bride, Empire Strikes Back)");
        Assert.AreEqual(1, soundCueCount,         "1 sound cue (Holy Grail)");
        Assert.AreEqual(13, conversationLineCount, "2 + 4 + 2 + 5 lines across the four conversations");

        var distinctBatchIds = await conn.QueryAsync<string>(
            "SELECT DISTINCT ImportBatchId FROM Conversations UNION SELECT DISTINCT ImportBatchId FROM StageDirections UNION SELECT DISTINCT ImportBatchId FROM SoundCues;");
        Assert.HasCount(1, distinctBatchIds.ToList(), "All conversation-related rows from one file should share one ImportBatchId");

        var actionEntityTypes = await conn.QueryAsync<string>(
            "SELECT DISTINCT EntityType FROM System_ImportActions WHERE EntityType IN ('Conversation', 'StageDirection', 'SoundCue');");
        CollectionAssert.AreEquivalent(new[] { "Conversation", "StageDirection", "SoundCue" }, actionEntityTypes.ToList(),
            "Conversation/StageDirection/SoundCue Add actions must be staged through System_ImportActions like every other entity type");
    }

    /// <summary>
    /// #68: reseeding (clear + reimport from source) reproduces the same conversation/stage-direction/
    /// sound-cue counts, not doubled — exercises the parse-plan-apply path a second time from a clean
    /// slate. Live re-import-without-clearing dedup (Add-detection by explicit id) is covered
    /// directly against <c>SqliteQuoteImportService</c> in
    /// <c>QuoteImportServiceTests.ImportAsync_SameExtendedFormatFileImportedTwice_DoesNotDuplicateConversationOrStageDirection</c>,
    /// since <see cref="QuotinatorDatabaseInitializer"/>'s own seeding is a no-op once any quote
    /// already exists (see <see cref="InitialiseAsync_CalledTwice_IsIdempotent"/>) and so cannot
    /// exercise that scenario itself.
    /// </summary>
    [TestMethod]
    public async Task ReseedAsync_CuratedFileOnly_ReproducesSameConversationCountsNotDoubled()
    {
        var batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        var db    = CreateInitializer([batch]);
        await db.InitialiseAsync();
        await db.ReseedAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        Assert.AreEqual(4, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Conversations WHERE IsDeleted = 0;"));
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StageDirections WHERE IsDeleted = 0;"));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SoundCues WHERE IsDeleted = 0;"));
        Assert.AreEqual(13, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ConversationLines WHERE IsDeleted = 0;"));
    }

    // ── #181: per-source conflict-resolution rule file ──────────────────────────

    /// <summary>
    /// End-to-end seeding proof: a second file re-introducing an already-seeded quote under Review
    /// policy, with a matching per-source rule (Keep) for the only field that differs, auto-resolves
    /// and applies immediately at startup instead of leaving a Pending action stuck in
    /// System_ImportActions — no manual decide/apply step needed.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_SecondFileReviewPolicyMatchingRule_AutoResolvesNoPendingActionLeft()
    {
        const string quoteId = "d1111111-1111-4111-8111-111111111111";
        var baselinePath = Path.Combine(_tempDir, "baseline.json");
        var conflictPath = Path.Combine(_tempDir, "conflict.json");
        var rulesPath    = Path.Combine(_tempDir, "conflict-rules.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(rulesPath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Keep"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        var batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review), RuleFilePath: rulesPath),
            ],
            ManifestPolicy.HardcodedDefault, "rule-file-test");

        var db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        Assert.AreEqual(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ImportActions WHERE Status = 'Pending';"),
            "The rule fully covers the only ambiguous field — nothing should be left Pending");
        Assert.AreEqual("Original text.", await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id;", new { id = quoteId }),
            "Keep must resolve to the existing (baseline) value");
    }

    /// <summary>
    /// #153: end-to-end proof that a registered, hash-verified override on the persistent volume is
    /// preferred over the bundled rule file the manifest actually references — the override says
    /// Replace where the bundled copy says Keep, and the applied result reflects Replace.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_RegisteredOverrideWithMatchingHash_IsPreferredOverBundledRuleFile()
    {
        const string quoteId = "d3111111-1111-4111-8111-111111111111";
        var baselinePath = Path.Combine(_tempDir, "override-baseline.json");
        var conflictPath = Path.Combine(_tempDir, "override-conflict.json");
        var bundledRulesPath = Path.Combine(_tempDir, "override-conflict-rules.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        // Bundled copy says Keep — if this were used, the applied text would stay "Original text.".
        File.WriteAllText(bundledRulesPath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Keep"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        var internalDownloadDir = Path.Combine(_tempDir, "sources", "download");
        var pathResolver = new RuleFileOverridePathResolver(internalDownloadDir, Path.Combine(_tempDir, "imports", "download"));
        var overridePath = pathResolver.Resolve(Path.GetFileName(bundledRulesPath), SeedBatchOrigin.Bundled);
        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        // Override says Replace — the applied text must come from here instead.
        var overrideContent =
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Replace"}]}]}"""
                .Replace("QUOTE_ID", quoteId);
        File.WriteAllText(overridePath, overrideContent);

        var registry = new SourceFileOverrideRegistry(new SqliteConnectionFactory(_dbPath));
        var batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review), RuleFilePath: bundledRulesPath),
            ],
            ManifestPolicy.HardcodedDefault, "override-test");

        // The registry table must exist before InitialiseAsync runs migrations — the very first
        // write in this test — so seed a bare, migration-free database via a throwaway initializer
        // first, matching how every other test in this file lets InitialiseAsync create the schema.
        await CreateInitializer([batch]).InitialiseAsync();
        await registry.RegisterAsync(Path.GetFileName(bundledRulesPath), SeedBatchOrigin.Bundled,
            EffectiveRuleFileResolver.ComputeContentHash(overrideContent), sourceBatchId: null);

        var db = CreateInitializer([batch], ruleFileOverridePathResolver: pathResolver, sourceFileOverrideRegistry: registry);
        await db.ReseedAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        Assert.AreEqual("Changed text.", await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id;", new { id = quoteId }),
            "The registered override (Replace) must win over the bundled rule file (Keep)");
    }

    /// <summary>
    /// #153: an override file physically present on disk but never registered (or registered under a
    /// stale hash) must never be silently trusted — falls back to the bundled rule file exactly as if
    /// no override existed at all.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_OverrideFileWithoutMatchingRegistration_FallsBackToBundledRuleFile()
    {
        const string quoteId = "d4111111-1111-4111-8111-111111111111";
        var baselinePath = Path.Combine(_tempDir, "unregistered-baseline.json");
        var conflictPath = Path.Combine(_tempDir, "unregistered-conflict.json");
        var bundledRulesPath = Path.Combine(_tempDir, "unregistered-conflict-rules.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(bundledRulesPath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Keep"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        var internalDownloadDir = Path.Combine(_tempDir, "sources2", "download");
        var pathResolver = new RuleFileOverridePathResolver(internalDownloadDir, Path.Combine(_tempDir, "imports2", "download"));
        var overridePath = pathResolver.Resolve(Path.GetFileName(bundledRulesPath), SeedBatchOrigin.Bundled);
        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        // An override file exists on disk, but is never registered below.
        File.WriteAllText(overridePath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Replace"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        var registry = new SourceFileOverrideRegistry(new SqliteConnectionFactory(_dbPath));
        var batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review), RuleFilePath: bundledRulesPath),
            ],
            ManifestPolicy.HardcodedDefault, "unregistered-override-test");

        var db = CreateInitializer([batch], ruleFileOverridePathResolver: pathResolver, sourceFileOverrideRegistry: registry);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        Assert.AreEqual("Original text.", await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id;", new { id = quoteId }),
            "An unregistered override file must never be trusted — the bundled rule file (Keep) must be used instead");
    }

    /// <summary>Regression guard: the same scenario with no rule file at all must behave exactly as before #181 — Pending, nothing overwritten.</summary>
    [TestMethod]
    public async Task InitialiseAsync_SecondFileReviewPolicyNoRuleFile_StagesPendingAsBefore()
    {
        const string quoteId = "d2111111-1111-4111-8111-111111111111";
        var baselinePath = Path.Combine(_tempDir, "baseline2.json");
        var conflictPath = Path.Combine(_tempDir, "conflict2.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));

        var batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review)),
            ],
            ManifestPolicy.HardcodedDefault, "no-rule-file-test");

        var db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ImportActions WHERE Status = 'Pending';"),
            "No rule file was referenced — behaviour must be unchanged from before #181");
    }

    /// <summary>
    /// End-to-end proof of #181's source-title alias mechanism: a second file's quote references a
    /// misspelled Source title that an alias file maps to the first file's already-established
    /// canonical Source — must resolve to that one Source, never create a duplicate.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_SecondFileMisspelledSourceWithMatchingAlias_ResolvesToExistingSourceNoDuplicate()
    {
        var canonicalPath = Path.Combine(_tempDir, "canonical.json");
        var misspeltPath  = Path.Combine(_tempDir, "misspelt.json");
        var aliasPath     = Path.Combine(_tempDir, "source-aliases.json");

        File.WriteAllText(canonicalPath,
            """[{"id":"e1111111-1111-4111-8111-111111111111","quote":"First quote.","originalLanguage":"en","source":"The Avengers","date":"2012","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]""");
        File.WriteAllText(misspeltPath,
            """[{"id":"e2222222-2222-4222-8222-222222222222","quote":"Second quote.","originalLanguage":"en","source":"Marvel's The Avengers","date":"2012","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]""");
        File.WriteAllText(aliasPath,
            """{"aliases":[{"title":"Marvel's The Avengers","type":"movie","canonicalTitle":"The Avengers","canonicalType":"movie"}]}""");

        var batch = new SeedBatch(
            [
                new SeedFile(canonicalPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(misspeltPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), SourceAliasFilePath: aliasPath),
            ],
            ManifestPolicy.HardcodedDefault, "source-alias-test");

        var db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sources WHERE Title = 'The Avengers';"),
            "The alias must resolve the misspelled title to the already-existing canonical Source — no duplicate");
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes;"), "Both quotes must still be seeded");
    }

    /// <summary>No source files configured — database is created but stays empty.</summary>
    [TestMethod]
    public async Task InitialiseAsync_EmptyBatches_DatabaseIsEmpty()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        Assert.AreEqual(0, db.QuoteCount);
    }

    /// <summary>Calling InitialiseAsync a second time on an already-seeded database is a no-op.</summary>
    [TestMethod]
    public async Task InitialiseAsync_CalledTwice_IsIdempotent()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var countAfterFirst = db.QuoteCount;
        await db.InitialiseAsync();

        Assert.AreEqual(countAfterFirst, db.QuoteCount);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>ResetAsync on an already-seeded database drops and recreates all tables and reseeds correctly.</summary>
    [TestMethod]
    public async Task ResetAsync_AfterInitialise_RebuildsSchemaAndReseeds()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var countAfterInit = db.QuoteCount;

        await db.ResetAsync();

        Assert.AreEqual(countAfterInit, db.QuoteCount, "Quote count after reset should match initial seed");
    }

    // ── System table preservation (#141) ────────────────────────────────────────

    private const string MarkerValue = "manual-test-marker";

    /// <summary>A full Reset must not destroy the audit trail — System_AuditEntries is excluded from the table wipe.</summary>
    [TestMethod]
    public async Task ResetAsync_AfterInitialise_PreservesExistingAuditEntries()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertAuditMarkerAsync();

        await db.ResetAsync();

        Assert.AreEqual(1, await CountAuditMarkerRowsAsync(), "Full Reset must preserve existing System_AuditEntries rows");
    }

    /// <summary>
    /// Quotinator.Data's own migrations concern only System_-prefixed tables (System_AuditEntries),
    /// which a Reset never drops — so System_SchemaVersion must never be wiped or replayed by a
    /// Reset, regardless of preserveSchemaVersion. This is stronger than "preserved": it's simply
    /// never touched.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_AnyParameter_NeverTouchesDataSchemaVersion()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();
        await InsertSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: false);
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(),
            "System_SchemaVersion must survive a default Reset — it was never wiped in the first place");

        await db.ResetAsync(preserveSchemaVersion: true);
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(),
            "System_SchemaVersion must survive a preserveSchemaVersion:true Reset too — same reason");
    }

    /// <summary>With the default parameter, Reset still clears and replays System_ConsumerSchemaVersion — unchanged historical behaviour for the consumer's own migrations.</summary>
    [TestMethod]
    public async Task ResetAsync_DefaultParameter_StillReplaysConsumerSchemaVersion()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertConsumerSchemaVersionMarkerAsync();

        await db.ResetAsync();

        Assert.AreEqual(0, await CountConsumerSchemaVersionMarkerRowsAsync(),
            "Default Reset should clear and replay System_ConsumerSchemaVersion, removing the pre-existing marker row");
    }

    /// <summary>With preserveSchemaVersion:true, Reset leaves existing System_ConsumerSchemaVersion rows untouched.</summary>
    [TestMethod]
    public async Task ResetAsync_PreserveSchemaVersionTrue_KeepsExistingConsumerVersionRows()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertConsumerSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: true);

        Assert.AreEqual(1, await CountConsumerSchemaVersionMarkerRowsAsync(),
            "preserveSchemaVersion:true should leave existing System_ConsumerSchemaVersion rows untouched");
    }

    /// <summary>Reseed (not Reset) has always left System_AuditEntries and System_SchemaVersion alone — this makes that behaviour explicit.</summary>
    [TestMethod]
    public async Task ReseedAsync_AfterInitialise_LeavesAuditEntriesAndSchemaVersionUntouched()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertAuditMarkerAsync();
        await InsertSchemaVersionMarkerAsync();

        await db.ReseedAsync();

        Assert.AreEqual(1, await CountAuditMarkerRowsAsync(),        "Reseed must not touch System_AuditEntries");
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(), "Reseed must not touch System_SchemaVersion");
    }

    private async Task InsertAuditMarkerAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO System_AuditEntries (Id, TableName, RecordId, Operation, Agent, PerformedAt, DateCreated) " +
            "VALUES (lower(hex(randomblob(16))), 'Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
            new { marker = MarkerValue });
    }

    private async Task<int> CountAuditMarkerRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE Agent = @marker;", new { marker = MarkerValue });
    }

    private async Task InsertSchemaVersionMarkerAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
    }

    private async Task<int> CountSchemaVersionMarkerRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_SchemaVersion WHERE AppliedAt = @marker;", new { marker = MarkerValue });
    }

    private async Task InsertConsumerSchemaVersionMarkerAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
    }

    private async Task<int> CountConsumerSchemaVersionMarkerRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_ConsumerSchemaVersion WHERE AppliedAt = @marker;", new { marker = MarkerValue });
    }

    // ── System-prefix naming convention (#141 amendment) ───────────────────────

    /// <summary>
    /// GetUserTables excludes any table whose name literally starts with "System_", proving
    /// Quotinator.Data needs no knowledge of specific system table names — a consuming project
    /// can define its own protected table (here, System_FooBar) with zero changes to Sql.cs.
    /// </summary>
    [TestMethod]
    public async Task GetUserTables_SystemPrefixedTable_IsExcluded()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE System_FooBar (Id INTEGER);");
        await conn.ExecuteAsync("CREATE TABLE FooBar (Id INTEGER);");

        var tables = (await conn.QueryAsync<string>(Sql.Schema.GetUserTables)).ToList();

        Assert.IsFalse(tables.Contains("System_FooBar"), "System_-prefixed tables must be excluded");
        Assert.IsTrue(tables.Contains("FooBar"), "Non-prefixed tables must still be included");
    }

    /// <summary>
    /// A table that merely starts with "System" without the underscore (e.g. SystemInventory) is
    /// NOT treated as protected — proves the ESCAPE clause in GetUserTables is doing real work,
    /// since SQL LIKE treats '_' as a single-character wildcard and an unescaped 'System_%' would
    /// wrongly match this table too.
    /// </summary>
    [TestMethod]
    public async Task GetUserTables_SystemPrefixWithoutUnderscore_IsNotExcluded()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE SystemInventory (Id INTEGER);");

        var tables = (await conn.QueryAsync<string>(Sql.Schema.GetUserTables)).ToList();

        Assert.IsTrue(tables.Contains("SystemInventory"),
            "A table starting with 'System' but no underscore must NOT be treated as protected");
    }

    /// <summary>A fresh database creates System_SchemaVersion directly — it is never created under the legacy name and then renamed.</summary>
    [TestMethod]
    public async Task InitialiseAsync_FreshDatabase_CreatesSystemSchemaVersionDirectly()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var legacyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';");
        var systemCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'System_SchemaVersion';");

        Assert.AreEqual(0, legacyCount, "A fresh database must never contain a table literally named SchemaVersion");
        Assert.AreEqual(1, systemCount, "A fresh database must create System_SchemaVersion directly");
    }

    /// <summary>
    /// Builds a fully up-to-date database, then downgrades it back to the pre-#141 table names
    /// (SchemaVersion, AuditEntries with the original IX_AuditEntries_* index names) and rolls
    /// Data's own version counter back to v1 (create-only, rename not yet applied) — simulating a
    /// real database that predates the #141 amendment, without hand-rolling the full legacy schema.
    /// </summary>
    private async Task DowngradeToLegacyNamesAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM System_SchemaVersion;");
        await conn.ExecuteAsync(
            "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
        await conn.ExecuteAsync("ALTER TABLE System_SchemaVersion RENAME TO SchemaVersion;");

        // Rebuild AuditEntries under its true migration-1 legacy shape (auto-increment long Id, no
        // RecordBase columns) rather than a bare rename — a bare rename would carry over migration
        // 5's RecordBase columns (added after this test's InitialiseAsync() call already ran the
        // full migration chain), which didn't exist in a genuinely pre-migration-2 database.
        await conn.ExecuteAsync("""
            CREATE TABLE AuditEntries (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName   TEXT    NOT NULL,
                RecordId    TEXT,
                Operation   TEXT    NOT NULL,
                Agent       TEXT,
                PerformedAt TEXT    NOT NULL
            );
            """);
        await conn.ExecuteAsync(
            "INSERT INTO AuditEntries (TableName, RecordId, Operation, Agent, PerformedAt) " +
            "SELECT TableName, RecordId, Operation, Agent, PerformedAt FROM System_AuditEntries;");
        await conn.ExecuteAsync("DROP TABLE System_AuditEntries;");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_TableName_RecordId ON AuditEntries (TableName, RecordId);");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_PerformedAt ON AuditEntries (PerformedAt);");
    }

    /// <summary>
    /// A database with a pre-existing legacy SchemaVersion table (simulating an upgrade from
    /// before the #141 amendment) gets it renamed to System_SchemaVersion, with the existing
    /// version-history row preserved rather than wiped.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_LegacySchemaVersionTable_IsRenamedWithRowsPreserved()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();
        await DowngradeToLegacyNamesAsync();

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var legacyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';");
        var preservedRow = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_SchemaVersion WHERE Version = 1 AND AppliedAt = @marker;", new { marker = MarkerValue });

        Assert.AreEqual(0, legacyCount, "The legacy SchemaVersion table must no longer exist after the rename");
        Assert.AreEqual(1, preservedRow, "The pre-existing version-history row must survive the rename, not be wiped");
        Assert.AreEqual(13, db2.DataSchemaVersion, "Data migrations 2-13 (the rename, System_ImportConflicts, System_ChangeLog, both RecordBase retrofits, ExistingBatchId, System_ImportActions, the Status CHECK constraint, Blocked/MarkCompletenessAs, OriginalDecision, the Stale status, and System_SourceFileOverrides) should all have replayed after the legacy rename");
    }

    /// <summary>Data migration 2 renames AuditEntries to System_AuditEntries and preserves existing rows and both indexes.</summary>
    [TestMethod]
    public async Task InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();
        await DowngradeToLegacyNamesAsync();

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO AuditEntries (TableName, RecordId, Operation, Agent, PerformedAt) " +
                "VALUES ('Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00');",
                new { marker = MarkerValue });
        }

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync();
        var legacyCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AuditEntries';");
        var preservedRow = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE Agent = @marker;", new { marker = MarkerValue });
        var indexNames = (await verifyConn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'System_AuditEntries';")).ToList();

        Assert.AreEqual(0, legacyCount, "The legacy AuditEntries table must no longer exist after Data migration 2");
        Assert.AreEqual(1, preservedRow, "The pre-existing audit row must survive the rename");
        Assert.IsTrue(indexNames.Contains("IX_System_AuditEntries_TableName_RecordId"), "TableName/RecordId index must exist under the new name");
        Assert.IsTrue(indexNames.Contains("IX_System_AuditEntries_PerformedAt"), "PerformedAt index must exist under the new name");
    }

    // ── Regression ────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression test for issue #106: if the App schema version is rolled back to v2 while the
    /// underlying tables already have v3 columns (ImportBatchId), the recorded version no longer
    /// matches the actual schema — a genuine anomaly, not something InitialiseAsync should ever
    /// silently guess its way through. It must fail loudly (no structural check, no message-matching
    /// recovery), leave the database exactly as it was before the attempt (backup restored), and
    /// require an explicit Reset to resolve. Uses the forced-incremental path so App migrations are
    /// recorded one row per version (the baseline path records a single row, leaving nothing to roll
    /// back to "v2" from).
    /// </summary>
    /// <remarks>
    /// Deletes every version row from 3 upward, not just "the last two" — <c>GetConsumerCurrentVersion</c>
    /// computes <c>MAX(Version)</c>, not row count, so leaving migration 5's row in place (e.g. deleting
    /// only 3 and 4) would leave the computed version at 5 and InitialiseAsync would see nothing pending
    /// to replay, defeating the whole scenario. Deleting 3 upward drops MAX back to 2, reproducing the
    /// original #106 scenario regardless of how many migrations now exist above it.
    /// </remarks>
    [TestMethod]
    public async Task InitialiseAsync_PartialMigrationState_FailsSafelyAndRequiresExplicitReset()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseForTestingAsync(forceIncremental: true);

        var countAfterInit = db.QuoteCount;

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DELETE FROM System_ConsumerSchemaVersion WHERE Version >= 3;");
        }

        var db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        using (var verifyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await verifyConn.OpenAsync();
            var quoteCountAfterFailedAttempt = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes;");
            Assert.AreEqual(countAfterInit, quoteCountAfterFailedAttempt,
                "Database must be restored to its pre-attempt state after a failed migration, not left partially migrated");
        }

        var db3 = CreateInitializer([AllFilesBatch()]);
        await db3.ResetAsync();
        Assert.AreEqual(11, db3.SchemaVersion, "An explicit Reset must fully resolve the version/schema mismatch");
    }

    // ── #143 — migration ownership split + baseline schema ─────────────────────

    private (QuotinatorDatabaseInitializer Db, string DbPath) CreateForcedIncrementalInitializer()
    {
        var dbPath        = Path.Combine(_tempDir, $"test_incremental_{Guid.NewGuid():N}.db");
        var factory       = new SqliteConnectionFactory(dbPath);
        var options       = new DatabaseOptions { DbPath = dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new SystemImportActionReader(factory);
        var actionWriter  = new SystemImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        var db = new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance,
            QuotinatorMigrations.Baseline);
        return (db, dbPath);
    }

    private static async Task<List<string>> DumpTableSchemaAsync(SqliteConnection conn, string table)
    {
        var lines = new List<string>();

        var columns = await conn.QueryAsync<(int cid, string name, string type, int notnull, string? dflt_value, int pk)>(
            $"SELECT cid, name, type, [notnull], dflt_value, pk FROM pragma_table_info('{table}');");
        foreach (var c in columns.OrderBy(c => c.cid))
            lines.Add($"COL {c.cid} {c.name} {c.type} notnull={c.notnull} default={c.dflt_value} pk={c.pk}");

        var indexes = await conn.QueryAsync<(string name, int unique)>(
            $"SELECT name, [unique] FROM pragma_index_list('{table}');");
        foreach (var idx in indexes.OrderBy(i => i.name))
        {
            var idxCols = await conn.QueryAsync<(int seqno, string? name)>(
                $"SELECT seqno, name FROM pragma_index_info('{idx.name}');");
            var colList = string.Join(",", idxCols.OrderBy(c => c.seqno).Select(c => c.name));
            lines.Add($"IDX {idx.name} unique={idx.unique} cols=({colList})");
        }

        return lines;
    }

    private static readonly string[] ConsumerDomainTables =
        ["ImportBatches", "Sources", "SourceTranslations", "Characters", "CharacterTranslations",
         "People", "Quotes", "QuoteTranslations", "QuoteGenres",
         "Conversations", "ConversationLines", "StageDirections", "StageDirectionTranslations",
         "SoundCues", "SoundCueTranslations",
         "Universe", "Series", "CharacterSources"];

    /// <summary>
    /// QuotinatorMigrations.Baseline must produce the exact same schema, table by table, as
    /// replaying QuotinatorMigrations.All incrementally. Comparison uses PRAGMA table_info/
    /// index_list/index_info rather than raw sqlite_master text, since hand-formatted baseline SQL
    /// and migration-assembled SQL (e.g. Sources' ImportBatchId appended via ALTER TABLE) would
    /// differ textually even when semantically identical.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema()
    {
        var dbA = CreateInitializer([]);
        await dbA.InitialiseAsync();

        var (dbB, dbPathB) = CreateForcedIncrementalInitializer();
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={_dbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={dbPathB}");
        await connB.OpenAsync();

        foreach (var table in ConsumerDomainTables)
        {
            var schemaA = await DumpTableSchemaAsync(connA, table);
            var schemaB = await DumpTableSchemaAsync(connB, table);
            CollectionAssert.AreEqual(schemaB, schemaA,
                $"Table '{table}' schema differs between the baseline and incremental paths — " +
                "update QuotinatorMigrations.Baseline to match QuotinatorMigrations.All's final result.");
        }
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text, so a baseline that
    /// silently dropped 'UserSeed' from ImportBatches.Type's constraint (or introduced a typo)
    /// would pass the structural schema comparison above undetected. This behavioural round-trip
    /// closes that gap for all three CHECK-constrained columns.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues()
    {
        var dbA = CreateInitializer([]);
        await dbA.InitialiseAsync();

        var (dbB, dbPathB) = CreateForcedIncrementalInitializer();
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={_dbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={dbPathB}");
        await connB.OpenAsync();

        foreach (var conn in new[] { connA, connB })
        {
            // QuoteGenres.QuoteId is a FK to Quotes(Id) — irrelevant to the CHECK constraint being
            // tested here, so disable enforcement rather than seed a matching Quotes row.
            await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'check-test.json', 'UserSeed', @now, 0, @now, 0);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'bad.json', 'NotARealType', @now, 0, @now, 0);",
                new { id = Guid.NewGuid().ToString(), now }));

            await conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, Status) " +
                "VALUES (@id, 'check-test-staged.json', 'Import', @now, 0, @now, 0, 'Staged');",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, Status) " +
                "VALUES (@id, 'bad-status.json', 'Import', @now, 0, @now, 0, 'NotARealStatus');",
                new { id = Guid.NewGuid().ToString(), now }));

            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, DateCreated, IsDeleted) VALUES (@id, 'CheckTest', 'Person', @now, 0);",
                new { id = Guid.NewGuid().ToString(), now });

            await conn.ExecuteAsync(
                "INSERT INTO QuoteGenres (Id, QuoteId, Genre, DateCreated, IsDeleted) " +
                "VALUES (@id, @quoteId, 'SciFi', @now, 0);",
                new { id = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now });

            // ConversationLines carries two independent CHECK constraints (#67): a simple
            // LineType-membership CHECK (ADR 008) and a separate cross-field CHECK enforcing that
            // exactly the FK matching LineType is populated. Both are exercised here.
            var quoteLineId = Guid.NewGuid().ToString();
            await conn.ExecuteAsync(
                "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
                new { id = quoteLineId, conversationId = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 2, 'NotARealLineType', @quoteId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), conversationId = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, StageDirectionId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 3, 'Quote', @stageDirectionId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), conversationId = Guid.NewGuid().ToString(), stageDirectionId = Guid.NewGuid().ToString(), now }));
        }
    }

    // ── #67 — Conversations schema ──────────────────────────────────────────────

    private static readonly string[] ConversationTablesWithRecordBase =
        ["Conversations", "ConversationLines", "StageDirections", "StageDirectionTranslations",
         "SoundCues", "SoundCueTranslations"];

    /// <summary>Every table added by #67 carries RecordBase's four audit columns — ADR 002 applies without exception, including the line/junction table and both translation tables.</summary>
    [TestMethod]
    public async Task ConversationTables_AllHaveRecordBaseColumns()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        foreach (var table in ConversationTablesWithRecordBase)
        {
            var columns = (await conn.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{table}');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var recordBaseColumn in new[] { "Id", "DateCreated", "DateModified", "DateDeleted", "IsDeleted" })
                Assert.IsTrue(columns.Contains(recordBaseColumn), $"{table} is missing RecordBase column {recordBaseColumn}");
        }
    }

    /// <summary><c>UNIQUE (ConversationId, Order)</c> rejects a second line at an already-used position.</summary>
    [TestMethod]
    public async Task ConversationLines_UniqueConstraint_RejectsDuplicateOrder()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        var now            = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var conversationId = Guid.NewGuid().ToString();

        await conn.ExecuteAsync(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
            new { id = Guid.NewGuid().ToString(), conversationId, quoteId = Guid.NewGuid().ToString(), now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
            new { id = Guid.NewGuid().ToString(), conversationId, quoteId = Guid.NewGuid().ToString(), now }));
    }

    /// <summary><c>UNIQUE (StageDirectionId, Language)</c> and <c>UNIQUE (SoundCueId, Language)</c> reject a second translation in the same language.</summary>
    [TestMethod]
    public async Task TranslationTables_UniqueConstraint_RejectsDuplicateLanguage()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        var now              = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var stageDirectionId = Guid.NewGuid().ToString();
        var soundCueId       = Guid.NewGuid().ToString();

        await conn.ExecuteAsync(
            "INSERT INTO StageDirectionTranslations (Id, StageDirectionId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @stageDirectionId, 'nl', 'Tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), stageDirectionId, now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO StageDirectionTranslations (Id, StageDirectionId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @stageDirectionId, 'nl', 'Andere tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), stageDirectionId, now }));

        await conn.ExecuteAsync(
            "INSERT INTO SoundCueTranslations (Id, SoundCueId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @soundCueId, 'nl', 'Tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), soundCueId, now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO SoundCueTranslations (Id, SoundCueId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @soundCueId, 'nl', 'Andere tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), soundCueId, now }));
    }

    /// <summary><see cref="ConversationLineType"/> round-trips through Dapper as a real enum, not an int — the <see cref="Quotinator.Data.Helpers.SafeEnumHandler{TEnum}"/> pattern already used for <see cref="Quotinator.Data.Entities.ImportBatchType"/>/<see cref="Quotinator.Data.Entities.ImportBatchStatus"/>.</summary>
    [TestMethod]
    public async Task ConversationLineType_RoundTripsThroughDapper()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        var lineId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, StageDirectionId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'StageDirection', @stageDirectionId, @now, 0);",
            new
            {
                id             = lineId.ToString(),
                conversationId = Guid.NewGuid().ToString(),
                stageDirectionId = Guid.NewGuid().ToString(),
                now            = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });

        var line = await conn.QuerySingleAsync<ConversationLineEntity>(
            "SELECT * FROM ConversationLines WHERE Id = @id;", new { id = lineId.ToString() });

        Assert.AreEqual(ConversationLineType.StageDirection, line.LineType.Parsed);
    }

    /// <summary>A fresh (zero-table) database takes the baseline path — both version tables end up with exactly one row each, at the final version.</summary>
    [TestMethod]
    public async Task InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var dataRows     = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_SchemaVersion;");
        var consumerRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ConsumerSchemaVersion;");

        Assert.AreEqual(1, dataRows,     "Baseline path should insert exactly one row into System_SchemaVersion");
        Assert.AreEqual(1, consumerRows, "Baseline path should insert exactly one row into System_ConsumerSchemaVersion");
        Assert.AreEqual(13, db.DataSchemaVersion);
        Assert.AreEqual(11, db.SchemaVersion);
    }

    /// <summary>
    /// An existing database with only App migrations pending still replays incrementally — the
    /// baseline path and the two migration phases never cross.
    /// </summary>
    /// <remarks>
    /// Builds the initial database with only migrations 1-3 actually applied (rather than applying
    /// all migrations and then deleting version rows) — migration 4 rebuilds the ImportBatches table
    /// from scratch, which would silently discard migration 5/6's ADD COLUMN effects if they were
    /// physically present, masking a genuine version/schema mismatch instead of exercising a real
    /// version-3 replay. Migrations 6+ (e.g. #55/#165's CompletenessStatus/NoValueKnown) ALTER tables that are
    /// never rebuilt, so replaying them a second time on top of already-applied columns would throw
    /// "duplicate column name" — a real bug in the old delete-then-replay technique, not a bug in
    /// the migrations themselves.
    /// </remarks>
    [TestMethod]
    public async Task InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally()
    {
        var partialMigrations = QuotinatorMigrations.All.Take(3).ToList();
        var db = CreateInitializer([], partialMigrations, useBaseline: false);
        await db.InitialiseForTestingAsync(forceIncremental: true);

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        Assert.AreEqual(11, db2.SchemaVersion,      "All eight remaining App migrations (4, 5, 6, 7, 8, 9, 10, and 11) should have replayed");
        Assert.AreEqual(13, db2.DataSchemaVersion, "Data's own migrations were already fully applied and must not replay");
    }

    /// <summary>
    /// A database created before the #143 migration-ownership split has a single System_SchemaVersion
    /// table holding the old combined history (one row per migration, spanning both Data's and the
    /// consumer's migrations together — 13 rows for the schema this test targets: 7 Data + 6 consumer),
    /// with no System_ConsumerSchemaVersion table at all yet. This recorded state doesn't match the
    /// actual on-disk schema (which already has the consumer's columns), so ordinary startup must fail
    /// loudly — no structural check, no message-matching recovery — leaving the database exactly as
    /// it was before the attempt (backup restored). An explicit Reset is the only sanctioned way to
    /// resolve it.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_PreSplitCombinedCounterDatabase_FailsSafelyAndRequiresExplicitReset()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();
        var quoteCountBefore = db.QuoteCount;

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DROP TABLE System_ConsumerSchemaVersion;");
            await conn.ExecuteAsync("DELETE FROM System_SchemaVersion;");
            for (var v = 1; v <= 13; v++)
                await conn.ExecuteAsync(
                    "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (@v, @at);",
                    new { v, at = $"2026-01-01T00:00:{v:D2}Z" });
        }

        var db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        using (var verifyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await verifyConn.OpenAsync();
            var quoteCountAfterFailedAttempt = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes;");
            Assert.AreEqual(quoteCountBefore, quoteCountAfterFailedAttempt,
                "Database must be restored to its pre-attempt state after the failed startup, not left partially migrated");
        }

        var db3 = CreateInitializer([AllFilesBatch()]);
        await db3.ResetAsync();
        Assert.AreEqual(11, db3.SchemaVersion, "An explicit Reset must fully resolve the mismatch");
    }

    // ── #179 — Series/Universe schema, Character↔Source many-to-many ───────────

    /// <summary>Migration009 adds Universe and Series, both insertable/readable, with Series.UniverseId nullable.</summary>
    [TestMethod]
    public async Task Migration_SeriesUniverseSchema_AddsUniverseAndSeriesTables()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var universeId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO Universe (Id, Name, DateCreated, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@id, 'Middle Earth', @now, 0, 'Incomplete', '[]');",
            new { id = universeId, now });

        var seriesId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO Series (Id, Name, UniverseId, DateCreated, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@id, 'The Lord of the Rings', @universeId, @now, 0, 'Incomplete', '[]');",
            new { id = seriesId, universeId, now });

        var standaloneSeriesId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO Series (Id, Name, UniverseId, DateCreated, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@id, 'Some Standalone Series', NULL, @now, 0, 'Incomplete', '[]');",
            new { id = standaloneSeriesId, now });

        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Universe WHERE Id = @id;", new { id = universeId }));
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Series;"));
        Assert.AreEqual(universeId, await conn.ExecuteScalarAsync<string>("SELECT UniverseId FROM Series WHERE Id = @id;", new { id = seriesId }));
    }

    /// <summary>
    /// Migration009 reshapes existing Character rows 1:1 into CharacterSources — zero merging. Applies
    /// migrations 1-8 first, inserts a Source/Character pair using the old SourceId-column shape, then
    /// completes the remaining migrations and confirms exactly one CharacterSources row resulted.
    /// </summary>
    [TestMethod]
    public async Task Migration_SeriesUniverseSchema_PopulatesCharacterSources1to1FromExistingSourceId()
    {
        var partialMigrations = QuotinatorMigrations.All.Take(8).ToList();
        var db1 = CreateInitializer([], partialMigrations, useBaseline: false);
        await db1.InitialiseForTestingAsync(forceIncremental: true);

        var sourceId    = Guid.NewGuid().ToString();
        var characterId = Guid.NewGuid().ToString();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, DateCreated, IsDeleted) VALUES (@id, 'Old Shape Source', 'Movie', @now, 0);",
                new { id = sourceId, now });

            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, SourceId, Name, DateCreated, IsDeleted) VALUES (@id, @sourceId, 'Gandalf', @now, 0);",
                new { id = characterId, sourceId, now });
        }

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync();

        var linkCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CharacterSources WHERE CharacterId = @characterId AND SourceId = @sourceId;",
            new { characterId, sourceId });
        Assert.AreEqual(1, linkCount, "Exactly one CharacterSources row should be created from the pre-existing Characters.SourceId value");

        var characterCount = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Characters WHERE Id = @characterId;", new { characterId });
        Assert.AreEqual(1, characterCount, "The migration must not merge or delete the pre-existing Character row — zero merging by design");
    }

    /// <summary>Migration009 drops Characters.SourceId and its old UNIQUE(SourceId, Name) constraint.</summary>
    [TestMethod]
    public async Task Migration_SeriesUniverseSchema_DropsCharactersSourceIdColumn()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var columns = (await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('Characters');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsFalse(columns.Contains("SourceId"), "Characters.SourceId must be dropped by Migration009");

        var indexes = await conn.QueryAsync<string>("SELECT name FROM pragma_index_list('Characters') WHERE [unique] = 1;");
        foreach (var idx in indexes)
        {
            var idxCols = (await conn.QueryAsync<string>($"SELECT name FROM pragma_index_info('{idx}');")).ToList();
            Assert.IsFalse(idxCols.Contains("SourceId", StringComparer.OrdinalIgnoreCase),
                $"Index '{idx}' still references SourceId — the old UNIQUE(SourceId, Name) constraint must be gone");
        }
    }

    // ── #174: Migration011_CharacterGlobalIdentity (ADR 013) ────────────────────

    /// <summary>
    /// Seeds a pre-#174 database at App migration v10 (Migration009's CharacterSources join landed,
    /// Migration011's merge has not) with two Sources and two same-named Characters, each linked to
    /// exactly one Source (the shape ADR 011 Decision 5 guarantees at this point in migration
    /// history), then completes migration through v11 and returns the survivor lookup for assertions.
    /// </summary>
    private async Task<(string source1Id, string source2Id, string character1Id, string character2Id)> SeedPreMergeCharactersAsync(
        string name = "Gandalf", string? name2 = null, string type1 = "Movie", string type2 = "Movie",
        string? seriesId1 = null, string? seriesId2 = null,
        string completeness1 = "Incomplete", string completeness2 = "Incomplete",
        string dateCreated1 = "2026-01-01 00:00:00", string dateCreated2 = "2026-01-02 00:00:00")
    {
        name2 ??= name;
        var partialMigrations = QuotinatorMigrations.All.Take(10).ToList();
        var db1 = CreateInitializer([], partialMigrations, useBaseline: false);
        await db1.InitialiseForTestingAsync(forceIncremental: true);

        var source1Id    = Guid.NewGuid().ToString();
        var source2Id    = Guid.NewGuid().ToString();
        var character1Id = Guid.NewGuid().ToString();
        var character2Id = Guid.NewGuid().ToString();

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();

            // Sources.SeriesId is a real FK to Series(Id) — a Series row must exist first.
            foreach (var seriesId in new[] { seriesId1, seriesId2 }.Where(s => s is not null).Distinct())
                await conn.ExecuteAsync(
                    "INSERT OR IGNORE INTO Series (Id, Name, DateCreated, IsDeleted) VALUES (@id, @name, @now, 0);",
                    new { id = seriesId, name = $"Series {seriesId}", now = dateCreated1 });

            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, SeriesId, DateCreated, IsDeleted) VALUES (@id, @title, @type, @seriesId, @now, 0);",
                new { id = source1Id, title = "Source One", type = type1, seriesId = seriesId1, now = dateCreated1 });
            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, SeriesId, DateCreated, IsDeleted) VALUES (@id, @title, @type, @seriesId, @now, 0);",
                new { id = source2Id, title = "Source Two", type = type2, seriesId = seriesId2, now = dateCreated2 });

            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, Name, CompletenessStatus, DateCreated, IsDeleted) VALUES (@id, @name, @completeness, @now, 0);",
                new { id = character1Id, name, completeness = completeness1, now = dateCreated1 });
            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, Name, CompletenessStatus, DateCreated, IsDeleted) VALUES (@id, @name, @completeness, @now, 0);",
                new { id = character2Id, name = name2, completeness = completeness2, now = dateCreated2 });

            await conn.ExecuteAsync(
                "INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted) VALUES (@id, @characterId, @sourceId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), characterId = character1Id, sourceId = source1Id, now = dateCreated1 });
            await conn.ExecuteAsync(
                "INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted) VALUES (@id, @characterId, @sourceId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), characterId = character2Id, sourceId = source2Id, now = dateCreated2 });
        }

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        return (source1Id, source2Id, character1Id, character2Id);
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_ConsolidatesSameNameRowsWithinKnownSeries()
    {
        var seriesId = Guid.NewGuid().ToString();
        var (source1Id, source2Id, character1Id, character2Id) =
            await SeedPreMergeCharactersAsync(seriesId1: seriesId, seriesId2: seriesId);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE Name = 'Gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(1, survivorCount, "Two same-named Characters whose Sources share a Series must consolidate into one row");

        var survivorId = await conn.ExecuteScalarAsync<string>(
            "SELECT Id FROM Characters WHERE Name = 'Gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(character1Id, survivorId, "The earlier-DateCreated row must survive");

        var linkedSourceCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CharacterSources WHERE CharacterId = @id AND IsDeleted = 0;", new { id = survivorId });
        Assert.AreEqual(2, linkedSourceCount, "The survivor must carry links to both original Sources");

        var mergedAwayDeleted = await conn.ExecuteScalarAsync<int>(
            "SELECT IsDeleted FROM Characters WHERE Id = @id;", new { id = character2Id });
        Assert.AreEqual(1, mergedAwayDeleted, "The merged-away row must be soft-deleted, never hard-deleted");
    }

    /// <summary>
    /// Character storage always preserves the exact casing a Name was originally written with, but the
    /// merge-candidate comparison itself is case-insensitive (confirmed directly by the developer
    /// during ADR 013's authoring — corrects an initial draft that wrongly extended Sources.Title's
    /// case-sensitive precedent to Character).
    /// </summary>
    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_MergesDespiteDifferingNameCasing()
    {
        var seriesId = Guid.NewGuid().ToString();
        var (_, _, character1Id, character2Id) = await SeedPreMergeCharactersAsync(
            name: "Gandalf", name2: "GANDALF", seriesId1: seriesId, seriesId2: seriesId);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE LOWER(Name) = 'gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(1, survivorCount, "Differing casing of the same Name must still merge — only storage preserves original casing, not the comparison");

        var survivorName = await conn.ExecuteScalarAsync<string>(
            "SELECT Name FROM Characters WHERE Id = @id;", new { id = character1Id });
        Assert.AreEqual("Gandalf", survivorName, "The surviving row's own original casing must be preserved, never rewritten to match the merged-away row's casing");

        var mergedAwayDeleted = await conn.ExecuteScalarAsync<int>(
            "SELECT IsDeleted FROM Characters WHERE Id = @id;", new { id = character2Id });
        Assert.AreEqual(1, mergedAwayDeleted);
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_NeverMergesAcrossDifferingSourceType()
    {
        var seriesId = Guid.NewGuid().ToString();
        await SeedPreMergeCharactersAsync(name: "Gandalf", type1: "Movie", type2: "Book",
            seriesId1: seriesId, seriesId2: seriesId);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE Name = 'Gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(2, survivorCount, "A shared Series must never override the Source.Type anchor invariant (ADR 011)");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown()
    {
        await SeedPreMergeCharactersAsync(name: "Sam", seriesId1: null, seriesId2: null);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE Name = 'Sam' AND IsDeleted = 0;");
        Assert.AreEqual(2, survivorCount, "Same Name, same Type, but no known Series relationship — conservative default must leave both rows separate");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow()
    {
        var seriesId = Guid.NewGuid().ToString();
        var partialMigrations = QuotinatorMigrations.All.Take(10).ToList();
        var db1 = CreateInitializer([], partialMigrations, useBaseline: false);
        await db1.InitialiseForTestingAsync(forceIncremental: true);

        var source1Id    = Guid.NewGuid().ToString();
        var source2Id    = Guid.NewGuid().ToString();
        var character1Id = Guid.NewGuid().ToString();
        var character2Id = Guid.NewGuid().ToString();
        var quoteId       = Guid.NewGuid().ToString();

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO Series (Id, Name, DateCreated, IsDeleted) VALUES (@id, @name, '2026-01-01 00:00:00', 0);",
                new { id = seriesId, name = $"Series {seriesId}" });
            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, SeriesId, DateCreated, IsDeleted) VALUES (@id, 'Source One', 'Movie', @seriesId, '2026-01-01 00:00:00', 0);",
                new { id = source1Id, seriesId });
            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, SeriesId, DateCreated, IsDeleted) VALUES (@id, 'Source Two', 'Movie', @seriesId, '2026-01-02 00:00:00', 0);",
                new { id = source2Id, seriesId });
            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, Name, CompletenessStatus, DateCreated, IsDeleted) VALUES (@id, 'Gandalf', 'Incomplete', '2026-01-01 00:00:00', 0);",
                new { id = character1Id });
            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, Name, CompletenessStatus, DateCreated, IsDeleted) VALUES (@id, 'Gandalf', 'Incomplete', '2026-01-02 00:00:00', 0);",
                new { id = character2Id });
            await conn.ExecuteAsync(
                "INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted) VALUES (@id, @characterId, @sourceId, '2026-01-01 00:00:00', 0);",
                new { id = Guid.NewGuid().ToString(), characterId = character1Id, sourceId = source1Id });
            await conn.ExecuteAsync(
                "INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted) VALUES (@id, @characterId, @sourceId, '2026-01-02 00:00:00', 0);",
                new { id = Guid.NewGuid().ToString(), characterId = character2Id, sourceId = source2Id });
            await conn.ExecuteAsync(
                "INSERT INTO Quotes (Id, QuoteText, OriginalLanguage, SourceId, CharacterId, DateCreated, IsDeleted) VALUES (@id, 'A line.', 'en', @sourceId, @characterId, '2026-01-02 00:00:00', 0);",
                new { id = quoteId, sourceId = source2Id, characterId = character2Id });
        }

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync();

        var resolvedCharacterId = await verifyConn.ExecuteScalarAsync<string>(
            "SELECT CharacterId FROM Quotes WHERE Id = @id;", new { id = quoteId });
        Assert.AreEqual(character1Id, resolvedCharacterId, "The quote's CharacterId must be re-pointed to the surviving row, not left dangling on the merged-away one");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_PreservesCompletenessStatusPerAlgorithm()
    {
        var seriesId = Guid.NewGuid().ToString();
        var (_, _, character1Id, _) = await SeedPreMergeCharactersAsync(
            seriesId1: seriesId, seriesId2: seriesId,
            completeness1: "Incomplete", completeness2: "Complete");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var survivorStatus = await conn.ExecuteScalarAsync<string>(
            "SELECT CompletenessStatus FROM Characters WHERE Id = @id;", new { id = character1Id });
        Assert.AreEqual("Complete", survivorStatus, "The most-reviewed CompletenessStatus across the merged group must win");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_BackfillsSourceTypeColumnFromLinkedSource()
    {
        var (_, _, character1Id, character2Id) = await SeedPreMergeCharactersAsync(
            name: "Sam", type1: "Movie", type2: "Book", seriesId1: null, seriesId2: null);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var type1 = await conn.ExecuteScalarAsync<string>("SELECT SourceType FROM Characters WHERE Id = @id;", new { id = character1Id });
        var type2 = await conn.ExecuteScalarAsync<string>("SELECT SourceType FROM Characters WHERE Id = @id;", new { id = character2Id });
        Assert.AreEqual("Movie", type1);
        Assert.AreEqual("Book", type2);
    }

    /// <summary>Every table added by #179 carries RecordBase's four audit columns — ADR 002 applies without exception, including the CharacterSources junction table.</summary>
    [TestMethod]
    public async Task SeriesUniverseTables_AllHaveRecordBaseColumns()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        foreach (var table in new[] { "Universe", "Series", "CharacterSources" })
        {
            var columns = (await conn.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{table}');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var recordBaseColumn in new[] { "Id", "DateCreated", "DateModified", "DateDeleted", "IsDeleted" })
                Assert.IsTrue(columns.Contains(recordBaseColumn), $"{table} is missing RecordBase column {recordBaseColumn}");
        }
    }

    // ── #180: curated series/universe overlay — end-to-end through the real bundled-seed path ──
    // Unlike SqliteQuoteImportService.ImportAsync (the live POST /import endpoint), the bundled
    // seed path's own LoadSourceFileAsync has no "at least one quote" requirement — appropriate
    // here since this overlay file's whole purpose is Source/Series/Universe enrichment with an
    // intentionally empty quotes[] section, matching data/sources/quotinator-series-universe.json's
    // own real shape.
    //
    // Confirmed with the developer (2026-07-16): under this file's Review policy, PlanSourcesAsync's
    // changed-field check has no "empty-existing-side is not a real conflict" special case (that
    // logic exists in FieldMergeResolver for MergeOurs/MergeTheirs and decide-time auto-resolution,
    // but not for the Review "should this even go Pending" gate) — so a first-time null-to-value
    // SeriesId fill stages Pending exactly like a genuine disagreement does. This is accepted as-is
    // for #180, matching the plan doc's original "a human decides" intent, at the cost of a fresh
    // install staging one Pending action per Source the overlay touches (see the real
    // data/sources/quotinator-series-universe.json's ~75 entries) until each is decided and applied.

    /// <summary>A Source with no existing SeriesId still stages a Pending action under this file's Review policy — the review gate has no first-time-fill exception, so nothing is silently applied.</summary>
    [TestMethod]
    public async Task SeedSeriesUniverseOverlay_NoExistingSeriesId_StagesPendingUnderReviewPolicy()
    {
        var sourceId = Quotinator.Core.Import.EntityIdentity.SourceId("Test Movie", "Movie");
        var quotesFile = Path.Combine(_tempDir, "quotes.json");
        File.WriteAllText(quotesFile, """[{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello there.","source":"Test Movie","type":"movie"}]""");
        var overlayFile = Path.Combine(_tempDir, "overlay.json");
        File.WriteAllText(overlayFile, $$"""
            {
              "quotes": [],
              "sources": [{"id":"{{sourceId}}","title":"Test Movie","type":"movie","seriesName":"Test Series"}],
              "series": [{"name":"Test Series"}]
            }
            """);

        var batch = new SeedBatch(
            [new SeedFile(quotesFile, null), new SeedFile(overlayFile, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review))],
            ManifestPolicy.HardcodedDefault, "overlay-test");
        var db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var pendingCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_ImportActions WHERE EntityType = 'Source' AND Status = 'Pending';");
        Assert.AreEqual(1, pendingCount, "Review policy stages Pending for any changed field, including a first-time null-to-value SeriesId fill");

        var seriesId = await conn.ExecuteScalarAsync<string?>(
            "SELECT SeriesId FROM Sources WHERE Id = @id;", new { id = sourceId });
        Assert.IsNull(seriesId, "Nothing applied yet — SeriesId stays null until the Pending action is decided and applied");
    }

    /// <summary>Re-seeding the exact same overlay content a second time is a true no-op — SeriesId already matches, so nothing is staged at all.</summary>
    [TestMethod]
    public async Task SeedSeriesUniverseOverlay_AlreadyTagged_NoActionStaged()
    {
        var sourceId = Quotinator.Core.Import.EntityIdentity.SourceId("Test Movie", "Movie");
        var quotesFile = Path.Combine(_tempDir, "quotes.json");
        File.WriteAllText(quotesFile, """[{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello there.","source":"Test Movie","type":"movie"}]""");
        var overlayFile = Path.Combine(_tempDir, "overlay.json");
        File.WriteAllText(overlayFile, $$"""
            {
              "quotes": [],
              "sources": [{"id":"{{sourceId}}","title":"Test Movie","type":"movie","seriesName":"Test Series"}],
              "series": [{"name":"Test Series"}]
            }
            """);

        var batch = new SeedBatch(
            [new SeedFile(quotesFile, null), new SeedFile(overlayFile, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins))],
            ManifestPolicy.HardcodedDefault, "overlay-test-seed");
        var db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var seriesId = await conn.ExecuteScalarAsync<string?>("SELECT SeriesId FROM Sources WHERE Id = @id;", new { id = sourceId });
        Assert.IsNotNull(seriesId, "Sanity check — NewestWins applies immediately, so SeriesId must already be set before the second pass");

        var reapplyBatchId = Guid.NewGuid();
        using (var reapplyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await reapplyConn.OpenAsync();
            var actions = await Quotinator.Core.Database.ImportActionPlanner.PlanAsync(
                (SqliteConnection)reapplyConn, [], reapplyBatchId, DuplicateResolutionPolicy.Review,
                sources: [new Quotinator.Core.Import.SourceEntry { Id = sourceId, Title = "Test Movie", Type = Quotinator.Core.Models.QuoteType.Movie, SeriesName = "Test Series" }]);

            Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Identical content — no change, no action staged at all");
        }
    }

}
