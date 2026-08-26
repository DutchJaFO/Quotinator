# Bulk-deciding a staged batch via file export and re-import, in both wire formats

**Smoke:** no
**Environment:** Fresh
**Traces to:** #163

## Preconditions

`GET /import/actions/export` flattens every decidable field of a batch's `Pending`/`Decided`/`Blocked`
Modify actions into rows; `POST /import/actions/bulk-decide` reads an edited version of that export
back and applies each row's decision.

Beyond the Fresh profile: a staged batch with pending Quote Modify actions is needed. This test stages
its own — the `review` import below produces one and must return `202`, which is what confirms the
precondition rather than assuming it.

## Determinism

**Submitting export's own unmodified output back into bulk-decide must round-trip cleanly with zero
errors. That exact scenario caught a bug no unit test could.**

ASP.NET's app-wide camelCase JSON default (`ConfigureHttpJsonOptions` in `Program.cs`) means export's
output is genuinely camelCase, but `ParseJsonRows`'s `element.Deserialize<ImportActionFieldRow>()` call
had no explicit `JsonSerializerOptions` and silently fell back to `System.Text.Json`'s case-sensitive,
PascalCase-only library default. Every row failed with "missing required properties" despite the data
being present.

**Every unit-level round trip used bare `JsonSerializer` calls on both sides, which silently agreed on
PascalCase and never exercised the app's real configuration.** Only a live HTTP round trip through the
actual pipeline surfaces this class of bug — which is why this test runs the export output back in
unedited rather than hand-writing an input.

- `actionsDecided` is compared to **the batch's own pending-action count**, derived in the same run, not
  a fixed number.
- The malformed-row case edits exactly one row and leaves the rest untouched, so "one bad row never
  aborts the rest" is observable rather than inferred.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-13 --port 18613
$base = "http://localhost:18613/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch to round-trip

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$pendingCount = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").totalCount
"batchId=$batchId pendingCount=$pendingCount"
```

**Expected:** a non-empty `batchId` and a non-zero `pendingCount` — the figure step 3 compares
`actionsDecided` against.

### 3. Export that batch as JSON, feed it straight back, and apply

```powershell
Invoke-WebRequest "$base/import/actions/export?batchId=$batchId&format=json" `
  -OutFile "$temp\export.json" -UseBasicParsing

$jsonResult = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$batchId" `
  --file "$temp\export.json" --field "batchId=$batchId" --expect 200 | ConvertFrom-Json
"actionsDecided=$($jsonResult.actionsDecided) errors=$(@($jsonResult.errors).Count) matchesPending=$($jsonResult.actionsDecided -eq $pendingCount)"

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
```

**Expected:** the JSON round trip returns `200` with `errors=0` and `matchesPending=True` —
`actionsDecided` equals the batch's own pending-action count, derived in this run rather than written
here.

### 4. Repeat the round trip via CSV

```powershell
$csvBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                 --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202 `
               | ConvertFrom-Json).batchId
$csvBatchId

Invoke-WebRequest "$base/import/actions/export?batchId=$csvBatchId&format=csv" `
  -OutFile "$temp\export.csv" -UseBasicParsing

$csvResult = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$csvBatchId&format=csv" `
  --file "$temp\export.csv" --field "batchId=$csvBatchId" --field "format=csv" --expect 200 | ConvertFrom-Json
"actionsDecided=$($csvResult.actionsDecided) errors=$(@($csvResult.errors).Count)"
```

**Expected:** the CSV round trip also returns `200` with `errors=0`.

### 5. Stage a third, still-undecided batch, and confirm nothing in it is decided yet

**Malformed-row resilience needs its own batch.** This must not reuse either batch above: both have
already had every row decided, so "every other row is still decided" would be true before the call and
the test could not fail in the direction it exists to catch.

```powershell
$thirdBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                   --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202 `
                 | ConvertFrom-Json).batchId
$thirdBatchId

Invoke-WebRequest "$base/import/actions/export?batchId=$thirdBatchId&format=csv" `
  -OutFile "$temp\export3.csv" -UseBasicParsing

