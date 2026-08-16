# #304 — Notification + action: let the user trigger a reseed

**Status:** Planning
**GitHub issue:** #304 (open)
**Depends on:** #278 (done, released v1.8.0 — the mechanism), #312 (done, this branch — the schema and the relocated seeding helper), #319 (not started — sequenced ahead of this issue, so this producer supplies translated title/body from the start rather than being retrofitted)

> **Next action: execute the Steps — but not yet.** All three open questions are answered
> (2026-08-16) and the plan is complete: design, steps, and verification table. It is blocked only on
> sequencing, not on any decision.
>
> **This issue is sequenced after #319** (developer direction, 2026-08-16). #319 adds
> `OriginalLanguage` and a `System_NotificationTranslation` table and changes the write API to take
> translations; this issue introduces a new producer with new user-facing text, so building it first
> would mean writing that producer twice.

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

One table rebuild of `System_Notification` widening both CHECKs:

- `DismissTriggerKey IN ('DatabaseReset', 'Reseed')`
- `MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended')`

Baseline updated to match in the same commit, per the schema-drift parity tests.

**Migration number is assigned at implementation time, not here.** `DataOwnedMigrations` currently ends
at 11 (`DatabaseInitializer.cs:101`), and #319 lands first with migrations of its own, so this issue's
number depends on how many #319 takes. The end-of-milestone consolidation pass folds them all anyway.

### Dedupe scope: active-only, not full-history

`NotificationSeeding.SeedOnceAsync` compares against the **full history — active, expired and
dismissed** (`NotificationSeeding.cs:58-63`), deliberately, so a dismissed notification is not rewritten
on the next restart. That is right for #279/#289/#81, whose notifications describe an event that
happened once. It is wrong for this one, which describes a **condition that can recur**: dedupe must
hold only while the condition is unresolved, and a Reset following a reseed or import must notify again
(developer decision, 2026-08-16).

So this issue adds a sibling helper — `NotificationSeeding.SeedWhileUnresolvedAsync` — identical to
`SeedOnceAsync` except that it compares only against notifications still **active** (undismissed,
unexpired, not soft-deleted), which `INotificationReader.GetActiveNotificationsAsync` already returns.
`SeedOnceAsync`'s own behaviour is untouched; the existing three producers keep it.

The rejected alternative was putting a timestamp or `System_AppVersion` row into
`IdentityComponents` — a field whose only purpose is defeating dedupe, and one that re-notifies per
Reset whether or not anything resolved the condition.

This helper is shared infrastructure, not #304-local: #302 and #303 describe recurring conditions too
(a file reseeding cleanly, a reseed leaving actions pending) and are the likely next callers.

### What resolves the notification

Active-only dedupe only encodes "unresolved" if the notification is actually dismissed when the
condition is resolved. Two things resolve it, and **both** must dismiss:

| Resolver | Where | Status today |
|---|---|---|
| A reseed | `NotificationActionExecutor`'s new `Reseed` case, and `POST /admin/database/reseed` | New wiring, both sides |
| Any import that populates content | `IImportActionService.ApplyBatchAsync`'s success path — the single choke point both `POST /import/` (with a `batchId`) and `POST /import/actions/apply` funnel through (`ImportEndpoints.cs:94`, `:347`) | New wiring; nothing dismisses on import today |

The import-side dismiss is what makes the second half of the answer ("or other import afterwards") hold
— without it, an operator who imports rather than reseeds leaves the notification active, and the next
Reset is silently deduped against it. Dismissing inside `ApplyBatchAsync` rather than at each endpoint
follows this milestone's own relocation principle: notification writes belong in the import machinery,
not bolted onto handlers reading its result afterward.

Today the only `DismissByTriggerAsync` call sites are `AdminEndpoints.cs:225` and
`NotificationActionExecutor.cs:43` — both `DatabaseReset`.

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

`NotificationMetadataDto.ReleaseState` is `required` on the base type, so this payload must state one:
`NotificationReleaseState.NotApplicable` — a reseed recommendation is not about a release. `Version` and
`ContentHash` stay `null` for the same reason; the changed-file set is carried in `ChangedFiles`, where
it participates in identity, rather than being hashed into `ContentHash`.

**No text in the payload.** Per `NotificationMetadataDto`'s own remarks (developer direction,
2026-08-16), metadata is strictly non-text: the file names here are identifiers the renderer and the
action consume, not prose. Title and body — and the language they are written in — are columns on the
row, which is exactly what #319 formalises.

### Text and translations (#319)

This producer writes two notifications with new user-facing text, and #319 lands first, so both supply
translated title/body at write time rather than being retrofitted:

