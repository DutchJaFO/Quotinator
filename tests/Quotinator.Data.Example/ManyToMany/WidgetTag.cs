using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Example.ManyToMany;

/// <summary>
/// Junction entity linking <see cref="Common.Widget"/> (left) to <see cref="Tag"/> (right).
/// Uses <see cref="RecordBase"/> so it supports soft-delete and audit trail automatically.
/// A <c>UNIQUE (WidgetId, TagId)</c> constraint in the DDL enforces the many-to-many invariant.
/// </summary>
[Table("WidgetTags")]
public sealed class WidgetTag : RecordBase
{
    public string WidgetId { get; set; } = string.Empty;
    public string TagId    { get; set; } = string.Empty;
}
