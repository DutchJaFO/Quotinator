# #302 — Notification: confirm files that reseed cleanly with no review needed

**Status:** Waiting for release
**GitHub issue:** #302
**Tiers required:** T1, T2
**Depends on:** #278, #304, #312, #319

---

## Description

A reseed already reports a per-file breakdown, but only in the API response and the log. This issue
adds the "everything's fine" half of that feedback to the UI: one `Success` notification per file that
reseeded with nothing left to review.

## Scope revision — where the notification is written from

**Recorded 2026-08-12, relocated here from `overview.md` 2026-08-22.** The notification write moves
into the seeding pipeline itself rather than being a separate call bolted onto `AdminEndpoints.cs`
after `ReseedAsync()` returns. Per developer direction: new notification content comes from the same
mechanism that already handles import content, not one-off `INotificationWriter.WriteAsync` calls
scattered across unrelated call sites.

`QuotinatorDatabaseInitializer`'s own per-file seeding loop already carries the exact signal at the
exact moment — `_actionService.ApplyBatchAsync(batchIdStr, ...)` returning `null` means the batch
fully applied with zero pending actions. The notification is written from inside that branch, not
reconstructed afterward from a snapshot report.

## Scope changes

**Two of the issue body's five items are already delivered, and one names a mechanism that no longer
exists. Reviewed against the code 2026-08-30.**

1. **The issue's item 3 — where the shared dedupe-write helper lives — was settled by #312, not here.**
   `NotificationSeeding` now lives in `src/Quotinator.Data/Notifications/NotificationSeeding.cs`, and
   its own class documentation records the decision for #302, #303 and #304 together. It is reachable
   from `Quotinator.Core`, which already calls `SeedWhileUnresolvedAsync` from
   `QuotinatorDatabaseInitializer`. Covered by
   `Quotinator.Data.Tests.Notifications.NotificationSeedingTests`; no work and no verification row
   belongs to this issue.

2. **The issue's item 1 — injecting `INotificationWriter` into `QuotinatorDatabaseInitializer` — was
   delivered by #304.** `INotificationReader`, `INotificationWriter` and `INotificationTextSource` are
   already constructor parameters. The old "shared with #303: whichever lands first does this step"
   arrangement no longer applies to either issue.

3. **The issue's item 4 — "rely on the existing configured expiry to age them out" — cannot be done as
   written.** #312 removed `Quotinator:NotificationDefaultExpiryHours` entirely (recorded in
   `changelog.en.json`'s unreleased section), and omitting `expiresAt` now means *never expires*
   rather than *apply the default*. **Developer decision, 2026-08-30:** these notifications carry no
   expiry — the operator dismisses them.

   The issue's "no dedupe" half does not follow from that, and was reversed in the same session:
   dedupe runs through `NotificationSeeding.SeedWhileUnresolvedAsync` (see step 5). Without it,
   "never expires" would leak — a row per file per reseed with nothing to clear them. With it, at most
   one live row exists per distinct per-file result, which is what makes the no-expiry decision hold.

4. **Typed metadata is in scope, and it costs a migration.** The issue names no payload, but every
   #312-era notification carries a `NotificationMetadataKind`, and a new member needs a table-rebuild
   migration widening the `MetadataKind` CHECK — the shape #304 already paid for in migration 15.
   **Developer decision, 2026-08-30:** add the kind, the payload DTO and the migration, rather than
   writing with `metadata: null`. Without it the file name and counts would exist only inside body
   prose, which #308 cannot lay out and no action can consume.

---

## Deviation from the planned step order

**The steps below were not executed in their numbered order.** Recorded here because the order is the
plan's substance, not its presentation: steps are discrete because each supplies something the next one
needs, so a deviation after the planning phase means the planning pass failed to analyse the plan for
completeness. If a bug surfaces in this issue's code, this section is the first place to look.

**Planned:** 1 → 9.
**Actual:** 2, 3, 6, 9, 1, 4, 5, 6, 7, 8, 9. Only 2 → 3 ran in sequence; step 1 ran fifth.