- Strings live in `i18ntext/UI.*.json` (en-GB baseline, plus `de` and `nl`), per the localisation
  checklist — never inline in the producer.
- `ContentChanged`'s body names the changed files, so its string is a template taking the file list as a
  parameter; `AfterReset`'s is fixed text.
- `OriginalLanguage` is `en`, matching every notification written to date.

The exact write-side signature comes from #319. If #319's shape turns out to differ from what is assumed
here, this section is what gets corrected — not the identity or dismiss design above, which #319 does not
touch.

### Action

`NotificationActionExecutor` gains a `Reseed` case calling `IDatabaseInitializer.ReseedAsync()`, then
`DismissByTriggerAsync(NotificationDismissTrigger.Reseed)` — mirroring the `DatabaseReset` case's
dismiss-on-success. The plain admin endpoint `POST /admin/database/reseed` dismisses the same way, so a
reseed triggered either route clears the recommendation.

`ReseedAsync(bool forceSourceRefresh = false)` is called with its default: the content is already
downloaded by the time trigger 1 fires, so forcing another network round-trip would be redundant.

---

## Decisions (2026-08-16)

2. **Should trigger 1 fire when `Quotinator:AutoUpdateSources` is off?** **No** — confirmed as the
   issue reads. No network check happens, so there is nothing to detect. Verification row 6 stands.

3. **Is `ContentChanged`/`AfterReset` the right split, or should trigger 2 be its own kind?** **One
   kind with a `Reason`**, as recommended. One registry entry, one payload type, executor stays
   single-cased.

1. **Does trigger 2 (after Reset) need dedupe at all?** Answered *"only dedupe if we do no reseed or
   other import afterwards"* — dedupe holds only while the condition is unresolved. The existing
   `SeedOnceAsync` cannot express that (it compares against dismissed rows on purpose), so this is
   built as **option (a): an active-only dedupe variant, plus an import-side dismiss** (developer
   decision, 2026-08-16). Option (b) — a timestamp or `System_AppVersion` row in identity — was
   rejected: a field whose only purpose is defeating dedupe, which also re-notifies per Reset whether
   or not anything resolved the condition. See "Dedupe scope" and "What resolves the notification"
   above.

**No open questions remain. This plan is ready to execute, once #319 has landed.**

---

## Steps

Red-first throughout, per `docs/testing-policy.md`: each step's tests are written and observed failing
before its implementation. Steps 1–4 are infrastructure with no behaviour visible until step 5.

### 1. Active-only dedupe helper

**Status:** ⬜ Not started

Add `NotificationSeeding.SeedWhileUnresolvedAsync`, identical to `SeedOnceAsync` except that it compares
against `INotificationReader.GetActiveNotificationsAsync()` rather than `GetPagedAsync(1, 0)`. Factor the
shared identity comparison out of `IdentifiesSameNotification` rather than copying it, so the two helpers
cannot drift in how they read a stored payload back.

`SeedOnceAsync` is not modified. Assert that as its own test: #279, #289 and #81 depend on its
full-history behaviour, and silently narrowing it would make all three re-announce themselves.

### 2. Enums

**Status:** ⬜ Not started

`NotificationDismissTrigger.Reseed` and `NotificationMetadataKind.ReseedRecommended`, plus a new
`ReseedReason { ContentChanged, AfterReset }` in its own file under `Quotinator.Data/Enums/` per ADR 016.

### 3. Migration and baseline

**Status:** ⬜ Not started

One table rebuild of `System_Notification` widening both CHECKs — SQLite has no
`ALTER TABLE … MODIFY CHECK`, and both widenings belong in the same rebuild rather than two. Follow ADR
008's enum-backed-column checklist. Update `DataBaselineSql` to match in the same commit; the schema-drift
parity tests fail otherwise.

### 4. Metadata payload

**Status:** ⬜ Not started

`ReseedRecommendedMetadataDto : NotificationMetadataDto` carrying `Reason` and `ChangedFiles`, with
`ReleaseState = NotApplicable` and `IdentityComponents = [Reason, string.Join('\n', ChangedFiles)]`.
Register it in `NotificationMetadataKinds` — that registry's guard test fails until you do.

### 5. Trigger 1 producer — content changed upstream

**Status:** ⬜ Not started

Inject `INotificationReader`/`INotificationWriter` into `QuotinatorDatabaseInitializer`. In
`OnInitialisedAsync`, keep the whole `SourceCacheResolution` rather than only `.EffectiveBatches`
(`QuotinatorDatabaseInitializer.cs:87` discards `Results` today), and after `SeedIfEmptyAsync` write the
notification when any `Results` entry is `SourceRefreshOutcome.Updated` **and** the seed did no work.

