using System.Text.Json;
using Quotinator.Tools.TestProbe.Json;

namespace Quotinator.Tools.TestProbe.Waiting;

/// <summary>Polls a JSON source until a field holds one of a set of values.</summary>
internal static class FieldWaiter
{
    /// <summary>
    /// Reads <paramref name="fetchBody"/> until the field at <paramref name="field"/> holds one of
    /// <paramref name="values"/>, or until <paramref name="timeout"/> has passed by <paramref name="now"/>.
    /// A null body (nothing answered) or a body that is not JSON is read again, not treated as an answer.
    /// </summary>
    /// <param name="fetchBody">Returns the current body, or null when nothing answered.</param>
    /// <param name="field">The dotted path of the field.</param>
    /// <param name="values">The values that end the wait, compared exactly.</param>
    /// <param name="timeout">How long to keep trying.</param>
    /// <param name="now">The current time.</param>
    /// <param name="pause">Waits between reads.</param>
    internal static FieldWaitResult Wait(
        Func<string?> fetchBody,
        string field,
        IReadOnlyCollection<string> values,
        TimeSpan timeout,
        Func<DateTimeOffset> now,
        Action pause)
    {
        DateTimeOffset deadline = now() + timeout;
        string? lastBody = null;
        string? lastValue = null;

        while (true)
        {
            string? body = fetchBody();

            if (body is not null)
            {
                lastBody = body;
                string? value = ReadField(body, field);

                if (value is not null)
                {
                    lastValue = value;

                    if (values.Contains(value, StringComparer.Ordinal))
                    {
                        return new FieldWaitResult(true, body, value);
                    }
                }
            }

            if (now() >= deadline)
            {
                return new FieldWaitResult(false, lastBody, lastValue);
            }

            pause();
        }
    }

    private static string? ReadField(string body, string field)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return JsonPathReader.TryRead(document.RootElement, field, out JsonElement value) ? JsonPathReader.Render(value) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
