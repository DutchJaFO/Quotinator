#!/usr/bin/env dotnet-script
#nullable enable
// Makes one HTTP call against a running Quotinator test container, for the three things Windows
// PowerShell 5.1 cannot do cleanly on its own. Everything else in docs/automated-testing/ uses
// Invoke-RestMethod directly — this exists for the gaps, not as a wrapper around the whole protocol.
//
// The three gaps, each measured rather than assumed (see #339's plan doc, "the suite is converted from
// bash to PowerShell"):
//
//   1. Multipart upload. Invoke-RestMethod gained -Form in PowerShell 7; 5.1 is what is installed, and
//      hand-building a multipart body in a document is not a test step anyone can read.
//   2. A status a test expects to be non-2xx. Invoke-RestMethod throws on all 88 of them across the
//      suite, and reading the problem body back out of the exception in 5.1 needs a StreamReader over
//      $_.Exception.Response — three lines of ceremony per assertion, repeated 88 times.
//   3. JSON reaching a process intact. PowerShell 5.1 strips the quotes from '{"a":"b"}' before a
//      native exe ever sees it, yielding {a:b}. Nothing here takes JSON as a command-line argument:
//      a request body arrives on stdin, and an import's settings are built from --duplicate-resolution.
//
// Output contract: the response body goes to stdout and nothing else does, so a step can pipe it
// straight into ConvertFrom-Json. The request line and the status go to stderr, so a reader following a
// document sees what was called and what answered — the same reason test-env.csx echoes its docker
// commands.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/http.csx -- --url <url> [options]
//
// Options:
//   --url    <url>        The full URL to call, including the host port (required)
//   --method <verb>       GET (default), POST, PUT, PATCH or DELETE
//   --api-key <key>       Value for the X-Api-Key header (default: smoketest, what the test
//                         environment sets). --no-key omits the header entirely, which is how a test
//                         asserts that an admin endpoint rejects an unauthenticated call
//   --no-key              Send no X-Api-Key header
//   --file   <path>       Upload this file as the multipart "file" field
//   --duplicate-resolution <mode>
//                         Adds the import "settings" multipart field as
//                         {"duplicateResolution":{"default":"<mode>"}} — the only settings shape the
//                         suite uses. Built here so no document has to put JSON on a command line
//   --json-stdin          Read the request body from stdin and send it as application/json. A leading
//                         BOM is stripped: PowerShell 5.1 adds one when it pipes a string to a native
//                         process, and it would otherwise reach the server as part of the body
//   --expect <code>       Exit 1 unless the response status is exactly this. What makes a failing step
//                         stop the run at the step that caused it
//   --wait-for <code>     Poll until the response status is exactly this, then carry on and report it
//                         like any other call. Replaces a shell `until … done` loop, and unlike one it
//                         gives up: a condition that never arrives exits 1 rather than hanging the run
//   --wait-timeout <s>    How long --wait-for keeps trying (default: 300)
//   --status              Print only the status code to stdout, not the body
//   --timeout <seconds>   Request timeout (default: 100)

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();
bool Flag(string flag) => Args.Contains(flag);

string? url = Value("--url");

if (string.IsNullOrEmpty(url))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/testing/http.csx -- --url <url> [--method <verb>] [...]");
    Environment.Exit(1);
    return;
}

string method = (Value("--method") ?? "GET").ToUpperInvariant();
string? file = Value("--file");
string? duplicateResolution = Value("--duplicate-resolution");
string? expect = Value("--expect");
string? waitFor = Value("--wait-for");

foreach ((string flag, string? given) in new[] { ("--expect", expect), ("--wait-for", waitFor) })
{
    if (given is not null && !int.TryParse(given, out _))
    {
        Console.Error.WriteLine($"{flag} takes an HTTP status code; {given} is not one.");
        Environment.Exit(1);
        return;
    }
}

