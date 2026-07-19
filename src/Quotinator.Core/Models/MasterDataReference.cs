namespace Quotinator.Core.Models;

/// <summary>A minimal, read-only reference to a related masterdata entity — just enough to display
/// without a separate lookup. Fetch the full record via that entity's own masterdata endpoint for more
/// detail. See CLAUDE.md's "Masterdata reference shape" convention.</summary>
public sealed record MasterDataReference(string Id, string Name);
