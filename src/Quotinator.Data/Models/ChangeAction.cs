namespace Quotinator.Data.Models;

/// <summary>The kind of database operation a <see cref="Quotinator.Data.Entities.SystemChangeLog"/> row records.</summary>
public enum ChangeAction
{
    /// <summary>A new record was written.</summary>
    Created,
    /// <summary>An existing record's fields were changed.</summary>
    Modified,
    /// <summary>A record was marked deleted.</summary>
    SoftDelete,
    /// <summary>A record was permanently removed.</summary>
    HardDelete
}
