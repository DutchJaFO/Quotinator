# Bulk-deciding a staged batch via file export and re-import, in both wire formats

**Smoke:** no
**Environment:** Fresh
**Traces to:** #163, #411

## Preconditions

`GET /import/actions/export` flattens every decidable field of a batch's `Pending`/`Decided`/`Blocked`
Modify actions into rows; `POST /import/actions/bulk-decide` reads an edited version of that export
back and applies each row's decision.

Beyond the Fresh profile: **nothing.** Bundled sources are switched off, and this test brings its own
files — a base file of six quotes, and three conflicting files of two quotes each, one per batch. A
change to bundled content cannot make it pass or fail. Each conflicting import must return `202`, which
is what confirms the precondition rather than assuming it.

## Determinism

**An export sent back through bulk-decide must round-trip cleanly with zero errors. That exact scenario
caught a bug no unit test could.**

ASP.NET's app-wide camelCase JSON default (`ConfigureHttpJsonOptions` in `Program.cs`) means export's
output is genuinely camelCase, but `ParseJsonRows`'s `element.Deserialize<ImportActionFieldRow>()` call
had no explicit `JsonSerializerOptions` and silently fell back to `System.Text.Json`'s case-sensitive,
PascalCase-only library default. Every row failed with "missing required properties" despite the data
being present.

**Every unit-level round trip used bare `JsonSerializer` calls on both sides, which silently agreed on
PascalCase and never exercised the app's real configuration.** Only a live HTTP round trip through the
actual pipeline surfaces this class of bug — which is why this test sends the export's own output back,
with only its decisions filled in, rather than hand-writing an input.

- **The export of a pending conflict carries no decisions.** Sent back unmodified, bulk-decide
  correctly refuses every action as ambiguous. Each round trip therefore sets `keep` on the `quoteText`
  rows first — the only field the conflicting files disagree on. The edited file goes through the same
  reader, so the camelCase deserialization is still exercised.
- **Each batch conflicts with different quotes.** A conflict already awaiting review is recognised as
  already reported and stages nothing new, so three batches of the same two quotes would leave the
  second and third empty.
- `actionsDecided` is compared to **the batch's own pending-action count**, derived in the same run.
- **The malformed-row batch has two actions**, so "one bad row never aborts the rest" is observable:
  one action's row is corrupted, the other's is decided, and the tally shows each.

## Steps

### 1. Create this test's own environment and fixture

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-13 --port 18613 `
  --env Quotinator__IncludeDefaultSources=false
$base = "http://localhost:18613/api/v1"
$temp = "$PWD\.claude\temp\qt-import-13"
New-Item -ItemType Directory -Force $temp | Out-Null

function Quote($n, $prefix) {
  [ordered]@{ id = "a411000$n-0000-4000-8000-00000000000$n"; quote = "$prefix line $n.";
              originalLanguage = 'en'; source = 'Quotinator 411 Fixture Film'; date = '2001';
              type = 'movie'; genres = @() }
}
function Write-Quotes($path, $numbers, $prefix) {
  $json = @{ quotes = @($numbers | ForEach-Object { Quote $_ $prefix }) } | ConvertTo-Json -Depth 5
  [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
}
Write-Quotes "$temp\base.json"             (1..6)  'The stored'
Write-Quotes "$temp\conflicting-json.json" (1, 2)  'A disagreeing'
Write-Quotes "$temp\conflicting-csv.json"  (3, 4)  'A disagreeing'
Write-Quotes "$temp\conflicting-bad.json"  (5, 6)  'A disagreeing'

function Stage-Batch($file) {
  (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
     --file "$temp\$file" --duplicate-resolution review --expect 202 | ConvertFrom-Json).batchId
}
function Pending($batchId) {
  (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").totalCount
}
function QuoteTextRows($rows) {
  @(for ($i = 0; $i -lt $rows.Count; $i++) { if ($rows[$i].Field -eq 'quoteText') { $i + 1 } })
}

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\base.json" --duplicate-resolution newest-wins --expect 200 --status
```

**Expected:** the app reports healthy, and the base import returns `200`.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch to round-trip

```powershell
$batchId = Stage-Batch 'conflicting-json.json'
$pendingCount = Pending $batchId
"batchId=$batchId pendingCount=$pendingCount"
```

**Expected:** a non-empty `batchId` and `pendingCount=2` — the figure step 3 compares `actionsDecided`
against.

### 3. Export that batch as JSON, decide it, send it back, and apply

```powershell
Invoke-WebRequest "$base/import/actions/export?batchId=$batchId&format=json" `
  -OutFile "$temp\export.json" -UseBasicParsing
$rows = @(Get-Content "$temp\export.json" -Raw | ConvertFrom-Json | ForEach-Object { $_ })
# Add-Member: ConvertFrom-Json leaves out a property whose value was null, so it cannot be assigned.
$rows | Where-Object { $_.field -eq 'quoteText' } |
  ForEach-Object { $_ | Add-Member -NotePropertyName decision -NotePropertyValue keep -Force }
[IO.File]::WriteAllText("$temp\decided.json", (ConvertTo-Json -InputObject $rows -Depth 5),
                        [Text.UTF8Encoding]::new($false))

