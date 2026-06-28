using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Example.OneToOne;

/// <summary>
/// Example <see cref="SqliteOneToOneRepository{TParent,TDetail}"/> using the
/// <b>separate-foreign-key</b> layout: the detail row has its own <c>Id</c> and a
/// <c>WidgetId</c> FK column pointing back to the parent.
/// </summary>
/// <remarks>
/// Use this layout when the detail can have an independent lifetime or may not always be present.
/// <c>GetDetailAsync</c> delegates to <see cref="SqliteOneToOneRepository{TParent,TDetail}.GetDetailByForeignKeyAsync"/>
/// with the FK column name.
/// </remarks>
public sealed class WidgetWithFkDetailRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext)
    : SqliteOneToOneRepository<Widget, WidgetDetailFk>(factory, auditWriter, callerContext)
{
    private readonly SqliteRepository<WidgetDetailFk> _detailRepo = new(factory, auditWriter, callerContext);

    protected override SqliteRepository<WidgetDetailFk> ChildRepository => _detailRepo;

    protected override IReadOnlyList<WidgetDetailFk> GetChildren(Widget parent) =>
    [
        new WidgetDetailFk
        {
            WidgetId = parent.Id.ToString("D").ToUpperInvariant(),
            Notes    = $"fk-detail for {parent.Label}"
        }
    ];

    public override Task<WidgetDetailFk?> GetDetailAsync(Guid parentId, IUnitOfWork? unitOfWork = null)
        => GetDetailByForeignKeyAsync("WidgetId", parentId, unitOfWork);
}
