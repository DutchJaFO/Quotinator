# Deciding an import Add answers an outcome, never a server error

**Smoke:** no
**Environment:** Fresh
**Traces to:** #410

## Preconditions

A container of this test's own, seeded at first start from a user-imports folder this test writes: a
base file, then a file whose every quote is held for a different reason, with its own rule file. Two
deltas from the Fresh profile:

- `Quotinator__IncludeDefaultSources=false` — the database holds only this test's content.
- `Quotinator__AutoPurgeUserImportActions=false` — the setting defaults to `true`, which deletes an
  applied user batch's actions and would leave no applied Add to decide.

The test never stops its container.

## Determinism

- **Every quote id is written in the files**, so each action is found by the id it was staged for.
- **Each held quote is held for exactly one reason**, following the unit test that stages it:
  - a second date — two TV quotes from one title under two dates (`ResolveSourceAsync_ReviewPolicy_SecondDateForAKnownTitle_StagesPending`);
  - a Keep rule matching nothing stored (`Seed_WithAKeepRuleMatchingABrandNewQuote_StagesPendingForReviewRatherThanApplyingOrIgnoring`);
  - the same text and source as a stored quote (`GetPagedAsync_BlockedCollisionAgainstAnExistingQuote_DoesNotCrash`);
  - the same text and source as another quote in its file (`PlanAsync_TwoQuotesWithTheSameTextAndSourceInOneFile_StagesTheSecondBlocked`).
- **A stale Add is not run here.** Reaching one needs a stored source renamed away from the title its
  generated id was hashed from, and this test would have to know that id before the seed runs;
  `DecideAsync_StaleQuoteAdd_ReturnsHeldForReviewWithoutThrowing` covers it.
- **Exception lines are counted before and after the decides**; the decides must add none.

## Steps

### 1. Write the imports folder and create this test's own environment

```powershell
$bind    = Join-Path $env:TEMP "qt-import-30-bind"
$imports = Join-Path $bind "imports"
# A folder left by an earlier run still holds its database, and the container would start against it.
if (Test-Path $bind) { Remove-Item -LiteralPath $bind -Recurse -Force }
New-Item -ItemType Directory -Force $imports | Out-Null
function Write-Json($name, $value) {
  [IO.File]::WriteAllText((Join-Path $imports $name), ($value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
function Quote($id, $text, $source, $date, $type = 'movie') {
  [ordered]@{ id = $id; quote = $text; originalLanguage = 'en'; source = $source; date = $date; type = $type; genres = @() }
}

Write-Json 'base.json' @{ quotes = @(
  Quote 'a4100001-0000-4000-8000-00000000000a' 'A line this test stores first.' 'Quotinator 410 Stored Film' '2001'
) }
Write-Json 'variants.json' @{ quotes = @(
  Quote 'a4100002-0000-4000-8000-00000000000b' 'A line from the earlier season.' 'Quotinator 410 Series' '2015' 'tv'
  Quote 'a4100003-0000-4000-8000-00000000000c' 'A line from the later season.'   'Quotinator 410 Series' '2017' 'tv'
  Quote 'a4100004-0000-4000-8000-00000000000d' 'A line a keep rule was written for.' 'Quotinator 410 Rule Film' '2002'
  Quote 'a4100005-0000-4000-8000-00000000000e' 'A line this test stores first.' 'Quotinator 410 Stored Film' '2001'
  Quote 'a4100006-0000-4000-8000-00000000000f' 'A line the file states twice.' 'Quotinator 410 Twice Film' '2003'
  Quote 'a4100007-0000-4000-8000-00000000001a' 'A line the file states twice.' 'Quotinator 410 Twice Film' '2003'
) }
Write-Json 'variants-rules.json' @{ rules = @(@{
  entityId       = 'a4100004-0000-4000-8000-00000000000d'
  existingRecord = @{ quoteText = 'A line a keep rule was written for.' }
  incomingRecord = @{ quoteText = 'A line a keep rule was written for.' }
  fields         = @(@{ field = 'quoteText'; resolution = 'keep' })
}) }
Write-Json 'manifest.json' @{
  duplicateResolution = @{ default = 'review' }
  files = @(
    @{ file = 'base.json';     name = 'test/base';     duplicateResolution = @{ default = 'newest-wins' } }
    @{ file = 'variants.json'; name = 'test/variants'; duplicateResolution = @{ default = 'review' }; ruleFile = 'variants-rules.json' }
  )
}

dotnet script scripts/testing/test-env.csx -- create --name qt-import-30 --port 18630 --bind $bind `
  --env Quotinator__IncludeDefaultSources=false --env Quotinator__AutoPurgeUserImportActions=false
$base = "http://localhost:18630/api/v1"
function Count-Thrown { @(docker logs qt-import-30 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count }
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop.

### 2. Find each Add and confirm why it is held

```powershell
$quotes = @((Invoke-RestMethod "$base/import/actions?pageSize=0").items | Where-Object { $_.entityType -eq 'Quote' })
function Action($id) { $quotes | Where-Object { $_.entityId -eq $id } }
$cases = [ordered]@{
  applied        = Action 'a4100001-0000-4000-8000-00000000000a'
  secondDate     = Action 'a4100003-0000-4000-8000-00000000000c'
  ruleOnNothing  = Action 'a4100004-0000-4000-8000-00000000000d'
  storedDuplicate = Action 'a4100005-0000-4000-8000-00000000000e'
  fileDuplicate  = Action 'a4100007-0000-4000-8000-00000000001a'
}
$cases.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value.actionType)/$($_.Value.status)" }
$before = Count-Thrown
"before=$before"
```

**Expected:** `applied=Add/Applied`, `secondDate=Add/Pending`, `ruleOnNothing=Add/Pending`,
`storedDuplicate=Add/Blocked`, `fileDuplicate=Add/Blocked`, and `before=0`.

**On failure:** a case in any other state was not held for the reason this test gives it, and deciding
it would test something else. Stop.

### 3. Decide each Add with no decisions

```powershell
foreach ($case in $cases.GetEnumerator()) {
  $body = '{}' | dotnet script scripts/testing/http.csx -- --method POST `
    --url "$base/import/actions/$($case.Value.id)/decide" --json-stdin --expect 422
  "$($case.Key): heldForReview=$($body -match 'held for review') alreadyResolved=$($body -match 'already been applied')"
}
```

**Expected:** each decide answers `422`. `applied: heldForReview=False alreadyResolved=True`; every other
case `heldForReview=True alreadyResolved=False`.

**On failure:** a `500` is the defect this document exists for.

### 4. Count the exception lines after the decides

```powershell
"after=$(Count-Thrown)"
docker logs qt-import-30 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
```

**Expected:** `after=0`, equal to `before`.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-30 --bind $bind
Remove-Item -LiteralPath $bind -Recurse -Force
```
