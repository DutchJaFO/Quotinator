# #423: Seeding fills any empty database, so a reset is undone by the next restart

**Status:** Waiting for release
**GitHub issue:** #423
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Tick the Definition of done on the issue, then wait for the release.** Every step and every
verification row is green.

---

## What this fixes

`SeedIfEmptyInternalAsync` decides whether to seed by counting quotes, so every startup against an empty
quote table loads the configured files. A reset empties that table, which makes #156's decision, that
standard seeding is fresh-install-only or explicit-call-only, hold only until the next restart. In the
Home Assistant add-on a restart is routine.

It also empties #304's reseed recommendation of meaning. The recommendation exists so the operator
chooses whether to reload content; the restart chooses for them and records it as `resolved` /
`reseeded`, indistinguishable from the operator having pressed the button.

The signal the gate needs already exists and is already in the right place. `ApplyMigrationsAsync`
computes `isEmptyDatabase` from `Sql.Schema.AnyTableExists` and reports it as
`MigrationsResult.TookBaselinePath`, which `InitialiseAsync` already passes to
`RunInitialisedHookAsync`, the method that calls `OnInitialisedAsync`. Nothing new has to be detected,
persisted or migrated: the flag simply has to reach the gate that is currently counting rows instead.

---

## Steps

### 1. Write the failing tests and confirm them red

**Status:** ✅ Done, 2026-10-10. Three red on their own assertions, each after its own preconditions
passed: `InitialiseAsync_AfterReset_SeedsNothing` (1 quote where 0 is required),
`ReseedAsync_AfterReset_SeedsOnDemand` (same, on its restart precondition) and
`InitialiseAsync_AfterReset_LeavesTheReseedRecommendationActive` (`IsDismissed` already true).

`InitialiseAsync_FreshDatabase_StillSeeds` is green today and is a control, not a red test. The issue's
own Failing tests table lists it as ❌, which is wrong: it asserts behaviour that is already correct and
has to stay correct, so it can only go red if the fix oversteps and stops a fresh database seeding. That
is worth having, and it is worth not miscounting as evidence the defect exists.

Four, one per property the issue names, all in `DatabaseInitializerTests`:

`InitialiseAsync_AfterReset_SeedsNothing`, the defect itself: initialise, reset, initialise again, and
the quote table is still empty.

`InitialiseAsync_FreshDatabase_StillSeeds`, the property that must not regress, which is also what
makes the first test a fix rather than a removal of seeding.

`ReseedAsync_AfterReset_SeedsOnDemand`, the operator's route back.
`ResetThenReseed_ProducesAFromScratchDatabase` already asserts this composition and passes today; the
new test asserts it from the state the restart now leaves, so the two are not the same case.

`InitialiseAsync_AfterReset_LeavesTheReseedRecommendationActive`, the consequence for #304: the
recommendation is still undismissed after the restart, because the condition it describes is still true.

### 2. Gate the seed on the database being new

**Status:** ✅ Done, 2026-10-10. All four of step 1's tests green, build clean. `OnInitialisedAsync`
gained `bool databaseWasCreated`, which `RunInitialisedHookAsync` passes from the `tookBaselinePath` it
already held, and Quotinator's override gates `SeedIfEmptyAsync` on it. `SeedIfEmptyInternalAsync`'s own
count is untouched and its remarks now say what it is for, the post-lock re-check, rather than implying
it is the decision. Both helpers take `seedRan` explicitly instead of inferring a load from the count
having been zero.

`OnInitialisedAsync` takes whether this run created the database, which `RunInitialisedHookAsync`
already holds, and Quotinator's override gates `SeedIfEmptyAsync` on it.

`SeedIfEmptyInternalAsync`'s own quote count stays exactly as it is. It is the re-check a waiter
performs after acquiring `SharedSeedLock`, which is what keeps two concurrent starts in one process
from seeding twice; it is not the decision, and the new gate sits in front of the call rather than
replacing that guard.

