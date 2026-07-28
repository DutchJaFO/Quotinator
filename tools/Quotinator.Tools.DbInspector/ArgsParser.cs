namespace Quotinator.Tools.DbInspector;

/// <summary>Parsed command-line arguments for a single query run.</summary>
internal readonly record struct QueryArgs(string DbPath, string Sql);

/// <summary>Parses the <c>--db</c> and <c>--sql</c> command-line arguments.</summary>
internal static class ArgsParser
{
    /// <summary>Returns the parsed arguments, or <c>null</c> if either <c>--db</c> or <c>--sql</c> is missing.</summary>
    internal static QueryArgs? Parse(string[] args)
    {
        string? dbPath = null;
        string? sql    = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--db")  dbPath = args[i + 1];
            if (args[i] == "--sql") sql    = args[i + 1];
        }

        return dbPath is null || sql is null ? null : new QueryArgs(dbPath, sql);
    }
}
