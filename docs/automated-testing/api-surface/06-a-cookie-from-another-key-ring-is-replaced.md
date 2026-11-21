# A cookie from another key ring is replaced on the first page, and logged only then

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** [#411](https://github.com/DutchJaFO/Quotinator/issues/411), [#503](https://github.com/DutchJaFO/Quotinator/issues/503)

## Preconditions

**Beyond the profile.** Three containers of this test's own, one after another on their own ports. Two use
the suite's shared key ring, which `test-env.csx` mounts at `/data/keys` by default; one uses `--own-keys`.

A request carrying an antiforgery cookie protected by a key the application does not have cannot be read.
The application logs a `CryptographicException` / `AntiforgeryValidationException` pair and issues a new
cookie in the same response. This is the Knowledgebase entry *The log reports that an antiforgery token
could not be decrypted*, provoked on purpose. Every browser-driven test must never see it, which is what
the shared key ring is for, and what step 3 proves.

**The test brings the cookie it validates** (developer, 2026-11-21: *if it has to validate a cookie then it
needs a cookie to validate*). Every request is made by `http.csx`, which saves the cookies a response sets
(`--cookies-out`) and sends a saved cookie with a later request (`--cookie-file`). No browser is involved,
so nothing is left behind for another test, and no page is closed that the test did not ask to close.

## Determinism

- **Every cookie a request sends is one the test saved itself.** Step 1's request sends none; every later
  request sends a cookie an earlier step of this test was issued. Nothing depends on what a browser held
  before the test began.
- **Each exception count is the lines logged since a mark taken just before the request**, so one request's
  lines cannot absorb another's.
- **Each log is read before its container is destroyed**, per the index's *Read the log before the
  application stops*.
- **Only the antiforgery cookie is read.** It is what this document's subject issues and replaces; any
  other cookie a response sets is not counted.

## Steps

### 1. With no cookie, the shared-ring container issues one and logs nothing

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-api-06-out
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-a --port 18106
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-a --mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\a-first.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18106/notifications --no-key --accept text/html --expect 200 --status --cookies-out C:\repos\Quotinator\.claude\temp\qt-api-06-out\a.cookies
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-api-06-out\a.cookies "\.AspNetCore\.Antiforgery\."
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-a --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\a-first.mark --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: the page answers `200`, the response sets an antiforgery cookie, as any
first visit does, and nothing is logged as thrown.

**On failure:** no antiforgery cookie means the page did not issue one, so every later step would send an
empty file and prove nothing. Stop: do not run step 2.

### 2. The same container does not replace its own cookie

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-a --mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\a-again.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18106/notifications --no-key --accept text/html --expect 200 --status --cookie-file C:\repos\Quotinator\.claude\temp\qt-api-06-out\a.cookies --cookies-out C:\repos\Quotinator\.claude\temp\qt-api-06-out\a-again.cookies
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-no-text C:\repos\Quotinator\.claude\temp\qt-api-06-out\a-again.cookies "\.AspNetCore\.Antiforgery\."
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-a --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\a-again.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-a
```

**Expected:** every command exits `0`: the container reads the cookie it issued, sets no new one, and logs
nothing.

**On failure:** a new antiforgery cookie, or an exception, means the container could not read a cookie it
issued itself, so the comparison in steps 3 and 4 would mean nothing. Stop: do not run step 3.

### 3. A new container on the shared ring reads that cookie

```
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-b --port 18107
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-b --mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\b.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18107/notifications --no-key --accept text/html --expect 200 --status --cookie-file C:\repos\Quotinator\.claude\temp\qt-api-06-out\a.cookies --cookies-out C:\repos\Quotinator\.claude\temp\qt-api-06-out\b.cookies
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-no-text C:\repos\Quotinator\.claude\temp\qt-api-06-out\b.cookies "\.AspNetCore\.Antiforgery\."
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-b --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\b.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-b
```

**Expected:** every command exits `0`. The container has a new volume and a new database, and still reads the
cookie step 1's container issued, because both read one key ring: no new cookie, nothing logged.

**On failure:** a new antiforgery cookie, or the exception pair, means the new container had keys of its own:
`test-env.csx` did not mount the shared ring. That is this test's red case: before #411 every test container
started with its own keys. Stop: do not run step 4.

### 4. A container with its own key ring replaces the cookie, and logs it

```
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-c --port 18108 --own-keys
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-c --mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-first.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18108/notifications --no-key --accept text/html --expect 200 --status --cookie-file C:\repos\Quotinator\.claude\temp\qt-api-06-out\a.cookies --cookies-out C:\repos\Quotinator\.claude\temp\qt-api-06-out\c.cookies
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-api-06-out\c.cookies "\.AspNetCore\.Antiforgery\."
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-c --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-first.mark --pattern "\[Runtime - Exception\]" --exactly 2
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-c --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-first.mark --pattern "thrown: CryptographicException" --exactly 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-c --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-first.mark --pattern "thrown: AntiforgeryValidationException" --exactly 1
```

**Expected:** every command exits `0`: the page still answers `200`, the response sets a replacement
antiforgery cookie, and exactly two lines are logged as thrown, one `CryptographicException` and one
`AntiforgeryValidationException`.

**On failure:** no replacement cookie means the application kept a cookie it cannot read. A count other than
two, or a different pair, is a change in how the unreadable cookie is reported. Either way, stop: do not run
step 5.

### 5. The replacement is read, and nothing more is logged

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-c --mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-again.mark
dotnet script scripts/testing/http.csx -- --url http://localhost:18108/notifications --no-key --accept text/html --expect 200 --status --cookie-file C:\repos\Quotinator\.claude\temp\qt-api-06-out\c.cookies --cookies-out C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-again.cookies
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-no-text C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-again.cookies "\.AspNetCore\.Antiforgery\."
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-api-06-c --since-mark C:\repos\Quotinator\.claude\temp\qt-api-06-out\c-again.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-c
```

**Expected:** every command exits `0`: the container reads the cookie it issued in step 4, sets no new one,
and logs nothing. The pair is logged on the first page only, which is this document's title.

**On failure:** a new cookie, or another exception, means the replacement was not accepted, so a browser would
log the pair on every page rather than once. Stop.

## Observed effect

**Measured 2026-09-19** while designing #411's shared key ring, against the browser-driven form this document
had before #503. A container with its own keys, visited by a browser holding a cookie from any other, logged
exactly one pair: `CryptographicException: The key {…} was not found in the key ring` and
`AntiforgeryValidationException: The antiforgery token could not be decrypted`, with frames ending in
`DefaultAntiforgery.GetCookieTokenDoesNotThrow`. The page rendered normally, and the next visit logged
nothing. The browser does not separate cookies by port, nor by a `*.localhost` host name, which is why that
form had to restore the browser's cookie afterwards, and why this one uses no browser.

## Canary

The control is inside the run. Steps 3 and 4 differ only in whether the container shares the key ring, and
send the same cookie: step 3 must read no new cookie and no exception, and step 4 must read a replacement and
the pair. If sharing had no effect, step 3 would read what step 4 reads, and the document fails.

A build before [#411](https://github.com/DutchJaFO/Quotinator/issues/411) is the pre-fix form, where every
container generated a key ring of its own and step 3 reads a replacement cookie and the pair.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-a
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-b
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-c
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-api-06-out
```

**Expected:** every command exits `0`. Each step destroys its own container already; these catch a run that
stopped early. The shared key ring, `.claude/temp/qt-keys`, stays: removing it makes the next browser-driven
test log the pair once.
