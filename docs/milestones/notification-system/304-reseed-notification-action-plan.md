# #304 — Notification + action: let the user trigger a reseed

**Status:** In progress (step 13)
**GitHub issue:** #304
**Tiers required:** T1, T2
**Depends on:** #278, #312, #319

> **Next action: re-run T1 on step 12's change.** Every step is implemented and all 33 verification rows
> are ✅, including the original T1 pass. Step 12 was added *because* of that pass and changes what the
> notifications page displays, so the developer confirming the new *Done* label in Visual Studio is the
> one thing left before this issue is `Waiting for release`.

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
  `SeedIfEmptyAsync` (`QuotinatorDatabaseInitializer.cs:85-91`), so trigger 1's hook point exists where
  the issue says it does. Line `:87` discards the resolution's `Results` today, keeping only
  `.EffectiveBatches`.
- `SourceRefreshOutcome.Updated` is produced by `SourceCacheUpdater` when new content is actually
  downloaded, distinct from `UpToDate`/`Failed`/`SkippedCollision`.

**Four claims in the issue body are stale — it was written on 2026-08-12, before #312 and #319 landed.**
They are corrected here rather than followed:

1. *"Trigger 1 needs the dedupe helper `Quotinator.Core` can actually reach … whichever lands first does
   the relocation."* Already done for **that** helper. #312 moved `NotificationSeeding` to
   `Quotinator.Data.Notifications`, which `Quotinator.Core` references. See correction 4 for the
   relocation that does still remain.
2. *"Trigger 2 … can keep using the existing `NotificationSeeding.SeedOnceAsync` as-is."* The signature
   changed: identity is now a typed metadata payload compared structurally, not a `dedupeKey` string.
   Both triggers need a payload type, not just trigger 1.
3. *"mirroring the existing `DatabaseReset` case exactly."* #312 gave
   `INotificationActionExecutor.ExecuteAsync` a metadata parameter specifically for this issue's
   "reseed *this* file" case. Mirroring `DatabaseReset` exactly would ignore the thing built for it —
   and would also copy two steps that do not apply to a reseed (see "Action" below).
4. **A second helper is misplaced, and #319 is what misplaced it.** `NotificationTranslations`
   — the helper that builds a producer's per-language title/body from `UI.*.json` — is `internal` to
   `Quotinator.Api.Startup` (`src/Quotinator.Api/Startup/NotificationTranslations.cs`). Notifications are
   Data-owned system content and `System_Notification` is ADR 018's own reference implementation of it,
   so notification text assembly belongs in `Quotinator.Data` regardless of which producer needs it —
   this is the same misplacement #312 corrected for `NotificationSeeding`, one layer over. Trigger 1's
   producer, in `Quotinator.Core`, is what makes it urgent rather than what defines where it goes. See
   "Text and translations" below.

