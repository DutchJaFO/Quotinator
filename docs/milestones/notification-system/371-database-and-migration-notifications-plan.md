# #371: Notify that the database was created, and that migrations were applied

**Status:** In progress
**GitHub issue:** #371
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Ask the developer for T1, then move this issue to `Waiting for release`.** Every step is ✅ and
every row in the Verification table is green except row 21, T1, which is the developer's own action
and cannot be performed or substituted here.

---

## What this adds

Creating a database from scratch and applying migrations to an existing one are both events an
operator has a direct interest in, and today both reach the log only. The log is not where an operator
looks, and the Notifications page says nothing about either.

The first signal already exists and is already computed in the right place.
`ApplyMigrationsAsync` reports `TookBaselinePath` and `DatabaseWasCreated` on its private
`MigrationsResult`, and `InitialiseAsync` already receives both. Nothing new has to be detected: the
value simply has to survive as far as the consumer, which is where every other notification producer
in this project already runs.

The second signal does not exist yet. `MigrationApplied` is a single human-readable
`"Data v8 → v27, App v5 → v9"` string built for the startup banner, and a `SchemaMigration` holds
only `Version` and `Sql`, so neither it nor `System_SchemaVersion` can say anything but *migration 27
applied*. A required `Description` on the record is what makes the per-migration notification possible,
and `required` is the point: it makes the description unskippable for every future migration instead of
something to remember.

### Where each notification is written

Both are written by `Program.cs` after `InitialiseAsync` returns, never from inside
`Quotinator.Data`'s migration code. `System_Notification` may not exist yet while migrations run, and
a table-rebuild migration may be mid-flight. `SchemaVersionOvershootDetected` is the established
precedent for exactly this shape: `Quotinator.Data` sets a flag, the consumer reads it and writes the
notification.

### Tier assessment

T1 and T2 are always required (`docs/release-verification.md`). T3 is not: none of its triggers is
reached (no ingress, `PathBase`, `UseForwardedHeaders`, DataProtection, SSL/Kestrel, `addon/config.yaml`,
`addon-beta/config.yaml`, add-on translation or log-output-format change), and the rendering surface
itself is unchanged, with two new rows flowing through the table #308 and #413 already built and
verified.

---

## Cross-check against authoritative sources

Four of the issue's own statements do not match the code as it stands. Recorded here because the issue
body is the specification and three of these would otherwise be implemented as written.

### The migration count, and which lists are in scope

The issue says the description *"already exists as the comment above each of the 24 production
entries and only needs moving onto the record"*. Measured 2026-10-10: `SchemaMigration` is constructed
at **37 production sites across three lists**, and 24 is the count of entries that have a comment, not
the count of entries.

| List | Entries | Have a comment to move |
|---|---|---|
| `DatabaseInitializer.DataOwnedMigrations` | 27 | 24 (v1, v2 and v3 have none of their own) |
| `QuotinatorMigrations.All` | 9 | 0, only `Migration00N_Name` identifiers |
| `ChangelogDatabaseInitializer.Migrations` | 1 | 0 |

So 13 descriptions are written fresh rather than moved, and `required` makes that unavoidable: it is a
compile-time constraint on every construction site, including the seven in test code (step 1 found
three more than this count first reached).

**Decided (developer, 2026-10-10): all three lists get a guard, one per owning project.** The issue
named only `DatabaseInitializerOwnershipTests`, which lives in `Quotinator.Data.Tests` and can see
`DataOwnedMigrations` and the changelog list but not `QuotinatorMigrations.All`. Guarding Data's list
alone would leave the forgettability `required` exists to remove in place for two of the three lists.

### `NotificationTableTests.EveryMetadataKind_HasALayout` no longer exists

The issue's expected-tests table names it as *"existing, and derived from the enum"*. #308 deleted it
on purpose, and said so where it was removed: every kind having an *entry* in a layout map was only a
property of that map, and no renderer read it. Six enum-derived tests now cover what that row wants,
and every one of them fails on a new member until it is handled:

`EveryMetadataKind_DeclaresWhetherItRendersDetail`,
`EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`,
`PayloadDetail_ForEveryKind_IsSelfDescribing`,
`EveryMetadataKind_WithAnUnreadablePayload_RendersNothingAndDoesNotThrow`,
`EveryMetadataKind_WithNoPayload_RendersNothing`, and
`EveryLiveNotificationKind_IsNamedInTheVariantDocument`.

