using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Example.OneToOne;

/// <summary>
/// Detail entity for the shared-primary-key one-to-one example.
/// Its <c>Id</c> is set to the parent <see cref="Common.Widget"/>'s <c>Id</c> before insert.
/// </summary>
[Table("WidgetDetails")]
public sealed class WidgetDetail : RecordBase
{
    public string Notes { get; set; } = string.Empty;
}
