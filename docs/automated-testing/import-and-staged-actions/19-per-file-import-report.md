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

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-import-19 --port 18619
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Read the seed preview

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18619/api/v1/admin/database/seed/preview"
```

**Expected:** `200` with a top-level `reports` array. One entry per configured source file, each with a
`fileName` and an `entityTypes` object keyed by entity type (`Quote`, `Source`, …), each carrying
`new`/`modified`/`blocked`/`discarded`/`pending`/`stale` counts.

### 3. Reseed

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" "http://localhost:18619/api/v1/admin/database/reseed"
```

**Expected:** `200`, with a row count present for each of
`quotes`, `sources`, `characters`, `people`, `series`, `universes`, `stageDirections`, `soundCues` and
`conversations`, plus `reports` in the same per-file shape.

### 4. Repeat against `POST /admin/database/reset`

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" "http://localhost:18619/api/v1/admin/database/reset"
```

**Expected:** the same shape, but every count `0` and `reports` reflecting no activity. Reset no longer
reimports bundled or user content after rebuilding the schema (#156), so there is nothing to report.

### 5. Import a single file

```bash
curl -s -X POST -H "X-Api-Key: smoketest" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:18619/api/v1/import"
```

**Expected:** `200` with a top-level `report` (singular — one file, not an array) alongside the existing
`summary`/`conflicts`/`errors` fields, shaped like one entry from `reports`.

### 6. Re-run the same call via `POST /api/v1/import/preview`

```bash
curl -s -X POST -H "X-Api-Key: smoketest" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:18619/api/v1/import/preview"
```

**Expected:** the same `report` shape, because the report reflects the actual staged actions regardless
of whether the batch was applied.

### 7. Confirm the removed fields are actually absent

On the seed-preview response specifically — an absence read by eye off a large JSON body is satisfied by
default, so it is counted instead:

```bash
curl -s "http://localhost:18619/api/v1/admin/database/seed/preview" > /tmp/seed-preview.json
grep -o 'totalQuotes\|uniqueQuotes\|crossFileDuplicates' /tmp/seed-preview.json | wc -l
grep -o 'fileName\|entityTypes' /tmp/seed-preview.json | wc -l
```

**Expected:** the first count is `0` — `totalQuotes`, `uniqueQuotes` and `crossFileDuplicates` are gone
— and the second is **non-zero**, since `fileName` and `entityTypes` are the fields that replaced them.

**The second count is the positive control, and without it the first proves nothing.** A pattern that
cannot match anything reports `0` for a removed field exactly as a genuinely removed field does; only
a field the same command *does* find separates them. `api-surface/04` shipped a removal check with no
control and passed it for weeks on patterns that could never match — see the index's *A removed or
added feature needs its own proof, alongside the normal behaviour*.

Reading the absence off the body by eye cannot fail either, which is why both are counted.

### 8. Confirm the startup line exists before reading it

`grep`'s own exit status is what distinguishes "the line is absent" from "the line is present and
wrong", and a bare `grep` in a pipeline discards it:

```bash
docker logs qt-import-19 2>&1 | grep -q "\[Database - Stats\]" && echo PRESENT || echo MISSING
```

**Expected:** `PRESENT`, before anything is read off the line.

**On failure:** `MISSING` means the line is absent entirely — wrong container, rotated log, never
emitted. A plain `grep` prints nothing and exits `1`, and that silence is indistinguishable from a pass,
so stop here rather than reading the next step's empty output as a result.

### 9. Read the startup line's counts

```bash
docker logs qt-import-19 2>&1 | grep "\[Database - Stats\]"
```

**Expected:** `[Database - Stats]` names every entity type above, not just the original four.

## Observed effect

Not yet established as a captured record. The startup log line is itself an observed effect and is
asserted above.

## Cleanup

```bash
rm -f /tmp/seed-preview.json
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-19
```
