# Bulk-deciding a staged batch via file export and re-import, in both wire formats

**Smoke:** no
**Traces to:** #163

## Preconditions

`GET /import/actions/export` flattens every decidable field of a batch's `Pending`/`Decided`/`Blocked`
Modify actions into rows; `POST /import/actions/bulk-decide` reads an edited version of that export
back and applies each row's decision.

A staged batch with pending Quote Modify actions is needed — the `review` import below produces one and
must return `202`.

## Determinism

**Submitting export's own unmodified output back into bulk-decide must round-trip cleanly with zero
errors. That exact scenario caught a bug no unit test could.**

ASP.NET's app-wide camelCase JSON default (`ConfigureHttpJsonOptions` in `Program.cs`) means export's
output is genuinely camelCase, but `ParseJsonRows`'s `element.Deserialize<ImportActionFieldRow>()` call
had no explicit `JsonSerializerOptions` and silently fell back to `System.Text.Json`'s case-sensitive,
PascalCase-only library default. Every row failed with "missing required properties" despite the data
being present.

**Every unit-level round trip used bare `JsonSerializer` calls on both sides, which silently agreed on
PascalCase and never exercised the app's real configuration.** Only a live HTTP round trip through the
actual pipeline surfaces this class of bug — which is why this test runs the export output back in
unedited rather than hand-writing an input.

- `actionsDecided` is compared to **the batch's own pending-action count**, derived in the same run, not
  a fixed number.
- The malformed-row case edits exactly one row and leaves the rest untouched, so "one bad row never
  aborts the rest" is observable rather than inferred.

## Steps

**Stage a batch and round-trip it as JSON:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
```

Note the returned `batchId`, then:

```bash
curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<batchId>&format=json" -o /tmp/export.json
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<batchId>" -F "file=@/tmp/export.json" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>"
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
```

**Repeat via CSV:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<new batchId>&format=csv" -o /tmp/export.csv
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<new batchId>" -F "file=@/tmp/export.csv" -F "format=csv" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<new batchId>&format=csv"
```

**Malformed-row resilience** — edit one row's `Decision` to an invalid value, leave the rest untouched:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<new batchId>" -F "file=@/tmp/export-with-one-bad-row.csv" -F "format=csv" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<new batchId>&format=csv"
```

**Unknown format, missing key, and no body at all:**

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" -F "batchId=<batchId>" -F "file=@/tmp/export.json" "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>&format=xml"
curl -s -w "\n%{http_code}\n" -X POST -F "batchId=<batchId>" -F "file=@/tmp/export.json" "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<batchId>"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/bulk-decide"
```

## Expected output

- The staging import returns `202`.
- The JSON round trip returns `200` with `errors: []` and `actionsDecided` matching the batch's own
  pending-action count.
- The CSV round trip also returns `200` with `errors: []`.
- The malformed row returns **`200`, never `422` for the whole request**, with exactly one entry in
  `errors[]` naming the bad row's `actionId`, and every other row's action still decided. "One bad row
  never aborts the rest of the file", matching the contract
  [`06-bodyless-request-validation.md`](06-bodyless-request-validation.md) covers for `POST /import`.
- Unknown `format` returns `422`; no `X-Api-Key` returns `401`.
- **No `batchId` and no body returns `422` with `"detail":"You must provide a batchId."`**

## Observed effect

Two live-only bugs, both found during #163's own T2 pass.

The camelCase deserialization failure described in Determinism was the first.

The second: a request with neither `batchId` nor a multipart body returned a bare, uninformative `400`
with no `detail`. The endpoint bound `IFormFile? file` directly as a minimal-API parameter, which
requires a form content-type to even attempt binding — so a request with no `Content-Type` or body
fails at the framework's own routing/binding layer rather than as a thrown exception, bypassing
`BadRequestExceptionHandler` entirely.

**That is the same bug class `POST /import` had fixed earlier** — see
[`06-bodyless-request-validation.md`](06-bodyless-request-validation.md) — and never retrofitted onto
this newer endpoint. Fixed by switching to `HttpRequest request` and checking `batchId`, then
`request.HasFormContentType`, before attempting to read the form.

## Cleanup

```bash
rm -f /tmp/export.json /tmp/export.csv /tmp/export-with-one-bad-row.csv
```
