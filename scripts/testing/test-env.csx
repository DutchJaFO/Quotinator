#!/usr/bin/env dotnet-script
#nullable enable
// Creates and destroys the Docker environment a single automated test owns.
//
// Every test in docs/automated-testing/ runs against its own container and its own volume, so that any
// two can run at the same time without reaching each other's state (see that suite's index, "Environment
// profiles"). That means 43 documents would otherwise each carry the same eight-line `docker run` block,
// and changing how a test environment is built would mean editing all of them. This script is the one
// place that block lives; a document names its own container and port and nothing else.
//
// It echoes every docker command before running it, so a reader following a document still sees exactly
// what was executed without opening this file.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/test-env.csx -- create  --name <name> --port <port> [options]
//   dotnet-script scripts/testing/test-env.csx -- reenter --name <name> --port <port> [options]
//   dotnet-script scripts/testing/test-env.csx -- destroy --name <name> [--bind <host-dir>]
//
// create starts from an empty volume; reenter runs the same recipe against data that is already there
// — a second startup, or an upgrade to a different --image over a database a prior one wrote. They are
// two commands rather than a flag on one because a step doing the second thing should say so, instead
// of a reader inferring it from which mount type happens to be in use. (With --bind the distinction is
// invisible to this script either way: a bind directory belongs to the document, and neither command
// touches it.)
//
// create and reenter options:
//   --name  <name>        Container name; its volume is <name>-data (required)
//   --port  <port>        Host port published to the container's 8080. Omit it for a container
//                         nothing connects to over HTTP — one waited on by its own log line, say.
//                         Omitting it implies --no-wait, since there is nothing to poll.
//   --image <ref>         Image to run (default: quotinator:local)
//   --bind  <dir>         Bind-mount this directory at /data instead of a named volume, passed to
//                         docker verbatim. Creating and removing it is the document's job, not this
//                         script's: a POSIX path like /tmp/x resolves inside the Docker VM while a
//                         relative one resolves on the host, and only the document knows which it
//                         means. Guessing wrong binds a different filesystem and the test reads an
//                         empty database that looks exactly like a passing check.
//   --env   <K=V>         Extra environment variable; repeatable.
//   --read-only           Run with a read-only root filesystem, for a test whose subject is what the
//                         application does when it cannot write.
//   --no-wait             Skip the readiness poll — for a container that publishes no port, or one
//                         expected to degrade rather than become healthy.
//   --wait-listening      Poll for any answer rather than a healthy one, for a degraded scenario
//                         where 503 is the expected outcome.
//
// destroy options:
//   --name  <name>        Container name; removes it and its <name>-data volume (required)
//   --bind                Declares the container used a bind mount, so there is no named volume to
//                         remove. The directory itself is the document's to clean up.

using System.Net.Http;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();
bool Flag(string flag) => Args.Contains(flag);

List<string> Values(string flag)
{
    List<string> found = [];
    for (int i = 0; i < Args.Count - 1; i++)
        if (Args[i] == flag) found.Add(Args[i + 1]);
    return found;
}

// quiet suppresses output for the pre-clean removals, where "no such container/volume" is the normal
// case rather than a problem — printing it invites a reader to treat a clean start as an error.
int Run(string arguments, bool ignoreFailure = false, bool quiet = false)
{
    Console.WriteLine($"$ docker {arguments}");

    System.Diagnostics.ProcessStartInfo startInfo = new("docker", arguments)
    {
        UseShellExecute        = false,
        RedirectStandardError  = quiet,
        RedirectStandardOutput = quiet,
    };

    using System.Diagnostics.Process process = new() { StartInfo = startInfo };

    process.Start();

    if (quiet)
    {
        process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
    }

    process.WaitForExit();

    if (process.ExitCode != 0 && !ignoreFailure)
    {
        Console.Error.WriteLine($"docker {arguments.Split(' ')[0]} failed with exit code {process.ExitCode}.");
        Environment.Exit(process.ExitCode);
    }

    return process.ExitCode;
}

