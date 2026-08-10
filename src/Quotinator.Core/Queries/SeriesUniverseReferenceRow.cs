namespace Quotinator.Core.Queries;

/// <summary>Read model for one (Series, Universe) pair from a batched active Universe reference lookup.</summary>
public sealed record SeriesUniverseReferenceRow(Guid SeriesId, Guid UniverseId, string UniverseName);
