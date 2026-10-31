# /version reports old counts after an import

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#468](https://github.com/DutchJaFO/Quotinator/issues/468)

## Symptom

`POST /api/v1/import` answers `200` and the content is served, but `GET /api/v1/version` still reports the counts
from before the import, for example:

```
"database":{"schemaVersion":9,"quotes":0,"sources":0,"characters":0,"people":0,"series":0,"seasons":0,"universes":0,"stageDirections":0,"soundCues":0,"conversations":0}
```

while `GET /api/v1/quotes` returns the imported quotes in the same moment.

## Does it prevent the app or API from functioning?

**No.** The import succeeded and the content is stored and served correctly. Only the counts `/version` reports are
out of date.

## Cause

The counts are read from the database at the end of startup, a reseed and a Reset, and held in memory. An API import
does not refresh them. A file seeded from `{dataDir}/imports/` at startup is counted correctly, because startup
refreshes them afterwards.

The database figures the UI shows read the same values, so they may lag the same way. That has not been tested.

## Remedy

To see the true numbers now, read `totalCount` from the list endpoints, such as `GET /api/v1/quotes` or
`GET /api/v1/masterdata/sources`. A restart also corrects the figures, but nothing needs restarting: no data is at
risk. No new issue should be filed for this: #468 covers it.

## Notes

This is not [Every database count reads 0 after a startup problem](degraded-startup-shows-every-count-as-zero.md),
where the application is degraded and `/version` answers `503`. Here `/version` answers `200` with `status=ready`.

Measured 2026-10-31. `database-lifecycle/03` carries the live check, and this entry goes when #468 is fixed.