**What the planning pass missed** — three ordering facts that were knowable before any code was
written, and each of which forced the deviation:

1. **Step 6's constructor injection is a compile prerequisite for steps 1, 5 and 7's tests.** None of
   those tests can compile until `QuotinatorDatabaseInitializer` takes `IAppVersionTracker` and
   `IVersionService`. Step 6 therefore belonged before step 1, not sixth.
2. **Step 9 edits the same two constructors as step 6.** Placed last, it forced those call sites to be
   rewritten twice, so it was pulled forward to sit beside step 6 rather than being done in its
   numbered position.
3. **Steps 1, 5 and 7 are one code path, not three.** The reseed-only gate, the write, and the
   zero-files case all live in `SeedIfEmptyInternalAsync` and its clean-apply branch, and share one
   test batch. Three separate numbers implied three separately observable steps, which they are not.

A defensible ordering, had this been analysed at planning time, is: 2, 3, 6+9, 4, 1+5+7, 8 — with
1/5/7 as a single step.

**What the deviation cost, and what it may later explain:**

- **`Status: In progress (step N)` was never truthful after step 3**, because more than one step was in
  flight. That is what produced the visible symptom: step 1 read `Not started` while 2 and 3 read
  `Done`, which was accurate — step 1 genuinely had not been started, fifth in line.
- **Red-first collapsed from per-step to per-cluster.** Verification rows 1–12 went red together and
  green together in one pass, so no individual row was observed failing for its own reason. If one of
  them later turns out to pass for an accidental reason, this is why it was not caught here.
- **Step 7's row (row 4, zero configured files) was never observed genuinely red.** It passed vacuously
  in the red run — nothing wrote a notification yet, so "no notification" was trivially true — and only
  became meaningful once the producer existed. The same vacuous-pass risk was spotted and guarded for
  row 8 during that batch, but row 4 was left unguarded, which is the per-cluster review missing what a
  per-step one would have caught on its own step.

  **Closed while writing this section.** The test now carries its own positive control: it reseeds with
  files against the same database and asserts confirmations appear, before showing that a fileless
  reseed adds none. Proven by disabling the producer — the old form still passed, the new form fails on
  the control.

---

## Steps

### 1. Tell the seeding loop whether this run is a reseed

**Status:** ✅ Done — `SeedIfEmptyInternalAsync(..., bool isReseed)`

`SeedIfEmptyInternalAsync` is shared by cold start (`OnInitialisedAsync` → `SeedIfEmptyAsync`) and by
reseed (`OnReseedAsync`), and nothing in it says which caller invoked it — so the issue's "reseed only,
never the first empty-database seed" rule cannot be read at the point the notification is written.
Thread an explicit flag through both call sites.

Deliberately a parameter rather than reconstructing the decision afterward from `LastSeedReport`: the
issue's own scope revision above puts the write inside the clean-apply branch, and a snapshot read
after the loop is exactly the reconstruction that revision rejected.

### 2. Add the `ReseedFileApplied` metadata kind and its payload

**Status:** ✅ Done

A new `NotificationMetadataKind.ReseedFileApplied` member, registered in
`NotificationMetadataKinds.PayloadTypes`, plus `ReseedFileAppliedMetadataDto` carrying the file name
and a **per-entity-type added/modified breakdown**. `ReleaseState` is `NotApplicable` — a reseed is not
about a release.

**Developer decision, 2026-08-30: the full per-entity breakdown, not the two quote-only counts the
branch already computes.** `imported`/`updated` in the clean-apply branch both filter on
`EntityType == ImportActionEntityTypes.Quote`, so they say nothing about the Sources, Characters,
People, Series and Universes the same file adds — a file that only corrected a Source's date would
report "0 added, 0 modified" and read as a no-op. The counts are taken from the same `actions` list,
grouped by `EntityType`, with the existing two locals left untouched (they feed
`Import_Batch.RecordCount`, a separate contract).

