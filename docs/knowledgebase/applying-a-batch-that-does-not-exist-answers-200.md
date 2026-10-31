# Applying an import batch answers 200, but nothing was imported

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#469](https://github.com/DutchJaFO/Quotinator/issues/469)

## Symptom

`POST /api/v1/import/actions/apply?batchId=<id>` answers `200` with an empty body, nothing was imported, and a
notification titled "The database holds no quotes" has gone from the active list.

## Does it prevent the app or API from functioning?

**No.** No content is changed. What it costs is a wrong answer: the call reads as a success when no such batch exists,
and the reseed recommendation is dismissed although the database still holds no quotes.

## Cause

The `batchId` does not name a batch: it was mistyped, or is not a valid id at all. This endpoint does not check that
the batch exists, and treats a batch with nothing pending as applied.

## Remedy

Check the id first: `GET /api/v1/import/batches/<id>` answers `404` for a batch that does not exist. Or apply through
`POST /api/v1/import?batchId=<id>`, which answers `404` itself. A dismissed recommendation changes nothing about the data:
run a reseed or import content as it advised. No new issue should be filed for this: #469 covers it.

## Notes

Measured 2026-11-01. `import-and-staged-actions/01` step 8 prints the status this call answers until #469 is fixed, and
this entry goes then.
