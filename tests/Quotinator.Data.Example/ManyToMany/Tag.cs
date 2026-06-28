using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Example.ManyToMany;

/// <summary>
/// Right-side entity in the many-to-many example.
/// Many <see cref="Common.Widget"/> rows can be linked to many <see cref="Tag"/> rows
/// through the <see cref="WidgetTag"/> junction table.
/// </summary>
[Table("Tags")]
public sealed class Tag : RecordBase
{
    public string Name { get; set; } = string.Empty;
}
