using System.Globalization;
using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Results;
using Quotinator.Tools.TestProbe.Waiting;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>wait-field</c>: polls a JSON endpoint until a field holds one of the given values. The only
/// observation is the body finally read, so the output can be piped into <c>json</c>.
/// </summary>
internal sealed class WaitFieldCommand : IProbeCommand
{
    private const int DefaultTimeoutSeconds = 180;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? url = OptionReader.Single(args, "--url");
        string? field = OptionReader.Single(args, "--field");
        string? values = OptionReader.Single(args, "--in");
        string timeoutText = OptionReader.Single(args, "--timeout") ?? DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture);

        if (url is null || field is null || values is null)
        {
            return ExpectationResult.Failed("wait-field needs --url, --field and --in");
        }

        if (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out int timeoutSeconds))
        {
            return ExpectationResult.Failed($"--timeout '{timeoutText}' is not a whole number of seconds");
        }

        using HttpClient client = new() { Timeout = RequestTimeout };

        FieldWaitResult result = FieldWaiter.Wait(
            () => Fetch(client, url),
            field,
            values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            TimeSpan.FromSeconds(timeoutSeconds),
            () => DateTimeOffset.UtcNow,
            () => Thread.Sleep(Pause));

        string[] body = result.Body is null ? [] : [result.Body];

        return result.Reached
            ? new ExpectationResult(body, [])
            : new ExpectationResult(body, [$"{field} did not reach one of {values} within {timeoutSeconds}s; last value: {result.LastValue ?? "(never read)"}"]);
    }

    /// <summary>The response body whatever its status code, or null when nothing answered.</summary>
    private static string? Fetch(HttpClient client, string url)
    {
        try
        {
            using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
