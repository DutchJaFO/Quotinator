# A missing `batchId` is rejected with the right message, and the log reports the real status

**Smoke:** no
**Environment:** Fresh
**Traces to:** #19

## Preconditions

Beyond the Fresh profile: **request logging must be genuinely on**, which Fresh does not set.
`Quotinator__LogRequests=true` **and** `Quotinator__LogLevel=debug` — request logging is Debug-only
across every category (#244), so `LogRequests=true` alone registers the middleware without raising the
level. That means its own container, since a configuration value is fixed at start.

## Determinism

- **Both halves must be checked.** The status code and the *logged* status code were wrong
  independently, and a test asserting only the response would have passed while the log still lied.
- The happy path is re-run afterwards, so a fix that returns `422` unconditionally cannot pass.
- **Request logging is confirmed working before anything is concluded from the log.** This is the
  difference between a test that can fail and one that cannot: with the two variables unset — the
  default, and the state of any container started from the plain profile — `docker logs` contains no
  `→` lines at all, so "no `→ 200` present" is satisfied by logging never having run. The first step
  therefore makes a request whose logged outcome is known and asserts that its line *appears*. Only
  then does the absence of a wrong line mean anything.
- **The batch for the happy path is staged by this document**, via a preview of the bundled curated
  file — the same way `03-batch-id-mode-alias.md` obtains one. Borrowing a batch from another test
  would make this unrunnable alone.
- **The log lines are counted with `[regex]::Matches`, not by matching lines.** Several `import/actions`
  entries can share a line, and a line-counting form would report `1` for any number of them.

## Steps

### 1. Create this test's own environment, with request logging genuinely on

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-11 --port 18611 `
  --env Quotinator__LogRequests=true --env Quotinator__LogLevel=debug
$base = "http://localhost:18611/api/v1"
```

**Expected:** the health poll returns — the container is up, with both logging variables set.

### 2. Prove request logging is actually emitting, before reading anything from the log

A request whose outcome is not in doubt, and its line must appear:

```powershell
Invoke-RestMethod "$base/health" | Out-Null
$log = docker logs qt-import-11 2>&1 | Out-String
([regex]::Matches($log, '→ 200')).Count
```

**Expected:** **request logging is live** — the `→ 200` count is non-zero.

**On failure:** if it is `0`, the two logging variables did not take effect and nothing below can be
concluded from the log; that is a setup failure, not a result. Stop.

### 3. Call all three staged-action endpoints with no `batchId`

```powershell
foreach ($action in 'apply', 'discard', 'reverse') {
  $problem = dotnet script scripts/testing/http.csx -- --method POST `
    --url "$base/import/actions/$action" --expect 422 | ConvertFrom-Json
  "$action -> $($problem.status): $($problem.detail)"
}
```

**Expected:** all three bodyless calls return `422` with a `detail` naming the missing `batchId` —
**never** the generic "Numeric parameters..." message.

### 4. Read the status those same calls were logged with

```powershell
$actionLines = (docker logs qt-import-11 2>&1 | Out-String) -split "`n" |
               Where-Object { $_ -match 'import/actions' }
"logged422=$(@($actionLines | Where-Object { $_ -match '→ 422' }).Count)"
"logged200=$(@($actionLines | Where-Object { $_ -match '→ 200' }).Count)"
```

**Expected:** `logged422=3` and `logged200=0` — the `import/actions` lines report the status the client
actually received.

Counting them is the assertion: a single missing line would otherwise be invisible.

### 5. Stage a batch for the happy path, this document's own

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/preview" `
              --file data/sources/quotinator-curated.json --duplicate-resolution skip `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** a non-empty `batchId`, which the next step applies.

### 6. Apply that batch with a real `batchId`

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
```

**Expected:** `200`, proving the fix did not simply make the endpoint return `422` unconditionally.

## Observed effect

**Found live via manual Visual Studio testing (T1), not by this suite** — worth recording, because it
is a class of defect a status-code check alone does not surface.

All three endpoints declared `batchId` as a required, non-nullable minimal-API parameter, so an omitted
`batchId` threw `BadHttpRequestException` at the binding layer before the handler ever ran. The global
safety net (`BadRequestExceptionHandler`) caught it and returned `422` — but with a message hard-coded
to numeric parameters, actively wrong for a missing `batchId`.

Separately, the completion log line for that same request read `→ 200`, not the `422` the client
actually received. `Program.cs` registered `UseExceptionHandler()` before `RequestLoggingMiddleware`,
so the exception unwound through the logging middleware's `finally` block — which reads
`context.Response.StatusCode`, still the untouched default at that point — before the exception handler
further out ever set the real status.

Fixed by declaring `batchId` as `string?` and validating it explicitly at the point of origin,
mirroring the numeric query parameter binding convention, and by moving `RequestLoggingMiddleware`'s
registration before `UseExceptionHandler()` so it wraps the exception handler rather than being wrapped
by it.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-11
```
