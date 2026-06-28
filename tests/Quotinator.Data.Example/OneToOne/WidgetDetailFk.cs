using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Example.OneToOne;

/// <summary>
/// Detail entity for the separate-foreign-key one-to-one example.
/// Has its own <c>Id</c> plus a <c>WidgetId</c> FK column pointing back to the parent.
/// </summary>
[Table("WidgetDetailsFk")]
public sealed class WidgetDetailFk : RecordBase
{
    public string WidgetId { get; set; } = string.Empty;
    public string Notes    { get; set; } = string.Empty;
}
