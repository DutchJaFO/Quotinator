using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Data.Entities;

[Table("People")]
public sealed class Person : RecordBase
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Imprecise ISO 8601 text (e.g. "1955" or "1955-02-24"). Null if unknown.</summary>
    public SafeValue<DateTime?> DateOfBirth { get; init; } = SafeDateValue.Empty;

    /// <summary>Imprecise ISO 8601 text. Null if the person is still living or the date is unknown.</summary>
    public SafeValue<DateTime?> DateOfDeath { get; init; } = SafeDateValue.Empty;
}
