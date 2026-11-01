using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Files;
using Quotinator.Tools.TestProbe.Logs;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>log</c>: counts a pattern in <c>docker logs</c> of a container, stdout and stderr both. With
/// <c>--mark</c> it instead records the newest line's timestamp, and with <c>--since-mark</c> it counts
/// only lines stamped after a recorded one, so a document can count what was logged since its previous
/// read without holding anything in a shell variable.
/// </summary>
internal sealed class LogCommand : IProbeCommand
{
    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? container = OptionReader.Single(args, "--container");
        string? markOut = OptionReader.Single(args, "--mark");
        string? sinceMark = OptionReader.Single(args, "--since-mark");

        if (container is null)
        {
            return ExpectationResult.Failed("log needs --container");
        }

        DateTimeOffset since = DateTimeOffset.MinValue;

        if (sinceMark is not null && !TryReadMark(sinceMark, out since))
        {
            return ExpectationResult.Failed($"--since-mark {sinceMark} is not a mark file written by --mark");
        }

        string? logs = ReadLogs(container, out string? failure);

        if (logs is null)
        {
            return ExpectationResult.Failed(failure ?? $"could not read the logs of {container}");
        }

        if (markOut is not null)
        {
            DateTimeOffset last = LogCounter.LastTimestamp(logs);
            FileOperations.WriteText(markOut, last.ToString("O", CultureInfo.InvariantCulture));
            return ExpectationResult.Passed($"marked {container} at {last:O} in {markOut}");
        }

        string? pattern = OptionReader.Single(args, "--pattern");

        if (pattern is null)
        {
            return ExpectationResult.Failed("log needs --pattern, or --mark");
        }

        if (!TryReadBound(args, "--at-least", out int? atLeast) || !TryReadBound(args, "--exactly", out int? exactly))
        {
            return ExpectationResult.Failed("--at-least and --exactly take a whole number");
        }

        return LogCounter.Check(LogCounter.Count(LogCounter.LinesAfter(logs, since), pattern), atLeast, exactly);
    }

    private static bool TryReadMark(string path, out DateTimeOffset mark)
    {
        mark = DateTimeOffset.MinValue;

        return File.Exists(path)
            && DateTimeOffset.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out mark);
    }

    private static bool TryReadBound(IReadOnlyList<string> args, string option, out int? bound)
    {
        string? text = OptionReader.Single(args, option);
        bound = null;

        if (text is null)
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        bound = value;
        return true;
    }

    /// <summary>
    /// Both streams of <c>docker logs --timestamps</c>, every line prefixed with the daemon's own timestamp,
    /// or null with <paramref name="failure"/> set when docker failed.
    /// </summary>
    private static string? ReadLogs(string container, out string? failure)
    {
        ProcessStartInfo start = new("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("logs");
        start.ArgumentList.Add("--timestamps");
        start.ArgumentList.Add(container);

        try
        {
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                failure = $"docker logs {container} exited {process.ExitCode}: {error.Result.Trim()}";
                return null;
            }

            failure = null;
            return output.Result + "\n" + error.Result;
        }
        catch (Win32Exception exception)
        {
            failure = $"could not start docker: {exception.Message}";
            return null;
        }
    }
}
