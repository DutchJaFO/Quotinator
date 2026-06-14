using Dapper.Contrib.Extensions;

namespace Quotinator.Data.Models;

/// <summary>Base class for all database-backed entities. Provides a UUID primary key and soft-delete audit columns.</summary>
public abstract class RecordBase
{
    /// <summary>Primary key. UUID v4, generated on construction.</summary>
    [ExplicitKey]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>UTC timestamp when the record was first written. Immutable after creation.</summary>
    public SafeValue<DateTime?> DateCreated  { get; init; } = SafeDateValue.Now;

    /// <summary>UTC timestamp of the most recent update. Empty until the record is first modified.</summary>
    public SafeValue<DateTime?> DateModified { get; set; }  = SafeDateValue.Empty;

    /// <summary>UTC timestamp when the record was soft-deleted. Empty on active records.</summary>
    public SafeValue<DateTime?> DateDeleted  { get; set; }  = SafeDateValue.Empty;

    /// <summary><c>true</c> when the record has been soft-deleted and should not appear in normal queries.</summary>
    public bool IsDeleted { get; set; }
}
