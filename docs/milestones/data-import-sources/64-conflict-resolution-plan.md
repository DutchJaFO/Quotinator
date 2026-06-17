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
6. `review` policy queues conflicts for manual resolution (Blazor UI, v3 milestone)
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
- [ ] `review` policy — queues conflict records; Blazor UI needed to act on them (deferred to v3)
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

When the policy is `review`, insert the incoming record into a `ConflictQueue` table (or equivalent) instead of applying it. The Blazor UI (v3 milestone) presents the queue for manual resolution. The schema for the queue table can be defined here but the UI is deferred.

### Per-import override

When #45 is implemented, the import endpoint accepts `?conflictPolicy=skip|newest-wins|review` to override the manifest/config default for that run.

### ImportBatch recording

When #58 is implemented, store the effective policy in `ImportBatches.ConflictPolicy`.