$jsonResult = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$batchId" `
  --file "$temp\decided.json" --field "batchId=$batchId" --expect 200 | ConvertFrom-Json
"actionsDecided=$($jsonResult.actionsDecided) errors=$(@($jsonResult.errors).Count) matchesPending=$($jsonResult.actionsDecided -eq $pendingCount)"

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
```

**Expected:** `actionsDecided=2 errors=0 matchesPending=True`, and the apply returns `200`.

**On failure:** `errors=2` with *"missing required properties"* is the camelCase defect this document
exists for. `errors=2` naming `quoteText` as ambiguous means the decisions were not written into the
file.

### 4. Repeat the round trip via CSV

```powershell
$csvBatchId = Stage-Batch 'conflicting-csv.json'
"pendingCount=$(Pending $csvBatchId)"

Invoke-WebRequest "$base/import/actions/export?batchId=$csvBatchId&format=csv" `
  -OutFile "$temp\export.csv" -UseBasicParsing
$quoteText = QuoteTextRows @(Import-Csv "$temp\export.csv")
dotnet script scripts/testing/corrupt-csv-cell.csx -- --in "$temp\export.csv" --out "$temp\half.csv" `
  --column Decision --value keep --row $quoteText[0]
dotnet script scripts/testing/corrupt-csv-cell.csx -- --in "$temp\half.csv" --out "$temp\decided.csv" `
  --column Decision --value keep --row $quoteText[1]

$csvResult = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$csvBatchId&format=csv" `
  --file "$temp\decided.csv" --field "batchId=$csvBatchId" --field "format=csv" --expect 200 | ConvertFrom-Json
"actionsDecided=$($csvResult.actionsDecided) errors=$(@($csvResult.errors).Count)"
```

**Expected:** `pendingCount=2`, each edit reports the replaced cell, and the CSV round trip returns
`actionsDecided=2 errors=0`.

**The edits are a script rather than a shell one-liner**, per
[ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md). The script matches the column
**by header name** and the row by its position among the data rows, found above by its `Field` value —
so a new column or field appearing in the export shifts nothing.

### 5. Stage a third batch, and bulk-decide a copy with one malformed row

**Malformed-row resilience needs its own batch.** Both batches above have already had every action
decided, so "the other action is still decided" would be true before the call and the test could not
fail in the direction it exists to catch.

```powershell
$thirdBatchId = Stage-Batch 'conflicting-bad.json'
"pendingCount=$(Pending $thirdBatchId)"

Invoke-WebRequest "$base/import/actions/export?batchId=$thirdBatchId&format=csv" `
  -OutFile "$temp\export3.csv" -UseBasicParsing
$export3 = @(Import-Csv "$temp\export3.csv")
$quoteText = QuoteTextRows $export3
$badAction  = $export3[$quoteText[0] - 1].ActionId
$goodAction = $export3[$quoteText[1] - 1].ActionId
dotnet script scripts/testing/corrupt-csv-cell.csx -- --in "$temp\export3.csv" --out "$temp\half3.csv" `
  --column Decision --value not-a-choice --row $quoteText[0]
dotnet script scripts/testing/corrupt-csv-cell.csx -- --in "$temp\half3.csv" --out "$temp\export3-bad.csv" `
  --column Decision --value keep --row $quoteText[1]

$badResult = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$thirdBatchId&format=csv" `
  --file "$temp\export3-bad.csv" --field "batchId=$thirdBatchId" --field "format=csv" --expect 200 | ConvertFrom-Json
"actionsDecided=$($badResult.actionsDecided) errors=$(@($badResult.errors).Count)"
$badResult.errors | ForEach-Object { $_.message }

$after = (Invoke-RestMethod "$base/import/actions?batchId=$thirdBatchId&pageSize=0").items
"bad=$(($after | Where-Object { $_.id -eq $badAction }).status) good=$(($after | Where-Object { $_.id -eq $goodAction }).status)"
```

**Expected:** `pendingCount=2`; the call returns **`200`, never `422` for the whole request**, with
`actionsDecided=1 errors=2`. Both errors concern the corrupted action:

- `Row 1: 'not-a-choice' is not a recognised Decision value.`
- `The following fields are ambiguous and need an explicit decision: quoteText` — its only decision was
  the rejected one, so the field it disagrees on is left undecided.

Then `bad=Pending good=Decided`. "One bad row never aborts the rest of the file", matching the contract
[`06-bodyless-request-validation.md`](06-bodyless-request-validation.md) covers for `POST /import`.

**On failure:** `good=Pending` means the bad row aborted the rest of the file.

### 6. Reject an unknown export format

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$batchId&format=xml" `
  --file "$temp\decided.json" --field "batchId=$batchId" --expect 422 --status
```

**Expected:** unknown `format` returns `422`.

### 7. Reject a request with no admin key

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/bulk-decide?batchId=$batchId" `
  --file "$temp\decided.json" --field "batchId=$batchId" --no-key --expect 401 --status
```

**Expected:** no `X-Api-Key` returns `401`.

### 8. Reject a request with no `batchId` and no body at all

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

Until #411 this document staged its batches from the bundled curated file. Since #373 that file stages
nothing against a database seeded from it, so step 2 answered `200` instead of `202`.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-13
Remove-Item -Recurse -Force -LiteralPath $temp
```
