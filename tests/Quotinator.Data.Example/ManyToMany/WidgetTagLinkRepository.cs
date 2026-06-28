using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Example.ManyToMany;

/// <summary>
/// Example <see cref="SqliteLinkRepository{TLeft,TRight,TJunction}"/> that manages
/// the many-to-many relationship between <see cref="Widget"/> (left) and <see cref="Tag"/> (right)
/// through the <see cref="WidgetTag"/> junction table.
/// </summary>
/// <remarks>
/// The concrete class only needs to declare the three abstract members:
/// <list type="bullet">
///   <item><description><c>LeftFkColumn</c> — the junction column pointing to <see cref="Widget"/></description></item>
///   <item><description><c>RightFkColumn</c> — the junction column pointing to <see cref="Tag"/></description></item>
///   <item><description><c>CreateJunction</c> — builds an unsaved <see cref="WidgetTag"/> row</description></item>
/// </list>
/// All read and write operations are provided by the base class.
/// </remarks>
public sealed class WidgetTagLinkRepository(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext)
    : SqliteLinkRepository<Widget, Tag, WidgetTag>(factory, auditWriter, callerContext)
{
    protected override string LeftFkColumn  => "WidgetId";
    protected override string RightFkColumn => "TagId";

    protected override WidgetTag CreateJunction(Guid leftId, Guid rightId) => new()
    {
        WidgetId = leftId.ToString("D").ToUpperInvariant(),
        TagId    = rightId.ToString("D").ToUpperInvariant()
    };
}