Gate on `_autoUpdateSources`: no notification when auto-update is off, since no network check ran.

### 6. Trigger 2 producer — after Reset

**Status:** ⬜ Not started

In `AdminEndpoints.cs`'s reset handler, at the existing `DismissByTriggerAsync(DatabaseReset)` call site
(`:225`), write the `AfterReset` notification. Reset drops and rebuilds `System_Notification`, so this
write must come after `ResetAsync` returns — it lands in an empty table.

### 7. Action

**Status:** ⬜ Not started

`NotificationActionExecutor` gains a `Reseed` case: `ReseedAsync()` with its default
`forceSourceRefresh` (the content is already downloaded by the time trigger 1 fires), then
`DismissByTriggerAsync(Reseed)`. Add `Reseed` to `CanExecute` so `NotificationTable` renders its
Run → Confirm control.

### 8. Dismiss wiring

**Status:** ⬜ Not started

`POST /admin/database/reseed` dismisses `Reseed` on success, and `IImportActionService.ApplyBatchAsync`
dismisses it on its own success path.

This step is what makes step 1's active-only dedupe mean "unresolved" — without it the notification is
never dismissed, so it stays active forever and dedupes every later occurrence against itself.

### 9. Text and translations (#319)

**Status:** ⬜ Not started

Add the title/body keys to `i18ntext/UI.{en-GB,de,nl}.json` — `ContentChanged`'s body takes the changed
file list as a parameter, `AfterReset`'s is fixed text — and write both notifications through #319's
translation-carrying write API with `OriginalLanguage = en`. `TranslationCompletenessTests` covers the
key-completeness half; row 17 covers resolution.

### 10. Docs

**Status:** ⬜ Not started

`docs/api-endpoints.md` if any endpoint description changes; `unreleased` entries in
`data/changelog/changelog.{en,nl,de}.json` in lockstep; `[Subsystem - Phase]` prefixes on any new log
lines.

**`.editorconfig` is not part of this step.** Per CLAUDE.md, a file joins the scoped `IDE0008` list
*the moment it is first touched*, with its `var` declarations converted in that same commit — so it
happens inside whichever step first opens each file, never batched here at the end. Listed as a
non-step so a reader does not go looking for it as one.

### 11. Verification

**Status:** ⬜ Not started

Work the table below top to bottom. T2 before T1, per `docs/release-verification.md`.

**Not in scope:** per-file reseed. Step 4's payload carries `ChangedFiles` so the executor *could* narrow
a reseed to one file later, but `ReseedAsync` has no per-file overload and adding one is its own issue.

---

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
| 8 | ❌ | The same unresolved condition restarting does not add a second notification | Unit test | Structural-identity dedupe against active rows, real SQLite |
| 9 | ❌ | A different set of changed files is a different notification | Unit test | Guards identity being the file set, not just the reason |
| 10 | ❌ | `SeedWhileUnresolvedAsync` suppresses only against *active* rows — a dismissed one does not suppress | Unit test | Real SQLite; the behavioural difference from `SeedOnceAsync` that the whole design rests on |
| 11 | ❌ | `SeedOnceAsync`'s own full-history behaviour is unchanged — a dismissed row still suppresses | Unit test | Regression guard for #279/#289/#81, which depend on it |
| 12 | ❌ | Running the action reseeds and dismisses the notification | Unit test | `NotificationActionExecutorTests`, mirroring the `DatabaseReset` case |
| 13 | ❌ | `POST /admin/database/reseed` dismisses it too, not only the notification action | Unit test | Endpoint test |
| 14 | ❌ | An import that populates content dismisses it | Unit test | `ApplyBatchAsync` success path — nothing dismisses on import today |
| 15 | ❌ | A Reset **after** a reseed writes a fresh notification rather than being deduped | Unit test | The recurrence case the developer's answer asks for; fails under `SeedOnceAsync` |
| 16 | ❌ | A Reset **after** an import likewise writes a fresh notification | Unit test | Same, via the step 8 import-side dismiss — the half that fails if only the reseed dismiss is wired |
| 17 | ❌ | Title and body resolve in `de`/`nl`, falling back to `en` when absent | Unit test | #319's resolution path, exercised by this producer's own strings |
| 18 | ❌ | Migration applies cleanly to a database at the previous released schema | Live (T2) | ADR 009, plus smoke-test 39e's intermediate-version check |
| 19 | ❌ | T1 — the notification appears and its Run → Confirm action reseeds | Live (T1) | Developer confirms in Visual Studio |
| 20 | ❌ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 21 | ❌ | Full test suite green | Build | `dotnet test --configuration Release -m:1` |