Two helpers currently read "quotes were empty before seeding" as "a seed ran on this run", and that
reading stops being true the moment an empty database no longer seeds.
`ResolveReseedIfContentLoadedAsync` is already safe by construction, since it also requires content to
be present afterward, but its condition is restated in terms of what actually happened rather than left
to be inferred. `RecommendReseedIfSourceContentChangedAsync` returns early on an empty database for the
stated reason that "the seed applied whatever changed on this very run", which is now false on a reset
database, and takes the same correction.

### 3. Re-base the tests that encode the old gate

**Status:** ✅ Done, 2026-10-10. Full suite green: 4,523 passed across 11 projects, 0 warnings, 0 errors.
The three tests the plan named are re-based, `HasPendingContentSeedAsync` now mirrors both of
`ReSeedGenresIfEmptyAsync`'s conditions, and the 15 further tests the run found are re-based onto the
one content load a start that did not create the database still performs. The developer's direction on
the finding below: the guard is not moved, because it was never missing.

The full run also found two things reading the code did not, which is why the plan said to establish
this by running:

**A fresh database is not the same thing as the baseline path, and gating on the wrong one broke 27
tests.** `MigrationsResult.TookBaselinePath` is `true` only when the database is new *and* a baseline is
configured *and* the incremental path was not forced. `SqliteQuoteServiceConversationTests` and the
accent-search fixtures configure no baseline, so their brand-new databases stopped seeding entirely.
`MigrationsResult` now carries `DatabaseWasCreated` from `isEmptyDatabase` as its own field:
`TookBaselinePath` keeps its backup role, and the seed gate asks the new one.

**`SqlAggregateGuard` flagged `QuotinatorDatabaseInitializer.cs` over the English word "having" in a
comment.** The guard scans whole-file text for an aggregate call alongside a grouping clause keyword, and
this file is full of `COUNT(`. Reworded, with a note at the site so the next writer does not re-introduce
it.

#### Finding: the startup content-load guard loses its empty-quotes trigger

The 14 failures are one defect, not fourteen. `InitialiseWithContentPendingAndNoDiskSpaceAsync` creates
an empty database on a first start, then expects a second start to load content, take a backup first, and
refuse when none is possible. After this change the second start does not load content at all, so there
is no backup attempt and no refusal.

That is not a test artefact. It is the empty-quotes trigger of #348's `BackupGuardedStep.ContentLoad`:

- A start that created the database seeds, and deliberately takes no backup, since it has nothing to lose.
- A start that did not create the database no longer seeds, so it has nothing to protect.

What is left of the guard on startup is `ReSeedGenresIfEmptyAsync`, the genres-empty-with-quotes-present
case, which is where these 14 tests belong.

**`ReseedAsync` not backing up is #348's own deliberate decision, not a gap this opens.** The reseed
reached through a notification action is fully guarded by `NotificationActionExecutor.PrepareReseedAsync`:
it offers back-up-then-reseed, remove-oldest-then-reseed and reseed-without-backup, writes an
`AuditOperation.BackupSkipped` entry when the operator declines one, and refuses before anything is
written. The call site states the principle, that `ReseedAsync` does one thing and the caller composing
the reseed composes the backup with it. Moving a backup into `ReseedAsync` would contradict that and
double the backup on the notification path.

The one place that composition is missing is `POST /admin/database/reseed`
(`AdminEndpoints.cs:187`), which calls `ReseedAsync` with no backup and no refusal. That is one
endpoint's missing composition, not a hole in the guard, and it is not this issue's subject. Filed
separately rather than fixed here.

**What the 15 re-based tests needed.** One shared fixture, `ContentSeedPendingDatabaseAsync`, which
seeds a database and then deletes its genre rows: quotes present with genres empty is the single state
in which a start that did not create the database still loads content. Three of them drive a follow-up
start or reseed, so the fixture returns its batch and they reuse it, because `SimpleQuoteBatch` writes a
new file with a new quote id per call and the genre re-seed only has work for quotes already stored.

Two of the 15 changed more than their setup:

