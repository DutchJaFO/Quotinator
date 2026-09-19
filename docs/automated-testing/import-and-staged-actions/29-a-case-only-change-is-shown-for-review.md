# A case-only change is shown for review, and needs a decision

**Smoke:** no
**Environment:** Fresh
**Traces to:** #409

## Preconditions

A container of this test's own with `Quotinator__IncludeDefaultSources=false`, so the database holds
only what this test writes. The test brings its own two files and never stops the container.

## Determinism

- **Two quotes, two conflicts.** The base file stores two quotes. The second file restates both under
  `review`: the first with its text in upper case and nothing else changed, the second with genuinely
  different text — the control.
- **Every read is scoped to the conflicting batch's own `batchId`.**
- **The control proves the reads work.** It reports `quoteText` before and after the fix; only the
  case-only quote's result changes.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-29 --port 18629 `
  --env Quotinator__IncludeDefaultSources=false
$base = "http://localhost:18629/api/v1"
$temp = "$PWD\.claude\temp\qt-import-29"
New-Item -ItemType Directory -Force $temp | Out-Null
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop.

### 2. Import the base file, then the case-only one under `review`

```powershell
$caseOnlyId = 'a4090001-0000-4000-8000-00000000000a'
$controlId  = 'a4090002-0000-4000-8000-00000000000b'
function Write-QuoteFile($path, $caseOnlyText, $controlText) {
  $quotes = foreach ($q in @(@($caseOnlyId, $caseOnlyText), @($controlId, $controlText))) {
    [ordered]@{ id = $q[0]; quote = $q[1]; originalLanguage = 'en'
                source = 'Quotinator 409 Fixture Film'; date = '2001'; type = 'movie'; genres = @() }
  }
  [IO.File]::WriteAllText($path, (@{ quotes = @($quotes) } | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
}
Write-QuoteFile "$temp\base.json"      'Surely you cannot be serious.' 'The stored control line.'
Write-QuoteFile "$temp\case-only.json" 'SURELY YOU CANNOT BE SERIOUS.' 'A disagreeing control line.'

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\base.json" --duplicate-resolution newest-wins --expect 200 --status
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\case-only.json" --duplicate-resolution review --expect 202 | ConvertFrom-Json).batchId
"batchId=$batchId"
```

**Expected:** `200` for the base file, `202` for the second, and a non-empty `batchId`.

**On failure:** a `200` for the second file means nothing was held for review. Stop.

### 3. List the pending actions

```powershell
$pending = @((Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items |
  Where-Object { $_.entityType -eq 'Quote' })
$caseOnly = $pending | Where-Object { $_.entityId -eq $caseOnlyId }
$control  = $pending | Where-Object { $_.entityId -eq $controlId }
"caseOnly=$(@($caseOnly.ambiguousFields) -join ',') control=$(@($control.ambiguousFields) -join ',')"
```

**Expected:** `caseOnly=quoteText control=quoteText`.

**On failure:** `caseOnly=` empty with `control=quoteText` is the defect this document exists for: the
change is held for review and the listing shows nothing to decide.

### 4. Decide the case-only quote with no decision

```powershell
$refusal = '{}' | dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/$($caseOnly.id)/decide" --json-stdin --expect 422
"refusalNamesQuoteText=$($refusal -match 'quoteText')"
```

**Expected:** the decide answers `422`, and `refusalNamesQuoteText=True`.

**On failure:** a `204` means the stored text was kept without anyone choosing it.

### 5. Take the incoming text

```powershell
'{"quoteText":{"choice":"replace"}}' | dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/$($caseOnly.id)/decide" --json-stdin --expect 204 --status
$decided = (Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items |
  Where-Object { $_.entityId -eq $caseOnlyId }
"status=$($decided.status) quoteText=$($decided.mergedFields.quoteText)"
```

**Expected:** `204`, then `status=Decided quoteText=SURELY YOU CANNOT BE SERIOUS.`

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-29
Remove-Item -Recurse -Force $temp
```
