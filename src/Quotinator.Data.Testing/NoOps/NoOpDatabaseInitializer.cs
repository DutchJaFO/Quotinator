using Quotinator.Data.Database;
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
    public int QuoteCount => 0;

    /// <inheritdoc/>
    public int SourceCount => 0;

    /// <inheritdoc/>
    public int CharacterCount => 0;

    /// <inheritdoc/>
    public int PeopleCount => 0;

    /// <inheritdoc/>
    public string? MigrationApplied => null;

    /// <inheritdoc/>
    public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates => [];

    /// <inheritdoc/>
    public Task InitialiseAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReseedAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ResetAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<SeedPreviewResult> PreviewSeedAsync()
        => Task.FromResult(new SeedPreviewResult([], [], 0, 0));
}
