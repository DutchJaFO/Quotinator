# Reversing an applied batch undoes it, and re-importing resurrects the rows

**Smoke:** no
**Environment:** Fresh
**Traces to:** #59

## Preconditions

A genuinely `Applied` batch to reverse — the `newest-wins` import below produces one, returning `200`
with nothing left pending.

The out-of-order check at the end needs **at least one other batch applied after** the one being
reversed. That is true of a normal database with seed plus this import history, but it is a
precondition, not a given.

## Determinism

- **`newest-wins` is required for the setup import**, so it applies cleanly and there is an `Applied`
  batch rather than a pending one.
- **Reversal introduces no new action status.** Every action still reads `"status":"Applied"`
  afterwards; the batch's own record being gone is the only signal it was undone. Expecting a
  `Reversed` status would report a false failure.
- There is **no `GET /import-batches` listing endpoint**, so confirming the batch record is gone needs
  `GET /api/v1/admin/audit` or `Quotinator.Tools.DbInspector` against `Import_Batch` showing
  `IsDeleted=1`.

## Steps

**Apply a batch cleanly:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
```

Note the returned `batchId`, then:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
```

**Reverse the same batch again:**

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```

**Re-import after reversal — the resurrection path:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Airplane&field=source"
```

**Attempt an out-of-order reversal:**

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<an older batchId>"
```

## Expected output

- `preview=true` returns `200` **without changing anything**; the real call also returns `200`.
- The actions listing still shows every action `"status":"Applied"` — see Determinism.
- Reversing the same batch again returns `404`: already reversed, treated as absent.
- The re-import succeeds (`200`/`202`, **never a silent no-op**) and the curated quotes are reachable
  again via the search. This is the resurrection fix proven live, rather than only by
  `ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow`.
- The out-of-order reversal returns `422` — the strict LIFO stack rule: only the most recently applied
  batch still live may be reversed, regardless of whether it shares any entities with the older one.

## Observed effect

Not yet established as a captured record. The `IsDeleted=1` state on `Import_Batch` is the load-bearing
observation, since no action status changes to signal the reversal.

## Cleanup

None.
