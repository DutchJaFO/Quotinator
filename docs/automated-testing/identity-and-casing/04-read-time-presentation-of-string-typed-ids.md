# String-typed id fields render canonically over a real HTTP round trip

**Smoke:** no
**Environment:** Fresh
**Traces to:** #210

## Preconditions

Beyond the Fresh profile: the import below uploads `data/sources/quotinator-curated.json` from the
repository itself, so the commands run from the repository root — the file comes from the working tree,
not from inside the container.

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

Run the **Fresh** profile, then:

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

The `review` import leaves a staged batch and its pending actions behind. Restore the Fresh profile
before the next test — do **not** leave them for a successor to page through, which is the
execution-order dependency the index forbids.