string command = Args.FirstOrDefault() ?? "";
string? name   = Value("--name");

if (string.IsNullOrEmpty(name) || command is not ("create" or "reenter" or "destroy"))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/testing/test-env.csx -- create|reenter|destroy --name <name> [...]");
    Environment.Exit(1);
    return;
}

string volume = $"{name}-data";
string? bind  = Value("--bind");

if (command == "destroy")
{
    Run($"rm -f {name}", ignoreFailure: true);

    // A bind-mounted test never creates the named volume, so removing it would always fail — and a
    // failure here would be reported as the test's, not as this script's.
    if (bind is null) Run($"volume rm {volume}", ignoreFailure: true, quiet: true);

    Console.WriteLine(bind is null
        ? $"Removed {name}."
        : $"Removed {name}. Its bind directory is this test's own to delete.");
    return;
}

string? port = Value("--port");

// No port is a legitimate shape — a container waited on by its own log line rather than by HTTP —
// so requiring one would force a document to invent a number it never uses, and that number would
// then contradict its own Determinism.
if (port is not null && (!int.TryParse(port, out int portNumber) || portNumber is < 1 or > 65535))
{
    Console.Error.WriteLine($"{port} is not a TCP port — the maximum is 65535.");
    Environment.Exit(1);
    return;
}

string image = Value("--image") ?? "quotinator:local";

// The container always goes, on both commands: reenter is about keeping the *data*, and a container
// still holding the database open would stop the new one from reading it.
Run($"rm -f {name}", ignoreFailure: true, quiet: true);

bool fresh = command == "create";

string mount;

if (bind is not null)
{
    // Verbatim — see the --bind note in the header. Resolving or creating this path would change
    // which filesystem it names.
    mount = $"-v {bind}:/data";
}
else
{
    // Only on create: a volume left by an earlier run would make "fresh" a lie, and the test reading
    // it would report a failure that belongs to its predecessor. reenter is the case where that
    // earlier run is the point.
    if (fresh) Run($"volume rm {volume}", ignoreFailure: true, quiet: true);

    mount = $"-v {volume}:/data";
}

List<string> settings =
[
    "-e Quotinator__DataDir=/data",
    "-e Quotinator__AdminApiKey=smoketest",
    "-e Quotinator__AutoPurgeBundledImportActions=true",
];

settings.AddRange(Values("--env").Select(e => $"-e {e}"));

string publish  = port is null ? "" : $"-p {port}:8080 ";
string readOnly = Flag("--read-only") ? "--read-only " : "";

Run($"run -d --name {name} {publish}{readOnly}{mount} {string.Join(" ", settings)} {image}");

if (port is null)
{
    Console.WriteLine($"Started {name}, publishing no port — wait on its log.");
    return;
}

if (Flag("--no-wait"))
{
    Console.WriteLine($"Started {name} on {port} (not waited on).");
    return;
}

bool listeningOnly = Flag("--wait-listening");
string health      = $"http://localhost:{port}/api/v1/health";

HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };

Console.WriteLine(listeningOnly
    ? $"Waiting for {name} to answer on {port}..."
    : $"Waiting for {name} to report healthy on {port}...");

DateTime deadline = DateTime.UtcNow.AddMinutes(5);

while (DateTime.UtcNow < deadline)
{
    try
    {
        HttpResponseMessage response = await client.GetAsync(health);

        // A degraded container answers 503 by design, and polling for 200 there would loop until the
        // deadline. Which of the two a test wants is its own choice, not this script's.
        if (listeningOnly || response.IsSuccessStatusCode)
        {
            Console.WriteLine($"{name} is up on {port}.");
            return;
        }
    }
    catch (HttpRequestException) { }
    catch (TaskCanceledException) { }

    await Task.Delay(1000);
}

Console.Error.WriteLine($"{name} did not come up on {port} within 5 minutes.");
Environment.Exit(1);
