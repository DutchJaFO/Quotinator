namespace Quotinator.Core.Repositories;

/// <summary>Resolves a Series name to its active (non-deleted) id — backs the name-valued form of the
/// #196 entity-scoped filter convention for #192's quote-read-path Series filter.</summary>
public interface ISeriesNameResolver
{
    /// <summary>The active Series id with this exact name, or <c>null</c> if none exists.</summary>
    Task<Guid?> ResolveIdByNameAsync(string name);
}
