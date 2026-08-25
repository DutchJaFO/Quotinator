#!/usr/bin/env dotnet-script
#nullable enable
// Writes a copy of a CSV with exactly one cell replaced, for a test that needs a deliberately
// malformed input row.
//
// docs/automated-testing/import-and-staged-actions/13-bulk-decide-via-file-export-import.md needs one
// data row of an export to carry an unusable Decision value, so it can assert that a single bad row
// never aborts the rest of the file. Producing that copy is a text transformation, and this repository
// does not write those in shell (see ADR 010) — that document previously described the edit in prose
// instead, which is a step no unattended run can perform.
//
// The column is named rather than numbered: the header is read and matched by name, so a new column
// appearing in the export cannot silently shift the edit onto a different field.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/corrupt-csv-cell.csx -- --in <path> --out <path>
//                                                        --column <header name> --value <replacement>
//                                                        [--row <1-based data row, default 1>]

using System.IO;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();

string? input   = Value("--in");
string? output  = Value("--out");
string? column  = Value("--column");
string? replace = Value("--value");
string  rowText = Value("--row") ?? "1";

if (input is null || output is null || column is null || replace is null || !int.TryParse(rowText, out int dataRow) || dataRow < 1)
{
    Console.Error.WriteLine(
        "Usage: dotnet-script scripts/testing/corrupt-csv-cell.csx -- --in <path> --out <path> " +
        "--column <header name> --value <replacement> [--row <1-based data row>]");
    Environment.Exit(1);
    return;
}

if (!File.Exists(input))
{
    Console.Error.WriteLine($"{input} does not exist.");
    Environment.Exit(1);
    return;
}

string[] lines = File.ReadAllLines(input);

if (lines.Length < dataRow + 1)
{
    Console.Error.WriteLine($"{input} has {lines.Length - 1} data row(s); row {dataRow} was requested.");
    Environment.Exit(1);
    return;
}

List<string> header = [.. lines[0].Split(',')];
int index = header.FindIndex(h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));

if (index < 0)
{
    Console.Error.WriteLine($"No column named '{column}'. Header is: {lines[0]}");
    Environment.Exit(1);
    return;
}

// Split on commas only — the export's own writer quotes nothing, and a quote-aware parse here would
// invent a format the file does not use.
string[] cells = lines[dataRow].Split(',');

if (cells.Length <= index)
{
    Console.Error.WriteLine($"Data row {dataRow} has {cells.Length} cell(s); column '{column}' is at index {index}.");
    Environment.Exit(1);
    return;
}

string original = cells[index];
cells[index]    = replace;
lines[dataRow]  = string.Join(",", cells);

File.WriteAllLines(output, lines);

Console.WriteLine($"{output}: row {dataRow}, column '{column}' (index {index}) '{original}' -> '{replace}'");
Console.WriteLine($"actionId on that row: {lines[dataRow].Split(',')[0]}");
