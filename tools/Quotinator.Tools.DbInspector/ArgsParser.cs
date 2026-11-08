namespace Quotinator.Tools.DbInspector;

/// <summary>Parsed command-line arguments for a single query run.</summary>
/// <param name="DbPath">The database file to query.</param>
/// <param name="Sql">The query to run.</param>
/// <param name="JsonOut">Where to also write the rows as JSON, or <c>null</c> to print the table only.</param>
internal readonly record struct QueryArgs(string DbPath, string Sql, string? JsonOut = null);

/// <summary>Parses the <c>--db</c> and <c>--sql</c> command-line arguments.</summary>
internal static class ArgsParser
{
    /// <summary>Returns the parsed arguments, or <c>null</c> if either <c>--db</c> or <c>--sql</c> is missing.</summary>
    internal static QueryArgs? Parse(string[] args)
    {
        string? dbPath  = null;
        string? sql     = null;
        string? jsonOut = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--db")       dbPath  = args[i + 1];
            if (args[i] == "--sql")      sql     = args[i + 1];
            if (args[i] == "--json-out") jsonOut = args[i + 1];
        }

        return dbPath is null || sql is null ? null : new QueryArgs(dbPath, sql, jsonOut);
    }

    /// <summary>Returns the <c>--db</c> argument, or <c>null</c> if it is missing (#305's <c>integrity</c> command).</summary>
    internal static string? ParseDb(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--db")
                return args[i + 1];
        }

        return null;
    }
}
