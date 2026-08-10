namespace Quotinator.Core.Queries;

/// <summary>Read model for a single active Series reference resolved from a Source's <c>SeriesId</c>.</summary>
public sealed record SeriesReferenceRow(Guid Id, string Name);
