# String-typed id fields render canonically over a real HTTP round trip

**Smoke:** no
**Traces to:** #210

## Preconditions

A running container with an admin key, and the bundled curated file available to import.

`batchId`, `entityId`, `existingBatchId` and `recordId` are `string`-typed, not `Guid`-typed, so unlike
`id` fields they get no automatic lowercase rendering from `System.Text.Json`'s `Guid` serialization
default. A `LOWER(...) AS ColumnName` wrap was added to `Sql.SystemImportActions.SelectColumns` and
`Sql.SystemAudit.SelectPaged` so they render canonically whatever casing is stored.

The import below uses `review` policy deliberately, so it produces pending actions to page through.

## Determinism

**Read this before treating a pass as meaningful.** Freshly generated `Guid`s render lowercase from
`GuidExtensions.ToCanonicalId()` regardless of the wrap under test, so **this run mainly confirms no
regression** — it does not exercise the read-time fix itself.

The actual fix, rendering an *already-uppercase stored* value as lowercase, is proven at the SQLite
integration tier by `ExistingBatchId_RoundTripsCorrectly`, which writes a deliberately mixed-case
fixture directly — bypassing capture-time canonicalization — and reads it back through this exact
query path.

A live run cannot easily manufacture pre-existing non-canonical data through the API alone, because
every write path now canonicalizes at capture time. That is a property of the system, not a gap in
this test, and it is why the unit-tier test is the primary evidence here.

## Steps

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=1"
curl -s "http://localhost:8080/api/v1/admin/audit?pageSize=1" -H "X-Api-Key: <your admin key>"
```

## Expected output

All of the following are lowercase:

- the import response's own `batchId`, and every `quoteId` under `pendingActionIds`
- the `/import/actions` response's `batchId`, `entityId` and `existingBatchId`
- the `/admin/audit` response's `recordId`

## Observed effect

Not yet established. See Determinism for what a pass here does and does not demonstrate — that
distinction matters more than the raw result.

## Cleanup

> **Outstanding.** This currently leaves its imported actions staged, on the assumption that another
> test will page through them. That is a dependency on execution order, which the index forbids.
> Recorded as a finding for #339's audit.
