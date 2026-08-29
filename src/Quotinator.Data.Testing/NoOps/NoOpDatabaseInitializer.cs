using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IDatabaseInitializer"/> for use in tests that register a fake service layer and must not touch any database.</summary>
public sealed class NoOpDatabaseInitializer : IDatabaseInitializer
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpDatabaseInitializer Instance = new();

    /// <inheritdoc/>
    public int SchemaVersion => 0;

    /// <inheritdoc/>
    public int DataSchemaVersion => 0;

    /// <inheritdoc/>
    public int QuoteCount => 0;

    /// <inheritdoc/>
    public int SourceCount => 0;

    /// <inheritdoc/>
    public int CharacterCount => 0;

    /// <inheritdoc/>
    public int PeopleCount => 0;

    /// <inheritdoc/>
    public int SeriesCount => 0;

    /// <inheritdoc/>
    public int UniverseCount => 0;

    /// <inheritdoc/>
    public int StageDirectionCount => 0;

    /// <inheritdoc/>
    public int SoundCueCount => 0;

    /// <inheritdoc/>
    public int ConversationCount => 0;

    /// <inheritdoc/>
    public string? MigrationApplied => null;

    /// <inheritdoc/>
    public bool SchemaVersionOvershootDetected => false;

    /// <inheritdoc/>
    public BackupOutcome CheckBackupReadiness(bool allowReserve = false) => BackupOutcome.Succeeded;

    /// <inheritdoc/>
    /// <remarks>
    /// Reports a written file without writing one — this type exists so a test touches no database and
    /// no filesystem. A test that cares what a backup attempt actually did supplies its own
    /// initializer rather than relying on this.
    /// </remarks>
    public Task<DatabaseBackupResult> CreateBackupAsync() =>
        Task.FromResult(DatabaseBackupResult.Success("noop-backup.db"));

    /// <inheritdoc/>
    public IReadOnlyList<FileImportReport> LastSeedReport => [];

    /// <inheritdoc/>
    public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

    /// <inheritdoc/>
    public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) => Task.FromResult(DatabaseOperationResult.Success());

    /// <inheritdoc/>
    public Task<SeedPreviewResult> PreviewSeedAsync()
        => Task.FromResult(new SeedPreviewResult([], []));

    /// <inheritdoc/>
    public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false)
        => Task.FromResult(new SourceCacheResolution([], []));
}
