using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Logs;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary><c>log</c>: counts a pattern in <c>docker logs</c> of a container, stdout and stderr both.</summary>
internal sealed class LogCommand : IProbeCommand
{
    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? container = OptionReader.Single(args, "--container");
        string? pattern = OptionReader.Single(args, "--pattern");

        if (container is null || pattern is null)
        {
            return ExpectationResult.Failed("log needs --container and --pattern");
        }

        if (!TryReadBound(args, "--at-least", out int? atLeast) || !TryReadBound(args, "--exactly", out int? exactly))
        {
            return ExpectationResult.Failed("--at-least and --exactly take a whole number");
        }

        string? logs = ReadLogs(container, out string? failure);

        return logs is null
            ? ExpectationResult.Failed(failure ?? $"could not read the logs of {container}")
            : LogCounter.Check(LogCounter.Count(logs, pattern), atLeast, exactly);
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

    /// <summary>Both streams of <c>docker logs</c>, or null with <paramref name="failure"/> set when docker failed.</summary>
    private static string? ReadLogs(string container, out string? failure)
    {
        ProcessStartInfo start = new("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("logs");
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
            return output.Result + error.Result;
        }
        catch (Win32Exception exception)
        {
            failure = $"could not start docker: {exception.Message}";
            return null;
        }
    }
}
