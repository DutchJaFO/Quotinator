using System.Data;
using Dapper;

namespace Quotinator.Data.Helpers;

/// <summary>
/// Dapper TypeHandler for <see cref="Guid"/>.
/// Forces UUID values to TEXT storage in SQLite so they remain human-readable and comparable
/// across all query paths. Without this handler, Dapper sets <c>DbType.Guid</c> on the parameter;
/// Microsoft.Data.Sqlite maps <c>DbType.Guid</c> to <c>SqliteType.Blob</c> (16 raw bytes), which
/// does not compare equal to the dashed-string UUID format used in hand-written queries.
/// </summary>
public sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
{
    /// <inheritdoc/>
    public override Guid Parse(object value)
        => Guid.Parse(value.ToString()!);

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        parameter.DbType = DbType.String;
        // Microsoft.Data.Sqlite stores Guid as uppercase TEXT by default; use the same
        // format so that all Guid values in the database share a consistent case and
        // binary TEXT comparisons (SQLite default) always match.
        parameter.Value  = value.ToString("D").ToUpperInvariant();
    }
}
