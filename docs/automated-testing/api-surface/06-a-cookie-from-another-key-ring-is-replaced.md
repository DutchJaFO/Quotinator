# A cookie from another key ring is replaced on the first page, and logged only then

**Smoke:** no
**Environment:** Fresh
**Traces to:** [#411](https://github.com/DutchJaFO/Quotinator/issues/411)

## Preconditions

**Beyond the profile.** Four containers of this test's own, one after another on their own ports, and
the browser pane. Three use the suite's shared key ring, which `test-env.csx` mounts at `/data/keys` by
default; one uses `--own-keys`.

A browser holding a cookie protected by a key the application does not have sends it with the next
page request. The application cannot read it, logs a `CryptographicException` /
`AntiforgeryValidationException` pair, and issues a new cookie in the same response. This is the
Knowledgebase entry *The log reports that an antiforgery token could not be decrypted*, provoked on
purpose. Every other browser-driven test must never see it — which is what the shared key ring is for,
and what step 2 proves.

## Determinism

- **Close the tab before every container change, and open the next page in a new one.** A page left
  open reconnects to whatever answers on its port next, and that reconnect logs lines of its own:
  measured 2026-09-19, the same provocation read 5 lines with the old page open and exactly 2 with it
  closed.
- **Step 1's first visit is not asserted.** Whether it logs the pair depends on what the browser held
  before this test began — nothing in a fresh pane, since the cookie lives only for the browser session.
  Step 1's second visit is what pins the browser to a cookie from the shared ring.
- **Each count is the lines added since the previous read**, so one visit's result cannot absorb
  another's.
- **Each log is read before its container is stopped**, per the index's *Read the log before the
  application stops*.
- **This test must end with step 4.** Step 3 leaves the browser holding a cookie the shared ring cannot
  read, and the next browser-driven test would report it as its own failure.

## Steps

### 1. Pin the browser to a cookie from the shared key ring

```powershell
$seen = @{}
function New-Thrown($name) {
  $total = @(docker logs $name 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count
  $new = $total - [int]$seen[$name]
  $seen[$name] = $total
  $new
}
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-a --port 18106
```

In the browser, in a new tab (closing any other): open `http://localhost:18106/notifications`, then
`New-Thrown qt-api-06-a`. Open the same address again, then `New-Thrown qt-api-06-a`.

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-a
```

**Expected:** the first count is either `0` or `2` — see Determinism. The second is `0`.

**On failure:** a non-zero second count means the application failed to replace the cookie it could not
read. Stop: every later step depends on the browser holding one it can.

### 2. A new container on the shared ring reads that cookie

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-b --port 18107
```

In the browser, close the tab and open `http://localhost:18107/notifications` in a new one, then
`New-Thrown qt-api-06-b`.

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-b
```

**Expected:** `0`. The container has a new volume and a new database, and still reads the cookie step 1's
container issued, because both read one key ring.

**On failure:** `2` means the new container had keys of its own — `test-env.csx` did not mount the shared
ring. That is this test's red case: before #411 every test container started with its own keys, and this
step read `2`.

### 3. A container with its own key ring cannot — once

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-c --port 18108 --own-keys
```

In the browser, close the tab and open `http://localhost:18108/notifications` in a new one, then
`New-Thrown qt-api-06-c`. Open the same address again, then `New-Thrown qt-api-06-c`. Then:

```powershell
docker logs qt-api-06-c 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]' |
  ForEach-Object { ($_.Line -split 'thrown: ')[1] }
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-c
```

**Expected:** `2`, then `0`, and the two lines are `CryptographicException` and
`AntiforgeryValidationException`. The page renders both times.

**On failure:** `0` on the first visit means the browser sent no cookie, so step 2's `0` proved nothing
either — rerun from step 1. A non-zero second count means the cookie was not replaced.

### 4. Restore the browser to the shared key ring

This step is the remedy the Knowledgebase entry names, exercised: nothing needs doing, and the next page
replaces the cookie.

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-06-d --port 18109
```

In the browser, close the tab and open `http://localhost:18109/notifications` in a new one, then
`New-Thrown qt-api-06-d`. Open the same address again, then `New-Thrown qt-api-06-d`.

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-06-d
```

**Expected:** `2`, then `0`. The browser now holds a cookie from the shared key ring again, as it did
before step 3.

**On failure:** a non-zero second count means the browser still holds step 3's cookie, and every
browser-driven test after this one will log a pair it did not cause. Do not continue with the suite
until it reads `0`.

## Observed effect

Measured 2026-09-19 while designing #411's shared key ring. A container with its own keys, visited by a
browser holding a cookie from any other, logs exactly one pair — `CryptographicException: The key {…}
was not found in the key ring` and `AntiforgeryValidationException: The antiforgery token could not be
decrypted` — with frames ending in `DefaultAntiforgery.GetCookieTokenDoesNotThrow`. The page renders
normally, and the next visit logs nothing. The browser does not separate cookies by port, nor by a
`*.localhost` host name, so no browser-side isolation keeps a provoking test from affecting the others:
restoring the cookie in step 4 is the only way.

## Cleanup

Each step destroys its own container. The shared key ring, `.claude/temp/qt-keys`, stays: removing it
makes the next browser-driven test log the pair once.
