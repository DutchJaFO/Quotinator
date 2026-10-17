# A thrown exception is logged where the app actually runs

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** [#397](https://github.com/DutchJaFO/Quotinator/issues/397)

## Preconditions

Nothing beyond the Fresh profile. The malformed file this test uploads is one it writes itself.

## Determinism

- The exception provoked is one `System.Text.Json` throws for invalid JSON; no non-throwing API detects
  that, so the throw does not depend on any check-then-throw surviving
  [#398](https://github.com/DutchJaFO/Quotinator/issues/398).
- `Quotinator__LogLevel` stays at the profile default. These lines are written at `Error`, and at
  `fatal` they are legitimately absent.
- Nothing else in the run provokes an exception, which is what makes the counts around each step
  readable.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-api-05 --port 18105
```

**Expected:** the app reports healthy, so the bundled seed has finished.

**On failure:** stop. Every step below reads this container's log.

### 2. Count the exception lines a healthy startup produced

```powershell
$before = ([regex]::Matches((docker logs qt-api-05 2>&1 | Out-String), '\[Runtime - Exception\]')).Count
"before=$before"
```

**Expected:** `before=0`. A healthy startup throws no exceptions.

**On failure:** a non-zero count names each exception with its type and throw site in the log. Read
them there; every one is a defect to resolve, not noise to tolerate.

### 3. Ask for something that cannot be parsed

```powershell
Set-Content -Path "$env:TEMP\qt-malformed.json" -Value '{ "quotes": [ { "quote": "unterminated' -Encoding utf8
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18105/api/v1/import" `
  --file "$env:TEMP\qt-malformed.json" --duplicate-resolution review --expect 422
```

**Expected:** `422`: the upload is rejected as invalid JSON.

### 4. Read the thrown line

```powershell
$log = docker logs qt-api-05 2>&1 | Out-String
$after = ([regex]::Matches($log, '\[Runtime - Exception\]')).Count
"after=$after"
($log -split "`n" | Select-String -SimpleMatch '[Runtime - Exception]' | Select-Object -Last 1).Line
```

**Expected:** `after` is greater than `before`, and the upload's lines are `ERR` lines each carrying an
8-character hexadecimal id: one naming `JsonReaderException`, `System.Text.Json`'s own subclass of
`JsonException`, and one or more naming `QuoteImportValidationException`, the check-then-throw #398
removes. Repeats of that second type share a single id, one exception object, notified once per throw
and once per rethrow, so count distinct ids rather than lines.

**On failure:** if `after` equals `before`, nothing logged the throw. Confirm the thrown-exception
handler is subscribed before the host builder is created.

### 5. Confirm an ordinary request logs no exception

```powershell
(Invoke-RestMethod "http://localhost:18105/api/v1/health").status
$quiet = ([regex]::Matches((docker logs qt-api-05 2>&1 | Out-String), '\[Runtime - Exception\]')).Count
"after=$after quiet=$quiet"
```

**Expected:** `healthy`, and `quiet` equal to `after`: a request that throws nothing adds no line.

## Observed effect

A healthy container is silent about exceptions: nothing in a normal startup or an ordinary request
produces an `[Runtime - Exception]` line, so any line that appears was caused by something a caller
did.

An upload the application cannot parse answers `422` to the caller and writes `ERR` lines on the way,
each carrying an id for the throw. The two sides can therefore be matched: a `422` a user reports has
a logged counterpart with an identifier, rather than being a failure visible only to whoever was
attached to a debugger at the time.

That pairing is the operator-facing point of this document. Before it, an exception thrown where the
application actually runs left nothing behind outside Visual Studio, so a report of a rejected upload
could not be traced at all.

## Canary

Build the commit before [#397](https://github.com/DutchJaFO/Quotinator/issues/397)'s first code
change, under a distinct tag, and run steps 2 to 5 against it.

Step 4 must read `after` equal to `before`. That is the defect this document exists for: an exception
thrown where the application actually runs, and logged nowhere outside Visual Studio.

Steps 2 and 5 are expected to pass on that build, and they are the control rather than a second
assertion. A healthy startup and an ordinary request throw nothing on either build, which is what
makes step 4's increase attributable to the upload instead of to whatever else the container was
doing.

Remove the container, image and worktree afterwards.

## Cleanup

```powershell
Remove-Item "$env:TEMP\qt-malformed.json"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-05
```
