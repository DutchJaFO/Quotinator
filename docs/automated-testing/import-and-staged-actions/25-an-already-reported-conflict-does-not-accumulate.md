# An already-reported conflict does not stage a duplicate on every reseed

**Smoke:** no
**Environment:** Fresh
**Traces to:** #376

**Pairs with [`26`](26-a-new-conflict-is-still-staged.md)**, which asserts the other half — a conflict
this reseed is seeing for the first time must still be staged. The two are separate documents on
purpose: each builds its own container and its own fixture, so neither can leave state that skews the
other, and a failure names which half broke.

## Preconditions

`Pending`, `Blocked` and `Stale` are never applied and never resolve on their own, so an entity
carrying one keeps its stored values. Before #376 only `Quote` consulted a check for this; every other
entity type re-derived the same conclusion on every reseed and staged a brand-new duplicate on top of
the action still awaiting review, growing without bound.

Beyond the Fresh profile: **nothing.** This test brings its own two files, dropped into
`{dataDir}/imports/`, and asserts only against its own Source id. A change to bundled content cannot
make it pass or fail.

**Why the user-imports folder rather than `POST /import`.** The bundled corpus stages **zero**
unresolved actions of any kind — measured, not assumed — so it cannot reach this state at all. And
`SqliteQuoteImportService` does not pass a `ConflictRuleLookup` to `ImportActionPlanner.PlanAsync`, so
the `review` branch's resolution never runs on the `POST /import` path either. Only
`QuotinatorDatabaseInitializer` supplies one, and `ManifestSeedPlanner.Plan` is directory-agnostic:
`{dataDir}/imports/` with its own `manifest.json` seeds through that initializer.

## Determinism

- **The disagreement has to live between two files.** A single file restating its own content produces
  `Unchanged` on every reseed. The first file's quote creates the Source with no date; the second
  file's `sources[]` entry declares one. The resulting `Pending` is never applied, so the stored row
  keeps no date and the next reseed reaches the same conclusion again — which is exactly the accumulation
  under test.
- **`Quotinator__AutoPurgeUserImportActions=false` is mandatory, not tidiness.** It defaults to `true`,
  and a batch reaching zero *pending* actions has its `Import_Action` rows deleted. This test's rows
  are `Pending` throughout, but the surrounding bundled batches are not, and the purge is what would
  make the listing below unreadable.
- **The container must be restarted after the files are copied in, *and* then reseeded.** Two separate
  requirements, and missing the second is the easier mistake. `SeedBatchesBuilder` only adds the
  user-imports batch when `Directory.Exists(importsDir)`, and `/data/imports` does not exist in a fresh
  volume — so the restart is what makes the batch exist at all. But startup seeding only runs against an
  empty database, and the restart's database is not empty, so the restart alone imports nothing: the
  logs after it show no seeding line whatsoever. An explicit reseed is what actually reads the fixture.
  Found by running this document: without the reseed, step 3's Source lookup returns nothing and every
  later step asserts against a Source that was never created.
- **Every listing is filtered to this test's own Source id.** The reseed stages actions for bundled
  content too; an unfiltered tally is satisfied by rows this test did not produce.
- **Assert the relationship across three reseeds, never a single absolute count.** One reseed showing
  `1` proves nothing — the defect is growth, so the claim is that the second and third passes report
  the same number as the first.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-25 --port 18625 `
  --env Quotinator__AutoPurgeUserImportActions=false
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18625/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Write the two-file fixture into `{dataDir}/imports/` and restart

```powershell
New-Item -ItemType Directory -Force "$temp\376-imports" | Out-Null

