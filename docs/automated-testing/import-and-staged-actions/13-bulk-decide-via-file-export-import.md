# Bulk-deciding a staged batch via file export and re-import, in both wire formats

**Smoke:** no
**Environment:** Fresh
**Traces to:** #163

## Preconditions

`GET /import/actions/export` flattens every decidable field of a batch's `Pending`/`Decided`/`Blocked`
Modify actions into rows; `POST /import/actions/bulk-decide` reads an edited version of that export
back and applies each row's decision.

Beyond the Fresh profile: a staged batch with pending Quote Modify actions is needed. This test stages
its own — the `review` import below produces one and must return `202`, which is what confirms the
precondition rather than assuming it.

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

**Malformed-row resilience — against a third, still-undecided batch.** This must not reuse either batch
above: both have already had every row decided, so "every other row is still decided" would be true
before the call and the test could not fail in the direction it exists to catch.

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
```

Note this **third** `batchId`, export it, and confirm nothing in it is decided yet:

```bash
curl -s "http://localhost:8080/api/v1/import/actions/export?batchId=<third batchId>&format=csv" -o /tmp/export3.csv
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<third batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

Now make the bad copy. Open `/tmp/export3.csv`, copy it to `/tmp/export3-bad.csv`, and in the copy
change **the first data row's `Decision` cell** to `not-a-choice` — a value no decision accepts. Leave
the header and every other row exactly as exported. Note that row's `actionId`; the response must name
it.

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  -F "batchId=<third batchId>" -F "file=@/tmp/export3-bad.csv" -F "format=csv" \
  "http://localhost:8080/api/v1/import/actions/bulk-decide?batchId=<third batchId>&format=csv"
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<third batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**The edit is described rather than scripted deliberately.** A one-line text transformation is not
something this repository writes in shell ([ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md));
if this test is ever automated, the edit belongs in `scripts/testing/` as a `.csx`, not inline here.

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
- **Malformed row** — before the call, the third batch's status tally shows its actions **undecided**.
  The call returns **`200`, never `422` for the whole request**, with exactly one entry in `errors[]`
  naming the edited row's `actionId`. After it, the tally shows every action decided **except** that
  one. "One bad row never aborts the rest of the file", matching the contract
  [`06-bodyless-request-validation.md`](06-bodyless-request-validation.md) covers for `POST /import`.

  **The before-tally is the load-bearing half.** Run against a batch that was already fully decided, the
  after-tally looks identical whether the call decided the remaining rows or did nothing at all — which
  is exactly how this check read before the third batch was introduced.
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
rm -f /tmp/export.json /tmp/export.csv /tmp/export3.csv /tmp/export3-bad.csv
```

Removing the exports does not undo what they decided. Three staged batches remain — the first applied,
the second decided but not applied, the third partly decided with one row rejected — along with the
curated file's re-imported rows. Restore the Fresh profile before the next test.
