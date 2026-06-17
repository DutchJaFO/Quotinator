using Quotinator.Core.Data;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>No-op IDatabaseInitializer for endpoint tests that use FakeQuoteService and must not touch the real database.</summary>
internal sealed class NoOpDatabaseInitializer : IDatabaseInitializer
{
    public int                              SchemaVersion       => 0;
    public int                              QuoteCount          => 0;
    public int                              SourceCount         => 0;
    public int                              CharacterCount      => 0;
    public int                              PeopleCount         => 0;
    public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates => [];
    public Task InitialiseAsync()                               => Task.CompletedTask;
    public Task ReseedAsync()                                   => Task.CompletedTask;
    public Task ResetAsync()                                    => Task.CompletedTask;
    public Task<SeedPreviewResult> PreviewSeedAsync()           =>
        Task.FromResult(new SeedPreviewResult([], [], 0, 0));
}
