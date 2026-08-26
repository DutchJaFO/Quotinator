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

**Every step below reports how many values it checked as well as how many were wrong.** A field that
is absent, or a list that came back empty, otherwise reports zero offenders and reads exactly like a
pass — the shape the index calls out under *A test that cannot be confirmed has failed*.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-id-04 --port 18204
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Import the curated file under the `review` policy

```powershell
function Test-Canonical($values) {
  $checked = @($values | Where-Object { $_ })
  $wrong   = @($checked | Where-Object { $_ -cne $_.ToLowerInvariant() })
  "checked=$($checked.Count) notLowercase=$($wrong.Count) $($wrong -join ' ')"
}

$import = dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18204/api/v1/import" `
  --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202 `
  | ConvertFrom-Json

Test-Canonical @($import.batchId)
Test-Canonical $import.pendingActionIds
```

**Expected:** `202`, and both lines report `notLowercase=0` with a **non-zero** `checked` — the
import's own `batchId`, and every id under `pendingActionIds`, are canonical.

**On failure:** `checked=0` on the second line means the `review` policy staged nothing, so the reads
below would page an empty list and report canonical ids they never saw. Stop.

### 3. Read a pending staged action

```powershell
$action = (Invoke-RestMethod "http://localhost:18204/api/v1/import/actions?status=pending&pageSize=1").items[0]
Test-Canonical @($action.batchId, $action.entityId, $action.existingBatchId)
```

**Expected:** `notLowercase=0`, with `checked` counting the fields that are actually populated —
`existingBatchId` is legitimately null on an action with no prior batch, and is skipped rather than
counted as a pass.

### 4. Read an audit entry

```powershell
$entry = (Invoke-RestMethod "http://localhost:18204/api/v1/admin/audit?pageSize=1" -Headers @{'X-Api-Key' = 'smoketest'}).items[0]
Test-Canonical @($entry.recordId)
```

**Expected:** `checked=1 notLowercase=0` — the `/admin/audit` response's `recordId` is canonical.

## Observed effect

Not yet established. See Determinism for what a pass here does and does not demonstrate — that
distinction matters more than the raw result.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-id-04
```
