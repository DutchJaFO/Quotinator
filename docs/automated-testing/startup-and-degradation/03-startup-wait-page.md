# During initialisation Kestrel answers, refusing with 503 rather than appearing dead or claiming success

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** no
**Traces to:** #280, #419

## Preconditions

**Beyond the profile.** This test starts its own container (`qt-startup-03` / `qt-startup-03-data`) because it
must issue requests *during* the startup window, before the profile's readiness poll would return: the
profile hands back an already-healthy app, which is precisely the state this test cannot observe from.
The volume must be new for the same reason it is new in the profile: against an already-seeded volume
startup completes almost immediately and the window this test observes does not exist.

## Determinism

**This test deliberately observes a transient state.** The first set of requests must land *before*
seeding completes, so polling for readiness would defeat it.

**All five requests go together, in one command.** The window lasts seconds, and a separate command per
request spends it: measured when this document first ran that way, `/health` and `/version` answered `503`
and everything after them `200`. [`startup-window.csx`](../../../scripts/testing/startup-window.csx) waits for
the first `503` and then sends them at once. Kestrel needs a moment to bind, and until it does the port
refuses connections, so the wait retries, up to a short limit.

**Requests first, assertions afterwards.** Step 1 only sends requests and saves what comes back; step 2
reads the saved answers, after the window has closed.

**The script is compiled before the container starts.** `dotnet script` compiles a changed script on first
use, which takes longer than the window. Its first command only prints its usage and exits `1`, so the
compile is spent before anything is being timed.

**What varies:** whether the requests land inside the startup window at all, which depends on how fast the
machine seeds. The window cannot be pinned from outside the application. The variability is bounded in the
safe direction: a missed window fails the command rather than passing it, so the result is a false negative
and never a false positive. If it starts failing spuriously, the fix is a larger seed or a slower start, not
a longer limit.

The wait in step 3 is an ordinary readiness wait and polls.

## Steps

### 1. Send the requests during initialisation, before seeding completes

```
dotnet script scripts/testing/startup-window.csx
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-03-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-03 --port 18403 --image quotinator:local --no-wait
dotnet script scripts/testing/startup-window.csx -- --port 18403 --out C:\repos\Quotinator\.claude\temp\qt-startup-03-out
```

**Expected:** the first command prints its usage and exits `1`. The last exits `0`, printing `503` for each
of `health`, `version`, `page`, `reset` and `random`. Never a hang, never a raw error, and never a `200` for
a page the caller did not ask for.

**On failure:** a `200` means seeding finished before the requests landed: the window was missed rather
than the wait page being broken (see Determinism). Destroy the container and re-run; do not lengthen the
limit.

### 2. Read what the window answered

The `page` request is a browser's, sending `Accept: text/html`. `reset` and `random` are an API client's,
sending `Accept: application/json`, in the same window, from the same container.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\health.body --equals status=starting
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\version.body --contains "title=starting up"
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\page.headers.json --contains Content-Type=text/html
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-startup-03-out\page.body "<meta[^>]+http-equiv\s*=\s*.refresh" --expect-no-text C:\repos\Quotinator\.claude\temp\qt-startup-03-out\page.body "(src|href)\s*=\s*.https?://"
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\reset.headers.json --contains Content-Type=application/problem+json --not-empty Retry-After
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\random.headers.json --contains Content-Type=application/problem+json
```

**Expected:** every command exits `0`. `/health` reads `starting`. `/version` carries the problem-details
*starting up* title rather than a version. The page is HTML, refreshes itself and pulls in nothing from
outside. The reset carries `application/problem+json` and a `Retry-After`; the JSON GET carries
`application/problem+json` too, not the HTML page.

**`/version` is gated like everything else** (#419): it cannot report complete version information or a
ready state before startup finishes, and a caller could otherwise reach for it as a readiness endpoint in
`/health`'s place.

**The reset's refusal is the defect #419 exists to remove.** Before it, this exact reset answered **`200`
with `text/html`**: an API caller was told its reset had succeeded, handed a web page, and nothing was reset.

**The two bodies in one window are the point.** One caller asked for a page and got one; another asked for
data and got problem details. Either assertion alone passes against a middleware that serves one body to
everybody. `Accept: text/html` is what makes a request a browser's: without it the page cannot be reached.

**That these requests were answered at all is what shows Kestrel bound before initialisation finished.**
A server that bound only afterwards would have refused them, so step 1 would have read no `503`.

**On failure:** a reset or JSON GET carrying `text/html` means the refusal ignores `Accept`.

### 3. Re-read health and version after seeding completes

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18403/api/v1/health --wait-for 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-03-out\health-ready.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\health-ready.json --equals status=healthy
dotnet script scripts/testing/http.csx -- --url http://localhost:18403/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-03-out\version-ready.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-03-out\version-ready.json --equals status=ready --present database
```

**Expected:** `/health` returns `200` and `healthy`; `/version` returns `200`, `status=ready`, with a
`database` block: the fields absent in step 1, now populated. What the database holds is not asserted, since
that depends on the bundled sources.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

| In the startup window | Answer |
|---|---|
| `GET /api/v1/health` | `503`, `{"status":"starting"}` |
| `GET /api/v1/version` | `503`, problem details titled *Quotinator is starting up* |
| `GET /`, `Accept: text/html` | `503`, `text/html; charset=utf-8`, auto-refreshing, no external assets |
| `POST /admin/database/reset?allowNoBackup=true`, `Accept: application/json` | `503`, `application/problem+json`, `Retry-After: 2` |
| `GET /quotes/random`, `Accept: application/json` | `503`, `application/problem+json` |

After the same container reached ready, `/health` read `healthy` and `/version` `status=ready` with a
`database` block.

## Canary

Build the commit before [#280](https://github.com/DutchJaFO/Quotinator/issues/280)'s first code
change, under a distinct tag, and run steps 1 and 3 against it.

Step 1 must find no `503` to read: the port refuses connections until initialisation finishes, and the first
answer is a `200`. Binding Kestrel before initialisation runs is what #280 added, and the difference between
*no answer* and *an answer that says wait* is this document's whole subject.

The `reset` and `random` requests belong to [#419](https://github.com/DutchJaFO/Quotinator/issues/419) and
need their own pre-fix build, the commit before #419's first code change: there a write issued during the
startup window is not refused, so the reset does not answer `503`.

Step 3 is the control on both builds: once seeding completes the application must be healthy and
report a version, so a run that never reaches ready has not tested the window at all.

**Measured 2026-11-01:**

| Build | Steps, in order | Reading |
|---|---|---|
| `quotinator:canary-280`, from `c755e5b0` | 1, 3 | Step 1 fails: the first answer is `200`, and no `503` is ever read. In step 3 `/health` reads `healthy` and `/version` answers `200`, naming `1.8.2` with a `database` block, and its `status=ready` check fails, a field that build predates. `Program.cs` at that commit has no wait page, and `5c83008f` adds one |
| `quotinator:canary-419`, from `ea08cac3` | 1, 2, 3 | Step 1 fails: only `health` answers `503`, and `version`, `page`, `reset` and `random` answer `200`. Step 2 fails on `/version`, which has no *starting up* title, and on the reset and the JSON GET, each `text/html`, the reset with no `Retry-After`. Step 2's `/health` and page reads pass. Step 3 passes |

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-03
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-03-out
```

**Expected:** the container, its volume and the output directory are gone, so the next run seeds from
nothing.
