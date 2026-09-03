namespace Quotinator.Core.Queries;

/// <summary>Read model for one (Season, Series) pair from a batched active Series reference lookup.</summary>
public sealed record SeasonSeriesReferencesBatchRow(Guid SeasonId, Guid SeriesId, string SeriesName);
