using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Tools.DbInspector;

if (args.Length > 0 && args[0] == "integrity")
{
    string? integrityDb = ArgsParser.ParseDb(args);
    if (integrityDb is null)
    {
        PrintUsage();
        return 1;
    }

    return await IntegrityCommand.RunAsync(integrityDb, Console.Out);
}

QueryArgs? parsed = ArgsParser.Parse(args);
if (parsed is null)
{
    PrintUsage();
    return 1;
}

using SqliteConnection connection = new(ConnectionStrings.BuildReadOnly(parsed.Value.DbPath));
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

List<IDictionary<string, object>> table = [.. rows.Cast<IDictionary<string, object>>()];
Console.WriteLine(TableFormatter.Format(table));

if (parsed.Value.JsonOut is string jsonOut)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut))!);
    File.WriteAllText(jsonOut, JsonRowWriter.ToJson(table));
    Console.WriteLine($"Wrote {table.Count} row(s) to {jsonOut}");
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("Quotinator.Tools.DbInspector — run a SQL query against a Quotinator SQLite database file.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --db <path-to-db-file> --sql \"<query>\" [--json-out <path>]");
    Console.WriteLine("  dotnet run -- integrity --db <path-to-db-file>");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -- --db C:/path/to/quotinatordata.db --sql \"SELECT Name, Type, Url FROM ImportBatches WHERE IsDeleted = 0\"");
    Console.WriteLine("  dotnet run -- integrity --db C:/path/to/quotinatordata.db");
}
