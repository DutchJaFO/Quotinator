#!/usr/bin/env dotnet-script
#nullable enable
// Writes a PNG of a page served by a running Quotinator test container, so a document that asserts
// something about rendering can also produce the picture a person checks it against.
//
// docs/automated-testing/README.md's "Every test must be able to run unattended" already allows a
// screenshot as evidence, provided the assertion beside it is machine-checkable. Until this script
// existed there was no way to honour that half: the suite could read the DOM but the picture only ever
// lived in whichever tool happened to be driving the browser, so it could not be attached to a run,
// compared against a later one, or shown to anyone. Found live in #308, whose verification asked for
// "one confirmed rendering per type, on both surfaces" and had nowhere to put them.
//
// It drives headless Edge (or Chrome) over the DevTools Protocol directly — no NuGet package, no
// driver binary to install, and nothing added to the solution's dependency footprint. Both browsers
// ship on the machines this suite runs on.
//
// Two things make it a capture tool rather than a screenshot wrapper:
//
//   1. --script-stdin runs JavaScript in the page before the shot, so a document can put the page into
//      the exact state it is asserting about: open a dialog, expand a detail, hide the rows other than
//      the one being captured. The script's own return value is printed to stdout, so the same call
//      that produces the picture also produces the assertion's evidence — one state, read and shot,
//      with no chance of the two describing different moments.
//   2. If that script returns an object carrying x/y/width/height, it is used as the capture region.
//      An element's own getBoundingClientRect() therefore crops the shot to that element, mechanically,
//      rather than a person choosing pixel bounds by eye.
//
// The script arrives on stdin, not as an argument, for the reason http.csx documents: PowerShell 5.1
// strips quotes from a string on its way to a native process, and JavaScript without quotes is not
// JavaScript.
//
// Output contract: the evaluated script's result goes to stdout as JSON and nothing else does, so a
// step can pipe it into ConvertFrom-Json. Progress and the written path go to stderr, matching
// http.csx and test-env.csx.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/capture-page.csx -- --url <url> --out <path.png> [options]
//   '<js>' | dotnet-script scripts/testing/capture-page.csx -- --url <url> --out <path.png> --script-stdin
//
// Options:
//   --url     <url>       Page to capture, including the host port (required)
//   --out     <path>      Where to write the PNG; parent directories are created (required)
//   --width   <px>        Viewport width (default: 1280)
//   --height  <px>        Viewport height (default: 900)
//   --wait-ms <ms>        Settle time after load before the script runs (default: 2000). Blazor Server
//                         needs its circuit up before anything interactive is on the page
//   --script-stdin        Read JavaScript from stdin, evaluate it after the wait, print its result, and
//                         use an {x,y,width,height} return value as the capture region. The body runs
//                         inside an async function, so `await` works and the value comes from `return`
//   --browser <name>      edge (default) or chrome
//   --full-page           Capture the whole scrollable page rather than the viewport. Ignored when the
//                         script returns a region

using System.Diagnostics;
using System.Threading;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();
bool Flag(string flag) => Args.Contains(flag);

string? url = Value("--url");
string? outPath = Value("--out");
if (url is null || outPath is null)
{
    Console.Error.WriteLine("usage: capture-page.csx -- --url <url> --out <path.png> [--width N] [--height N] [--wait-ms N] [--script-stdin] [--browser edge|chrome] [--full-page]");
    return 1;
}

int width = int.TryParse(Value("--width"), out int w) ? w : 1280;
int height = int.TryParse(Value("--height"), out int h) ? h : 900;
int waitMs = int.TryParse(Value("--wait-ms"), out int ms) ? ms : 2000;
bool fullPage = Flag("--full-page");

string[] candidates = (Value("--browser") ?? "edge").ToLowerInvariant() switch
{
    "chrome" =>
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    ],
    _ =>
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    ]
};

string? browser = candidates.FirstOrDefault(File.Exists);
if (browser is null)
{
    Console.Error.WriteLine($"No browser found. Looked for:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", candidates)}");
    return 1;
}

string? script = null;
if (Flag("--script-stdin"))
{
    script = await Console.In.ReadToEndAsync();
    // PowerShell 5.1 prefixes a BOM when it pipes a string to a native process; it would be a syntax
    // error at the top of the evaluated source. Same fix as http.csx's --json-stdin.
    script = script.TrimStart('\uFEFF').Trim();
    if (script.Length == 0) script = null;
}

// A port the caller did not choose, so two captures in the same document never collide.
int port = new Random().Next(9500, 9899);
string profile = Path.Combine(Path.GetTempPath(), $"qt-capture-{port}");