$thirdBefore = (Invoke-RestMethod "$base/import/actions?batchId=$thirdBatchId&pageSize=0").items
$thirdBefore | Group-Object status | Select-Object Count, Name
"decidedBefore=$(@($thirdBefore | Where-Object { $_.status -ne 'Pending' }).Count)"
```

**Expected:** `decidedBefore=0` — the third batch's actions are all still **undecided**.

**On failure:** a non-zero `decidedBefore` means the malformed-row check below cannot
fail — the after-tally would look identical whether the call decided the remaining rows or did nothing
at all, which is exactly how this check read before the third batch was introduced. Stop and stage a
genuinely undecided batch.

### 6. Bulk-decide a copy with one malformed row, and re-read the tally

Make the bad copy first. The header and every other row stay exactly as exported; only the first data
row's `Decision` cell becomes `not-a-choice`, a value no decision accepts:

```powershell
dotnet script scripts/testing/corrupt-csv-cell.csx -- `
  --in "$temp\export3.csv" --out "$temp\export3-bad.csv" `
  --column Decision --value not-a-choice
```

**Expected:** the script reports the replaced cell and the `actionId` on that row — needed below, so
the tally can be read against the right action.

**The edit is a script rather than a shell one-liner**, per
[ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md): this repository does not write
text transformations in `sed`/`awk`. Until #339's full run this step described the edit in prose and
asked the reader to make it by hand, which no unattended run can do. The script matches the column **by
header name**, so a new column appearing in the export shifts nothing.

```powershell
$badResult = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$thirdBatchId&format=csv" `
  --file "$temp\export3-bad.csv" --field "batchId=$thirdBatchId" --field "format=csv" --expect 200 | ConvertFrom-Json
"errors=$(@($badResult.errors).Count)"
$badResult.errors

$thirdAfter = (Invoke-RestMethod "$base/import/actions?batchId=$thirdBatchId&pageSize=0").items
$thirdAfter | Group-Object status | Select-Object Count, Name
"stillPending=$(@($thirdAfter | Where-Object { $_.status -eq 'Pending' }).Count)"
```

**Expected:** the call returns **`200`, never `422` for the whole request**, with `errors=1`,
identifying the edited row and naming the rejected value — observed as
`Row 1: 'not-a-choice' is not a recognised Decision value.` `stillPending=0`. "One bad row never aborts
the rest of the file", matching the contract
[`06-bodyless-request-validation.md`](06-bodyless-request-validation.md) covers for `POST /import`.

**Two things this step asked for until #339's full run, neither of which the export's shape allows.**
It required the error to name the row's `actionId`; the message identifies the row by position
instead, so assert the single error and its content rather than a field it does not carry. And it
required the tally to show every action decided *except* that one — but **the export is one row per
decidable field, not per action**: 104 rows for 13 actions on the curated file. Corrupting one field
row leaves its action decided by its other rows, which is exactly what was observed, so the tally
after this call correctly reads every action `Decided`.

**What the tally does establish** is that the other 103 rows were processed rather than the file being
rejected wholesale — `stillPending=0` here against step 5's `decidedBefore=0` is the comparison that
makes it mean something.

### 7. Reject an unknown export format

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$batchId&format=xml" `
  --file "$temp\export.json" --field "batchId=$batchId" --expect 422 --status
```

**Expected:** unknown `format` returns `422`.

### 8. Reject a request with no admin key

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$batchId" `
  --file "$temp\export.json" --field "batchId=$batchId" --no-key --expect 401 --status
```

**Expected:** no `X-Api-Key` returns `401`.

### 9. Reject a request with no `batchId` and no body at all

```powershell
$noBatch = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide" --expect 422 | ConvertFrom-Json
"status=$($noBatch.status) hasDetail=$([bool]$noBatch.detail)"
$noBatch.detail
```

**Expected:** `422` with a `detail` naming the missing `batchId` — never a bare `400` with none.

## Observed effect

Two live-only bugs, both found during #163's own T2 pass.

The camelCase deserialization failure described in Determinism was the first.

The second: a request with neither `batchId` nor a multipart body returned a bare, uninformative `400`
with no `detail`. The endpoint bound `IFormFile? file` directly as a minimal-API parameter, which
requires a form content-type to even attempt binding — so a request with no `Content-Type` or body
fails at the framework's own routing/binding layer rather than as a thrown exception, bypassing
`BadRequestExceptionHandler` entirely.

**That is the same bug class `POST /import` had fixed earlier** — see
[`06-bodyless-request-validation.md`](06-bodyless-request-validation.md) — and never retrofitted onto
this newer endpoint. Fixed by switching to `HttpRequest request` and checking `batchId`, then
`request.HasFormContentType`, before attempting to read the form.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-13
Remove-Item "$temp\export.json", "$temp\export.csv", "$temp\export3.csv", "$temp\export3-bad.csv" `
            -ErrorAction SilentlyContinue
```
