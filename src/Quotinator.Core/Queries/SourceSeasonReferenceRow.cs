namespace Quotinator.Core.Queries;

/// <summary>Read model for a single active Season reference resolved from a Source's <c>SeasonId</c>.</summary>
public sealed record SourceSeasonReferenceRow(Guid Id, int Number, string? Title, string? Subtitle);