`InitialiseAsync_ContentLoadWithNoBackupPossible_LoadsNothing` asserted an empty quote table and was
**passing for the wrong reason**: nothing loaded, so nothing was there, which is what the refusal was
supposed to prove and no longer could. It now asserts no genre row was written, which is the load that
was actually refused.

`InitialiseAsync_ExecuteStepFails_SurfacesDistinctFailureReason` lost its injection, not its subject. A
throwing audit writer failed the quote import an empty quote table used to trigger; the genre re-seed
writes no audit entry, and cannot be failed through its files either, since `LoadSourceFileAsync`
treats a missing file and invalid JSON as outcomes rather than exceptions (ADR 022's shape, found
already correct). A dropped genre table is the remaining way in and a real condition: the step fails on
its own insert, after a backup that succeeded, and the exception is a `SqliteException` rather than an
`InvalidOperationException`. It now also covers `SafeHasPendingContentSeedAsync`'s documented
fail-safe, since the counting query is what throws first and must be read as "assume pending, take the
backup".

**A genre re-seed resolves a notification waiting on content, and the resolver had to be measured
rather than inferred.** `ResolveReseedIfContentLoadedAsync` was given `seedRan`, which is false on
every start that did not create the database, so an earlier refusal would never have been resolved by
the one load such a start can still perform. The caller now measures both counts across both load steps
and passes whether anything was actually loaded, which is the question the method's own name asks.

Three, each re-based rather than deleted, because each asserts something that survives on its own terms.

`InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup` (#277) asserts the startup after a reset
takes a backup because content-seed has real work to do. The work is what goes away, so the assertion
inverts: that start takes no backup, because there is nothing left for a backup to protect.

`InitialiseAsync_ContentSeedNeeded_TakesBackup` (#277) reaches its pending state through an empty quote
table on a non-baseline start, which is the same path. Its subject, that a start with genuine
content-seed work ahead of it backs up first, is unaffected, so it is rebuilt on the one case that still
qualifies: genres empty with quotes present, which is what `ReSeedGenresIfEmptyAsync` actually acts on.
`HasPendingContentSeedAsync` is corrected in the same step to mirror both of that method's conditions
rather than only the first, since a database with no quotes cannot have pending genre work either.

`Initialise_WithNonContentRowsOnly_StillSeeds` asserts that a database holding only non-content rows is
empty for seeding purposes. See *Scope changes*: the concern behind it is preserved, the case it
constructs is not, and it is re-based onto a brand-new database so the decision it records stays
testable.

The full suite is then run and anything else the change legitimately invalidates is re-based the same
way. `DatabaseInitializerTests` alone calls `ResetAsync` at ten sites, and `AdminEndpointsTests`,
`NotificationActionExecutorTests`, `ProgramNotificationSeedingRegressionTests`,
`StartupSummaryLoggerTests`, `ConflictResolutionTests`, `SourceCacheWiringTests` and
`UserSystemReseedConceptTests` each use it too, so the count is established by running them, not by
reading them.

### 4. Correct the two automated-test documents

**Status:** ✅ Done, 2026-10-10, written; run in step 8.

*A startup that cannot take a backup loads nothing* no longer reaches its refusal through a Reset, which
cannot reach it any more. It stops the container cleanly, deletes the genre rows with
`execute-sql.csx`, then re-enters, so the pending load is the genre re-seed. The evidence moved with the
mechanism: `quotes=0` proved the refusal when an emptied quote table was the trigger, and the quote
count is now constant throughout, so a new `Genre-Count` reads the genre table through `DbInspector`
(read-only; `execute-sql.csx` runs `ExecuteNonQuery` and cannot return a count) and `Genres` is what
each step asserts. Its canary section carries a note: those three substantive reds are about reporting
rather than how the refusal was reached, so they stand, but the canary ran the old setup and re-running
it needs an image built from `4cfe1006`.

*Every kind renders what its layout promises* dropped its second container, which is what that document
itself said this fix would allow. Step 8 now restarts the container step 7 already reset, and asserts
`quotes=0` alongside the active recommendation: without that, a listing taken before the producer
dismissed the row would look identical to the reset surviving.

*A startup that cannot take a backup loads nothing* reaches its refusal state through this defect: it
resets to empty the quote table and restarts so that the startup has content to load. With the fix that
route is gone, and the document needs a setup that produces a genuine content load on a start, which is
a fresh container rather than a restarted one.

*Every kind renders what its layout promises* says outright that its second container exists only
because the restart re-seeds and resolves the recommendation before it can be read, and that the step
can drop that container once this is fixed. It does.

### 5. Delete the knowledgebase entry

**Status:** ✅ Done, 2026-10-10. **Deleted**, with its row removed from `docs/knowledgebase/README.md`
and its entry from `Quotinator.slnx`. Deleted rather than retired by `docs/knowledgebase.md`'s own test,
*did this entry ship*: its affected-versions column read `1.9.0-alpha onwards` and 1.9.0 is unreleased,
so no operator outside development ever met the condition and the git history is its record.

`docs/knowledgebase/a-reset-is-undone-by-the-next-restart.md`, and its row in the knowledgebase index.
Deleted rather than retired, per `docs/knowledgebase.md`'s retention rule and the issue's own statement:
no release ever carried this condition.

### 6. Changelog

**Status:** ✅ Done, 2026-10-10. `423` added to `unreleased.issues` and one `highlights` plus one
`fixed` entry in each of `en`, `nl` and `de`, all three in the same commit. The markdown is not
regenerated here: that happens at release, which is what every recent changelog commit on this branch
did. `ChangelogSchemaTests` green, 42 passed.

`changelog.en.json`'s `unreleased` with `423` in `unreleased.issues`, and matching translated entries in
`nl` and `de` in the same commit. A `highlights` entry, because a reset that stays reset is exactly what
an operator observes.

### 7. T1

**Status:** ✅ Done, 2026-10-10, the developer's own Visual Studio run. The application reaches
*Quotinator ready* on `1.9.0-alpha`, migrating an existing database (Data v3 → v27, App v5 → v9) and
reporting 795 quotes.

The run went past startup and read this issue's subject directly. A reset at `20:39:02` rebuilt from the
baseline and left every count `0`. The restart at `20:39:51` logged
`[Database - Init] schema is up to date` followed by `[Database - Stats] 0 quotes  0 sources ...` and
**no `[Database - Seed]` line of any kind**: the database stayed as the reset left it. The Notifications
page showed *The database holds no quotes* still `Active`, `Action required`, carrying its *Back up,
then reseed* button. Choosing that button at `20:40:55` took a backup, imported all six files and
returned the database to 795 quotes.

The application still starts. Nothing beyond startup.

### 8. T2

**Status:** ✅ Done, 2026-10-10, against `quotinator:local` built from this working tree. Eleven of
eleven documents run: the designated smoke set of nine, plus the two this issue changed, plus the
issue's own live case.

**The issue's own live case, run in full.** A fresh container seeds `795` quotes; a reset leaves `0`
with an active `reseedrecommended`; a `docker restart` leaves `quotes=0` and the recommendation
undismissed, with `dismissReason` and `resolution` both empty. The proof it did not seed is positive,
not an absence: `seeding complete` appears **once** across two boot banners, and the restart boot's only
`[Database -` line is `[Database - Stats] 0 quotes 0 sources ...`.

***A startup that cannot take a backup loads nothing*, every step as written on the re-based setup.**
Steps 1, 2, 3, 4, 5, 6 and 7. The refusal is reached: `health=200`, `quotes=795` unchanged, `Genres` `0`,
`refusalLogged=True`, one `actionrequired` notification offering exactly
`removeoldestbackupthenreseed,reseedwithoutbackup`. All three options then run through the page and each
brings `Genres` back to `25`: *Reseed without a backup* with `backupsAdded=0`, `skipLogged=True`,
`skipAudited=1`; *Remove the oldest backup* with `fillerGone=True`, `removalAudited=1`,
`backupAudited=1`; and, with the filler removed by hand, `offered=backupthenreseed` alone, which writes
`backupsAdded=1`. No `[Runtime - Exception]` line at any stop. Its *Observed effect* carries these
measurements.

***Every kind renders what its layout promises*, step 8 on one container.** Step 1 produced
`announcement=1 importreviewpending=1 reseedfileapplied=5 whatsnew=1`, step 7's reset left
`reseedrecommended=1` alone, and the restart then read `reseedrecommended dismissed=False` beside
`announcement` and `whatsnew`, with `quotes=0`. The popup renders it with its own title (*The database
holds no quotes*), a 160-character body, `expandsInPlace=False` and `buttons=0`. `thrown=0`.

***Reset wipes the entire database and does not reseed*** (smoke): `quotes=0`, `audit=1`,
`status=NoResults items=0`, and both schema-version counters `1` before and `1` after
`preserveSchemaVersion=true`.

***A fresh seed resolves every bundled file with nothing left pending*** (smoke): `zeroCounts=` empty,
`pending=0`, duplicate-Source checks A and B both `(no rows)`, `undeclared date variants = 0`.

**The designated smoke set, all nine, run.** *Reset is a full wipe* and *A fresh seed resolves every
bundled file* as above. *Baseline*: `health=healthy`, `version=1.9.0-alpha` matching
`Directory.Build.props`, every search scope answering, `NoResults` where the document says so.
*Pagination contract*: `pageSize=0` returning every row with the effective size echoed on all three
endpoints, `422` for `pageSize=501` and for a page past the last, default `20`. *Staged-action review
workflow*: decide, undo, re-decide, apply, one `Applied` group holding the batch. *Per-file import
report*: `missingTypes=[]`, five reports, a reset reporting every count zero. *Notification system*:
`401` without a key and `404` with one, 3 operations tagged `Notifications` with the tag declared, the
empty state, the four constructed rows reading `Active`/`Expired`/`Dismissed`/`No longer applicable`,
and the Action button's Cancel leaving 795 quotes where Confirm dropped them to `0` with the
notification table emptied. *Changelog served from its own database*: a separate
`quotinatorchangelog.db`, 126 entries across 3 languages, served from the database with `fallbacks=0`.

**Two known defects met, neither this issue's.** The wait-page document's step 1 fails exactly as
[#438](https://github.com/DutchJaFO/Quotinator/issues/438) describes: `/version --expect 200` answers
`503`, which is #419's correct gating, and `autoRefresh=False` because `http.csx` sends no HTML
`Accept`. Its steps 2 to 4 pass. The import-report document passes while its `$expectedInLog` omits
`seasons`, which the `[Database - Stats]` line reports: #438's second defect, reproduced.

**One artefact of driving the suite headlessly, recorded so it is not read as a result.** The
notification document's closing `thrown=0` read `4`, all `SocketException`, which is the documented
[#402](https://github.com/DutchJaFO/Quotinator/issues/402) class that
`docs/knowledgebase/transport-connection-cancelled-during-a-request.md` names: four rapid headless
browser sessions each killed between steps. Nothing in the product threw.

The smoke set, plus the two documents step 4 changes, plus the issue's own live case: reset, restart,
and both the empty database and the still-active recommendation read back.

---

## Scope changes

**Seeding is gated on this run having created the database** (developer decision, 2026-10-10), rather
than on a persisted marker distinguishing never-seeded from reset. The issue says a genuinely fresh
database still seeds and says nothing about a database that exists and has never held content, of which
there are two cases: a start that refused the content load because no backup could be taken (#348), and
a container configured with no source files.

Under this gate neither of those auto-seeds on a later start; both wait for an explicit reseed, which
#348's own notification already offers as its action. The alternative, a persisted marker that keeps the
three states apart, would buy an automatic seed for the refused case at the cost of a migration, the
matching baseline update and its schema-drift test, and a decision on where the marker lives. It was
weighed and not taken.

**This supersedes the shape, not the substance, of the 2026-09-02 statement that "'empty' means no
seedable content, not that no table has rows."** That decision exists to stop the gate being broadened
to "any row anywhere", which would make a brand-new database read as already seeded once a
baseline-seeded reference table exists (#310/#268) and skip the seed in silence. This gate counts no
rows at all, so that failure mode is not reachable through it, and the concern is satisfied rather than
overruled. What does not survive is `Initialise_WithNonContentRowsOnly_StillSeeds`'s construction: it
applies the Data schema out of band and then expects a start to seed, which is precisely the
database-exists-but-holds-no-content case the decision above assigns to an explicit reseed. Re-based
onto a brand-new database, it still asserts the thing worth asserting, that the baseline path seeds
whatever else the baseline itself put there.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A startup after a reset seeds nothing | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_SeedsNothing` |
| 2 | ✅ | A genuinely fresh database still seeds on its first start | Unit test | `DatabaseInitializerTests.InitialiseAsync_FreshDatabase_StillSeeds`, a control: green before the fix, red only if the fix oversteps |
| 3 | ✅ | A reseed after a reset still seeds on demand | Unit test | `DatabaseInitializerTests.ReseedAsync_AfterReset_SeedsOnDemand` |
| 4 | ✅ | The reseed recommendation is still active after the restart | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_LeavesTheReseedRecommendationActive` |
| 5 | ✅ | A fresh database seeds whether or not a baseline is configured | Unit test | `SqliteQuoteServiceConversationTests` and the accent-search fixtures, which configure none; 27 red when the gate read `TookBaselinePath` |
| 6 | ✅ | A start after a reset takes no backup, because nothing is left to seed | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup`, re-based per step 3 |
| 7 | ✅ | A start with genuine content-seed work ahead of it still backs up first | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentSeedNeeded_TakesBackup`, rebuilt on the genres-empty-with-quotes-present case |
| 8 | ✅ | Pending content-seed work is reported only where the seed would actually act | Unit test | `HasPendingContentSeedAsync` asserted through rows 6 and 7, which are the two states that separate it |
| 9 | ✅ | The baseline path seeds whatever else the baseline itself populated | Unit test | `DatabaseInitializerTests.Initialise_WithNonContentRowsOnly_StillSeeds`, re-based per step 3 |
| 10 | ✅ | A content load that cannot be backed up is still refused, reported and named | Unit test | The nine `InitialiseAsync_ContentLoadWithNoBackupPossible_*` tests, on `ContentSeedPendingDatabaseAsync` |
| 11 | ✅ | A later start that does load content resolves an earlier refusal | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentLoadedAfterAnEarlierRefusal_ResolvesTheRefusal`, which the measured `contentLoaded` is what makes reachable |
| 12 | ✅ | A reseed still leaves the refusal for its own caller to resolve | Unit test | `DatabaseInitializerTests.ReseedAsync_LeavesTheRefusalForItsCallerToResolve` |
| 13 | ✅ | A failing load step still restores its backup and rethrows | Unit test | `DatabaseInitializerTests.InitialiseAsync_ExecuteStepFails_SurfacesDistinctFailureReason`, re-based onto a dropped genre table |
| 14 | ✅ | No remaining test depends on a restart re-seeding | Live | `dotnet test --configuration Release --verbosity normal -m:1`: 4,523 passed across 11 projects |
| 15 | ✅ | Build and full test run are clean | Live | `dotnet build --configuration Release` then the run above, both `0 Warning(s)  0 Error(s)` |
| 16 | ✅ | A reset survives a restart, and the recommendation waits for the operator | Live | T2: reset, `docker restart`, then `/api/v1/quotes?pageSize=1` reports 0 and `/api/v1/notifications` still holds the undismissed `reseedRecommended` |
| 17 | ✅ | A refused content load is still reached and still reported | Live | *A startup that cannot take a backup loads nothing*, on its corrected setup |
| 18 | ✅ | The recommendation's own layout is read on one container | Live | *Every kind renders what its layout promises*, with its second container dropped |
| 19 | ✅ | The application still starts | Live | T1, Visual Studio, developer's own action: reached *Quotinator ready*, and the restart after a reset logged `0 quotes` with no seed line |
| 20 | ✅ | T2 scope passes | Live | The smoke set, all nine, plus the two documents named above; the two pre-existing #438 defects met and recorded, not fixed here |
