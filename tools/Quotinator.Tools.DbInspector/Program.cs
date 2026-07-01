using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Tools.DbInspector;

var parsed = ArgsParser.Parse(args);
if (parsed is null)
{
    PrintUsage();
    return 1;
}

using var connection = new SqliteConnection($"Data Source={parsed.Value.DbPath}");
IEnumerable<dynamic> rows;
try
{
    rows = connection.Query(parsed.Value.Sql);
}
catch (SqliteException ex)
{
    Console.Error.WriteLine($"Query failed: {ex.Message}");
    return 1;
}

Console.WriteLine(TableFormatter.Format(rows.Cast<IDictionary<string, object>>()));
return 0;

static void PrintUsage()
{
    Console.WriteLine("Quotinator.Tools.DbInspector — run a SQL query against a Quotinator SQLite database file.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --db <path-to-db-file> --sql \"<query>\"");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  dotnet run -- --db C:/path/to/quotinatordata.db --sql \"SELECT Name, Type, Url FROM ImportBatches WHERE IsDeleted = 0\"");
}