Shape: `FileName`, `Origin`, plus a list of `ReseedEntityCountDto` (`EntityType`, `Added`, `Modified`),
one entry per type that has a non-zero count — an all-zero type states nothing, matching the payload's
own omit-what-is-unset rule. Entity type values come from `ImportActionEntityTypes`, never a second set
of literals.

**`Origin` was added after the T2 pass, not planned** (developer decision, 2026-08-30). The bundled +
user-imports variant produced two confirmations both named `quotinator-curated.json`, because
`FileName` is a bare name and both directories can hold the same one — a user copying a bundled file to
customise it produces exactly that. They were both written that run only because their breakdowns
happened to differ; had both been no-ops they would have shared an identity and the second would have
been silently suppressed. `Origin` is `FileResourceOrigin`, the application's existing provenance
vocabulary, mapped from `SeedBatchOrigin` through `SeedBatchOriginExtensions.ToFileResourceOrigin` —
extracted from the inline copy in this initializer's own file-resource capture (#251) rather than
written a second time.

**An empty breakdown is written, not skipped.** Considered and rejected (developer decision,
2026-08-30): a confirmation reporting that a file added nothing is not useless — it shows which
sections were actually used, and reminds the reader that they can seed their own files. The producer's
own comment previously claimed it "never writes one"; that was false as written and is corrected.

`IdentityComponents` is the file name plus the flattened breakdown, and it is load-bearing: step 5
dedupes against it, so this is what decides whether a reseed's result is "the same confirmation the
operator is already looking at" or a genuinely new one. Flattened to a single string in a fixed type
order (the base compares identity as a sequence of scalars, and #304's `ChangedFiles` sets the
precedent), so the same result produces the same identity regardless of grouping order.

Metadata is non-text data per `NotificationMetadataDto`'s own rule: the file name and counts are
identifiers and numbers a renderer consumes, never the prose — that lives in Title/Body, added by
step 4.

### 3. Widen the `MetadataKind` CHECK, the baseline, and the drift tests

**Status:** ✅ Done — migration 17, `NotificationReseedFileAppliedMigrations`

SQLite cannot widen a CHECK in place, so this is a table rebuild — the next free version in
`DatabaseInitializer.DataOwnedMigrations`, following migration 15's own precedent and structure. That
is **17** as of 2026-08-30; take whatever is next if another issue in this milestone lands one first,
since a migration version is frozen once applied and cannot be claimed in advance.
`DataBaselineSql`'s `System_Notification` CHECK is updated to match in the same commit, and
`DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` gains the new
value.

This milestone's end-of-milestone migration consolidation rewrites this migration along with the rest,
which is what keeps a rebuild affordable here rather than something to defer.

### 4. Add the notification's title and body in all three languages

**Status:** ✅ Done — `ReseedFileAppliedTitle`/`ReseedFileAppliedBody`

Two new keys on `NotificationMessageKeys` and their strings in `UI.en-GB.json`, `UI.nl.json` and
`UI.de.json`, resolved at write time through `NotificationTranslations.Original` and
`NotificationTranslations.Build` — the arrangement #319 established, so the text is stored per language
rather than in whichever culture the host happened to default to.

**The body takes the file name and the totals; the per-entity breakdown stays in the payload for #308
to lay out.** Stated as an assumption rather than left implicit: rendering a variable-length per-type
list *inside the body string* would need a translated noun per entity type in three languages, with
plural forms — and #308 is the issue that owns per-type layout and would be designing that key set
anyway. Step 2's decision is about what the notification *carries*, and the payload carries all of it;
this is only about how much of it the one-line body spells out today. Say so if the body itself should
enumerate the types now.

### 5. Write the per-file success notification from the clean-apply branch

**Status:** ✅ Done — `ConfirmFileAppliedCleanlyAsync`

In the `applyResult is null` branch, one `Success` notification per file, gated on step 1's flag.

