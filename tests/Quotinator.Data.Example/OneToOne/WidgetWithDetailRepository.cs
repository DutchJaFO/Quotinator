using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Example.OneToOne;

/// <summary>
/// Example <see cref="SqliteOneToOneRepository{TParent,TDetail}"/> using the
/// <b>shared-primary-key</b> layout: the detail row's <c>Id</c> equals the parent's <c>Id</c>.
/// </summary>
/// <remarks>
/// Use this layout when parent and detail are always created and deleted together.
/// The DDL should declare the detail table's PK as a FK reference to the parent table.
/// </remarks>
public sealed class WidgetWithDetailRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext)
    : SqliteOneToOneRepository<Widget, WidgetDetail>(factory, auditWriter, callerContext)
{
    private readonly SqliteRepository<WidgetDetail> _detailRepo = new(factory, auditWriter, callerContext);

    protected override SqliteRepository<WidgetDetail> ChildRepository => _detailRepo;

    protected override IReadOnlyList<WidgetDetail> GetChildren(Widget parent) =>
    [
        new WidgetDetail
        {
            Id    = parent.Id,   // shared PK: detail.Id must equal parent.Id
            Notes = $"detail for {parent.Label}"
        }
    ];

    public override Task<WidgetDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? unitOfWork = null)
        => GetDetailBySharedKeyAsync(parentId, unitOfWork);
}
