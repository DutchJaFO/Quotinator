# #64 — Conflict resolution policy

**Status:** Partially done  
**GitHub issue:** #64  
**Depends on:** #63 (manifest field), #45 (per-import override), #58 (batch recording)

---

## Spec requirements

1. Three policies: `skip`, `newest-wins`, `review`
2. Default policy: `newest-wins`
3. Policy can be set at the manifest level via `duplicateResolution` in `manifest.json`
4. Policy can be overridden per-source via `Quotinator__DefaultConflictPolicy` config key
5. Policy can be overridden per-import run via query parameter on the import endpoint (needs #45)
6. `review` policy queues conflicts for human resolution; incoming record is not applied until resolved
7. Applied policy is recorded in the `ImportBatch` row (needs #58)

---

## Step status

- [x] `skip` policy implemented — existing record wins, new record discarded
- [x] `overwrite` policy implemented — new record wins (≈ spec's `newest-wins`)
- [x] Per-manifest policy via `duplicateResolution` field in `manifest.json`
- [x] Per-source config overrides via `Quotinator:DuplicateResolution:*`
- [ ] **Rename `overwrite` → `newest-wins`** — the spec uses `newest-wins` throughout
- [ ] **Default is currently `skip`** — spec default is `newest-wins`; change the default
- [ ] `Quotinator__DefaultConflictPolicy` config key — current key naming differs from spec (`Quotinator:DuplicateResolution:Default`)
- [ ] `review` policy — queues conflict to `ConflictQueue`; Blazor UI resolves or rolls back (deferred to v3)
- [ ] Per-import override via query parameter — needs #45
- [ ] Record applied policy in `ImportBatch` — needs #58

---

## Remaining work

### Rename `overwrite` → `newest-wins`

In `DuplicateResolutionPolicy` enum (or string values) and all references: rename `Overwrite`/`"overwrite"` → `NewestWins`/`"newest-wins"`.

Update `manifest.json` and `manifest.schema.json` values accordingly.

### Change default

`DuplicateResolutionPolicy.Default` should be `newest-wins`, not `skip`. Update `appsettings.json` and `DatabaseInitializer`.

### Config key normalisation

Align the config key to `Quotinator__DefaultConflictPolicy` (matching the spec) instead of `Quotinator:DuplicateResolution:Default`.

### `review` policy

When the policy is `review`, the incoming record is written to a `ConflictQueue` table instead of being applied. The existing record stays untouched. This applies at seeding time too — the seeder queues conflicts rather than skipping or overwriting.

The Blazor UI (v3 milestone) presents the queue. The user can either resolve each conflict individually or undo the entire import batch (soft-reset via #59). After resolving or rolling back, re-import runs the file again cleanly.

A `ConflictQueue` table schema is required — this may be added here or tracked as a separate issue.

### Per-import override

When #45 is implemented, the import endpoint accepts `?conflictPolicy=skip|newest-wins|review` to override the manifest/config default for that run.

### ImportBatch recording

When #58 is implemented, store the effective policy in `ImportBatches.ConflictPolicy`.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `skip` policy keeps existing record; incoming discarded | Unit test | No test exists |
| 2 | ❌ | `newest-wins` policy overwrites existing with incoming (currently named `overwrite`) | Unit test | No test exists |
| 3 | ❌ | Rename `overwrite` → `newest-wins` in enum, config values, manifest schema, and all references | Unit test | Not implemented |
| 4 | ❌ | Default policy is `newest-wins` (currently `skip`) | Unit test | Not implemented |
| 5 | ❌ | Config key is `Quotinator__DefaultConflictPolicy` (currently `Quotinator:DuplicateResolution:Default`) | Unit test | Not implemented |
| 6 | ❌ | `review` policy writes incoming record to `ConflictQueue`; existing record untouched | Unit test | Not implemented — requires `ConflictQueue` table |
| 7 | ❌ | Per-import override via `?conflictPolicy=` query parameter (needs #45) | Unit test | Requires #45 |
| 8 | ❌ | Applied policy recorded in `ImportBatch` row (needs #58) | Unit test | Requires #58 |
