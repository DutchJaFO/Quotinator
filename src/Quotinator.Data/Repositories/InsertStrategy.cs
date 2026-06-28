namespace Quotinator.Data.Repositories;

/// <summary>Controls how <see cref="IRepository{T}.InsertManyAsync"/> writes a collection of entities.</summary>
public enum InsertStrategy
{
    /// <summary>
    /// Inserts all entities in a single SQL round-trip with a matching bulk audit write. Fastest option.
    /// </summary>
    Bulk,

    /// <summary>
    /// Inserts each entity individually by calling <see cref="IRepository{T}.InsertAsync"/> per entity.
    /// A failure on any entity propagates immediately with that entity's exception.
    /// Produces one audit entry per entity through the normal single-entry write path.
    /// </summary>
    Sequential
}