The last of those is a deliverable the issue does not mention at all: it asserts that
`notifications-and-changelog/14-every-kind-renders-what-its-layout-promises.md` names every member of
the enum, so that document has to gain both new kinds.

### The CHECK-constraint test is in a different class than the issue says

The issue lists *"`DatabaseInitializerOwnershipTests` ... `...AcceptSameCheckConstraintValues`"*. The
test called `Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` is in
`Quotinator.Core.Tests`' `DatabaseInitializerTests` and covers the **consumer** schema.
`MetadataKind` is a Data-owned column, so the tests that actually cover it are
`DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues`,
plus `NotificationMetadataKind_IsAcceptedByTheBaseline` and
`NotificationMetadataKind_IsAcceptedByTheIncrementalReplay`, which are already `DynamicData` over the
enum and therefore go red on a new member without being edited.

### Three requirements have no test row at all in the issue

Requirement 4 (written from the consumer, never from Data's migration code), the translation
obligation #319 imposes on every notification title and body, and the registration of each new payload
type in `NotificationMetadataKinds` are all mandatory and all unnamed. The verification table below
carries a row for each.

---

## Steps

### 1. Create the signatures only

**Status:** ✅ Done, 2026-10-11. Build clean at 0 warnings, 0 errors, with no behaviour added: the two
new initializer properties still return `false` and `[]` because `InitialiseAsync` does not assign
them yet, and both payloads' `IdentityComponents` still return `[]`.

**The placeholder is an empty string, deliberately.** It satisfies `required` so the solution
compiles, and leaves `EveryMigration_HasANonEmptyDescription` able to go red in step 2. A placeholder
like `"TODO"` would have made that guard green on its first run, which is the trap this step exists
to avoid.

**44 construction sites, not the 41 the cross-check counted.** The first build found two the
`new SchemaMigration { … }` search had missed, both in `DatabaseInitializerTests` and both written
as a multi-line `SchemaMigration pending = new() { … }`, plus one multi-line entry in
`DatabaseInitializerOwnershipTests`. The production total of 37 is unchanged; the test-code total is
seven, not four. Corrected in step 3 and in the cross-check above.

Signatures first, per `docs/testing-policy.md`'s *Red first means signatures first*: a test binding to
a type that does not exist breaks the build instead of going red. Nothing in this step has a body.

- `SchemaMigration.Description`, `required string`, with every existing entry given a placeholder so
  the solution still compiles. The real text lands in step 3.
- `MigrationPhase` in `src/Quotinator.Data/Enums/`, members `Data` and `App`. A closed set owned by
  `Quotinator.Data`, so an enum rather than the `string phaseName` `ApplyMigrationPhaseAsync` takes
  today. `ToString()` yields the same two words the banner already prints, so
  `StartupSummaryLoggerTests.LogReady_BannerContainsMigrationLine_WhenMigrationApplied` keeps passing
  unchanged.
- `AppliedMigrationResult` in `src/Quotinator.Data/Database/`, carrying `MigrationPhase Phase`,
  `int Version` and `string Description`. Named per ADR 016's `Result` row: the in-process outcome of
  applying one migration, handed back to the caller, crossing no boundary of its own.
- `IDatabaseInitializer.DatabaseWasCreated` (`bool`) and
  `IDatabaseInitializer.MigrationsApplied` (`IReadOnlyList<AppliedMigrationResult>`), returning
  `false` and `[]`, implemented on `DatabaseInitializer`, `NoOpDatabaseInitializer` and the five test
  stubs that implement the interface.
- `NotificationMetadataKind.DatabaseCreated` and `NotificationMetadataKind.MigrationApplied`.
- `DatabaseCreatedMetadataDto` and `MigrationAppliedMetadataDto` in
  `src/Quotinator.Data/Notifications/`, properties declared, `IdentityComponents` returning `[]`.

**The trap this step names for itself:** having the file open, writing the real body is one keystroke
away, and then the test is green on its first run and has proven nothing. #375 lost four tests that
way.

### 2. Write every named test and confirm it red

**Status:** ✅ Done, 2026-10-11. Build clean at 0 warnings; every named test run and its actual reading
recorded below.

**Red on their own assertion** (13): `FreshDatabase_ReportsItWasCreatedFromBaseline` (`false` where
`true` is required), `AppliedMigrations_AreReportedIndividually` (0 entries where 2 are required),
`All_Descriptions_AreNonEmpty` (v1 empty), `EveryDataOwnedMigration_HasANonEmptyDescription` (all 27
empty), `EveryChangelogMigration_HasANonEmptyDescription` (v1 empty),
`EveryKind_HasARegisteredPayloadType` (both new kinds unregistered),
`NotificationMetadataKind_IsAcceptedByTheBaseline` and `...IsAcceptedByTheIncrementalReplay` (both new
kinds, both paths, `SQLite Error 19` against the un-widened CHECK),
`DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` (same error),
`Startup_DatabaseCreated_SeedsTheCreationNotification` (no such notification written),
`Startup_AfterPendingMigrations_SeedsOneNotificationPerMigration` (0 where 2 are required),
`MigrationDocuments_SayEveryMigrationCarriesADescription` (both documents silent),
`EveryMetadataKind_DeclaresWhetherItRendersDetail` and
`EveryLiveNotificationKind_IsNamedInTheVariantDocument` (both new kinds undeclared and unnamed), and
`EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares` (`KeyNotFoundException` on the same map).

**Red on a precondition that is the same unimplemented property** (3):
`ExistingUpToDateDatabase_ReportsNoCreation`, `ApplyingMigrations_WritesNoNotificationItself` and
`AppliedMigration_IsReportedByItsDescriptionNotOnlyItsVersion`. Each reads the property from the other
side, so its precondition and its assertion fail for one reason; all three turn green on their own
assertions once step 5 lands.

**Green, and therefore controls rather than red tests** (2): `NoPendingMigrations_ReportsNoAppliedMigrations`
and `Startup_WithNothingToReport_SeedsNeitherNotification`. Neither can be red before implementation,
because an unimplemented `MigrationsApplied` already returns empty, which is the answer they want. They
are still worth having: each goes red if the implementation over-reports, which is the specific mistake
requirement 5 exists to prevent. Not counted as evidence the gap exists.
`ApplyingMigrations_WritesNoNotificationItself` is a control for the same reason, and its precondition
failing above is the only thing currently keeping it red.

**`DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` was extended
here rather than in step 7.** A test the issue leaves untouched until after the widening can only ever
be seen green, which is the thing red-first exists to prevent, so its two new rows were written now and
confirmed failing against the old constraint.

**`EveryChangelogMigration_HasANonEmptyDescription` is in `ChangelogDatabaseInitializerTests`, not
`DatabaseInitializerOwnershipTests` where this plan named it.** That class already owns every other
proof about that initializer, and a guard belongs with its subject. It shares the ownership class's
reflection helper rather than carrying a second copy.

**Both guards read their list by reflection** (`BindingFlags.NonPublic | BindingFlags.Static`), the way
`SqlQueryGuardTests` already reaches `Sql`'s private statics, rather than by adding a test-only accessor
to production code. Each asserts the field was found and the list is non-empty first, so a rename fails
loudly instead of passing over an empty set.

### 3. Every migration states what it did

**Status:** ✅ Done, 2026-10-11. All 44 descriptions written, build clean at 0 warnings, and the three
guards from step 4 now pass (`All_Descriptions_AreNonEmpty`,
`EveryDataOwnedMigration_HasANonEmptyDescription`, `EveryChangelogMigration_HasANonEmptyDescription`).

**No migration's SQL changed.** Confirmed from the diff rather than asserted: `git diff --stat` over
`src/Quotinator.Data/Database/*Migrations.cs` is empty, and the consumer file's diff contains nothing
but the list entries' own lines. Every `Sql =` still names the identical constant.

**The comments were trimmed, not deleted, and that is a correction to this step as planned.** The plan
expected most comments to be duplicated by the description and therefore removable. Reading them showed
they are not the same thing: a description is one sentence an operator can read, while almost every
comment carries *developer* rationale the description must not contain, why a widening needs a table
rebuild, why a data-only migration needs no baseline counterpart, why an `ADD COLUMN` was enough. So
each comment lost only the sentence the description now carries and kept its rationale. Two were left
untouched in full: migration 14's one-time in-place-edit exception, and the squash note above the list.

Replace step 1's placeholders with real text: 24 descriptions moved off the comment that already
carries them, and 13 written fresh (`DataOwnedMigrations` v1 to v3, all nine of
`QuotinatorMigrations.All`, and the changelog list's single entry). The seven test-code construction
sites get a description naming what the test's own fixture migration does.

Where a comment is moved onto the record, the comment itself goes: two copies of the same sentence in
the same place is exactly the drift the project's own duplication rule forbids. A comment carrying
something the description does not say (migration 14's one-time in-place-edit note, the squash note
above v1) stays where it is.

**No migration's `Sql` changes by a single byte.** The freeze rule governs a migration's SQL, and
adding a sibling property changes nothing a database has already applied.

### 4. Guard the description in all three lists

**Status:** ✅ Done, 2026-10-11. Written in step 2 (red) and green since step 3. The changelog guard
lives in `ChangelogDatabaseInitializerTests`, for the reason recorded in step 2.

One test per owning project, each derived from the list rather than from a count, so a future migration
with a blank description fails rather than shipping:

- `DatabaseInitializerOwnershipTests.EveryDataOwnedMigration_HasANonEmptyDescription`
- `DatabaseInitializerOwnershipTests.EveryChangelogMigration_HasANonEmptyDescription`
- `QuotinatorMigrationsTests.All_Descriptions_AreNonEmpty`, beside the `All_SqlStrings_AreNonEmpty`
  it copies the shape of

### 5. The initializer reports creation and the migrations it applied

**Status:** ✅ Done, 2026-10-11. Build clean at 0 warnings, and all six tests from step 2 that were red
on this step are green: `FreshDatabase_ReportsItWasCreatedFromBaseline`,
`ExistingUpToDateDatabase_ReportsNoCreation`, `AppliedMigrations_AreReportedIndividually`,
`AppliedMigration_IsReportedByItsDescriptionNotOnlyItsVersion`,
`NoPendingMigrations_ReportsNoAppliedMigrations` and `ApplyingMigrations_WritesNoNotificationItself`.
`StartupSummaryLoggerTests` still passes in full, so the banner line is unchanged by the refactor.

**An entry is appended only after its own transaction commits**, so a migration that rolled back is
never reported as applied.

**`LogApplyingMigrationPhase` now takes `MigrationPhase` rather than a `string`.** Passing
`phase.ToString()` at the call site raised `CA1873`: it renders whether logging is enabled or not. A
`LoggerMessage`-generated method defers formatting until the message is actually written, so moving the
enum into the signature removes the eager call rather than hiding it behind a local.

`ApplyMigrationPhaseAsync` returns its applied migrations as `IReadOnlyList<AppliedMigrationResult>`,
and the existing `"{Phase} v{from} → v{to}"` banner string is derived from that list instead of being
returned alongside it. One source, so the banner and the notifications cannot disagree.

`MigrationsResult` carries the combined list, and **`InitialiseAsync` is what assigns the two public
properties**, not `ApplyMigrationsAsync`. That placement is load-bearing: `DropAndRebuildAsync` calls
`ApplyMigrationsAsync` too, against a database it has just dropped every table from, so
`DatabaseWasCreated` would read `true` after an operator's Reset. Assigning in `InitialiseAsync` leaves
a Reset unable to disturb either property. `InitialiseForTestingAsync` mirrors it, as it already mirrors
everything else `InitialiseAsync` does.

### 6. Two new kinds, their payloads, and one migration widening the CHECK

**Status:** ✅ Done, 2026-10-11. Migration 28,
`NotificationDatabaseLifecycleMigrations.WidenMetadataKindForDatabaseLifecycle`, one rebuild for both
values. `NotificationMetadataKindsTests` green, and both `NotificationMetadataKind_IsAcceptedByThe*`
tests green for the two new kinds on both paths.

**Neither value needed a new dismiss trigger or resolution**, so unlike migrations 25 and 26 this
rebuild widens one CHECK rather than three: both notifications report an event that has happened, and
nothing resolves a fact.

`DatabaseCreatedMetadataDto`: `DataSchemaVersion` and `AppSchemaVersion`, both `required int`.
`ReleaseState` is `NotApplicable`, following `SchemaVersionOvershootMetadataDto`: this describes the
database's own state, which no version number characterises. `IdentityComponents` is the two versions.

`MigrationAppliedMetadataDto`: `Phase`, `MigrationVersion` and `Description`. **`MigrationVersion`, not
the base class's `Version`**, which means *the release this notification is about* and is a different
thing entirely. `ReleaseState` is `NotApplicable` for the same reason as above.
`IdentityComponents` is `[Phase, MigrationVersion]`, deliberately excluding the description: a
corrected description must not make an already-announced migration announce itself again.

Both are registered in `NotificationMetadataKinds.PayloadTypes`, which is what
`NotificationMetadataKindsTests` is already asserting over.

One migration appended to `DataOwnedMigrations` widens `MetadataKind`'s CHECK with both values. A table
rebuild, because SQLite cannot widen a CHECK in place, and one rebuild for both values rather than two
that each copy every row, following migrations 15, 18, 25 and 26. Per ADR 008 the widening and the new
members land in the same migration.

### 7. The baseline matches, and the drift tests cover both new values

**Status:** ✅ Done, 2026-10-11, in the same commit as step 6's migration.
`DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` green, along with
all 73 tests in `Quotinator.Data.Tests`' two schema classes.

`DataBaselineSql`'s own `MetadataKind` CHECK is updated to the migration's final result **in this same
commit**, per CLAUDE.md's baseline rule, and
`DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` is extended with
the two new values.

### 8. `NotificationTable` declares what each new kind renders

**Status:** ✅ Done, 2026-10-11. All 80 `NotificationTableTests` green, including the six enum-derived
guards that were red in step 2.

**`MetadataFor` needed a real fixture per kind, not the shared default.** Both payloads carry `required`
properties, so the bare `{"releaseState":"NotApplicable"}` default would fail to deserialize and
`PayloadDetail` would return an empty table, letting
`EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares` pass for the wrong reason: no detail because
the payload was unreadable, rather than because the kind declares none.

**Doc 14 needed more than the two kind names the guard test checks for.** Its own cold start now
produces `DatabaseCreated`, so the document would have failed as written: step 1's expected reading went
from four kinds to five, step 2's `opensDetail` list and capture set gained the row, and the kind table
gained its trigger. `MigrationApplied` got an exemption note in the shape `BackupRefused` already uses,
pointing at the new document: every cold start here takes the baseline path, which applies no individual
migration, so this run genuinely cannot produce it.

**Doc 14's `Observed effect` was left without a row for `DatabaseCreated`**, with a line saying why. The
recorded run is from 2026-09-24 and predates the kind; writing a row for it would record an observation
nobody made.

Both kinds declare **no detail table**. The body leads for every kind with no exception (developer,
2026-09-02), and neither payload has anything a table would align: the creation notice carries two
version numbers its body states, exactly as `BackupQuotaReached` does, and a migration notice is one
notification per migration, so its description is a sentence rather than a row. Declaring `false` *is*
the per-type layout decision requirement 6 asks for, and
`EveryMetadataKind_DeclaresWhetherItRendersDetail` is what makes the declaration unskippable.

`NotificationTableTests.RendersDetail` gains both entries, and
`notifications-and-changelog/14-every-kind-renders-what-its-layout-promises.md` gains both kind names
and a trigger for each, which is what `EveryLiveNotificationKind_IsNamedInTheVariantDocument` asserts.

### 9. Titles and bodies, in all three languages

**Status:** ✅ Done, 2026-10-11. `TranslationCompletenessTests` green, build clean at 0 warnings.

**A migration's own description is not translated, and the text is shaped to say so rather than hide
it.** The description lives on `SchemaMigration` in code, so it is English in every language. A body of
bare `{0}` would have presented that English sentence as though it were the localised body; instead the
body is a localised lead-in around it (`Applied to this database: {0}`), so what is translated and what
is not is visible in the rendered text. Translating the 37 descriptions themselves is a different piece
of work and is not in this issue's scope: the issue places the description on the record, which is a
code location, and nothing in it asks for a fourth translation surface.

Four keys on `ApiMessages`, not `NotificationMessageKeys`: the producer runs inside `Quotinator.Api`,
which is the split those two classes document. `NotificationSchemaOvershootTitle`/`Body` is the
precedent, including the `{0}`/`{1}` substitution of the version numbers through
`NotificationTranslations.Original` and `NotificationTranslations.Build` so the numbers are not
embedded in prose written once in English.

Both titles and both bodies are added to `UI.en-GB.json`, `UI.de.json` and `UI.nl.json` in the same
commit. `TranslationCompletenessTests` is the gate.

### 10. `Program.cs` writes both notifications after initialisation

**Status:** ✅ Done, 2026-10-11. All six `ProgramNotificationSeedingRegressionTests` green, including
the two from step 2 that were red here and the negative control. Full solution run: 4,540 tests,
4,538 passed, 0 warnings; the two failures were this issue's own outstanding steps (the T2 documents'
cross-reference and step 12's documentation), not regressions.

Two producers, each in its own non-fatal guard beside the three already there, both reading the
initializer's properties rather than anything of their own:

- `DatabaseWasCreated` writes one `Information` notification through
  `NotificationSeeding.SeedOnceAsync`. `SeedOnceAsync`, not the active-only sibling: this is an event
  that happened once, not a condition that can recur. No dismiss trigger, because nothing resolves a
  fact, and no expiry, matching #312's opt-in default and #413's finding that a default expiry outlives
  its own correctness.
- `MigrationsApplied` writes one `Information` notification per element, same helper, same reasoning.
  Nothing is persisted beyond the payload, because the description is derivable from this build's own
  list by version number, and an append-only frozen list means a database migrated by an older build
  resolves correctly against a newer one's.

**A long upgrade produces one row per migration**, which is what the issue asks for, and on an upgrade
spanning many releases that is a double-figure count. Recorded as a consequence of the shape the issue
specifies, not as an objection to it.

Neither producer runs when its signal is absent, so an ordinary restart against an up-to-date database
writes nothing, and the baseline path applies no individual migration so a fresh database produces the
creation notification alone.

### 11. The T2 documents, red against the pre-issue build

**Status:** ✅ Done, 2026-10-11. Both documents run red against a build from `d0f4b9c4` (the commit
before this issue's work, carrying the plan doc and no implementation), then green against an image
built from this branch. Each document's own `Observed effect` records both readings; the plan does not
repeat them.

**The red run's mechanics**, per `docs/testing-policy.md`'s *Red first applies to automated tests*:
`git worktree add --detach .claude/temp/wt-pre371 HEAD`, `docker build` under `quotinator:pre371`, both
documents executed against it, then the container, image and worktree all removed before the green
image was built.

**What each document read red.** Doc 15: `announcement=1 reseedfileapplied=5 whatsnew=1` with
`databasecreated` absent where `1` is required, and no such row on the page. Doc 16: the log reported
`28` migrations applied against `0` notifications.

**Doc 16's red run is the one that proves its instrument.** Its expectation is derived from the log
rather than written down, so a broken pattern would have produced `0` against `0` and passed while
measuring nothing. It produced `28` against `0`, and the green run produced `29` against `29`, the
count moving by exactly this build's own new migration without the document being edited.

**One defect in the documents, found by running them**, which is the point of running them: both used a
`/.../i` regex literal to match a notification body, and the sandbox executing the step read it as a
filesystem path and refused the command. Replaced with `.includes(...)` in both. A document that cannot
be executed as written is a defect in the document, whoever's guard trips on it.

**Doc 15's step 3 carried a positive control for its own instrument.** Its assertion is that a row is
*absent* on the pre-issue build, which is indistinguishable from a selector that matches nothing, so the
same run also located the announcement row (`control: true`, 7 rows read). Required by the index's *A
negative assertion needs a positive control in the same run, using the same instrument*.

**Two documents, not the one this plan named.** The first draft was a single document declaring
`**Environment:** Fresh, then Upgraded`, which
`RepositoryStructureTests.EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile` rejected, and
correctly: the index's `+` notation means *Constrained layered on a base*, not *two legs in two bases*.
That is not a gap in the suite's grammar, it is the grammar saying these are two tests. They have two
different triggers, two different environments, and no shared state, so they are now:

| Document | Environment | Port | What it establishes |
|---|---|---|---|
| [*A created database says so*](../../automated-testing/notifications-and-changelog/15-a-created-database-says-so.md) | Fresh | `18515` | The creation notification is written, no migration notification is, and a restart writes neither again |
| [*Each applied migration says what it did*](../../automated-testing/notifications-and-changelog/16-each-applied-migration-says-what-it-did.md) | Upgraded | `18516` | One notification per migration applied, each naming what it did, and a second restart adds none |

The upgrade document uses the previous published tag, which is the upgrade users are about to perform,
and is the only arrangement that shows a description resolved by a *newer* build for a migration an
*older* one applied.

**The count is read three ways, because the count alone cannot fail usefully.** A correct run and a run
that resolved every description to one shared fallback produce the same number of notifications; only
the count of *distinct* bodies separates them, and only an empty-body check catches a description that
resolved to nothing.

**No step asserts a migration number, a schema version or a notification total**, per the README's own
two rules on each. What is asserted is the relationship: the count of migration notifications equals
the count of migrations the same run reports applying, each notification's body is non-empty and
differs from its neighbours, and the restart adds none.

Red first, per `docs/testing-policy.md`'s *Red first applies to automated tests*: `git worktree add`
the commit before this issue's work, `docker build` it under its own tag, run the document's steps,
confirm they fail where they should, then remove the container, image and worktree. A document first
run against the finished build shows that something happens, not that it would have caught the absence
it exists for.

### 12. Record the description rule where the next migration will look

**Status:** ✅ Done, 2026-10-11.
`RepositoryStructureTests.MigrationDocuments_SayEveryMigrationCarriesADescription` green, and all 45
tests in that class pass.

**Both entries say the description is operator-facing and the comment keeps the rationale**, which is
the distinction step 3 discovered while writing the 44 descriptions. Without it the next author has no
way to tell what belongs in which, and the duplication the rule exists to prevent comes back.

A new mandatory rule for every future migration needs a durable home, and `git log` is not it. Its own
commit, separate from the code, per `process.md`'s rule on plan-doc and documentation commits:

- CLAUDE.md's *Schema migration policy*, as one more bullet in the list a migration author reads
- `docs/database-conventions.md`'s `## Migrations` section, which carries the same rules for the
  database-facing reader

Both are asserted over the documents' own text, because a documentation row nobody executes is the
shape `process.md` forbids: a step waiting on a human to reread a file. The precedent is
`RepositoryStructureTests.SourceRefreshDocuments.cs`, which does exactly this for #424 and splits a
paragraph on blank lines so that the rule and its subject must appear together rather than separately
somewhere in the file. A new partial, `RepositoryStructureTests.MigrationDescriptions.cs`, carries
`MigrationDocuments_SayEveryMigrationCarriesADescription`.

### 13. Boyscout pass, solution, and `.editorconfig`

**Status:** ✅ Done, 2026-10-11. Build clean at 0 warnings, and the full suite green: 4,540 tests,
4,540 passed, 0 warnings, `-m:1`.

**Nine files added to the `IDE0008` section and 13 to `IDE0090`**, the rest of the 29 touched files
already being listed. Widening the scope surfaced **35** warnings, cleared over three fixer passes
(14, then 6, then 0), confirming the rule's own note that one pass is not enough.

**An incremental build hid one of the follow-on warnings, and that is worth knowing for the next pass.**
After the three fixer passes reported zero, `dotnet build` kept reporting zero across several further
runs, but `dotnet build --no-incremental` surfaced an `IDE0305` in `QuotinatorMigrationsTests` line 13:
the `var` conversion had turned a `.ToList()` into one the analyzer wants as a collection expression,
and the project was no longer being re-analysed. Fixed, and the from-scratch build then reported zero.
CLAUDE.md already warns that explicit types expose `IDE0028`/`IDE0305`; what it does not say, and what
this found, is that an incremental build can report a clean result while one is outstanding. **Verify a
boyscout pass with `--no-incremental`.**

**The other three boyscout rules had nothing to do here**, checked rather than assumed: none of the new
files emits a log line, so no `[Subsystem - Phase]` prefix was missing, and no `Sql.*` nested class was
touched, so no column name needed `nameof`. `MigrationPhase`'s placement already follows ADR 016
(`Enums/`), and no `.razor` file is involved.

Scope is exactly the files this issue created or touched, per CLAUDE.md. A breach in an untouched file
is surfaced to the developer instead of fixed here.

Four touched files are in neither scoped `.editorconfig` section yet and are added to both:
`SchemaMigration.cs`, `ChangelogDatabaseInitializer.cs`, `QuotinatorMigrationsTests.cs`, and every new
file this issue creates. `QuotinatorMigrationsTests.cs` is pre-conversion `var` throughout, so it
converts in this pass. Both `IDE0008` and `IDE0090` run until the counts stop dropping, and the
`IDE0028`/`IDE0305` warnings the conversion exposes are fixed in the same pass.

This plan doc and the new T2 document are both added to `Quotinator.slnx`, and this doc is linked from
`overview.md`'s Plan doc column.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A database created from scratch is reported as created | Unit test | `DatabaseInitializerTests.FreshDatabase_ReportsItWasCreatedFromBaseline` |
| 2 | ✅ | An existing, up-to-date database reports no creation | Unit test | `DatabaseInitializerTests.ExistingUpToDateDatabase_ReportsNoCreation` |
| 3 | ✅ | A created database produces the creation notification, carrying both schema versions | Unit test | `ProgramNotificationSeedingRegressionTests.Startup_DatabaseCreated_SeedsTheCreationNotification` |
| 4 | ✅ | Every migration carries a required, non-empty description, in all three lists | Unit test | `DatabaseInitializerOwnershipTests.EveryDataOwnedMigration_HasANonEmptyDescription`, `.EveryChangelogMigration_HasANonEmptyDescription`, `QuotinatorMigrationsTests.All_Descriptions_AreNonEmpty` |
| 5 | ✅ | Each applied migration is reported individually, one entry per migration run | Unit test | `DatabaseInitializerTests.AppliedMigrations_AreReportedIndividually` |
| 6 | ✅ | An applied migration is reported by what it did, not only by its number | Unit test | `DatabaseInitializerTests.AppliedMigration_IsReportedByItsDescriptionNotOnlyItsVersion` |
| 7 | ✅ | A database with no pending migrations reports none applied | Unit test | `DatabaseInitializerTests.NoPendingMigrations_ReportsNoAppliedMigrations` |
| 8 | ✅ | Each applied migration produces one notification naming what it did | Unit test | `ProgramNotificationSeedingRegressionTests.Startup_AfterPendingMigrations_SeedsOneNotificationPerMigration` |
| 9 | ✅ | Applying migrations writes no notification from inside the initializer (control) | Unit test | `DatabaseInitializerTests.ApplyingMigrations_WritesNoNotificationItself` |
| 10 | ✅ | A restart with neither event writes neither notification | Unit test | `ProgramNotificationSeedingRegressionTests.Startup_WithNothingToReport_SeedsNeitherNotification` |
| 11 | ✅ | Each new kind has a registered payload type | Unit test | `NotificationMetadataKindsTests` (derived from the enum, no edit needed) |
| 12 | ✅ | Both new kinds are accepted by the baseline and by the incremental replay | Unit test | `DatabaseInitializerOwnershipTests.NotificationMetadataKind_IsAcceptedByTheBaseline`, `.NotificationMetadataKind_IsAcceptedByTheIncrementalReplay` (both `DynamicData` over the enum) |
| 13 | ✅ | Baseline and incremental replay accept the same `MetadataKind` values after the widening | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` |
| 14 | ✅ | Both new kinds declare a layout and render exactly what they declare | Unit test | `NotificationTableTests.EveryMetadataKind_DeclaresWhetherItRendersDetail`, `.EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares`, `.PayloadDetail_ForEveryKind_IsSelfDescribing` |
| 15 | ✅ | Neither new kind throws or renders detail on an unreadable or absent payload | Unit test | `NotificationTableTests.EveryMetadataKind_WithAnUnreadablePayload_RendersNothingAndDoesNotThrow`, `.EveryMetadataKind_WithNoPayload_RendersNothing` |
| 16 | ✅ | Both new kinds are named in the per-kind rendering document | Unit test | `NotificationTableTests.EveryLiveNotificationKind_IsNamedInTheVariantDocument` |
| 17 | ✅ | Both titles and both bodies exist in all three languages | Unit test | `TranslationCompletenessTests` |
| 18 | ✅ | A first boot shows the creation notification and no migration notification; a restart shows neither | Live | `docs/automated-testing/notifications-and-changelog/15-a-created-database-says-so.md` |
| 19 | ✅ | An upgrade from the previous published tag shows one notification per applied migration, each naming what it did | Live | `docs/automated-testing/notifications-and-changelog/16-each-applied-migration-says-what-it-did.md` |
| 20 | ✅ | The new rule is recorded where a migration author will read it | Unit test | `RepositoryStructureTests.MigrationDocuments_SayEveryMigrationCarriesADescription`, over CLAUDE.md and `docs/database-conventions.md` |
| 21 | ❌ | T1: the application still starts | Live | Developer starts `Quotinator.Api` in Visual Studio and confirms it starts without error |
