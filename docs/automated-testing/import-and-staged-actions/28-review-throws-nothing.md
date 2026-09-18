# Reviewing conflicted import actions throws nothing

**Smoke:** no
**Environment:** Fresh
**Traces to:** #370

## Preconditions

A container of this test's own with `Quotinator__IncludeDefaultSources=false`, so the database holds
only what this test writes. The test brings its own two files and never stops the container.

## Determinism

- **Three quotes, three conflicts.** The base file stores three quotes; the second file restates the
  same three ids with different text under `review`, which stages exactly three `Pending` Quote actions.
- **Every read is scoped to the conflicting batch's own `batchId`.**
- **Exception lines are counted, never read by eye.** `[Runtime - Exception]` lines are counted before
  the reads and after them; the reads must add none.
- **The positive control is that the reads still do their job**: every pending row reports `quoteText`
  as ambiguous, and the undecided decide is refused naming it. An empty count is only meaningful beside
  proof that the code path ran.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-28 --port 18628 `
  --env Quotinator__IncludeDefaultSources=false
$base = "http://localhost:18628/api/v1"
$temp = "$PWD\.claude\temp\qt-import-28"
New-Item -ItemType Directory -Force $temp | Out-Null
function Count-Thrown { @(docker logs qt-import-28 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count }
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop.

### 2. Import the base file, then the conflicting one under `review`

```powershell
$ids = 'a3700001-0000-4000-8000-00000000000a', 'a3700002-0000-4000-8000-00000000000b', 'a3700003-0000-4000-8000-00000000000c'
function Write-QuoteFile($path, $prefix) {
  $quotes = foreach ($i in 0..2) {
    [ordered]@{ id = $ids[$i]; quote = "$prefix line $($i + 1)."; originalLanguage = 'en'
                source = 'Quotinator 370 Fixture Film'; date = '2001'; type = 'movie'; genres = @() }
  }
  [IO.File]::WriteAllText($path, (@{ quotes = @($quotes) } | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
}
Write-QuoteFile "$temp\base.json"        'The stored'
Write-QuoteFile "$temp\conflicting.json" 'A disagreeing'

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\base.json" --duplicate-resolution newest-wins --expect 200 --status
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\conflicting.json" --duplicate-resolution review --expect 202 | ConvertFrom-Json).batchId
"batchId=$batchId"
```

**Expected:** `200` for the base file, `202` for the conflicting one, and a non-empty `batchId`.

**On failure:** a `200` for the second file means nothing was staged, and every step below would read
an empty batch. Stop.

### 3. Count the exception lines before the reads

```powershell
$before = Count-Thrown
"before=$before"
```

**Expected:** `before=0` — importing two well-formed files throws nothing.

### 4. List, export, render the review page, and decide one row with no decisions

```powershell
$pending = @((Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items |
  Where-Object { $_.entityType -eq 'Quote' })
"pending=$($pending.Count) reportingQuoteText=$(@($pending | Where-Object { $_.ambiguousFields -contains 'quoteText' }).Count)"

$export = @((Invoke-RestMethod "$base/import/actions/export?batchId=$batchId&format=json") | ForEach-Object { $_ })
"exportQuoteTextRows=$(@($export | Where-Object { $_.entityType -eq 'Quote' -and $_.field -eq 'quoteText' }).Count)"

$page = Invoke-WebRequest "http://localhost:18628/import-review" -UseBasicParsing
"reviewPage=$($page.StatusCode)"

$refusal = '{}' | dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/$($pending[0].id)/decide" --json-stdin --expect 422
"refusalNamesQuoteText=$($refusal -match 'quoteText')"
```

**Expected:** `pending=3 reportingQuoteText=3`, `exportQuoteTextRows=3`, `reviewPage=200`, the decide
answers `422`, and `refusalNamesQuoteText=True`.

**On failure:** fewer than three rows reporting `quoteText`, or a decide that is not refused, means the
reads did not exercise the conflict — the count in step 5 would then prove nothing.

### 5. Count the exception lines after the reads

```powershell
$after = Count-Thrown
"after=$after"
docker logs qt-import-28 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
```

**Expected:** `after=0`, equal to `before` — listing, exporting, rendering the review page and refusing
an undecided decide threw nothing.

**On failure:** each line names the exception type and its id. `UnresolvedFieldConflictException` is
the defect this document exists for.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-28
Remove-Item -Recurse -Force $temp
```
