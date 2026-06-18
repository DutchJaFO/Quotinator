# #65 — Import endpoint: preview / dry-run

**Status:** Partially done  
**GitHub issue:** #65  
**Depends on:** #45 (import endpoint)

---

## Spec requirements

The spec's preview capability is the `preview=true` query parameter on the import endpoint (`POST /api/v1/quotes/import?preview=true`). It should:

1. Accept the same payload as the import endpoint
2. Run all validation and conflict detection
3. Return the full result shape (summary with imported/skipped/conflicts/errors counts)
4. Not write any records to the database (transaction rollback)

Response shape identical to `POST /api/v1/quotes/import` (see #45 plan).

---

## What currently exists

`GET /api/v1/admin/database/seed/preview` — returns a list of source files that would be scanned at startup with estimated quote counts and cross-file duplicate detection. This satisfies a different need (startup source preview) and is **not** the spec's preview feature.

The current implementation does not conflict with #65 — it can coexist. But it does not satisfy the issue requirements on its own.

---

## Step status

- [x] `GET /api/v1/admin/database/seed/preview` — startup source preview (different feature, keep it)
- [ ] `POST /api/v1/quotes/import?preview=true` — transaction-rollback preview on import payload (needs #45)

---

## Remaining work

This issue is a subset of #45. When #45 is implemented, the `preview=true` query parameter must:

1. Open a database transaction
2. Run the full import logic (validation, conflict detection, inserts)
3. Roll back the transaction
4. Return the result summary (what *would* have happened)

No additional endpoint is needed — `preview=true` is a query parameter on the import endpoint.

This issue can be closed when #45 is complete and the `preview=true` path is tested.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `POST /api/v1/quotes/import?preview=true` runs full pipeline without committing (transaction rollback) | Unit test | Requires #45 |
| 2 | ❌ | Response includes `summary` with `new`, `skipped`, `conflicts.sameId`, `conflicts.sameText` counts | Unit test | Requires #45 |
| 3 | ❌ | `sameId` conflicts include `fieldDiffs` listing only changed fields | Unit test | Requires #45 |
| 4 | ❌ | `sameText` conflicts have no `fieldDiffs` | Unit test | Requires #45 |
| 5 | ❌ | Active conflict policy (#64) applied during preview; policy-skipped records counted in `skipped` | Unit test | Requires #45 and #64 |
| 6 | ❌ | No `ImportBatch` row created for a preview run | Unit test | Requires #45 and #58 |
