#nullable enable

// Sends startup-and-degradation/03's requests together, the moment the application first answers 503.
//
// The startup window lasts seconds and a separate command per request spends it, so the document missed the
// window when each request was its own command: /health and /version answered 503, and everything after
// them 200. One command that fires them at once is what keeps them all inside it (ADR 010: a document step
// is a single command, and the logic behind one is C#).
//
// Each request's body goes to <out>/<name>.body and its headers, as a JSON object, to
// <out>/<name>.headers.json, for TestProbe to read after the window has closed.
//
// Usage:
//   dotnet-script scripts/testing/startup-window.csx -- --port <port> --out <directory> [options]
//
//   --port <port>         The host port the container publishes (required)
//   --out <directory>     Where the bodies and headers are written (required)
//   --wait-timeout <s>    How long to wait for the first 503 (default: 20)
//   --api-key <key>       Value for the X-Api-Key header (default: smoketest)
//
// Exits 1 when the first answer is not a 503, or when any request answers anything but 503: the window was
// missed, and nothing written is about the startup state.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();

string? port = Value("--port");
string? outDirectory = Value("--out");

if (port is null || outDirectory is null)
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/testing/startup-window.csx -- --port <port> --out <directory>");
    Environment.Exit(1);
    return;
}

int waitTimeout = int.TryParse(Value("--wait-timeout"), out int given) ? given : 20;
string apiKey = Value("--api-key") ?? "smoketest";
string origin = $"http://localhost:{port}";

HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };

(string Name, string Method, string Path, string? Accept)[] requests =
[
    ("health", "GET", "/api/v1/health", null),
    ("version", "GET", "/api/v1/version", null),
    ("page", "GET", "/", "text/html"),
    ("reset", "POST", "/api/v1/admin/database/reset?allowNoBackup=true", "application/json"),
    ("random", "GET", "/api/v1/quotes/random", "application/json"),
];

DateTime deadline = DateTime.UtcNow.AddSeconds(waitTimeout);
int? first = null;

while (first is null && DateTime.UtcNow < deadline)
{
    try
    {
        first = (int)(await client.GetAsync($"{origin}/api/v1/health")).StatusCode;
    }
    catch (HttpRequestException)
    {
        // Kestrel has not bound yet.
        await Task.Delay(100);
    }
}

if (first != (int)HttpStatusCode.ServiceUnavailable)
{
    Console.Error.WriteLine(first is null
        ? $"{origin} did not answer within {waitTimeout}s."
        : $"The first answer was {first}, not 503: the startup window was missed.");
    Environment.Exit(1);
    return;
}

Directory.CreateDirectory(outDirectory);

async Task<int> Send((string Name, string Method, string Path, string? Accept) request)
{
    using HttpRequestMessage message = new(new HttpMethod(request.Method), origin + request.Path);
    message.Headers.Add("X-Api-Key", apiKey);

    if (request.Accept is not null)
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(request.Accept));

    using HttpResponseMessage response = await client.SendAsync(message);

    Dictionary<string, string> headers = response.Headers.Concat(response.Content.Headers)
        .ToDictionary(header => header.Key, header => string.Join(", ", header.Value));

    System.Text.UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
    File.WriteAllText(Path.Combine(outDirectory!, $"{request.Name}.body"), await response.Content.ReadAsStringAsync(), utf8);
    File.WriteAllText(Path.Combine(outDirectory!, $"{request.Name}.headers.json"), JsonSerializer.Serialize(headers), utf8);

    int status = (int)response.StatusCode;
    Console.WriteLine($"{request.Name}: {request.Method} {request.Path} = {status}");
    return status;
}

int[] statuses = await Task.WhenAll(requests.Select(Send));

if (statuses.Any(status => status != (int)HttpStatusCode.ServiceUnavailable))
{
    Console.Error.WriteLine("Not every request answered 503: the startup window was missed.");
    Environment.Exit(1);
}
