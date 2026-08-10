namespace Quotinator.Core.Queries;

/// <summary>Read model for a single active Universe reference resolved from a Series' <c>UniverseId</c>.</summary>
public sealed record UniverseReferenceRow(Guid Id, string Name);
