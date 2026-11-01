# The review list is empty, but a file is waiting for review

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#474](https://github.com/DutchJaFO/Quotinator/issues/474)

## Symptom

`GET /api/v1/import/actions?status=...` or `?entityType=...` answers `200` with an empty `items` list, while the same request
without that filter returns rows.

## Does it prevent the app or API from functioning?

**No.** Nothing is lost and nothing is written. The empty answer is simply wrong about why it is empty.

## Cause

The filter value is not one the endpoint knows, usually a typing mistake. It is passed straight to the query, which finds no row
holding it, where `GET /api/v1/import/batches` refuses the same mistake with `422`.

## Remedy

Check the value against the list in the [endpoint reference](../api-endpoints.md). `status` takes `Pending`, `Decided`, `Applied`,
`Discarded`, `Blocked` or `Stale`, in any casing.
