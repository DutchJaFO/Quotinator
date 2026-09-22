# A `Complete` row still blocks when a rule resolves to a different value

**Smoke:** no
**Environment:** Fresh
**Traces to:** #382, #181

**Pairs with [`23`](23-complete-row-blocks-only-on-a-real-write.md)**, which asserts the other half — a
`Complete` row whose resolution writes nothing must *not* block. The two are separate documents on
purpose: each builds its own container and its own fixture, so neither can leave state that skews the
other, and a failure names which half broke without the reader having to work out whether an earlier
step caused it.

## Preconditions

#382 narrowed `CompletenessGuard.ShouldBlock` so that a `Complete` row is held only when something
would genuinely be written to it. **This document is what stops that narrowing going too far.** A fix
that simply removed the guard call would satisfy `23` completely and be a straightforward regression;
only this one tells the two apart.

It is also what remains of #181's decision. #181 held that a rule must never write to a `Complete` row
unannounced, and enforced it by running the guard before any rule resolution at all. #382 reversed the
ordering; the guarantee that survives is asserted here.

Beyond the Fresh profile: **nothing.** This test brings its own Source, its own quote and its own
conflict-rule file, dropped into `{dataDir}/imports/`. It asserts only against its own entity id and
never reads a bundled row, so a change to bundled content cannot make it pass or fail.

**Why the user-imports folder rather than `POST /import`.** `SqliteQuoteImportService` does not pass a
`ConflictRuleLookup` to `ImportActionPlanner.PlanAsync` at all, so the `review` branch's resolution
never runs on that path and a rule can never fire through it — measured, not assumed. Only
`QuotinatorDatabaseInitializer` supplies one, and `ManifestSeedPlanner.Plan` is directory-agnostic, so
`{dataDir}/imports/` with its own `manifest.json` and `ruleFile` reaches the resolution branch.

## Determinism

- **`Quotinator__AutoPurgeUserImportActions=false` is mandatory, not tidiness.** It defaults to `true`,
  and a batch that reaches zero *pending* actions has its `Import_Action` rows deleted. The action this
  test exists to read is `Blocked`, never `Pending`, so with the default it is purged before it can be
  listed and the final assertion reads an empty set.
- **The container must be restarted after the files are copied in.** `SeedBatchesBuilder` only adds the
  user-imports batch when `Directory.Exists(importsDir)`, and `/data/imports` does not exist in a fresh
  volume. A reseed issued before the restart reports `importing from 5 source file(s)` — the bundled
  set only — and silently does nothing with the fixture. Step 3 asserts the file count for that reason.
- **The rules file starts empty and the rule is added only at step 5.** A `Keep` or `Replace` rule
  present during the initial Add has nothing on the existing side to resolve against, which holds the
  Add itself for review (`keepOrReplaceAgainstNothing`) and the fixture never lands. The rule must
  arrive after the row exists.
- **The rule must name both sides exactly.** `existingRecord.quoteText` is the stored text and
  `incomingRecord.quoteText` the file's new text. A mismatch makes the rule `Stale`, and the action
  stages `Stale` rather than `Blocked` — a different outcome that reads at a glance as a failure of the
  guard. See [`16`](16-conflict-rule-staleness.md).
- **The row is made `Complete` with `keep`, not `replace`.** `keep` leaves the stored text exactly as
  the imports file has it, so at step 5 the only disagreement is the one the rule then resolves. A
  `replace` would leave the stored text already differing from the file, and the block would be earned
  by that difference rather than by the rule's resolution.
- **Every listing is filtered to this test's own `entityId`.** The reseed stages actions for bundled
  content too; an unfiltered tally is satisfied by rows this test did not produce.
- **Read the whole action list, never the count alone.** #374's accumulation guard makes `PlanAsync`
  skip any quote that already carries an unresolved (`Pending`, `Blocked` or `Stale`) action, so a
  reseed can stage *nothing at all* for the row — which reads as `blocked=0`, indistinguishable from a
  guard that wrongly declined to block. Step 4 ends with its action applied, so nothing is outstanding
  when step 5 runs; the printed list is what proves it.
- **This document is green both before and after #382.** It is a regression guard, not a red-then-green
  test: the pre-fix guard was stricter, so it blocked this case too. A run against a pre-fix build that
  reports anything other than `blocked=1` is a fixture problem, not a finding.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-24 --port 18624 `
  --env Quotinator__AutoPurgeUserImportActions=false
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18624/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Write the fixture into `{dataDir}/imports/` and restart

```powershell
New-Item -ItemType Directory -Force "$temp\382-neg-imports" | Out-Null

