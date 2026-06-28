using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Example.Common;

/// <summary>
/// Shared example entity used across all repository pattern examples.
/// In production code this would be a domain entity such as <c>Quote</c> or <c>Source</c>.
/// </summary>
[Table("Widgets")]
public sealed class Widget : RecordBase
{
    public string Label { get; set; } = string.Empty;
}
