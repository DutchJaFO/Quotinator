using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// One layer of the SQLite database check (#490, ADR 028). Each layer is its own class, so adding one or
/// replacing how a layer is checked touches no other layer; <see cref="SqliteDatabaseCheck"/> holds the order.
/// </summary>
internal interface IDatabaseCheckLayer
{
    /// <summary>The layer this class checks.</summary>
    DatabaseCheckLayer Layer { get; }

    /// <summary>Whether the layer runs at <see cref="DatabaseCheckDepth.Fast"/>; otherwise it is reported skipped.</summary>
    bool RunsAtFast { get; }

    /// <summary>Runs the layer against the database the subject describes. Writes nothing to the original files.</summary>
    /// <param name="subject">The database being checked, and what earlier layers found about it.</param>
    Task<DatabaseCheckLayerResult> RunAsync(SqliteCheckSubject subject);
}
