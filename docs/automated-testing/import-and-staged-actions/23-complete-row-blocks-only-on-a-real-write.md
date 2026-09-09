# A `Complete` row is not blocked when the resolution writes nothing

**Smoke:** no
**Environment:** Fresh
**Traces to:** #382

**Pairs with [`24`](24-complete-row-still-blocks-a-real-write.md)**, which asserts the other half — a
`Complete` row whose rule resolves to a *different* value must still block. The two are separate
documents on purpose: each builds its own container and its own fixture, so neither can leave state
that skews the other, and a failure names which half broke without the reader having to work out
whether an earlier step caused it.

## Preconditions

`CompletenessGuard.ShouldBlock` holds an import against a row a human has marked `Complete`. Until
#382 it was handed the field set computed *before* the import's own resolution ran, at five of the six
sites that resolve — so a `Complete` row was staged `Blocked` over a field the resolution was about to
settle straight back to its stored value. The block was real and had to be cleared by hand, for a
change that would never have been made.

Beyond the Fresh profile: **nothing.** This test brings its own Source, its own quote and its own
conflict-rule file, dropped into `{dataDir}/imports/`. It asserts only against its own entity id and
never reads a bundled row, so a change to bundled content cannot make it pass or fail.

**Why the user-imports folder rather than `POST /import`.** `SqliteQuoteImportService` does not pass a
`ConflictRuleLookup` to `ImportActionPlanner.PlanAsync` at all, so the `review` branch's resolution
never runs on that path and the behaviour under test is unreachable through it — measured, not assumed
(an `Incomplete` row imported that way stages `Pending` with an empty `ambiguousFields`, which is what
a skipped resolution looks like). Only `QuotinatorDatabaseInitializer` supplies one.
`ManifestSeedPlanner.Plan` is directory-agnostic and resolves each entry's `ruleFile` relative to
whichever directory it scans, so `{dataDir}/imports/` with its own `manifest.json` seeds through that
initializer and reaches the resolution branch — no rule-file override endpoint involved.

## Determinism

- **`Quotinator__AutoPurgeUserImportActions=false` is mandatory, not tidiness.** It defaults to `true`,
  and a batch that reaches zero *pending* actions has its `Import_Action` rows deleted. The action this
  test exists to read is `Blocked` or `Applied`, never `Pending`, so with the default the rows are
  purged before they can be listed and every assertion below reads an empty set.
- **The container must be restarted after the files are copied in.** `SeedBatchesBuilder` only adds the
  user-imports batch when `Directory.Exists(importsDir)`, and `/data/imports` does not exist in a fresh
  volume. A reseed issued before the restart reports `importing from 5 source file(s)` — the bundled
  set only — and silently does nothing with the fixture. Step 3 asserts the file count for that reason.
- **The quote declares its Source's date but omits its own.** That asymmetry is the whole mechanism:
  the stored quote's `date` comes from the Source (1999), the incoming file states none, and
  `FieldMergeResolver` settles it back to the stored value without any rule. It is what the guard reads
  after #382 and did not read before.
- **The row is made `Complete` with `keep`, not `replace`.** `keep` leaves the stored text exactly as
  the imports file has it, so the final reseed disagrees on nothing but `date` — the field under test.
  A `replace` would leave the stored text differing from the file, and the reseed would block for that
  reason instead of the one this document names.
- **Every listing is filtered to this test's own `entityId`.** The reseed stages actions for bundled
  content too; an unfiltered tally is satisfied by rows this test did not produce.
- **Read the whole action list, never the count alone.** #374's accumulation guard makes `PlanAsync`
  skip any quote that already carries an unresolved (`Pending`, `Blocked` or `Stale`) action, so a
  reseed can stage *nothing at all* for the row. That reads as `blocked=0` — indistinguishable from
  the guard correctly declining to block. Step 4 ends with its action applied, so nothing is
  outstanding when step 5 runs; the printed list is what proves it.
- **The rules file stays empty here.** No `ConflictResolutionRule` is needed: the resolution this
  document turns on is `FieldMergeResolver` settling an omitted field back to its stored value. The
  file must still exist and be declared in the manifest, because a `ruleFile` is what makes the seed
  path build a `ConflictRuleLookup` at all — with none, the `review` branch never resolves and the
  action falls through to `Pending`. [`24`](24-complete-row-still-blocks-a-real-write.md) is where a
  real rule is exercised.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-23 --port 18623 `
  --env Quotinator__AutoPurgeUserImportActions=false
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18623/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Write the fixture into `{dataDir}/imports/` and restart

```powershell
New-Item -ItemType Directory -Force "$temp\382-imports" | Out-Null