One cost the issue understates: **widening the `DismissTriggerKey` CHECK requires a table rebuild.**
SQLite has no `ALTER TABLE … MODIFY CHECK` (CLAUDE.md's ADR 008 checklist, point 2), so adding `Reseed`
means create-new-table + copy + drop + rename. Adding a `NotificationMetadataKind` member widens a
second CHECK on the same table — so both go in **one** rebuild migration, not two.

## Design

### Two triggers, two hook points

| # | Condition | Where it writes | Why there |
|---|---|---|---|
| 1 | A source file's content changed upstream, on a database that was not empty | `QuotinatorDatabaseInitializer.OnInitialisedAsync`, after `SeedIfEmptyAsync` returns | Part of the import/refresh machinery — the same relocation principle #302/#303 follow, not a `Program.cs` producer reading exposed state afterward |
| 2 | A successful `POST /admin/database/reset` | `AdminEndpoints.cs`'s reset handler, at the existing `DismissByTriggerAsync(DatabaseReset)` call site (`:260`) | Reset is not an import/seed operation and has no hook inside the seeding loop |

Trigger 1 fires only when the database was **not** empty — if `SeedIfEmptyAsync` did real work, the new
content is already in, and there is nothing to recommend.

**The issue body says "right after `ResolveEffectiveBatchesAsync` returns"; the write actually belongs
after `SeedIfEmptyAsync`,** because the condition is defined by whether the seed did work and that is
not known before it runs. The refresh outcome comes from the resolution either way — keeping the whole
`SourceCacheResolution` rather than only `.EffectiveBatches` is what makes it available at the later
point.

### Knowing whether the seed did work

`SeedIfEmptyAsync` and `SeedIfEmptyInternalAsync` both return a bare `Task` and early-return when
`Sql.Quotes.CountAll` is non-zero (`QuotinatorDatabaseInitializer.cs:266-282`) — nothing reports back
whether seeding happened. Rather than change either signature, `OnInitialisedAsync` reads
`Sql.Quotes.CountAll` on the same connection *before* calling `SeedIfEmptyAsync`: a non-zero count is
precisely the issue's own "on a database that was not empty", and it is the same gate
`SeedIfEmptyInternalAsync` itself applies.

`HasPendingContentSeedAsync` was considered and is the wrong question — it is also true when Genres is
empty but Quotes is not, which is a genre reseed, not the content-freshness condition this trigger
describes.

### Schema

One table rebuild of `System_Notification` widening both CHECKs:

- `DismissTriggerKey IN ('DatabaseReset', 'Reseed')`
- `MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew', 'ReseedRecommended')`

Baseline updated to match in the same commit, per the schema-drift parity tests.

**Migration number is assigned at implementation time, not here.** `DataOwnedMigrations` currently ends
at 14 (`DatabaseInitializer.cs:122`, #319's translation backfill), so this issue's is 15 unless
something lands between. The end-of-milestone consolidation pass folds them all anyway.

### Dedupe scope: active-only, not full-history

`NotificationSeeding.SeedOnceAsync` compares against the **full history — active, expired and
dismissed** (`NotificationSeeding.cs:60-65`), deliberately, so a dismissed notification is not rewritten
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
| A reseed | `NotificationActionExecutor`'s new `Reseed` case, and `POST /admin/database/reseed` (`AdminEndpoints.cs:183`) | New wiring, both sides |
| Any import that populates content | `IImportActionService.ApplyBatchAsync`'s success path — the single choke point both `POST /import/` (with a `batchId`) and `POST /import/actions/apply` funnel through (`ImportEndpoints.cs:94`, `:347`), implemented by `SqliteImportActionService.ApplyBatchAsync` (`:383`) | New wiring; nothing dismisses on import today |

The import-side dismiss is what makes the second half of the answer ("or other import afterwards") hold
— without it, an operator who imports rather than reseeds leaves the notification active, and the next
Reset is silently deduped against it. Dismissing inside `ApplyBatchAsync` rather than at each endpoint
follows this milestone's own relocation principle: notification writes belong in the import machinery,
not bolted onto handlers reading its result afterward.

Today the only `DismissByTriggerAsync` call sites are `AdminEndpoints.cs:260` and
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

This producer writes two notifications with new user-facing text. #319 has landed, so the write-side
shape is known rather than assumed:

- Strings live in `i18ntext/UI.*.json` (en-GB baseline, plus `de` and `nl`), per the localisation
  checklist — never inline in the producer.
- `NotificationTranslations.Build(textSource, titleKey, bodyKey, titleArgs, bodyArgs)` builds the
  per-language set, and `NotificationTranslations.Original(...)` resolves the English text stored on the
  row itself. Both feed `SeedWhileUnresolvedAsync`'s `translations` parameter, which forwards to
  `INotificationWriter.WriteAsync`.
- `ContentChanged`'s body names the changed files, so its string is a template taking the file list as a
  `bodyArgs` parameter; `AfterReset`'s is fixed text.
- The stored original's language is `en`, which the entity supplies itself — `WriteAsync` has no
  `originalLanguage` parameter, and `NotificationTranslations.OriginalLanguage` is the constant the
  builder excludes a translation row for.

**`NotificationTranslations` moves to `Quotinator.Data.Notifications`, alongside `NotificationSeeding`
and `NotificationTranslation`.** Notifications are a feature of the database system, not of Quotinator's
own domain — ADR 018 makes `System_Notification` the reference implementation of Data-owned system
content, and nothing about assembling a notification's text is Quotinator-specific. Its only
domain-facing aspect is the metadata payload, whose *contents* describe a domain feature while the
mechanism carrying them does not. Putting the text builder in `Quotinator.Core` would assert the
opposite — that notifications are a Quotinator-only concept — and would strand the next
non-Quotinator consumer of `Quotinator.Data` exactly as the Api-internal placement strands this one.

**Its `IApiLocalizer` parameter cannot travel with it, so the dependency is inverted instead.**
`IApiLocalizer` is a `Quotinator.Core` type, and ADR 018's "Dependency edge" permits `Quotinator.Data`
to depend only on a project that is *already* domain-agnostic — which Core is not. So
`Quotinator.Data.Notifications` declares its own single-method abstraction,
`INotificationTextSource.ForEveryLanguage(key, args)`, and `IApiLocalizer` extends it. That is the only
member `NotificationTranslations` ever used, so `ApiLocalizer` satisfies it with no behavioural change —
the declaration moves onto the base interface and `Program.cs` registers `INotificationTextSource`
against the same instance. Data owns the contract, Core supplies the implementation, and Data's
domain-agnostic invariant holds.

Scoped to notifications rather than declared as a general localisation abstraction, per ADR 017's
reasoning: a generic contract designed against a single consumer risks being wrong in ways only a second
consumer reveals. If a second kind of Data-owned content ever needs per-language text, that is when the
name and shape get revisited.

`Quotinator.Core` and `Quotinator.Api` both reference `Quotinator.Data`, so trigger 1's producer and
#279's, #289's and #81's existing ones all reach it, and `NotificationTranslationSourceTests` continues
to cover it.

### Action

`NotificationActionExecutor` gains a `Reseed` case calling `IDatabaseInitializer.ReseedAsync()`, then
`DismissByTriggerAsync(NotificationDismissTrigger.Reseed)`. The plain admin endpoint
`POST /admin/database/reseed` dismisses the same way, so a reseed triggered either route clears the
recommendation.

`ReseedAsync(bool forceSourceRefresh = false)` is called with its default: the content is already
downloaded by the time trigger 1 fires, so forcing another network round-trip would be redundant.

**Two things the `DatabaseReset` case does are deliberately not copied.** `databaseHealth.MarkHealthy()`
and `appVersionTracker.RecordCurrentAsync(...)` both exist because Reset rebuilds the schema and wipes
`System_AppVersion` (`NotificationActionExecutor.cs:42-55`). A reseed replaces content within an intact
schema — it neither degrades health nor empties the version history — so calling either would assert a
recovery that never happened. This is the concrete shape of correction 3 above.

---

## Decisions

1. **Does trigger 2 (after Reset) need dedupe at all?** (2026-08-16) Answered *"only dedupe if we do no
   reseed or other import afterwards"* — dedupe holds only while the condition is unresolved. The
   existing `SeedOnceAsync` cannot express that (it compares against dismissed rows on purpose), so this
   is built as **option (a): an active-only dedupe variant, plus an import-side dismiss** (developer
   decision, 2026-08-16). Option (b) — a timestamp or `System_AppVersion` row in identity — was
   rejected: a field whose only purpose is defeating dedupe, which also re-notifies per Reset whether
   or not anything resolved the condition. See "Dedupe scope" and "What resolves the notification"
   above.

2. **Should trigger 1 fire when `Quotinator:AutoUpdateSources` is off?** (2026-08-16) **No** — confirmed
   as the issue reads. No network check happens, so there is nothing to detect. Verification row 12
   stands.

3. **Is `ContentChanged`/`AfterReset` the right split, or should trigger 2 be its own kind?**
   (2026-08-16) **One kind with a `Reason`**, as recommended. One registry entry, one payload type,
   executor stays single-cased.

4. **Where does the misplaced `NotificationTranslations` helper go?** (developer decision, 2026-08-30)
   **`Quotinator.Data.Notifications`, made public, with its `IApiLocalizer` parameter replaced by a
   Data-owned `INotificationTextSource` that `IApiLocalizer` extends.** Notifications are a feature of
   the database system and are not user-domain by design; only their metadata concepts relate to
   features of the user domain. `Quotinator.Core` was proposed first and rejected on exactly that
   ground — it would imply notifications are a Quotinator-only item. Duplicating the helper was
   rejected for the reason #312 relocated `NotificationSeeding` rather than copying it: two copies of a
   translation builder drift, and the drift is invisible until a user reads a half-translated
   notification.

5. **Where do this producer's message keys live, given `Quotinator.Core` cannot reach `ApiMessages`?**
   (developer decision, 2026-08-30) **`Quotinator.Data.Notifications.NotificationMessageKeys`.** Keys
   belong with the machinery that emits them, the same reasoning that placed the notification text
   builder there. The rejected alternative was a `Core → Constants` project reference keeping every key
   in `ApiMessages`; `Quotinator.Constants` is genuinely dependency-free so nothing prevented it, but
   adding an edge to the documented project graph to reach four string constants buys less than it
   costs. Accepted consequence: notification keys now live in two places, split by which project writes
   the notification rather than by what the text says. Trigger 2's keys follow trigger 1's.

6. **Does the `Quotinator.Data` → `Quotinator.Core` boundary guard belong in this issue?** (developer
   decision, 2026-08-30) **Yes.** It protects the dependency inversion this issue introduces, so it
   ships with it rather than as separate follow-up work.

**This question should have been settled during planning and was not.** The plan traced whether the
Core-side producer could reach `NotificationTranslations` — correction 4, and the reason
`INotificationTextSource` exists — and stopped one link short of asking the same thing about the message
keys that helper consumes. Both were answerable at planning time from the project files alone. Recorded
here because the failure was in the cross-check, not in the work: `process.md`'s Planning step 3 asks
what prior issues introduced that this issue must extend, and #319 introduced both the helper and its key
dependency; only one of the two was followed through.

**No open questions remain. This plan is ready to execute.**

---

## Steps

Red-first throughout, per `docs/testing-policy.md`: each step's tests are written and observed failing
before its implementation. Steps 1–5 are infrastructure with no behaviour visible until step 6.

### 1. Move the translation helper into Quotinator.Data

**Status:** ✅ Done

Declare `INotificationTextSource` in `Quotinator.Data/Notifications/` with the single member
`IReadOnlyDictionary<string, string> ForEveryLanguage(string key, params object[] args)`, and make
`IApiLocalizer` extend it — moving that member's declaration onto the base interface rather than
redeclaring it. `ApiLocalizer` needs no change beyond that; register `INotificationTextSource` in
`Program.cs` against the same singleton instance.

Then move `NotificationTranslations` from `Quotinator.Api/Startup/` to `Quotinator.Data/Notifications/`
(namespace `Quotinator.Data.Notifications`, per the file-placement rule), make it `public`, and swap its
`IApiLocalizer` parameters for `INotificationTextSource`. Update the three existing producers' `using`
lines and `NotificationTranslationSourceTests`.

No behaviour change, so its existing tests must stay green untouched — that is what proves the move was
clean, and it is why this step is red-first only in the sense that the build breaks until the
registration exists.

The boundary guard is proven the ordinary way, against input the test owns. The real repository cannot
supply the violating case — a `Quotinator.Data` → `Quotinator.Core` reference is circular and fails
restore before any test runs — so the walk is exercised against `.csproj` files the test writes itself,
one of which *does* contain the forbidden shape. That is the README's "generate the defective input at
run time" option, and it makes the walk's sensitivity a red-green fact rather than an assumption:
mutating it to stop recursing fails both the fixture test and the transitive control, confirmed
2026-08-30 and reverted.

### 2. Active-only dedupe helper

**Status:** ✅ Done

Add `NotificationSeeding.SeedWhileUnresolvedAsync`, identical to `SeedOnceAsync` except that it compares
against `INotificationReader.GetActiveNotificationsAsync()` rather than `GetPagedAsync(1, 0)`. Factor the
shared identity comparison out of `IdentifiesSameNotification` rather than copying it, so the two helpers
cannot drift in how they read a stored payload back.

`SeedOnceAsync` is not modified. Assert that as its own test: #279, #289 and #81 depend on its
full-history behaviour, and silently narrowing it would make all three re-announce themselves.

The shared half came out as `WriteUnlessAlreadyPresentAsync`, taking the candidate rows — the only
thing that differs between the two helpers — so the comparison and the write exist once. Rows 15 and 16
assert opposite outcomes through that one path (`SeedWhileUnresolvedAsync` writes again after a
dismissal, `SeedOnceAsync` still suppresses), which is what stops either from passing vacuously: a
change collapsing the two behaviours cannot leave both green.

Rows 16 and 17 are that pair.

### 3. Enums

**Status:** ✅ Done

`NotificationDismissTrigger.Reseed` and `NotificationMetadataKind.ReseedRecommended`, plus a new
`ReseedReason { ContentChanged, AfterReset }` in its own file under `Quotinator.Data/Enums/` per ADR 016.

### 4. Migration and baseline

**Status:** ✅ Done

One table rebuild of `System_Notification` widening both CHECKs — SQLite has no
`ALTER TABLE … MODIFY CHECK`, and both widenings belong in the same rebuild rather than two. Follow ADR
008's enum-backed-column checklist. Update `DataBaselineSql` to match in the same commit; the schema-drift
parity tests fail otherwise.

Landed as migration 15, `NotificationReseedTriggerMigrations.WidenDismissTriggerAndMetadataKind`.

**Two things the rebuild turned up, both fixed here rather than left for a later reader.**

`ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental` asserted a literal `14` for both
the replayed row count and the resulting version. That is the hardcoded-count shape
`docs/automated-testing/README.md` warns about — it goes stale whenever any milestone adds a migration,
and the reflex fix is to edit the digit rather than to ask whether the replay still did what the test
claims. Its claim is *one row per version*, so it now asserts exactly that (`DataSchemaVersion` equals the
row count) with a non-zero floor, and no longer needs touching next time.

The `NotificationSeedingTests` fixture hand-replays the migration list rather than calling the
initializer, so it needed migration 15 adding by hand. Worth knowing before writing the next
notification test: that fixture is a maintained copy of the sequence, and a new migration affecting
`System_Notification` has to be added to it or the tests fail on a CHECK rather than on their subject.

### 5. Metadata payload

**Status:** ✅ Done

`ReseedRecommendedMetadataDto : NotificationMetadataDto` carrying `Reason` and `ChangedFiles`, with
`ReleaseState = NotApplicable` and `IdentityComponents = [Reason, string.Join('\n', ChangedFiles)]`.
Register it in `NotificationMetadataKinds` — that registry's guard test fails until you do.

### 6. Trigger 1 producer — content changed upstream

**Status:** ✅ Done

Inject `INotificationReader`/`INotificationWriter`/`INotificationTextSource` into
`QuotinatorDatabaseInitializer`.
In `OnInitialisedAsync`, keep the whole `SourceCacheResolution` rather than only `.EffectiveBatches`
(`QuotinatorDatabaseInitializer.cs:87` discards `Results` today), read `Sql.Quotes.CountAll` before
`SeedIfEmptyAsync`, and afterwards write the notification when any `Results` entry is
`SourceRefreshOutcome.Updated` **and** that pre-seed count was non-zero.

Gate on `_autoUpdateSources`: no notification when auto-update is off, since no network check ran.

Landed as `RecommendReseedIfSourceContentChangedAsync`. Both gates were mutation-checked: disabling them
failed rows 11 and 12 specifically, confirmed 2026-08-30 and reverted, so neither negative row is passing
for an incidental reason. The changed-file list is ordered before it reaches identity, so the same set of
files cannot re-notify merely because a refresh reported them in a different order.

**Message keys went to `Quotinator.Data.Notifications.NotificationMessageKeys`, not `ApiMessages`** —
decision 5. This producer is the first outside `Quotinator.Api`, and `Quotinator.Core` does not reference
`Quotinator.Constants`. Keys sit with the machinery that emits them (ADR 018), and the strings themselves
stay in the same three `UI.*.json` files as everything else.

**Two shared test helpers moved to `Quotinator.Data.Testing`**, which exists for exactly this:
`TestNotificationReader` (was internal to `Quotinator.Data.Tests`, and `Quotinator.Core.Tests` needed a
real reader) and a new `NoOpNotificationTextSource`. The alternative was a second copy of the reader
builder in a second test project.

### 7. Trigger 2 producer — after Reset

**Status:** ✅ Done

In `AdminEndpoints.cs`'s reset handler, at the existing `DismissByTriggerAsync(DatabaseReset)` call site
(`:260`), write the `AfterReset` notification. Reset drops and rebuilds `System_Notification`, so this
write must come after `ResetAsync` returns — it lands in an empty table.

The handler gains `INotificationReader` and `INotificationTextSource` alongside the `INotificationWriter`
it already had. It goes through `SeedWhileUnresolvedAsync` like trigger 1 rather than writing directly:
the dedupe is a no-op here today, since Reset has just emptied the table, but the alternative encodes
"Reset always wipes notifications" into a second place, and #278's own comment at this call site already
records that a future non-wiping action would make that assumption wrong.

The refusal case is asserted too, and is not merely symmetry: a producer bolted on after the handler
rather than inside its success path would recommend a reseed after a reset that never ran.

### 8. Action

**Status:** ✅ Done

`NotificationActionExecutor` gains a `Reseed` case: `ReseedAsync()` with its default
`forceSourceRefresh` (the content is already downloaded by the time trigger 1 fires), then
`DismissByTriggerAsync(Reseed)` — and neither `MarkHealthy()` nor `RecordCurrentAsync(...)`, for the
reason given under "Action" above. Add `Reseed` to `CanExecute` so `NotificationTable` renders its
Run → Confirm control.

### 9. Dismiss wiring

**Status:** ✅ Done

`POST /admin/database/reseed` dismisses `Reseed` on success, and `SqliteImportActionService.ApplyBatchAsync`
dismisses it on its own success path.

This step is what makes step 2's active-only dedupe mean "unresolved" — without it the notification is
never dismissed, so it stays active forever and dedupes every later occurrence against itself.

`SqliteImportActionService` gains `INotificationWriter`, which meant updating 18 construction sites
across 13 test files; production resolves it through DI and needed no change. Required rather than an
optional trailing parameter on purpose: a null-defaulted dependency would let a test construct the
service and silently skip the dismiss, which is the exact failure this step exists to prevent.

**Rows 23 and 24 moved down a layer from where the plan put them.** They were written as
`AdminEndpointsTests.Reset_After{AReseed,AnImport}_WritesAFreshRecommendation`, but the endpoint tests
run against `NoOpDatabaseInitializer` and a fake writer with no reader behind it, so nothing there can
exercise a dedupe decision — the test would have asserted its own fake. They are now real-SQLite tests
in `NotificationSeedingTests`, against the helper that actually makes the call, with the dismiss step
standing in for whatever resolved the condition. The endpoint's own half of that wiring is rows 21 and
22, which is where an endpoint test can genuinely observe something.

### 10. Text and translations

**Status:** ✅ Done

Add the title/body keys to `i18ntext/UI.{en-GB,de,nl}.json` — `ContentChanged`'s body takes the changed
file list as a `bodyArgs` parameter, `AfterReset`'s is fixed text — and write both notifications through
`NotificationTranslations` (relocated in step 1) into `SeedWhileUnresolvedAsync`'s `translations`
parameter. `TranslationCompletenessTests` covers the key-completeness half; row 25 covers resolution.

Four keys, translated the same way every other UI string in this project is — the "never auto-translate"
rule in CLAUDE.md is scoped to *quote content*, and does not reach UI strings or changelog entries
(developer confirmation, 2026-08-30, correcting an extra review step this plan had briefly assumed).

Row 25 asserts the substitution reaches every language, not just English: a body template whose `{0}`
was only ever filled in for the original would still pass a test that checked English alone.

### 11. Docs

**Status:** ✅ Done

`docs/api-endpoints.md` — both `POST /admin/database/reset` and `POST /admin/database/reseed` change
observable behaviour (one now writes a notification, the other now dismisses one), so both descriptions
and their `[Description]` attributes are updated together per CLAUDE.md's sync rule.

A new T2 document, `docs/automated-testing/notifications-and-changelog/10-reseed-recommendation-and-action.md`,
added in the same commit as the feature per CLAUDE.md's "the list only grows". It exercises the producer
and the Run → Confirm action rather than asserting the absence of a complaint, and ends by proving the
remedy: run the action, then confirm the recommendation is gone and content is back — the positive
control `docs/automated-testing/README.md` requires of any document that provokes a state.

`unreleased` entries in `data/changelog/changelog.{en,nl,de}.json` in lockstep; `[Subsystem - Phase]`
prefixes on any new log lines.

Done: both endpoint descriptions and their `docs/api-endpoints.md` rows, the new T2 document plus its
index row and `Quotinator.slnx` entry, and `#304` added to `unreleased.issues` with a highlight and an
`added` entry in all three languages, `CHANGELOG.md` regenerated.

**No new log lines were added, so no `[Subsystem - Phase]` prefix was needed.** Both producers are
silent by design: a notification *is* the operator-facing signal here, and logging the same thing beside
it would say it twice.

The T2 document was not added to the README's second table. That table maps *legacy* numbers so
references written before the category split still resolve; a document written after it has no legacy
number, and inventing `44a` for it would both break the plain-integer rule and imply a history it does
not have.

**`.editorconfig` is not part of this step.** Per CLAUDE.md, a file joins the scoped `IDE0008` list
*the moment it is first touched*, with its `var` declarations converted in that same commit — so it
happens inside whichever step first opens each file, never batched here at the end. Listed as a
non-step so a reader does not go looking for it as one.

### 12. Distinguish "resolved by running the action" from "dismissed by the user"

**Status:** ✅ Done

**Found in T1 (2026-08-30).** The developer reset via REST, saw the recommendation Active with its
Run control, ran it, and the reseed completed — and the row then displayed **Afgewezen** (dismissed),
which reads as *"I declined to do this"*. The action had in fact been carried out.

The cause is that `System_Notification` records only `IsDismissed` plus `DismissedAt`. There is no record
of *why* a notification stopped being active, so a user clicking Dismiss and an action completing land in
the same state and the UI can only render one label for both.

**Not introduced by this issue** — #289's schema-overshoot notification carries the `DatabaseReset`
action and has behaved this way since #278 shipped. #304 is what makes it routine, since this is the
first notification whose action a user runs as a matter of course. Folded into this issue by developer
decision (2026-08-30) rather than filed separately, so the reseed recommendation never ships with a
state that misreports what the user did.

Work: a `NotificationDismissReason` enum in `Quotinator.Data/Enums/`, an enum-backed column with its
`CHECK` per ADR 008 — an `ALTER TABLE ... ADD COLUMN` with the constraint inline, which SQLite permits,
so no table rebuild — the baseline updated to match, the reason set at each dismiss site (the user's own
dismiss versus the three action-driven ones), the value carried on the response DTO, and a distinct UI
label in all three locale files.

Landed as migration 16 plus `NotificationDismissReason { Dismissed, Resolved }`. The user's own dismiss
records `Dismissed`; `DismissByTriggerAsync` — whose every caller is an action that did the work —
records `Resolved`. The label is *Done* / *Erledigt* / *Uitgevoerd*.

**A dismissed row with no recorded reason keeps the old label rather than being guessed into a bucket.**
Rows dismissed before this column existed genuinely have an unknown reason, and calling them "Done"
would invent history. That is why the enum has no `Unknown` member — `null` already means it.

**Three things this step turned up, none of them predicted:**

- `SafeValue<NotificationDismissReason?>` needs its own Dapper type handler, like every other
  enum-backed column. Without it the insert path throws `NotSupportedException` on *every* notification
  write, not only a dismissal — so the failure was loud rather than subtle.
- The CVE-2025-6965 aggregate guard flagged `DatabaseInitializer.cs` because a comment written in this
  step used the word *"having"*, which its `GROUP BY|HAVING` pattern matched, while `Math.Max(`
  elsewhere in the file matched its aggregate pattern. Reworded — but see step 13's note, because a
  guard trippable by English prose is worth knowing about.
- **Four separate test fixtures hand-replay the notification migration list**, and each needed updating.
  `NotificationTranslationTests` is the instructive one: its `SchemaThroughMigration11` array is
  deliberately frozen to define "the schema before #319", so the catching-up belongs in its
  `ApplyTranslationSchemaAsync` helper — editing the frozen array would have destroyed what it exists to
  express.

### 13. Verification

**Status:** 🔵 T1 outstanding — every other row ✅

Work the table below top to bottom. T2 before T1, per `docs/release-verification.md`.

Every row is ✅. T1 ran on 2026-08-30 and passed on its own terms — and found the state-label defect that
became step 12, plus a timestamp defect recorded below.

**Two `notification-system` process observations from the T1 pass, both about the guard rails rather
than the feature:**

- The CVE-2025-6965 aggregate guard scans whole source files, comments included, so English prose can
  trip it: the word *"having"* in a comment plus an unrelated `Math.Max(` in the same file is enough.
  The immediate fix is to reword, which is exactly the habit that would devalue the guard if it became
  routine — a real hit could be "fixed" the same way. Worth its own issue rather than a reword and
  silence.
- Four test fixtures hand-replay the notification migration list. Each new migration touching
  `System_Notification` has to be added to all four by hand, and the failure mode is a `no such column`
  error in tests unrelated to the change. #304 hit this twice, for two different migrations.

**Three defects in the new T2 document, found by running it rather than by reading it** — recorded in
the document itself, since each is a trap the suite has hit before:

1. Its readiness gate polled `/health` for a quote count. `/health` returns `{"status":"healthy"}` and
   nothing else, so the gate could never become true — and it hung rather than failing, exactly the
   shape `notifications-and-changelog/04` already records. Now reads `/quotes`' own `totalCount`.
2. `$rec.Count` printed empty for the single-match case, because PowerShell 5.1 unrolls a one-element
   array on return and a bare `PSCustomObject` has no `Count`. It failed precisely when the test was
   working, which is the README's own warning about this. **Fixed once at the observed site and not as
   a class, which left three more** — the zero case prints `0` from the unwrapped form, so the others
   looked correct until a clean end-to-end run reached step 4 and it printed empty. All four are
   wrapped now, and the document says the rule is the wrap rather than the site.
3. It counted every row carrying the recommendation's kind, including dismissed ones. `GET
   /notifications` returns full history — resolving the condition dismisses a row, it does not delete
   it — so the count never fell back to zero and step 3 failed against a correct application. This was
   the cause-2 case (the expectation was wrong, not the feature): the row read `isDismissed: true` at
   the moment the count read `1`.

**This issue's relevant T2 set**, per `docs/automated-testing/README.md`'s end-of-issue scope (the
designated smoke set, plus what this issue touched):

- `notifications-and-changelog/10` — new; this issue's own producer and action
- `notifications-and-changelog/01` — the startup notification system this producer joins
- `notifications-and-changelog/02` — structural dedupe, which `SeedWhileUnresolvedAsync` now varies
- `notifications-and-changelog/03` — the migration path, for the CHECK-widening rebuild
- `notifications-and-changelog/08` — per-language resolution, which this producer's text must satisfy
- `database-lifecycle/03` — Reset is a full wipe, the state trigger 2 fires from

**Not in scope:** per-file reseed. Step 5's payload carries `ChangedFiles` so the executor *could* narrow
a reseed to one file later, but `ReseedAsync` has no per-file overload and adding one is its own issue.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Moving `NotificationTranslations` to `Quotinator.Data.Notifications`, behind `INotificationTextSource`, changes no behaviour | Unit test | `NotificationTranslationSourceTests` — all 7 methods green with no edit at all, not even the `using` the plan expected: the file already imported `Quotinator.Data.Notifications` for `NotificationTranslation` |
| 2 | ✅ | `Quotinator.Data` still takes no project reference to `Quotinator.Core` after the move | Unit test | `RepositoryStructureTests.QuotinatorData_DoesNotReferenceQuotinatorCore` — walks references transitively, with two in-run positive controls (Data→Changelog direct, Core→Changelog only via Data) |
| 3 | ✅ | The reference walk actually detects a forbidden reference, and does not report an unreferenced project | Unit test | `RepositoryStructureTests.ProjectReferenceWalk_FindsAnIndirectReference_AndNotAnUnreferencedProject` — against `.csproj` fixtures the test writes, one containing the violating shape the real repo cannot hold. Red-green confirmed by mutating the walk to stop recursing, which failed this and row 2's control |
| 4 | ✅ | `NotificationDismissTrigger` gains `Reseed`; the `DismissTriggerKey` CHECK accepts it on both the baseline and incremental paths | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` (extended). Red first with `CHECK constraint failed: DismissTriggerKey ... IN ('DatabaseReset')`, so the widening is what turned it green |
| 5 | ✅ | The widened CHECKs still reject an unknown value | Unit test | Same test — an unknown `DismissTriggerKey` *and* an unknown `MetadataKind` must both still throw. Folded in rather than given its own method: it is the same rebuild's other half, and a rebuild that rewrote either CHECK too loosely fails here |
| 6 | ✅ | `NotificationMetadataKind` gains `ReseedRecommended`, with a registered payload type | Unit test | `NotificationMetadataKindsTests.EveryKind_HasARegisteredPayloadType` and `.EveryRegisteredPayloadType_ReportsTheKindItIsRegisteredUnder` — existing guards, and they did fail on the unregistered kind before the registry entry was added |
| 7 | ✅ | Baseline and incremental replay produce an identical `System_Notification` schema after the rebuild | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` — still green, which is what proves the rebuild preserved column order (`Title`…`OriginalLanguage` trail `IsDeleted` because migrations 5 and 12 added them by `ALTER TABLE`) |
| 8 | ✅ | `ReseedRecommendedMetadataDto` round-trips `Reason` and `ChangedFiles` through storage | Unit test | `ReseedRecommendedMetadataTests.Payload_RoundTripsReasonAndChangedFiles` — real SQLite, read back via the row's own `MetadataKind`; plus `.Identity_DiffersByChangedFileSet_AndByReason` as its control, since identity that matched everything would satisfy the round-trip just as happily |
| 9 | ✅ | A source file whose content changed on a non-empty database writes one `ActionRequired` notification | Unit test | `DatabaseInitializerTests.Initialise_ContentChangedOnNonEmptyDatabase_WritesReseedRecommendation` — real SQLite, through the initializer's own seeding path; asserts type, dismiss trigger and metadata kind, and is the positive control for rows 10–12 |
| 10 | ✅ | Nothing is written when no source content changed | Unit test | `DatabaseInitializerTests.Initialise_NoSourceContentChanged_WritesNoNotification` — same fixture, `UpToDate` instead of `Updated` |
| 11 | ✅ | Nothing is written when the database was empty — the seed already applied the new content | Unit test | `DatabaseInitializerTests.Initialise_EmptyDatabaseSeeded_WritesNoNotification` — the pre-seed count gate; fails when that gate is removed |
| 12 | ✅ | Nothing is written when `Quotinator:AutoUpdateSources` is disabled | Unit test | `DatabaseInitializerTests.Initialise_AutoUpdateSourcesDisabled_WritesNoNotification` — fails when that gate is removed |
| 13 | ✅ | A successful Reset writes one `ActionRequired` notification | Unit test | `AdminEndpointsTests.Reset_Success_WritesReseedRecommendation`, with `.Reset_WhenRefused_WritesNoReseedRecommendation` as its negative counterpart — a refused reset left the content in place, so recommending a reseed would be wrong |
| 14 | ✅ | The same unresolved condition restarting does not add a second notification | Unit test | `NotificationSeedingTests.SeedWhileUnresolvedAsync_SameIdentityTwice_WritesOnce` — real SQLite; asserts the first call *did* write, so the suppression is not vacuous |
| 15 | ✅ | A different set of changed files is a different notification | Unit test | `NotificationSeedingTests.SeedWhileUnresolvedAsync_DifferentChangedFiles_WritesAgain` — guards identity being the file set, not just the reason; asserts the same set *is* suppressed in the same run, so it cannot pass by writing everything |
| 16 | ✅ | `SeedWhileUnresolvedAsync` suppresses only against *active* rows — a dismissed one does not suppress | Unit test | `NotificationSeedingTests.SeedWhileUnresolvedAsync_PreviousDismissed_WritesAgain` — real SQLite; the behavioural difference from `SeedOnceAsync` that the whole design rests on, with row 14 as its positive control. Also asserts both rows survive: the dismissed one is resolved, not deleted |
| 17 | ✅ | `SeedOnceAsync`'s own full-history behaviour is unchanged — a dismissed row still suppresses | Unit test | `NotificationSeedingTests.SeedOnceAsync_PreviousDismissed_StillSuppresses` — regression guard for #279/#289/#81. Asserts the opposite outcome of row 16 through the same shared code path, so a change collapsing the two helpers cannot leave both green |
| 18 | ✅ | `CanExecute(Reseed)` is true, so `NotificationTable` renders the Run → Confirm control | Unit test | `NotificationActionExecutorTests.CanExecute_Reseed_ReturnsTrue` — a separate branch from `ExecuteAsync`, so it needs its own row |
| 19 | ✅ | Running the action reseeds and dismisses the notification | Unit test | `NotificationActionExecutorTests.ExecuteAsync_Reseed_CallsReseedAndDismissesMatchingNotifications` — also asserts `forceSourceRefresh` stays `false`, the one argument the call makes a choice about |
| 20 | ✅ | The `Reseed` case does **not** mark health or record an app version, unlike `DatabaseReset` | Unit test | `NotificationActionExecutorTests.ExecuteAsync_Reseed_DoesNotTouchDatabaseHealthOrAppVersion` — the deliberate difference from the case it otherwise mirrors, and the copy-paste this catches is the likeliest way to get it wrong |
| 21 | ✅ | `POST /admin/database/reseed` dismisses it too, not only the notification action | Unit test | `AdminEndpointsTests.Reseed_Success_DismissesReseedRecommendation` |
| 22 | ✅ | An import that populates content dismisses it | Unit test | `SqliteImportActionServiceTests.ApplyBatchAsync_Success_DismissesReseedRecommendation`, with `.ApplyBatchAsync_LeavesActionsPending_DoesNotDismissReseedRecommendation` as its counterpart — a batch that applied nothing closed no gap |
| 23 | ✅ | A resolved condition recurring writes a fresh notification rather than being deduped | Unit test | `NotificationSeedingTests.SeedWhileUnresolvedAsync_ConditionResolvedThenRecurs_WritesAgain` — real SQLite, asserted where the dedupe decision is actually made rather than through an endpoint whose fakes cannot exercise it. Fails under `SeedOnceAsync` |
| 24 | ✅ | A condition recurring **while still unresolved** does not write again | Unit test | `NotificationSeedingTests.SeedWhileUnresolvedAsync_ConditionRecursWhileUnresolved_DoesNotWriteAgain` — the control for row 23, which would otherwise pass equally well against dedupe being switched off entirely |
| 25 | ✅ | Title and body resolve in `de`/`nl`, with the changed file substituted into each | Unit test | `NotificationTranslationSourceTests.ReseedRecommended_TakesTitleAndBodyFromTheLocaleFiles` and `.ReseedRecommended_OriginalIsEnglish`. Sensitivity confirmed by removing one `nl` key: both this and the parity guard failed, then restored |
| 26 | ✅ | Every new locale key exists, non-empty, in all three files | Unit test | `TranslationCompletenessTests.AllLanguageFiles_HaveExactlyTheSameKeysAsBaseline` (existing) |
| 27 | ✅ | The producer and its remedy work in a real container, and running it resolves the condition | Live (T2) | `notifications-and-changelog/10-reseed-recommendation-and-action.md` — run verbatim on a fresh container 2026-08-30 after its own corrections, all 4 steps green: 799 quotes → reset → 0 quotes + 1 `actionrequired` recommendation → reseed → 799 quotes + 0 active → second reset → 1 again |
| 28 | ✅ | Migration applies cleanly to a database at the previous released schema | Live (T2) | Executed 2026-08-30 against a real `ghcr.io/dutchjafo/quotinator:1.8.3` database (799 quotes): `applying 12 pending Data migration(s) (version 3 → 15)`, `schema updated (data v15, app v5)`, no SQLite error, content intact. Then a Reset on that upgraded database wrote the recommendation — proving the rebuild's widened CHECK accepts `Reseed` on the **incremental** path, which row 4's baseline/incremental parity cannot show on its own |
| 29 | ✅ | T1 — the notification appears and its Run → Confirm action reseeds | Live (T1) | Developer's Visual Studio run, 2026-08-30: reset via REST at 16:17:54 wrote the recommendation Active with its Run control; running it reseeded (`reseed requested`, 799 quotes) and the row left the active list. It also found rows 32–33 |
| 30 | ✅ | A notification resolved by running its action reads as done, not as dismissed | Unit test | `NotificationTableTests.GetDisplayStatus_DismissedBecauseResolved_IsResolved`, with `.GetDisplayStatus_DismissedByUser_IsDismissed` as its counterpart and `.GetDisplayStatus_DismissedWithNoRecordedReason_IsDismissed` for pre-#304 rows. Plus `NotificationWriterTests.DismissByTriggerAsync_RecordsResolvedRatherThanDismissed`, and the reason recorded on the user's own dismiss |
| 31 | ✅ | The `DismissReason` CHECK rejects a value outside the enum | Unit test | `NotificationWriterTests.DismissReason_UnknownValue_IsRejectedByTheCheckConstraint` — ADR 008's negative half |
| 32 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — output captured in full: zero lines matching `: warning NNNN` or `: error NNNN`, summary `0 Warning(s) / 0 Error(s)` |
| 33 | ✅ | Full test suite green | Build | `dotnet test --configuration Release -m:1` — 10 `Test Run Successful.` lines, **3,692 passed / 0 failed** (814, 42, 2, 11, 16, 9, 1468, 1309, 5, 16), zero `Test Run Failed`/`Aborted`, zero warning or error lines anywhere in the captured output |
