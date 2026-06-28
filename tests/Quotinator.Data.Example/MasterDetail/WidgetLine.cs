using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Example.MasterDetail;

/// <summary>
/// Child entity for the master/detail example.
/// Each <c>Widget</c> has zero or more <c>WidgetLine</c> rows.
/// <c>ParentId</c> is the FK pointing back to the parent <c>Widget</c>.
/// </summary>
[Table("WidgetLines")]
public sealed class WidgetLine : RecordBase
{
    public string ParentId { get; set; } = string.Empty;
    public string Value    { get; set; } = string.Empty;
}
