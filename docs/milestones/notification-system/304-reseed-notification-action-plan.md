# #304 — Notification + action: let the user trigger a reseed

**Status:** Planning
**GitHub issue:** #304 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism), #312 (done, this branch — the schema and the relocated seeding helper)

> **Next action: answer the three open questions below, then write the Steps.** This plan is not ready
> to execute. The design and verification table are complete, but questions 1 and 3 change the metadata
> payload's shape and therefore the tests, so numbering steps before they are answered would produce
> steps that need rewriting. Nothing here is blocked on code — only on those answers.

---

## Background

Reseeding is only reachable today via `POST /admin/database/reseed` with an admin API key. There is no
Blazor-reachable way to trigger it. Two situations should recommend one, and per the developer's own
direction on the issue, neither may reseed automatically in the background — each surfaces as an
`ActionRequired` notification the user runs explicitly, reusing #278's Run → Confirm flow.

## Verified against the code before planning

The issue's structural claims hold:

- `NotificationDismissTrigger` has exactly one member, `DatabaseReset`
  (`src/Quotinator.Data/Enums/NotificationDismissTrigger.cs`).
- `OnInitialisedAsync` calls `ResolveEffectiveBatchesAsync(forceRefresh: false)` and then
  `SeedIfEmptyAsync` (`QuotinatorDatabaseInitializer.cs:85-88`), so trigger 1's hook point exists where
  the issue says it does.
- `SourceRefreshOutcome.Updated` is produced by `SourceCacheUpdater` when new content is actually
  downloaded, distinct from `UpToDate`/`Failed`/`SkippedCollision`.

**Three claims in the issue body are stale — it was written on 2026-08-12, before #312 landed.** They
are corrected here rather than followed:

1. *"Trigger 1 needs the dedupe helper `Quotinator.Core` can actually reach … whichever lands first does
   the relocation."* Already done. #312 moved `NotificationSeeding` to `Quotinator.Data.Notifications`,
   which `Quotinator.Core` references. No relocation work remains for this issue.
2. *"Trigger 2 … can keep using the existing `NotificationSeeding.SeedOnceAsync` as-is."* The signature
   changed: identity is now a typed metadata payload compared structurally, not a `dedupeKey` string.
   Both triggers need a payload type, not just trigger 1.
3. *"mirroring the existing `DatabaseReset` case exactly."* #312 gave
   `INotificationActionExecutor.ExecuteAsync` a metadata parameter specifically for this issue's
   "reseed *this* file" case. Mirroring `DatabaseReset` exactly would ignore the thing built for it.

