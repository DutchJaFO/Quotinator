namespace Quotinator.Core.Queries;

/// <summary>Read model for one (Character, Source) pair from a batched active Source reference lookup.</summary>
public sealed record LinkRow(Guid CharacterId, Guid SourceId, string SourceTitle);
