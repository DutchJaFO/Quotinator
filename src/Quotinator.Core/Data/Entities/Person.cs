using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Core.Data.Entities;

/// <summary>A real person who said or wrote a quote — an author or public figure.</summary>
[Table("People")]
public sealed class Person : RecordBase
{
    /// <summary>The person's full name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Imprecise ISO 8601 birth date (e.g. "1955" or "1955-02-24"). Empty when unknown.</summary>
    public SafeValue<DateTime?> DateOfBirth { get; init; } = SafeDateValue.Empty;

    /// <summary>Imprecise ISO 8601 death date. Empty when the person is still living or the date is unknown.</summary>
    public SafeValue<DateTime?> DateOfDeath { get; init; } = SafeDateValue.Empty;
}
