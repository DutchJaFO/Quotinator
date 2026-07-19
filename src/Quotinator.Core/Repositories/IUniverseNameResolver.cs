namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Universe name to its active (non-deleted) id — backs the name-valued form of the
/// #196 entity-scoped filter convention for #192's quote-read-path Universe filter.</summary>
public interface IUniverseNameResolver
{
    /// <summary>The active Universe id with this exact name, or <c>null</c> if none exists.</summary>
    Task<Guid?> ResolveIdByNameAsync(string name);
}