$manifest = @'
{
  "duplicateResolution": { "default": "review" },
  "files": [
    { "file": "376-a-quotes.json",  "name": "quotinator/376-quotes" },
    { "file": "376-b-sources.json", "name": "quotinator/376-sources" }
  ]
}
'@
$quotes = @'
{
  "quotes": [
    { "id":"a1111376-0000-4000-8000-000000000001", "quote":"Quotinator 376 fixture line.", "originalLanguage":"en", "source":"Quotinator 376 Fixture Film", "date":null, "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
$sources = @'
{
  "quotes": [],
  "sources": [
    { "title": "Quotinator 376 Fixture Film", "type": "movie", "date": "1999" }
  ]
}
'@
[IO.File]::WriteAllText("$temp\376-imports\manifest.json",        $manifest, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\376-imports\376-a-quotes.json",    $quotes,   [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\376-imports\376-b-sources.json",   $sources,  [Text.UTF8Encoding]::new($false))

docker exec qt-import-25 sh -c "mkdir -p /data/imports"
docker cp "$temp\376-imports\manifest.json"      qt-import-25:/data/imports/
docker cp "$temp\376-imports\376-a-quotes.json"  qt-import-25:/data/imports/
docker cp "$temp\376-imports\376-b-sources.json" qt-import-25:/data/imports/
docker exec qt-import-25 sh -c "ls /data/imports"

docker restart qt-import-25 | Out-Null
```

**Expected:** all three files listed, and the container restarts.

**The manifest lists the quotes file first.** Reversing the order makes the `sources[]` entry create
the Source with its date, leaving nothing to disagree about — the fixture then asserts nothing.

### 3. Reseed, then confirm the fixture was picked up and find its Source id

```powershell
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
docker logs qt-import-25 2>&1 | Select-String 'file\(s\) processed' | Select-Object -Last 1
$src = (Invoke-RestMethod "$base/masterdata/sources?pageSize=0").items |
         Where-Object { $_.title -eq 'Quotinator 376 Fixture Film' }
"id=$($src.id) date=$($src.date)"
```

**Expected:** `seeding complete — 7 file(s) processed` — five bundled plus this test's two — and the
Source exists with `date` empty.

**On failure:** `5 file(s) processed`, or no Source found, means the user-imports batch was not seen —
either the restart in step 2 did not happen, or the reseed above was skipped. The restart alone is not
enough: startup seeding only runs against an empty database. Every later step would then be asserting
against bundled content. Stop.

### 4. Read the unresolved count for this Source after the first seed

```powershell
function Unresolved($sourceId) {
  ((Invoke-RestMethod "$base/import/actions?pageSize=0").items |
    Where-Object { $_.entityType -eq 'Source' -and $_.entityId -eq $sourceId -and
                   $_.status -in @('Pending','Blocked','Stale') }).Count
}
$first = Unresolved $src.id
"after cold start: $first"
```

**Expected:** `after cold start: 1` — the declared date disagrees with the stored one and no rule
covers it, so exactly one action awaits review.

**On failure:** `0` means the disagreement never arose and the rest of the document asserts nothing.
Check step 3's `date` was empty.

### 5. Reseed twice and confirm the count never grows

```powershell
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
$second = Unresolved $src.id
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
$third  = Unresolved $src.id
"$first -> $second -> $third"
```

**Expected:** `1 -> 1 -> 1`. The conflict was reported once and every later reseed recognises it as
already reported.

**On failure:** `1 -> 2 -> 3` is the defect this document exists for — one fresh duplicate per reseed,
piled on an action still awaiting review.

### 6. Confirm it was reported rather than silently skipped

```powershell
((Invoke-RestMethod "$base/import/actions?pageSize=0").items |
  Where-Object { $_.entityType -eq 'Source' -and $_.entityId -eq $src.id }) |
  Group-Object actionType | Select-Object Name, Count
```

**Expected:** `AlreadyReported=2` — one per reseed — alongside `Modify=1`, `Add=1` and `Unchanged=2`
(the quotes file's own resolution of the same Source, which is not what this test asserts).

Suppressing the duplicate must not make the row vanish from the report: `Incoming` is derived from the
staged actions themselves, so staging nothing at all would drop it from the totals as well, and a
shrinking total reads as content the file stopped mentioning.

**On a pre-fix image there is no `AlreadyReported` group at all** — that kind did not exist — and the
`Modify` count is 3 rather than 1.

### 7. Tear down

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-25
Remove-Item -Recurse -Force "$temp\376-imports"
```

**Expected:** the container and its volume are gone.
