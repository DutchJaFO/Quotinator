#!/usr/bin/env dotnet-script
#nullable enable
// Fills a running container's data volume until less than one copy of a given file fits on it, so the
// backup of that file runs out of room partway through (#348).
//
// Exists because the condition has to be derived, not written down: the free space and the database's
// size both drift with the dataset and with the size of the write-ahead log, and two figures calibrated by
// hand in docs/automated-testing/backup/03-refuses-a-reset-when-the-disk-fills-during-the-backup.md went
// stale twice. This reads both inside the container, writes a filler leaving half the file's size free,
// and reads the free space again, so the precondition is measured on every run.
//
// It echoes every docker command before running it, as test-env.csx does, so a reader sees what ran.
//
// Developer-only: never referenced by src/Quotinator.Api, never built into the Docker image.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/fill-volume.csx -- --container <name> --file <path-in-container>
//
// Options:
//   --container <name>   The running container whose volume is filled (required)
//   --file      <path>   The file a copy of which must no longer fit, e.g. /data/quotinatordata.db (required)
//   --filler    <path>   Where the filler is written (default: beside --file, named filler.bin)
//
// Prints the free space left, the file's size, and roomForACopy. Exits 0 when less than one copy of the
// file fits afterwards, 1 when it still does (the fill did not take), 2 on a usage or docker error.

using System.Diagnostics;

string? Value(string name)
{
    int i = Args.IndexOf(name);
    return i >= 0 && i + 1 < Args.Count ? Args[i + 1] : null;
}

string? container = Value("--container");
string? file = Value("--file");
if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(file))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/testing/fill-volume.csx -- --container <name> --file <path-in-container> [--filler <path>]");
    return 2;
}

string directory = file[..file.LastIndexOf('/')];
string filler = Value("--filler") ?? $"{directory}/filler.bin";

(int exitCode, string output) Docker(string arguments)
{
    Console.WriteLine($"$ docker {arguments}");
    ProcessStartInfo startInfo = new("docker", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using Process process = Process.Start(startInfo)!;
    string output = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output);
}

// df -k prints a header line, then one line per filesystem; the fourth column is the space available.
long? AvailableKb()
{
    (int code, string output) = Docker($"exec {container} df -k {directory}");
    if (code != 0) return null;
    string last = output.Trim().Split('\n')[^1];
    string[] columns = last.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return columns.Length > 3 && long.TryParse(columns[3], out long kb) ? kb : null;
}

long? SizeKb()
{
    (int code, string output) = Docker($"exec {container} du -k {file}");
    if (code != 0) return null;
    string first = output.Trim().Split('\t', ' ')[0];
    return long.TryParse(first, out long kb) ? kb : null;
}

long? available = AvailableKb();
long? size = SizeKb();
if (available is null || size is null)
{
    Console.Error.WriteLine("Could not read the free space or the file's size inside the container.");
    return 2;
}

long fill = available.Value - size.Value / 2;
if (fill > 0)
    Docker($"exec {container} dd if=/dev/zero of={filler} bs=1024 count={fill}");

long? left = AvailableKb();
if (left is null)
{
    Console.Error.WriteLine("Could not read the free space after filling.");
    return 2;
}

bool roomForACopy = left.Value >= size.Value;
Console.WriteLine($"leftKB={left} fileKB={size} roomForACopy={roomForACopy}");
return roomForACopy ? 1 : 0;