$manifest = @'
{
  "duplicateResolution": { "default": "review" },
  "files": [
    { "file": "382-quotes.json", "name": "quotinator/382-fixture", "ruleFile": "382-rules.json" }
  ]
}
'@
$quotes = @'
{
  "sources": [
    { "title": "Quotinator 382 Fixture Film", "type": "movie", "date": "1999" }
  ],
  "quotes": [
    { "id":"a1111382-0000-4000-8000-000000000001", "quote":"Original fixture text.", "originalLanguage":"en", "source":"Quotinator 382 Fixture Film", "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
[IO.File]::WriteAllText("$temp\382-imports\manifest.json",    $manifest, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\382-imports\382-quotes.json",  $quotes,   [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\382-imports\382-rules.json",   '{ "rules": [] }', [Text.UTF8Encoding]::new($false))

docker exec qt-import-23 sh -c "mkdir -p /data/imports"
docker cp "$temp\382-imports\manifest.json"   qt-import-23:/data/imports/
docker cp "$temp\382-imports\382-quotes.json" qt-import-23:/data/imports/
docker cp "$temp\382-imports\382-rules.json"  qt-import-23:/data/imports/
docker exec qt-import-23 sh -c "ls /data/imports"

docker restart qt-import-23 | Out-Null
```

**Expected:** all three files listed, and the container restarts.

**The quote deliberately omits its own `date`** while its Source declares one. Adding a `date` to the
quote removes the field the resolver settles and the test asserts nothing.

### 3. Reseed, and confirm the fixture was actually picked up

```powershell
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
docker logs qt-import-23 2>&1 | Select-String 'reseed requested' | Select-Object -Last 1
(Invoke-RestMethod "$base/quotes/a1111382-0000-4000-8000-000000000001") | Select-Object quote, date, source
```

**Expected:** the reseed line reads `importing from 6 source file(s)` — five bundled plus this test's
own — and the quote reads back `Original fixture text.` with `date` `1999` and source
`Quotinator 382 Fixture Film`.

**On failure:** `5 source file(s)`, or a `404` on the quote, means the user-imports batch was not seen
— almost always the restart in step 2 not having happened. Every later step would then be asserting
against bundled content. Stop.

### 4. Mark the row `Complete` without changing it

```powershell
$mark = @'
{
  "quotes": [
    { "id":"a1111382-0000-4000-8000-000000000001", "quote":"Temporary variant text.", "originalLanguage":"en", "source":"Quotinator 382 Fixture Film", "date":"1999", "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
[IO.File]::WriteAllText("$temp\382-mark-complete.json", $mark, [Text.UTF8Encoding]::new($false))

$b = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
        --file "$temp\382-mark-complete.json" --duplicate-resolution review --expect 202 `
      | ConvertFrom-Json).batchId
$a = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$b&pageSize=0").items |
       Where-Object { $_.entityType -eq 'Quote' }
"ambiguous=$($a.ambiguousFields -join ',')"

Invoke-RestMethod -Method Post -Uri "$base/import/actions/$($a.id)/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"},"markCompletenessAs":"Complete"}' | Out-Null
dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$b" --expect 200 --status
(Invoke-RestMethod "$base/quotes/a1111382-0000-4000-8000-000000000001").quote
```

**Expected:** `ambiguous=quoteText`, the apply returns `200`, and the quote still reads
`Original fixture text.` — `keep` wrote nothing, and the row is now `Complete`.

**`completenessStatus` cannot be read back**: `GET /quotes/{id}` does not expose it, unlike every
masterdata response. Step 6 is what proves the mark took effect — if it did not, that step's expected
`Blocked` becomes `Decided` and the failure is visible there.

### 5. Reseed — the resolution writes nothing, so nothing is blocked

```powershell
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
$acts = (Invoke-RestMethod "$base/import/actions?pageSize=0").items |
          Where-Object { $_.entityId -eq 'a1111382-0000-4000-8000-000000000001' }
$acts | Select-Object status, actionType
"blocked=$(@($acts | Where-Object { $_.status -eq 'Blocked' }).Count)"
(Invoke-RestMethod "$base/quotes/a1111382-0000-4000-8000-000000000001").quote
```

**Expected:** `blocked=0`, a new `Applied` / `ResolvedToExisting` action, and the quote still reads
`Original fixture text.`

The incoming file omits `date`; `FieldMergeResolver` settles it back to the Source's stored `1999`, so
the resolved payload equals the stored row and the apply would write nothing. Before #382 the guard was
handed the *unresolved* payload, saw `date` going from `1999` to nothing, and staged `Blocked` — a hold
an operator had to clear by hand for a change that was never going to be made.

**On failure:** `blocked=1` is this document's whole point failing — the guard is still reading the
unresolved payload. `blocked=0` with *no* new action at all is a different failure: the row was never
re-evaluated, which the printed action list is there to distinguish.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-23
Remove-Item -Recurse -Force "$temp\382-imports", "$temp\382-mark-complete.json"
```
