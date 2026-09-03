namespace Quotinator.Core.Queries;

/// <summary>Read model for one (Source, Season) pair from a batched active Season reference lookup.
/// <c>Number</c> is <c>long</c> for the same reason as <see cref="SourceSeasonReferenceRow"/> — see its
/// remarks.</summary>
public sealed record SourceSeasonReferencesBatchRow(Guid SourceId, Guid SeasonId, long Number, string? Title, string? Subtitle);