if (file is not null && !File.Exists(file))
{
    Console.Error.WriteLine($"File not found: {file}");
    Environment.Exit(1);
    return;
}

int timeoutSeconds = int.TryParse(Value("--timeout"), out int parsed) ? parsed : 100;

HttpContent? content = null;

if (file is not null)
{
    MultipartFormDataContent form = [];

    // The API reads the upload as "file" and its options as "settings"; both names are the endpoint's,
    // not this script's choice.
    StreamContent fileContent = new(File.OpenRead(file));

    fileContent.Headers.ContentType = new MediaTypeHeaderValue(
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".csv"  => "text/csv",
            _       => "application/octet-stream",
        });

    form.Add(fileContent, "file", Path.GetFileName(file));

    if (duplicateResolution is not null)
        form.Add(new StringContent($"{{\"duplicateResolution\":{{\"default\":\"{duplicateResolution}\"}}}}"), "settings");

    content = form;
}
else if (Flag("--json-stdin"))
{
    // PowerShell 5.1 prefixes a BOM and appends a newline when it pipes a string into a native process.
    // The newline is harmless in JSON; the BOM is not — it reaches the server as body content and the
    // request fails to parse, which reads as an application defect rather than as a shell artefact.
    string body = Console.In.ReadToEnd().TrimStart('﻿').Trim();

    content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
}

if (waitFor is not null && content is not null)
{
    // An HttpContent can only be sent once, so a poll would silently send an empty body from the second
    // attempt onward. Nothing in the suite needs to wait on a call that carries one.
    Console.Error.WriteLine("--wait-for cannot be combined with a request body or an upload.");
    Environment.Exit(1);
    return;
}

// Not a `using` declaration: a C# script's top level does not accept them, and wrapping the rest of the
// file in a block to get one would buy nothing here — the process ends a few lines below.
HttpClient client = new() { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

HttpRequestMessage Build()
{
    HttpRequestMessage built = new(new HttpMethod(method), url) { Content = content };

    if (!Flag("--no-key"))
        built.Headers.Add("X-Api-Key", Value("--api-key") ?? "smoketest");

    return built;
}

Console.Error.WriteLine($"$ {method} {url}");

HttpResponseMessage? response = null;

if (waitFor is null)
{
    try
    {
        response = await client.SendAsync(Build());
    }
    catch (Exception ex)
    {
        // A connection that never answered is not a status code, and reporting it as one would let a
        // document assert against a container that is not running.
        Console.Error.WriteLine($"{method} {url} did not complete: {ex.Message}");
        Environment.Exit(1);
        return;
    }
}
else
{
    int waitTimeout = int.TryParse(Value("--wait-timeout"), out int given) ? given : 300;
    DateTime deadline = DateTime.UtcNow.AddSeconds(waitTimeout);

    Console.Error.WriteLine($"  waiting for {waitFor}, up to {waitTimeout}s...");

    while (DateTime.UtcNow < deadline)
    {
        try
        {
            HttpResponseMessage attempt = await client.SendAsync(Build());

            if ((int)attempt.StatusCode == int.Parse(waitFor))
            {
                response = attempt;
                break;
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        await Task.Delay(1000);
    }

    if (response is null)
    {
        // A wait that never arrives has to end the run. The shell loops this replaces did not, and one
        // of them ran ten minutes before it was stopped by hand.
        Console.Error.WriteLine($"{url} did not answer {waitFor} within {waitTimeout}s.");
        Environment.Exit(1);
        return;
    }
}

int status = (int)response.StatusCode;
string responseBody = await response.Content.ReadAsStringAsync();

Console.Error.WriteLine($"< {status} {response.ReasonPhrase}");

Console.WriteLine(Flag("--status") ? status.ToString() : responseBody);

if (expect is not null && status != int.Parse(expect))
{
    Console.Error.WriteLine($"Expected {expect}, got {status}.");
    Environment.Exit(1);
}