var startInfo = new ProcessStartInfo(browser)
{
    UseShellExecute = false,
    RedirectStandardError = true,
    RedirectStandardOutput = true
};
foreach (string arg in new[]
{
    "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
    "--no-default-browser-check", "--disable-extensions", "--disable-background-networking",
    $"--remote-debugging-port={port}", $"--user-data-dir={profile}",
    $"--window-size={width},{height}", "about:blank"
})
{
    startInfo.ArgumentList.Add(arg);
}

Console.Error.WriteLine($"$ {Path.GetFileName(browser)} --headless --remote-debugging-port={port} {url}");
Process proc = Process.Start(startInfo)!;

int exitCode = 0;
try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    string? pageSocket = null;
    for (int attempt = 0; attempt < 60 && pageSocket is null; attempt++)
    {
        try
        {
            string listing = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list");
            pageSocket = JsonNode.Parse(listing)?.AsArray()
                .FirstOrDefault(t => (string?)t?["type"] == "page")?["webSocketDebuggerUrl"]?.GetValue<string>();
        }
        catch (HttpRequestException) { }
        if (pageSocket is null) await Task.Delay(250);
    }

    if (pageSocket is null)
    {
        Console.Error.WriteLine($"The browser never offered a page target on port {port}.");
        return 1;
    }

    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(pageSocket), CancellationToken.None);

    int nextId = 0;
    async Task<JsonNode?> Call(string method, object? parameters = null)
    {
        int id = ++nextId;
        string request = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } });
        await ws.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, true, CancellationToken.None);

        // Everything the page reports arrives on the same socket, so read past the events until the
        // reply carrying this call's own id shows up.
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await ws.ReceiveAsync(buffer, CancellationToken.None);
                message.Write(buffer, 0, received.Count);
            }
            while (!received.EndOfMessage);

            JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()));
            if ((int?)node?["id"] == id)
            {
                if (node?["error"] is JsonNode error)
                {
                    throw new InvalidOperationException($"{method} failed: {error.ToJsonString()}");
                }
                return node?["result"];
            }
        }
    }

    await Call("Page.enable");
    await Call("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });
    await Call("Page.navigate", new { url });
    await Task.Delay(waitMs);

    object? clip = null;
    if (script is not null)
    {
        // Wrapped in an async function so a script can `await` — opening a dialog and waiting for it
        // to render is most of what a capture script does, and Runtime.evaluate rejects a bare
        // top-level await. The cost is that the script states its own result with `return`, rather
        // than trailing off in an expression the way a browser console would.
        JsonNode? evaluated = await Call("Runtime.evaluate", new
        {
            expression = $"(async () => {{{Environment.NewLine}{script}{Environment.NewLine}}})()",
            awaitPromise = true,
            returnByValue = true,
            userGesture = true
        });

        if (evaluated?["exceptionDetails"] is JsonNode thrown)
        {
            Console.Error.WriteLine($"The page script threw: {thrown["exception"]?["description"] ?? thrown.ToJsonString()}");
            return 1;
        }

        JsonNode? value = evaluated?["result"]?["value"];
        Console.Out.WriteLine(value?.ToJsonString() ?? "null");

        // An element's own getBoundingClientRect() is the intended shape here, so a document crops to
        // an element by returning one rather than by naming pixels.
        if (value?["width"] is not null && value["height"] is not null)
        {
            clip = new
            {
                x = (double?)value["x"] ?? 0,
                y = (double?)value["y"] ?? 0,
                width = (double)value["width"]!,
                height = (double)value["height"]!,
                scale = 1
            };
        }
    }

    JsonNode? shot = clip is not null
        ? await Call("Page.captureScreenshot", new { format = "png", clip, captureBeyondViewport = true })
        : await Call("Page.captureScreenshot", new { format = "png", captureBeyondViewport = fullPage });

    string? encoded = shot?["data"]?.GetValue<string>();
    if (encoded is null)
    {
        Console.Error.WriteLine("The browser returned no image data.");
        return 1;
    }

    string full = Path.GetFullPath(outPath);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllBytes(full, Convert.FromBase64String(encoded));
    Console.Error.WriteLine($"Wrote {full} ({new FileInfo(full).Length:n0} bytes)");

    // Ask the browser to close rather than killing it. A page torn down with the process still
    // attached leaves its Blazor circuit half-open, and the server logs a WebSocketException for every
    // one — measured 2026-09-24: thirteen captures against one container produced 72 of them, against
    // 0 before any capture ran. That is log noise rather than a fault, but it is noise this suite
    // creates for itself, and it would sit in the middle of the same log the document reads for real
    // exceptions.
    try { await Call("Browser.close"); } catch (Exception) { }
    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch (Exception) { }
    proc.WaitForExit(5000);
}
finally
{
    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
    try { proc.WaitForExit(5000); } catch { }
    try { if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true); } catch { }
    proc.Dispose();
}

return exitCode;
