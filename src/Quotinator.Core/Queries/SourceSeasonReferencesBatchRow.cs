namespace Quotinator.Core.Queries;

/// <summary>Read model for one (Source, Season) pair from a batched active Season reference lookup.</summary>
public sealed record SourceSeasonReferencesBatchRow(Guid SourceId, Guid SeasonId, int Number, string? Title, string? Subtitle);