**Written through `NotificationSeeding.SeedWhileUnresolvedAsync`, the same helper #304's producer
uses** — not a bare `INotificationWriter.WriteAsync`. The thing it suppresses is an identical per-file
result that is still active and undismissed: reseeding twice with nothing changed in between is the
same confirmation the operator is already looking at, not news. `SeedOnceAsync` is the wrong sibling
here — its full-history comparison would mean a file confirms cleanly once and then never again, even
after the operator dismissed it.

That choice is what bounds the row count, and it is why no expiry is needed: dedupe-while-active caps
the notification at one live row per distinct per-file result, and dismissal is the operator's own
deliberate act rather than a condition some other code has to resolve.

`expiresAt` stays `null` and no `dismissTrigger` is set. Leaving `dismissTrigger` unset is load-bearing,
not incidental: `POST /admin/database/reseed` calls
`DismissByTriggerAsync(NotificationDismissTrigger.Reseed)` *after* `ReseedAsync()` returns, so a row
carrying that trigger would be dismissed by the very reseed that wrote it.

### 6. Know the version before writing, rather than falling back to null

**Status:** ✅ Done — `CurrentAppVersionIdAsync`

**Developer direction, 2026-08-30: the app version must be known before any notification is added.**
`appVersionId: null` is not an acceptable outcome here — the initializer establishes the version, then
writes.

Inject `IAppVersionTracker` and `IVersionService` (both reachable — `IVersionService` lives in
`Quotinator.Core.Services`, the same project as this initializer). Take `GetLastActiveAsync()`'s row
id; if nothing has ever been recorded, call `RecordCurrentAsync` first and use that row.

**Both are required constructor parameters, registered in DI and resolved with
`sp.GetRequiredService`** — never optional, and never backed by a `?? new ...` fallback.

This was first planned the other way, copying `DatabaseInitializer`'s own
`IDiskSpaceProvider? diskSpaceProvider = null` / `?? new DiskSpaceProvider()`. **Developer correction,
2026-08-30: that is not a precedent, it is a defect** — the container is what supplies these, and the
only way one could arrive null is if it were never registered, which the fallback then hides by handing
out a second unmanaged instance instead of failing. CLAUDE.md's DI policy permits `new` only where the
container *cannot* supply the value (a computed path, a runtime config value); a registered service
that the one production call site already resolves is not that. Step 9 fixes the original.

The cost is accepted rather than designed around: 16 call sites across 13 test files, and the boyscout
rule applies to every one of them (see Test placement).

**This recording must stay on the reseed path and must never move into `OnInitialisedAsync`.**
`Program.cs` reads `GetLastActiveAsync()` *after* `InitialiseAsync()` and strictly before its own
`RecordCurrentAsync` — that read is #81's what's-new catch-up lower bound, and its own comment states
that recording the current version first "would overwrite the answer". A reseed runs long after that
sequence completed, so recording there disturbs nothing.

The same constraint is why #304's producer passes `null` and cannot simply be corrected in passing: it
runs inside `OnInitialisedAsync`, before the version is recorded, and there is no position in that
sequence where it could learn the version without displacing #81's read.

**#304's null is out of scope here and #302 does not wait on it** (developer direction, 2026-08-30).
Every row in this issue's verification table is satisfiable against the code as it stands today,
because this issue writes only from the reseed path, where the version is already recorded. Nothing
below is a dependency — recorded only so the next reader does not re-derive why the startup-path
producer differs from this one.

The startup sequence is being revisited in a future milestone, which may dissolve the constraint rather
than require working around it: if seeding moves to run after serving begins, it also runs after the
current version is recorded, and the initializer's own producers gain the same provenance this step
gives #302.

### 7. Write nothing for a reseed that touches zero files

**Status:** ✅ Done — existing early return, now proven

E.g. no configured sources. No notification at all, not an empty one. `SeedIfEmptyInternalAsync`'s
existing `effectiveBatches.Count == 0` early return already produces this; the step is to prove it
holds rather than to add code.

### 8. Expose `AppVersionId` on the notification response

