# Every seed and import surface reports per-file, per-entity-type counts

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #221

## Preconditions

Nothing beyond the Fresh profile. This replaces the old flat `duplicates` count everywhere a seed or
import operation reports back, and every surface it checks — seed preview, reseed, reset, import,
import preview and the startup log — is reachable on a Fresh container.

## Determinism

- **The shape is what is asserted, not the numbers.** One entry per configured source file, each with
  a `fileName` and an `entityTypes` object; the counts inside are data.
- **The removed fields matter as much as the added ones.** `totalQuotes`, `uniqueQuotes` and
  `crossFileDuplicates` must be absent — a response still carrying them means the old shape survived
  somewhere.
- **Every entity type must appear, named — not counted.** The set is
  `quotes`/`sources`/`characters`/`people`/`series`/`universes`/`stageDirections`/`soundCues`/`conversations`,
  and it replaced an original four. Assert the names, never the number: a count is a property of the
  domain model rather than the dataset, but it still goes stale the moment a tenth type ships — and it
  goes stale the same way a migration number does, reading as a failure that gets "fixed" by editing
  the digit. A missing name is visible as a missing name; a new type simply is not in the list yet.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-19 --port 18619
$base = "http://localhost:18619/api/v1"

# The startup line writes each type as the human-readable plural it renders, which is deliberately not
# the API's field name — `stage directions`, not `stageDirections`. Asserting the API spelling against
# the log reports three types missing that are plainly there; measured 2026-08-26.
$expectedInLog = 'quotes', 'sources', 'characters', 'people', 'series', 'universes',
                 'stage directions', 'sound cues', 'conversations'
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Read the seed preview

```powershell
$preview = dotnet script scripts/testing/http.csx -- --url "$base/admin/database/seed/preview" --expect 200 | ConvertFrom-Json
"reports=$(@($preview.reports).Count)"
$preview.reports | Select-Object -First 1 | ForEach-Object {
  "fileName=$($_.fileName) entityTypes=$(($_.entityTypes.PSObject.Properties.Name) -join ',')"
  $_.entityTypes.PSObject.Properties | Select-Object -First 1 | ForEach-Object {
    "counts=$(($_.Value.PSObject.Properties.Name) -join ',')"
  }
}
```

**Expected:** `200` with a non-zero `reports` — one entry per configured source file, each with a
`fileName` and an `entityTypes` object keyed by entity type (`Quote`, `Source`, …), each carrying
`new`/`modified`/`blocked`/`discarded`/`pending`/`stale` counts.

### 3. Reseed

```powershell
$reseed = dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reseed" --expect 200 | ConvertFrom-Json
$reseed | Select-Object quotes, sources, characters, people, series, universes, stageDirections, soundCues, conversations
"reports=$(@($reseed.reports).Count)"
```

**Expected:** `200`, with a row count present for each of
`quotes`, `sources`, `characters`, `people`, `series`, `universes`, `stageDirections`, `soundCues` and
`conversations`, plus a non-zero `reports` in the same per-file shape.

### 4. Repeat against `POST /admin/database/reset`

```powershell
$reset = dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reset" --expect 200 | ConvertFrom-Json
$reset | Select-Object quotes, sources, characters, people, series, universes, stageDirections, soundCues, conversations
"nonZero=$(@($reset.PSObject.Properties | Where-Object { $_.Name -ne 'reports' -and $_.Value -is [int] -and $_.Value -ne 0 }).Count)"
"reports=$(@($reset.reports).Count)"
```

**Expected:** the same shape, `nonZero=0` and `reports=0`. Reset no longer reimports bundled or user
content after rebuilding the schema (#156), so there is nothing to report.

### 5. Import a single file

```powershell
$import = dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file data/sources/quotinator-curated.json --duplicate-resolution newest-wins --expect 200 | ConvertFrom-Json

"fileName=$($import.report.fileName)"
"entityTypes=$(($import.report.entityTypes.PSObject.Properties.Name) -join ',')"
"isArray=$($import.report -is [array])"
```

**Expected:** `200` with a top-level `report` (singular — `isArray=False`, one file rather than an
array) alongside the existing `summary`/`conflicts`/`errors` fields, shaped like one entry from
`reports`.

### 6. Re-run the same call via `POST /api/v1/import/preview`

```powershell
$importPreview = dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/preview" `
  --file data/sources/quotinator-curated.json --duplicate-resolution newest-wins | ConvertFrom-Json

"fileName=$($importPreview.report.fileName)"
"entityTypes=$(($importPreview.report.entityTypes.PSObject.Properties.Name) -join ',')"
```

**Expected:** the same `report` shape, because the report reflects the actual staged actions regardless
of whether the batch was applied.

### 7. Confirm the removed fields are actually absent

On the seed-preview response specifically — an absence read by eye off a large JSON body is satisfied by
default, so it is counted instead:

```powershell
$body = dotnet script scripts/testing/http.csx -- --url "$base/admin/database/seed/preview" --expect 200 | Out-String
"removed=$(([regex]::Matches($body, 'totalQuotes|uniqueQuotes|crossFileDuplicates')).Count)"
"replacements=$(([regex]::Matches($body, 'fileName|entityTypes')).Count)"
```

**Expected:** `removed=0` — `totalQuotes`, `uniqueQuotes` and `crossFileDuplicates` are gone
— and `replacements` is **non-zero**, since `fileName` and `entityTypes` are the fields that replaced
them.

**The second count is the positive control, and without it the first proves nothing.** A pattern that
cannot match anything reports `0` for a removed field exactly as a genuinely removed field does; only
a field the same command *does* find separates them. `api-surface/04` shipped a removal check with no
control and passed it for weeks on patterns that could never match — see the index's *A removed or
added feature needs its own proof, alongside the normal behaviour*.

Reading the absence off the body by eye cannot fail either, which is why both are counted.

### 8. Confirm the startup line exists before reading it

Whether the line is there at all is a separate question from whether it is right, and a search that
prints nothing answers neither:

```powershell
$log = docker logs qt-import-19 2>&1 | Out-String
"statsLines=$(([regex]::Matches($log, '\[Database - Stats\]')).Count)"
```

**Expected:** `statsLines` is non-zero, before anything is read off the line.

**On failure:** `statsLines=0` means the line is absent entirely — wrong container, rotated log, never
emitted. Step 9 would then print nothing, and that silence is indistinguishable from a pass, so stop
here rather than reading its empty output as a result.

### 9. Read the startup line's counts

```powershell
$log -split "`n" | Select-String -SimpleMatch '[Database - Stats]'
$statsLine = ($log -split "`n" | Where-Object { $_ -match '\[Database - Stats\]' }) -join ' '
"missingTypes=[$(@($expectedInLog | Where-Object { $statsLine -notmatch [regex]::Escape($_) }) -join ',')]"
```

**Expected:** `missingTypes=[]` — `[Database - Stats]` names every entity type, not just
the original four. Reported as names rather than a count, so a tenth entity type shipping later reads
as "not in the list yet" instead of a wrong number.

Observed 2026-08-26:
`799 quotes  461 sources  12 characters  3 people  30 series  7 universes  2 stage directions  1 sound cues  4 conversations`.

## Observed effect

Not yet established as a captured record. The startup log line is itself an observed effect and is
asserted above.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-19
```
