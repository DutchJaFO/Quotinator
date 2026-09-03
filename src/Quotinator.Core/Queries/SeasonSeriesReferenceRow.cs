namespace Quotinator.Core.Queries;

/// <summary>Read model for a single active Series reference resolved from a Season's <c>SeriesId</c>.</summary>
public sealed record SeasonSeriesReferenceRow(Guid Id, string Name);