**Status:** ✅ Done — response, endpoint description and `api-endpoints.md`

**Developer decision, 2026-08-30.** `NotificationResponse` carries `Metadata`, `MetadataKind`, expiry
and dismissal, but not provenance — so the guarantee step 6 establishes is currently observable only
from inside a unit test or a direct database read, and neither the T2 document nor any API consumer can
see it. Add the field to `NotificationResponse` and `NotificationEndpoints.ToResponse`, rendered as a
canonical lowercase id like every other id on that response.

Endpoint documentation moves in the same commit, per CLAUDE.md's keep-API-documentation-in-sync rule:
`docs/api-endpoints.md` and the `[Description]` attributes in `NotificationEndpoints.cs`.

This is the one piece of scope here that #302's own behaviour does not require — it exists so
provenance can be asserted rather than assumed, which is the same reasoning #304's T1 pass applied when
it found a notification state unobservable outside the Blazor page.

### 9. Make `diskSpaceProvider` a required dependency

**Status:** ✅ Done — required on both constructors, guarded by a test

**Developer decision, 2026-08-30.** `DatabaseInitializer`'s `IDiskSpaceProvider? diskSpaceProvider = null`
/ `?? new DiskSpaceProvider()` is the same defect step 6 was originally about to copy, already shipped.
It is registered in DI and passed explicitly by the only production call site, so the fallback is
unreachable in production and exists purely to spare test call sites — which is not the DI policy's
exception, and is precisely how the bad shape propagates to the next person who reads it as a pattern.

Make the parameter required on both `DatabaseInitializer` and `QuotinatorDatabaseInitializer`'s
pass-through, and drop the `??`. Measured first: `new DatabaseInitializer(` appears in only four test
files, all four already converted and already listed in `.editorconfig`, so this half carries no
boyscout tail of its own.

Deliberately fixed here rather than filed: the call sites are the ones step 6 is already editing, and
leaving the original in place while adding a correct sibling beside it is what makes the next reader
pick the wrong one.

---

## Test placement

The eight initializer-level tests go in `Quotinator.Core.Tests.Database.DatabaseInitializerTests`,
alongside #304's own producer tests rather than in a new class. That file is already long (2,330 lines),
and sibling test classes in the same folder each build their own initializer — so a new class is the
cheaper-looking option. It is rejected because #302 and #304 are the same producer at two different
call sites: separating them would put the reseed-path and startup-path notification tests in different
files, where a change to one stops being visibly a change to the other's sibling. `CreateInitializer`
is reused rather than duplicated for the same reason.

The T2 document (row 20) is a new file under `docs/automated-testing/notifications-and-changelog/` and
must be added to `Quotinator.slnx` in the commit that creates it, like every other document in that
folder — it is not picked up automatically.

