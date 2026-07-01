using Dapper;
using Microsoft.Data.Sqlite;

var options = ParseArgs(args);
if (options is null)
{
    PrintUsage();
    return 1;
}

using var connection = new SqliteConnection($"Data Source={options.Value.DbPath}");
IEnumerable<dynamic> rows;
try
{
    rows = connection.Query(options.Value.Sql);
}
catch (SqliteException ex)
{
    Console.Error.WriteLine($"Query failed: {ex.Message}");
    return 1;
}

PrintTable(rows);
return 0;

static (string DbPath, string Sql)? ParseArgs(string[] args)
{
    string? dbPath = null;
    string? sql    = null;

    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--db")  dbPath = args[i + 1];
        if (args[i] == "--sql") sql    = args[i + 1];
    }

    return dbPath is null || sql is null ? null : (dbPath, sql);
}

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

static void PrintTable(IEnumerable<dynamic> rows)
{
    var rowList = rows.Cast<IDictionary<string, object>>().ToList();
    if (rowList.Count == 0)
    {
        Console.WriteLine("(no rows)");
        return;
    }

    var columns    = rowList[0].Keys.ToList();
    var widths     = columns.ToDictionary(c => c, c => c.Length);
    var cellValues = rowList
        .Select(row => columns.ToDictionary(c => c, c => row[c]?.ToString() ?? "NULL"))
        .ToList();

    foreach (var row in cellValues)
        foreach (var col in columns)
            widths[col] = Math.Max(widths[col], row[col].Length);

    Console.WriteLine(string.Join("  ", columns.Select(c => c.PadRight(widths[c]))));
    foreach (var row in cellValues)
        Console.WriteLine(string.Join("  ", columns.Select(c => row[c].PadRight(widths[c]))));
}
