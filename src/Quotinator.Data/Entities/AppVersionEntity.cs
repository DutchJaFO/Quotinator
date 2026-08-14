using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// The last app version that completed a healthy startup (#81) — read before migrations run on the
/// following boot so the what's-new notification producer can tell which releases were missed.
/// Exactly one non-deleted row is an application-level invariant, enforced by
/// <see cref="Repositories.IAppVersionTracker"/>'s own upsert logic, not the schema.
/// </summary>
[Table("System_AppVersion")]
public sealed class AppVersionEntity : RecordBase
{
    /// <summary>The recorded version string (e.g. <c>1.8.3</c>).</summary>
    public string Version { get; init; } = string.Empty;
}