**The boyscout `var` conversion travels in its own commit** (developer direction, 2026-08-30). Step 6
makes two constructor parameters required, which edits 16 call sites across 13 test files; the boyscout
rule then owes an explicit-type conversion and an `.editorconfig` entry for each. Those go in a
separate commit from the functional change, so #302's own diff stays reviewable as #302 rather than
being buried under roughly 1,540 mechanical edits — `ImportActionPlannerTests` (565 `var`) and
`SqliteImportActionServiceTests` (489) dominate the count. Only `DatabaseInitializerTests` among the 13
is already converted and listed.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | One `Success` notification per file that reseeds with nothing left to review | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_WritesOneSuccessNotificationPerFile` |
| 2 | ✅ | No per-file notification on the first empty-database seed | Unit test | `DatabaseInitializerTests.Initialise_FirstEmptyDatabaseSeed_WritesNoPerFileNotification` |
| 3 | ✅ | No per-file notification for a file left awaiting review | Unit test | `DatabaseInitializerTests.Reseed_FileLeftAwaitingReview_WritesNoSuccessNotification` |
| 4 | ✅ | No notification for a reseed that touches zero files | Unit test | `DatabaseInitializerTests.Reseed_NoConfiguredFiles_WritesNoNotification` |
| 5 | ✅ | An identical per-file result is not written again while the first notification is still active | Unit test | `DatabaseInitializerTests.Reseed_TwiceWithNoChange_DoesNotRewriteTheActiveNotification` |
| 6 | ✅ | After the operator dismisses it, the next reseed confirms the same file again | Unit test | `DatabaseInitializerTests.Reseed_AfterDismissal_WritesTheConfirmationAgain` |
| 7 | ✅ | A file whose added/modified counts changed notifies separately rather than being suppressed | Unit test | `ReseedFileAppliedMetadataTests.Identity_DiffersByBreakdown_AndIsOrderIndependent` |
| 8 | ✅ | The notification has no expiry and is not dismissed by the reseed that wrote it | Unit test | `DatabaseInitializerTests.Reseed_FileAppliedCleanly_NotificationHasNoExpiryAndSurvivesReseedDismissal` |
| 9 | ✅ | An already-recorded app version is the one stamped on the notification, not a second row | Unit test | `DatabaseInitializerTests.Reseed_WithRecordedVersion_StampsThatVersionOnTheNotification` |
| 10 | ✅ | With no version ever recorded, one is recorded before the notification is written, and `AppVersionId` is never null | Unit test | `DatabaseInitializerTests.Reseed_WithNoRecordedVersion_RecordsOneBeforeWriting` |
| 11 | ✅ | The breakdown counts every entity type the file added or modified, not quotes alone | Unit test | `DatabaseInitializerTests.Reseed_FileAddingNonQuoteEntities_CountsThemInTheBreakdown` |
| 12 | ✅ | An entity type with no added or modified rows is omitted rather than reported as zero | Unit test | `DatabaseInitializerTests.Reseed_EntityTypeWithNoChanges_IsAbsentFromTheBreakdown` |
| 13 | ✅ | Payload round-trips the file name and the per-entity breakdown through the `Metadata` column | Unit test | `ReseedFileAppliedMetadataTests.Payload_RoundTripsFileNameAndBreakdown` |
| 14 | ✅ | Two different breakdowns produce different identities, and grouping order does not affect identity | Unit test | `ReseedFileAppliedMetadataTests.Identity_DiffersByBreakdown_AndIsOrderIndependent` |
| 15 | ✅ | The new kind has a registered payload type | Unit test | `NotificationMetadataKindsTests` (existing guard; fails on an unregistered member) |
| 16 | ✅ | The new migration and the baseline accept the same `MetadataKind` values | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` |
| 17 | ✅ | The new migration and the baseline produce an identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` |
| 18 | ✅ | Title and body exist non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 19 | ✅ | `GET /api/v1/notifications` returns `appVersionId` for a notification that carries one | Unit test | `NotificationEndpointsTests.GetNotifications_ReturnsAppVersionId` |
| 20 | ✅ | Neither initializer constructor makes a DI-suppliable service dependency optional | Unit test | `RepositoryStructureTests.InitializerConstructors_DoNotMakeAServiceDependencyOptional` |
| 21 | ✅ | A real reseed against a real database writes one notification per cleanly-applied file, with its breakdown and provenance readable through the API | Automated (T2) | `docs/automated-testing/notifications-and-changelog/11-clean-reseed-confirmation.md` |
| 22 | ✅ | The notifications render on `/notifications` after a live reseed | Live | T1 (developer, 2026-09-01): reseed run from `/notifications`, one `Success` confirmation per cleanly-applied file, in Dutch, each naming its file and counts |
| 23 | ✅ | The same confirmations appear in the startup modal after a restart | Live | T1 (developer, 2026-09-01): reseed at 12:15–12:16, restart at 12:16:18, all four confirmations listed in the modal as `Actief` with no expiry |
| 24 | ✅ | The startup modal carries the confirmations after a restart, and a never-reseeded install shows none | Automated (T2) | `11-clean-reseed-confirmation.md` step 8 |
| 25 | ✅ | A user-imports file is confirmed exactly as a bundled one — origin does not gate the producer | Unit test | `DatabaseInitializerTests.Reseed_UserImportsOnly_ConfirmsEachFile` |
| 26 | ✅ | Both origins in one reseed confirm every file across both, named individually | Unit test | `DatabaseInitializerTests.Reseed_BundledAndUserImports_ConfirmsEveryFileFromBothOrigins` |
| 27 | ✅ | Two files with the same bare name from different directories are two confirmations, not one | Unit test | `ReseedFileAppliedMetadataTests.Identity_DiffersByOrigin_ForTheSameFileNameAndBreakdown` |
| 28 | ✅ | Origin survives the `Metadata` column round-trip | Unit test | `ReseedFileAppliedMetadataTests.Payload_RoundTripsFileNameAndBreakdown` |
| 29 | ✅ | All four seeding variants behave correctly against real configuration | Automated (T2) | `11-clean-reseed-confirmation.md` step 7 |

**The two surfaces need different sequences, which row 22 originally ran together as one step.**
`StartupSuccessModal` is shown once per process run after a healthy startup, so a reseed — which
happens long afterwards — cannot populate it in that same run. The modal half needs a **restart after
the reseed**, with the confirmations still undismissed. Split into rows 22 and 23 so a later T1 run
does not read the single row, try both in one sitting, and conclude the modal is broken.

Recorded because it was got wrong twice while writing this plan: first as a single row implying one
sequence, then by concluding the modal half was unachievable and scoping it out. Both were assumptions
about a component this issue does not own; the developer's T1 settled it by restarting and observing.

**T1 also confirmed row 7 live, which unit tests could only prove at payload level.**
`quotinator-series-universe.json` read *69 toegevoegd* on the first reseed and *106* on a reseed after
a reset, because the reset meant Universes and Series had to be recreated as well
(`Universe[new=7] Series[new=30] Source[new=69]` in the startup log). The same file with a genuinely
different result produced its own confirmation rather than being suppressed as a duplicate.

**The four seeding variants are proven at both levels** (developer direction, 2026-08-30: positive and
negative proof requires testing seeding with and without files in each folder). Rows 4, 1, 25 and 26
cover no-files / bundled-only / user-only / both as unit tests; row 29 covers the same matrix at T2,
where the batch list comes from real configuration rather than being handed to the initializer.

**The T2 pass found a real defect, in step 7's four-variant matrix.** The bundled + user-imports variant
wrote two confirmations both named `quotinator-curated.json` with no way to tell them apart, and only
their differing breakdowns kept them from collapsing into one. Fixed by adding `Origin` to the payload
and its identity — see step 2. The matrix existed because positive proof alone is not proof; steps 1–6
would never have reached this state.

**T2 pass, 2026-08-30, all six steps green** — four bundled files confirmed, each with real
provenance, dedupe holding across a second reseed, and confirmations reappearing after dismissal. Two
things the run established that no unit test had:

- `quotinator-series-universe.json` reports `Source: added=69` and **no `Quote` line at all**. Under the
  quote-only counts this issue replaced, that file would have confirmed itself as "0 added, 0 updated" —
  a silent no-op report for a file that had just added 69 Sources. The strongest evidence for the
  per-entity decision, and it only shows against real bundled content.
- The document's own Cleanup step named a `remove` verb the harness does not have (it is `destroy`),
  and its step 3 required a `Quote` line on every file, which the fourth file does not have. Both were
  found by running it and are fixed in the document.

**Row 7 is verified at the payload level, not through a live reseed** — recorded rather than left as a
silent substitution. The row was planned as `Reseed_WithDifferentCounts_WritesASecondNotification`, but
a reseed of the same file always produces the same counts (the file's content is what decides them), so
there is no way to make a real reseed differ without editing bundled content mid-test. The identity test
proves the same property where it actually lives: a different breakdown is a different identity, which
is what the dedupe compares.
