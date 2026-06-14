using Dapper.Contrib.Extensions;

namespace Quotinator.Data.Models;

public abstract class RecordBase
{
    [ExplicitKey]
    public Guid Id { get; init; } = Guid.NewGuid();

    public SafeValue<DateTime?> DateCreated  { get; init; } = SafeDateValue.Now;
    public SafeValue<DateTime?> DateModified { get; set; }  = SafeDateValue.Empty;
    public SafeValue<DateTime?> DateDeleted  { get; set; }  = SafeDateValue.Empty;
    public bool IsDeleted { get; set; }
}
