# Every seed and import surface reports per-file, per-entity-type counts

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #221

## Preconditions

Nothing beyond the Fresh profile. This replaces the old flat `duplicates` count everywhere a seed or
import operation reports back, and every surface it checks — seed preview, reseed, reset, import,
import preview and the startup log — is reachable on the profile's own container.

## Determinism

- **The shape is what is asserted, not the numbers.** One entry per configured source file, each with
  a `fileName` and an `entityTypes` object; the counts inside are data.
- **The removed fields matter as much as the added ones.** `totalQuotes`, `uniqueQuotes` and
  `crossFileDuplicates` must be absent — a response still carrying them means the old shape survived
  somewhere.
- **All nine entity types must appear**, not the original four. That number is a property of the
  domain model rather than of the dataset, so it is a legitimate assertion.

## Steps

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/admin/database/seed/preview"
```

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
```

Repeat against `POST /admin/database/reset`:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reset"
```

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
```

Re-run the same call via `POST /api/v1/import/preview`:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import/preview"
```

```bash
docker logs qt-env 2>&1 | grep "\[Database - Stats\]"
```

## Expected output

**Seed preview** — `200` with a top-level `reports` array. **Not** `totalQuotes`, `uniqueQuotes` or
`crossFileDuplicates`, all removed. One entry per configured source file, each with a `fileName` and an
`entityTypes` object keyed by entity type (`Quote`, `Source`, …), each carrying
`new`/`modified`/`blocked`/`discarded`/`pending`/`stale` counts.

**Reseed** — `200` with all nine entity-type row counts —
`quotes`/`sources`/`characters`/`people`/`series`/`universes`/`stageDirections`/`soundCues`/`conversations`
— plus `reports` in the same per-file shape.

**Reset** — the same shape, but every count `0` and `reports` reflecting no activity. Reset no longer
reimports bundled or user content after rebuilding the schema (#156), so there is nothing to report.

**Import** — `200` with a top-level `report` (singular — one file, not an array) alongside the existing
`summary`/`conflicts`/`errors` fields, shaped like one entry from `reports`.

**Import preview** — the same `report` shape, because the report reflects the actual staged actions
regardless of whether the batch was applied.

**Startup log** — `[Database - Stats]` shows all nine counts, not just the original four.

## Observed effect

Not yet established as a captured record. The startup log line is itself an observed effect and is
asserted above.

## Cleanup

**The Fresh profile must be re-established after this test, not merely after the group it sits in.**
`POST /admin/database/reset` above wipes the database and — since #156 — deliberately does not reseed.
The curated import that follows it repopulates that one file and nothing else, so the container ends
this test holding neither the bundled seed nor the audit, notification and `Import_Action` rows the
first boot wrote. This test is in the smoke set, and the smoke tests that follow it read seeded data,
so leaving the container in that state fails them for a reason that has nothing to do with what they
verify.
