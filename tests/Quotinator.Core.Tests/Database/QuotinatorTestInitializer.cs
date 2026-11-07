using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// Builds a real <see cref="QuotinatorDatabaseInitializer"/> over a database file with every collaborator
/// it does not need replaced by a no-op, for tests in more than one class. Moved here from
/// <c>DatabaseInitializerTests</c> when #305's integrity tests needed the same construction, rather than
/// copied: two copies of a twenty-five-argument constructor call drift.
/// </summary>
internal static class QuotinatorTestInitializer
{
    /// <summary>An initializer for the database at <paramref name="dbPath"/>.</summary>
    /// <param name="dbPath">The database file.</param>
    /// <param name="backupsPath">Where backups are written.</param>
    /// <param name="batches">The content to load; none by default.</param>
    /// <param name="migrations">The consumer migrations; this application's own by default.</param>
    /// <param name="diskSpaceProvider">The free space the backup check sees; unlimited by default.</param>
    internal static QuotinatorDatabaseInitializer Create(
        string dbPath, string backupsPath,
        IReadOnlyList<SeedBatch>? batches = null,
        IReadOnlyList<SchemaMigration>? migrations = null,
        IDiskSpaceProvider? diskSpaceProvider = null)
    {
        SqliteConnectionFactory factory = new(dbPath);
        DatabaseOptions options = new() { DbPath = dbPath, BackupsPath = backupsPath };
        SqliteImportBatchRepository importBatches = new(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        ImportActionReader actionReader = new(factory);
        ImportActionWriter actionWriter = new(factory);
        ImportActionResolutionCoordinator coordinator = new(actionReader, actionWriter, factory);
        SqliteImportActionService actionService = new(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory, NoOpNotificationWriter.Instance);

        return new QuotinatorDatabaseInitializer(factory, options, migrations ?? QuotinatorMigrations.All, batches ?? [], importBatches,
            coordinator, actionService, actionWriter,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance,
            NoOpFileResourceRepository.Instance,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
            new AppVersionTracker(factory), new VersionService(), diskSpaceProvider ?? NoOpDiskSpaceProvider.Instance,
            QuotinatorMigrations.Baseline);
    }

    /// <summary>A batch holding one generated quote, written to a fresh file in <paramref name="directory"/>.</summary>
    /// <param name="directory">Where the quote file is written.</param>
    internal static SeedBatch SimpleQuoteBatch(string directory)
    {
        string quotesFile = Path.Combine(directory, $"quotes-{Guid.NewGuid():N}.json");
        File.WriteAllText(quotesFile,
            $$"""[{"id":"{{Guid.NewGuid()}}","quote":"Hello there.","source":"Test Movie","type":"movie","genres":["drama"]}]""");
        return new SeedBatch([new SeedFile(quotesFile, null)], ManifestPolicy.HardcodedDefault, "simple-test-seed");
    }
}

/// <summary>Reports a fixed amount of free disk space, so a test can make a backup impossible.</summary>
/// <param name="availableBytes">The free space every query reports.</param>
internal sealed class FakeDiskSpaceProvider(long availableBytes) : IDiskSpaceProvider
{
    /// <inheritdoc/>
    public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
}
