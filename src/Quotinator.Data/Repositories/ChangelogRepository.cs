using Microsoft.Extensions.DependencyInjection;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Writes a <see cref="ChangelogEntryEntity"/> and its <see cref="ChangelogLineEntity"/> children
/// atomically, via #75's master/detail pattern (<see cref="AggregateRepository{TParent,TChild}"/>).
/// Uses <see cref="NullAuditEntryWriter"/> — the changelog database has no <c>Audit_Entry</c> table of
/// its own (ADR 018: it is deliberately isolated from the main database's audit infrastructure along
/// with everything else domain-coupled).
/// </summary>
/// <remarks>Initialises the repository with the changelog database's own keyed connection factory and caller context.</remarks>
/// <param name="factory">The keyed <see cref="IDbConnectionFactory"/> for the changelog database (see <see cref="DatabaseConnectionKeys.Changelog"/>).</param>
/// <param name="callerContext">Identifies the caller attributed to each write — unused in practice since <see cref="NullAuditEntryWriter"/> discards it, kept for interface parity with every other repository.</param>
public sealed class ChangelogRepository(
    [FromKeyedServices(DatabaseConnectionKeys.Changelog)] IDbConnectionFactory factory,
    ICallerContext callerContext)
    : AggregateRepository<ChangelogEntryEntity, ChangelogLineEntity>(factory, NullAuditEntryWriter.Instance, callerContext)
{
    private readonly SqliteRepository<ChangelogLineEntity> _lineRepository =
        new(factory, NullAuditEntryWriter.Instance, callerContext);

    // AggregateRepository.GetChildren(TParent) only ever receives the parent entity, with no channel
    // of its own to carry the children alongside it (unlike the doc's own Widget/Order example, which
    // assumes a navigation property directly on the parent entity — not used here, to avoid marking
    // ChangelogEntryEntity's shape with a non-column property purely for this one call). Set immediately
    // before InsertWithLinesAsync's own InsertAsync call, read back by the base class within the same
    // synchronous call stack — never left set across calls, so this is safe despite looking stateful.
    private IReadOnlyList<ChangelogLineEntity> _childrenForNextInsert = [];

    /// <summary>Inserts <paramref name="entity"/> and <paramref name="lines"/> atomically.</summary>
    public Task InsertWithLinesAsync(ChangelogEntryEntity entity, IReadOnlyList<ChangelogLineEntity> lines, IUnitOfWork? unitOfWork = null)
    {
        _childrenForNextInsert = lines;
        return InsertAsync(entity, unitOfWork);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<ChangelogLineEntity> GetChildren(ChangelogEntryEntity parent) => _childrenForNextInsert;

    /// <inheritdoc/>
    protected override SqliteRepository<ChangelogLineEntity> ChildRepository => _lineRepository;
}
