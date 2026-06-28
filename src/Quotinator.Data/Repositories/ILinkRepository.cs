using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Many-to-many link repository: manages the junction table between
/// <typeparamref name="TLeft"/> and <typeparamref name="TRight"/> entities.
/// </summary>
/// <remarks>
/// The junction entity type (<c>TJunction</c>) is an implementation detail of the concrete class
/// and does not appear on this interface.
/// </remarks>
/// <typeparam name="TLeft">Left side of the relationship.</typeparam>
/// <typeparam name="TRight">Right side of the relationship.</typeparam>
public interface ILinkRepository<TLeft, TRight>
    where TLeft  : RecordBase
    where TRight : RecordBase
{
    /// <summary>
    /// Creates a link between <paramref name="leftId"/> and <paramref name="rightId"/>.
    /// If a soft-deleted row already exists it is restored; otherwise a new row is inserted.
    /// </summary>
    Task LinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Soft-deletes the active link between <paramref name="leftId"/> and <paramref name="rightId"/>.
    /// No-op when no active link exists.
    /// </summary>
    Task UnlinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Restores a previously soft-deleted link between <paramref name="leftId"/> and <paramref name="rightId"/>.
    /// No-op when no soft-deleted link exists.
    /// </summary>
    Task RestoreLinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Returns all active <typeparamref name="TRight"/> entities linked to <paramref name="leftId"/>.
    /// </summary>
    Task<IReadOnlyList<TRight>> GetRightAsync(Guid leftId, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Returns all active <typeparamref name="TLeft"/> entities linked to <paramref name="rightId"/>.
    /// </summary>
    Task<IReadOnlyList<TLeft>> GetLeftAsync(Guid rightId, IUnitOfWork? unitOfWork = null);
}
