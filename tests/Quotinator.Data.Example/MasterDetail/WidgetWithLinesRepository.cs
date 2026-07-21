using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Helpers;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Example.MasterDetail;

/// <summary>
/// Example <see cref="AggregateRepository{TParent,TChild}"/> that inserts a <see cref="Widget"/>
/// and its <see cref="WidgetLine"/> collection atomically in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// The child repository is created internally — consumers only inject the three standard
/// dependencies (<c>IDbConnectionFactory</c>, <c>ISystemAuditWriter</c>, <c>ICallerContext</c>).
/// </para>
/// <para>
/// In production code, <c>GetChildren</c> typically reads a navigation property set by the caller
/// before passing the entity to <c>InsertAsync</c>.  This example derives lines from the
/// widget's <c>Label</c> so the class is fully self-contained.
/// </para>
/// </remarks>
public sealed class WidgetWithLinesRepository(
    IDbConnectionFactory factory,
    ISystemAuditWriter auditWriter,
    ICallerContext callerContext)
    : AggregateRepository<Widget, WidgetLine>(factory, auditWriter, callerContext)
{
    private readonly SqliteRepository<WidgetLine> _lineRepo = new(factory, auditWriter, callerContext);

    protected override SqliteRepository<WidgetLine> ChildRepository => _lineRepo;

    protected override IReadOnlyList<WidgetLine> GetChildren(Widget parent) =>
    [
        new WidgetLine { ParentId = parent.Id.ToCanonicalId(), Value = $"{parent.Label} — Line 1" },
        new WidgetLine { ParentId = parent.Id.ToCanonicalId(), Value = $"{parent.Label} — Line 2" }
    ];
}
