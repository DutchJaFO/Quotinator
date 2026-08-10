namespace Quotinator.Core.Queries;

/// <summary>Read model for a single active Source reference resolved from a Character's linked Sources.</summary>
public sealed record SourceRow(Guid Id, string Title);