One cost the issue understates: **widening the `DismissTriggerKey` CHECK requires a table rebuild.**
SQLite has no `ALTER TABLE … MODIFY CHECK` (CLAUDE.md's ADR 008 checklist, point 2), so adding `Reseed`
means create-new-table + copy + drop + rename. Adding a `NotificationMetadataKind` member widens a
second CHECK on the same table — so both go in **one** rebuild migration, not two.

## Design

### Two triggers, two hook points

| # | Condition | Where it writes | Why there |
|---|---|---|---|
| 1 | A source file's content changed upstream, on a database that was not empty | `QuotinatorDatabaseInitializer.OnInitialisedAsync`, right after `ResolveEffectiveBatchesAsync` | Part of the import/refresh machinery — the same relocation principle #302/#303 follow, not a `Program.cs` producer reading exposed state afterward |
| 2 | A successful `POST /admin/database/reset` | `AdminEndpoints.cs`'s reset handler, at the existing `DismissByTriggerAsync(DatabaseReset)` call site | Reset is not an import/seed operation and has no hook inside the seeding loop |

Trigger 1 fires only when the database was **not** empty — if `SeedIfEmptyAsync` did real work, the new
content is already in, and there is nothing to recommend.

### Schema

Data migration 9, one table rebuild of `System_Notification` widening both CHECKs:

- `DismissTriggerKey IN ('DatabaseReset', 'Reseed')`
- `MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended')`

Baseline updated to match in the same commit, per the schema-drift parity tests.

### Metadata payload

`ReseedRecommendedMetadataDto : NotificationMetadataDto`, kind `ReseedRecommended`, registered in
`NotificationMetadataKinds` (the guard test fails otherwise). Carries **why** the reseed is recommended,
which is also what identifies it:

- `Reason` — `ContentChanged` or `AfterReset` (a new enum, `Enums/`, per ADR 016).
- `ChangedFiles` — the file names whose content changed, for `ContentChanged`; empty for `AfterReset`.

`IdentityComponents` is `[Reason, string.Join('\n', ChangedFiles)]`, so a different set of changed files
is a different notification while the same set restarting is not. This is also the payload the action
executor receives, which is what makes a future per-file reseed a change to the executor rather than to
the contract.

### Action

`NotificationActionExecutor` gains a `Reseed` case calling `IDatabaseInitializer.ReseedAsync()`, then
`DismissByTriggerAsync(NotificationDismissTrigger.Reseed)` — mirroring the `DatabaseReset` case's
dismiss-on-success. The plain admin endpoint `POST /admin/database/reseed` dismisses the same way, so a
reseed triggered either route clears the recommendation.

`ReseedAsync(bool forceSourceRefresh = false)` is called with its default: the content is already
downloaded by the time trigger 1 fires, so forcing another network round-trip would be redundant.

---

## Open questions — for the developer, not to be assumed

1. **Does trigger 2 (after Reset) need dedupe at all, or should every Reset re-notify?** A Reset leaves
   an empty database; the notification is dismissed by the following reseed. If an operator resets
   twice without reseeding, structural identity (`Reason = AfterReset`, no files) would suppress the
   second notification even though the condition is freshly true again. Options: (a) accept — the first
   notification is still active and says the same thing; (b) include the reset's timestamp or the
   `System_AppVersion` row in identity so each Reset notifies once. **Recommendation: (a)** — the
   notification is still on screen saying exactly the right thing, and (b) adds a field whose only
   purpose is to defeat dedupe.

2. **Should trigger 1 fire when `Quotinator:AutoUpdateSources` is off?** Requirement 5 says no
   notification when the setting is disabled, because no network check happens. Confirming that is the
   intent rather than "check anyway, just don't download".

3. **Is `ContentChanged`/`AfterReset` the right split, or should trigger 2 be its own kind?** They share
   an action (reseed) and a trigger, differing only in why. One kind with a `Reason` keeps the executor
   single-cased; two kinds would mean two registry entries and two payload types for one action.
   **Recommendation: one kind with `Reason`.**

---

## Steps

Numbered on approval of the above. Written out once the open questions are answered, since 1 and 3
change the payload's shape and therefore the tests.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `NotificationDismissTrigger` gains `Reseed`; the `DismissTriggerKey` CHECK accepts it and still rejects an unknown value | Unit test | Behavioural round-trip on both baseline and incremental paths, matching the existing `AcceptSameNotificationCheckConstraintValues` pattern |
| 2 | ❌ | `NotificationMetadataKind` gains `ReseedRecommended`, with a registered payload type | Unit test | `NotificationMetadataKindsTests` (existing guard — fails automatically if the registry entry is missing) |
| 3 | ❌ | Baseline and incremental replay produce an identical `System_Notification` schema after the rebuild | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` (existing, must still pass) |
| 4 | ❌ | A source file whose content changed on a non-empty database writes one `ActionRequired` notification | Unit test | Real-SQLite test against the initializer's own seeding path |
| 5 | ❌ | Nothing is written when no content changed, or when the database was empty (the seed already applied it) | Unit test | Same fixture, negative cases |
| 6 | ❌ | Nothing is written when `Quotinator:AutoUpdateSources` is disabled | Unit test | Same fixture, configuration off |
| 7 | ❌ | A successful Reset writes one `ActionRequired` notification | Unit test | Endpoint test at the existing `DismissByTriggerAsync` call site |
| 8 | ❌ | The same unresolved condition restarting does not add a second notification | Unit test | Structural-identity dedupe, real SQLite |
| 9 | ❌ | A different set of changed files is a different notification | Unit test | Guards identity being the file set, not just the reason |
| 10 | ❌ | Running the action reseeds and dismisses the notification | Unit test | `NotificationActionExecutorTests`, mirroring the `DatabaseReset` case |
| 11 | ❌ | `POST /admin/database/reseed` dismisses it too, not only the notification action | Unit test | Endpoint test |
| 12 | ❌ | Migration applies cleanly to a database at the previous released schema | Live (T2) | ADR 009, plus smoke-test 39e's intermediate-version check |
| 13 | ❌ | T1 — the notification appears and its Run → Confirm action reseeds | Live (T1) | Developer confirms in Visual Studio |
| 14 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 15 | ❌ | Full test suite green | Build | `dotnet test --configuration Release -m:1` |
