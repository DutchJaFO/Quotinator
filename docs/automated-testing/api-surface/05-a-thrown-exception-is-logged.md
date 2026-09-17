# A thrown exception is logged where the app actually runs

**Smoke:** no
**Environment:** Fresh
**Traces to:** [#397](https://github.com/DutchJaFO/Quotinator/issues/397)

## Preconditions

Nothing beyond the Fresh profile. The malformed file this test uploads is one it writes itself.

## Determinism

The exception provoked here is one `System.Text.Json` throws for invalid JSON, which no non-throwing
API can detect — so it stays a legitimate throw whatever [#398](https://github.com/DutchJaFO/Quotinator/issues/398)
converts, and this test does not depend on any check-then-throw surviving. Nothing else in the run
provokes an exception, which is what makes the before-and-after counts around the health call readable.

`Quotinator__LogLevel` is left at the profile's default, because these lines are written at `Error` and
an operator's own level setting is respected: at `fatal` they would legitimately be absent.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-05 --port 18105
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** stop. Every step below reads this container's log.

### 2. Count the exception lines the healthy startup produced

```powershell
$before = ([regex]::Matches((docker logs qt-api-05 2>&1 | Out-String), '\[Runtime - Exception\]')).Count
"before=$before"
```

**Expected:** a number, printed. Zero is the healthy case and is not required — the framework throws
and handles exceptions of its own during startup, and each is triaged rather than filtered. This value
is the baseline the later steps compare against, not an assertion in itself.

### 3. Ask for something that cannot be parsed

```powershell
Set-Content -Path "$env:TEMP\qt-malformed.json" -Value '{ "quotes": [ { "quote": "unterminated' -Encoding utf8
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18105/api/v1/import" `
  --file "$env:TEMP\qt-malformed.json" --duplicate-resolution review --expect 422
```

**Expected:** `422`. The upload is rejected for content that is not valid JSON, which is the behaviour
this step needs — the point is what the log now contains, not the status code.

### 4. Read the thrown line

```powershell
$log = docker logs qt-api-05 2>&1 | Out-String
$after = ([regex]::Matches($log, '\[Runtime - Exception\]')).Count
"after=$after"
($log -split "`n" | Select-String -SimpleMatch '[Runtime - Exception]' | Select-Object -Last 1).Line
```

**Expected:** `after` is greater than `before`, and the last such line is an `ERR` line naming
`JsonException` and carrying an 8-character hexadecimal id. This is the whole point of the test: an
exception the application caught itself is visible in a container log, where before this issue it
appeared only in a debugger.

**On failure:** if `after` equals `before`, nothing logged the throw. Check that the thrown-exception
handler is subscribed before the builder is created — a subscription that happens later misses nothing
here, but a missing one misses everything.

### 5. Confirm an ordinary request logs no exception

```powershell
(Invoke-RestMethod "http://localhost:18105/api/v1/health").status
$quiet = ([regex]::Matches((docker logs qt-api-05 2>&1 | Out-String), '\[Runtime - Exception\]')).Count
"after=$after quiet=$quiet"
```

**Expected:** `healthy`, and `quiet` equal to `after` — a request that throws nothing adds no line. This
is the control for step 4: without it, a handler that logged on every request would pass just as well.

## Observed effect

Not yet established. This document records what a pass requires; what the container emits around these
requests has not been captured beyond the lines asserted here. See
[the index](../README.md#test-outcomes-feed-the-knowledgebase) for why that matters.

## Cleanup

```powershell
Remove-Item "$env:TEMP\qt-malformed.json"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-05
```
