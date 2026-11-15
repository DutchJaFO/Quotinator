using Microsoft.Data.Sqlite;
using Quotinator.Data.Enums;
using Quotinator.Logging;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// A layer of the database check that reads the database through SQLite (#490, ADR 028). What the four have
/// in common is here: there is nothing to check without a file, a connection that cannot be opened soundly
/// makes the layer inconclusive (decision D), and SQLite rejecting the file is the layer's failure.
/// </summary>
internal abstract class QueryLayer : IDatabaseCheckLayer
{
    /// <inheritdoc/>
    public abstract DatabaseCheckLayer Layer { get; }

    /// <inheritdoc/>
    public abstract bool RunsAtFast { get; }

    /// <inheritdoc/>
    public async Task<DatabaseCheckLayerResult> RunAsync(SqliteCheckSubject subject)
    {
        if (!subject.FileExists)
            return LayerResults.NothingToCheck(Layer);

        try
        {
            (SqliteConnection? connection, string? whyNot) = await subject.OpenAsync();
            return connection is null
                ? LayerResults.Inconclusive(Layer, whyNot!)
                : await CheckAsync(connection, subject);
        }
        // ADR 022: nothing lets the code ask whether SQLite can read a file without reading it, so SQLite's own
        // rejection is the answer, handled here, at the first point an outcome can be formed.
        catch (SqliteException ex)
        {
            subject.Logger.LogExceptionHandled(ex, $"SQLite rejected the database during the {Layer} layer of its check");
            return LayerResults.Failed(Layer, ex.Message);
        }
    }

    /// <summary>Runs this layer's check on an open connection to the database.</summary>
    /// <param name="connection">The open connection, to the original or to its recovered copy.</param>
    /// <param name="subject">The database being checked.</param>
    protected abstract Task<DatabaseCheckLayerResult> CheckAsync(SqliteConnection connection, SqliteCheckSubject subject);

    /// <summary>
    /// Runs a check pragma whose healthy result is a single row, <c>ok</c>, and passes or fails on it, carrying
    /// SQLite's own lines as the finding.
    /// </summary>
    protected async Task<DatabaseCheckLayerResult> OkPragmaAsync(SqliteConnection connection, string pragma)
    {
        List<string> lines = [];
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = pragma;
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));

        return lines is ["ok"] ? LayerResults.Passed(Layer) : LayerResults.Failed(Layer, LayerResults.Join(lines));
    }
}