$manifest = @'
{
  "duplicateResolution": { "default": "review" },
  "files": [
    { "file": "382-quotes.json", "name": "quotinator/382-neg-fixture", "ruleFile": "382-rules.json" }
  ]
}
'@
$quotes = @'
{
  "sources": [
    { "title": "Quotinator 382 Negative Fixture Film", "type": "movie", "date": "1999" }
  ],
  "quotes": [
    { "id":"a2222382-0000-4000-8000-000000000002", "quote":"Original fixture text.", "originalLanguage":"en", "source":"Quotinator 382 Negative Fixture Film", "date":"1999", "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
[IO.File]::WriteAllText("$temp\382-neg-imports\manifest.json",   $manifest, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\382-neg-imports\382-quotes.json", $quotes,   [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\382-neg-imports\382-rules.json",  '{ "rules": [] }', [Text.UTF8Encoding]::new($false))

docker exec qt-import-24 sh -c "mkdir -p /data/imports"
docker cp "$temp\382-neg-imports\manifest.json"   qt-import-24:/data/imports/
docker cp "$temp\382-neg-imports\382-quotes.json" qt-import-24:/data/imports/
docker cp "$temp\382-neg-imports\382-rules.json"  qt-import-24:/data/imports/
docker exec qt-import-24 sh -c "ls /data/imports"

docker restart qt-import-24 | Out-Null
dotnet script scripts/testing/http.csx -- --url "$base/health" --wait-for 200 --status
```

**Expected:** all three files listed, and the container restarts and answers health with `200`.

**Wait for health before step 3.** `docker restart` returns when the process starts, not when it
answers; a reseed sent straight after it fails with the connection closed — measured 2026-09-22 in
[*A `Complete` row is not blocked when the resolution writes nothing*](23-complete-row-blocks-only-on-a-real-write.md),
which shares this step.

**The quote states its own `date` here**, unlike [`23`](23-complete-row-blocks-only-on-a-real-write.md).
That is deliberate: this document's block must be earned by the rule's resolution alone, so no other
field may disagree at step 5.

### 3. Reseed, and confirm the fixture was actually picked up

```powershell
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
docker logs qt-import-24 2>&1 | Select-String 'reseed requested' | Select-Object -Last 1
(Invoke-RestMethod "$base/quotes/a2222382-0000-4000-8000-000000000002") | Select-Object quote, date, source
```

**Expected:** the reseed line reads `importing from 6 source file(s)` — five bundled plus this test's
own — and the quote reads back `Original fixture text.` with `date` `1999`.

**On failure:** `5 source file(s)`, or a `404` on the quote, means the user-imports batch was not seen
— almost always the restart in step 2 not having happened. Stop.

### 4. Mark the row `Complete` without changing it

```powershell
$mark = @'
{
  "quotes": [
    { "id":"a2222382-0000-4000-8000-000000000002", "quote":"Temporary variant text.", "originalLanguage":"en", "source":"Quotinator 382 Negative Fixture Film", "date":"1999", "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
[IO.File]::WriteAllText("$temp\382-neg-mark-complete.json", $mark, [Text.UTF8Encoding]::new($false))

$b = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
        --file "$temp\382-neg-mark-complete.json" --duplicate-resolution review --expect 202 `
      | ConvertFrom-Json).batchId
$a = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$b&pageSize=0").items |
       Where-Object { $_.entityType -eq 'Quote' }
"ambiguous=$($a.ambiguousFields -join ',')"

Invoke-RestMethod -Method Post -Uri "$base/import/actions/$($a.id)/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"},"markCompletenessAs":"Complete"}' | Out-Null
dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$b" --expect 200 --status
(Invoke-RestMethod "$base/quotes/a2222382-0000-4000-8000-000000000002").quote
```

**Expected:** `ambiguous=quoteText`, the apply returns `200`, and the quote still reads
`Original fixture text.` — `keep` wrote nothing, and the row is now `Complete`.

**`completenessStatus` cannot be read back**: `GET /quotes/{id}` does not expose it, unlike every
masterdata response. Step 5 is what proves the mark took effect — if it did not, its expected `Blocked`
becomes `Decided`.

### 5. Add a `Replace` rule, change the file, and reseed

```powershell
$negQuotes = @'
{
  "sources": [
    { "title": "Quotinator 382 Negative Fixture Film", "type": "movie", "date": "1999" }
  ],
  "quotes": [
    { "id":"a2222382-0000-4000-8000-000000000002", "quote":"Third fixture text.", "originalLanguage":"en", "source":"Quotinator 382 Negative Fixture Film", "date":"1999", "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
$negRules = @'
{
  "rules": [
    {
      "entityId": "a2222382-0000-4000-8000-000000000002",
      "existingRecord": { "quoteText": "Original fixture text." },
      "incomingRecord": { "quoteText": "Third fixture text." },
      "fields": [ { "field": "quoteText", "resolution": "Replace" } ]
    }
  ]
}
'@
[IO.File]::WriteAllText("$temp\382-neg-imports\382-quotes.json", $negQuotes, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\382-neg-imports\382-rules.json",  $negRules,  [Text.UTF8Encoding]::new($false))
docker cp "$temp\382-neg-imports\382-quotes.json" qt-import-24:/data/imports/
docker cp "$temp\382-neg-imports\382-rules.json"  qt-import-24:/data/imports/

Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
$acts = (Invoke-RestMethod "$base/import/actions?pageSize=0").items |
          Where-Object { $_.entityId -eq 'a2222382-0000-4000-8000-000000000002' }
$acts | Select-Object status, actionType
"blocked=$(@($acts | Where-Object { $_.status -eq 'Blocked' }).Count)"
(Invoke-RestMethod "$base/quotes/a2222382-0000-4000-8000-000000000002").quote
```

**Expected:** `blocked=1` — a `Blocked` / `Modify` action — and the quote still reads
`Original fixture text.` The third text was never written.

The rule resolves `quoteText` towards the incoming side, so the resolved payload differs from the
stored row and the apply genuinely would write. A row a human marked `Complete` is held for their
decision instead, which is what #181 existed to guarantee and what #382 deliberately kept.

**On failure:** `blocked=1` missing, with a `Decided` or `Applied` action in its place, means a
`Complete` row accepted a genuine overwrite — a more serious defect than the one #382 fixed.
`blocked=0` with no new action at all means the row was never re-evaluated (see Determinism). A
`Stale` action means the rule's recorded snapshot does not match both sides.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-24
Remove-Item -Recurse -Force "$temp\382-neg-imports", "$temp\382-neg-mark-complete.json"
```
