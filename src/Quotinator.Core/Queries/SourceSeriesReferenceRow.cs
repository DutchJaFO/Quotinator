namespace Quotinator.Core.Queries;

/// <summary>Read model for one (Source, Series) pair from a batched active Series reference lookup.</summary>
public sealed record SourceSeriesReferenceRow(Guid SourceId, Guid SeriesId, string SeriesName);
