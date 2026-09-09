# A conflict seen for the first time is still staged on a later reseed

**Smoke:** no
**Environment:** Fresh
**Traces to:** #376

**Pairs with [`25`](25-an-already-reported-conflict-does-not-accumulate.md)**, which asserts the other
half — an already-reported conflict must not stage a duplicate. This document is the regression guard
for that suppression: it must be **green on both the pre-fix and post-fix images**, because a fix that
simply stopped staging unresolved actions would pass `25` perfectly and hide every new conflict.

## Preconditions

#376 makes the planner consult, before staging `Pending`/`Blocked`/`Stale`, whether the entity already
carries one. The risk that check introduces is over-reach: a conflict this pass is genuinely seeing for
the first time must still reach the review queue.

Beyond the Fresh profile: **nothing.** This test brings its own files, dropped into
`{dataDir}/imports/`, and asserts only against its own two Source ids.

**Why the user-imports folder rather than `POST /import`** — see
[`25`](25-an-already-reported-conflict-does-not-accumulate.md)'s own note. The reasoning is identical
and is not repeated here.

## Determinism

- **Two titles, not one.** The first disagrees from the cold start and is therefore already reported by
  the time the second appears. Without the first, the test cannot distinguish "new conflicts are still
  staged" from "no suppression happened at all".
- **Count distinct entities, not rows.** On a pre-fix image the already-known conflict duplicates as
  well, so the row count is `3` where the post-fix count is `2`. A row-count assertion therefore cannot
  be green on both images — it would be measuring the accumulation rather than this document's own
  question. Distinct entity ids is `2` on both.
- **`Quotinator__AutoPurgeUserImportActions=false` is mandatory** — same reason as
  [`25`](25-an-already-reported-conflict-does-not-accumulate.md).
- **The container must be restarted after the files are copied in, and then reseeded** — same two
  requirements as [`25`](25-an-already-reported-conflict-does-not-accumulate.md), for the same reason:
  the restart makes the user-imports batch exist, and only an explicit reseed actually reads it, since
  startup seeding runs against an empty database alone. Step 3 asserts the file count.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-26 --port 18626 `
  --env Quotinator__AutoPurgeUserImportActions=false
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18626/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy.

**On failure:** stop rather than running the rest against an app that never became healthy.

### 2. Write the fixture — two titles, one declared

```powershell
New-Item -ItemType Directory -Force "$temp\376b-imports" | Out-Null

$manifest = @'
{
  "duplicateResolution": { "default": "review" },
  "files": [
    { "file": "376b-a-quotes.json",  "name": "quotinator/376b-quotes" },
    { "file": "376b-b-sources.json", "name": "quotinator/376b-sources" }
  ]
}
'@
$quotes = @'
{
  "quotes": [
    { "id":"b1111376-0000-4000-8000-000000000001", "quote":"Quotinator 376b first line.",  "originalLanguage":"en", "source":"Quotinator 376b First Film",  "date":null, "character":null, "author":null, "type":"movie", "genres":[], "translations":{} },
    { "id":"b1111376-0000-4000-8000-000000000002", "quote":"Quotinator 376b second line.", "originalLanguage":"en", "source":"Quotinator 376b Second Film", "date":null, "character":null, "author":null, "type":"movie", "genres":[], "translations":{} }
  ]
}
'@
$sourcesOne = @'
{
  "quotes": [],
  "sources": [
    { "title": "Quotinator 376b First Film", "type": "movie", "date": "1991" }
  ]
}
'@
[IO.File]::WriteAllText("$temp\376b-imports\manifest.json",        $manifest,   [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\376b-imports\376b-a-quotes.json",   $quotes,     [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$temp\376b-imports\376b-b-sources.json",  $sourcesOne, [Text.UTF8Encoding]::new($false))

docker exec qt-import-26 sh -c "mkdir -p /data/imports"
docker cp "$temp\376b-imports\manifest.json"       qt-import-26:/data/imports/
docker cp "$temp\376b-imports\376b-a-quotes.json"  qt-import-26:/data/imports/
docker cp "$temp\376b-imports\376b-b-sources.json" qt-import-26:/data/imports/
docker restart qt-import-26 | Out-Null
```

**Expected:** the container restarts.

### 3. Reseed, then confirm the fixture was picked up and capture both Source ids

```powershell
Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
docker logs qt-import-26 2>&1 | Select-String 'file\(s\) processed' | Select-Object -Last 1
$all    = (Invoke-RestMethod "$base/masterdata/sources?pageSize=0").items
$first  = $all | Where-Object { $_.title -eq 'Quotinator 376b First Film'  }
$second = $all | Where-Object { $_.title -eq 'Quotinator 376b Second Film' }
"first=$($first.id) second=$($second.id)"
```

**Expected:** `seeding complete — 7 file(s) processed`, and both Sources exist.

**On failure:** `5 file(s) processed` means the user-imports batch was not seen — either the restart in
step 2 or the reseed above did not happen. Stop.

### 4. Confirm exactly one conflict is outstanding

```powershell
function DistinctUnresolved {
  (((Invoke-RestMethod "$base/import/actions?pageSize=0").items |
     Where-Object { $_.entityType -eq 'Source' -and
                    $_.entityId -in @($first.id, $second.id) -and
                    $_.status -in @('Pending','Blocked','Stale') }) |
   Select-Object -ExpandProperty entityId -Unique).Count
}
DistinctUnresolved
```

**Expected:** `1` — only the first title has been declared with a date, so only it disagrees.

### 5. Declare the second title too, and reseed

```powershell
$sourcesBoth = @'
{
  "quotes": [],
  "sources": [
    { "title": "Quotinator 376b First Film",  "type": "movie", "date": "1991" },
    { "title": "Quotinator 376b Second Film", "type": "movie", "date": "1992" }
  ]
}
'@
[IO.File]::WriteAllText("$temp\376b-imports\376b-b-sources.json", $sourcesBoth, [Text.UTF8Encoding]::new($false))
docker cp "$temp\376b-imports\376b-b-sources.json" qt-import-26:/data/imports/

Invoke-RestMethod -Method Post -Uri "$base/admin/database/reseed" -Headers $key | Out-Null
DistinctUnresolved
```

**Expected:** `2`. The first title's conflict was already reported and is not staged again; the second
is new and must reach the review queue.

**Measured on both images**, which is what makes this a regression guard: `1 → 2` distinct on each.
The underlying *row* counts differ — `1 → 3` pre-fix against `1 → 2` post-fix — which is the difference
`25` exists to catch and precisely why this document counts entities instead.

**On failure:** `1` means the check is over-reaching and suppressing a conflict nobody has seen — the
failure mode this document exists to catch, and one that `25` would not notice.

### 6. Tear down

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-26
Remove-Item -Recurse -Force "$temp\376b-imports"
```

**Expected:** the container and its volume are gone.
