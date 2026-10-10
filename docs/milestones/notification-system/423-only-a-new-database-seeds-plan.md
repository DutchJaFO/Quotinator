# #423: Seeding fills any empty database, so a reset is undone by the next restart

**Status:** Planning
**GitHub issue:** #423
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute this plan**, starting at step 1. The one question the issue leaves open, what counts as fresh
enough to seed, is decided in *Scope changes* below, so nothing is outstanding.

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

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

*A startup that cannot take a backup loads nothing* reaches its refusal state through this defect: it
resets to empty the quote table and restarts so that the startup has content to load. With the fix that
route is gone, and the document needs a setup that produces a genuine content load on a start, which is
a fresh container rather than a restarted one.

*Every kind renders what its layout promises* says outright that its second container exists only
because the restart re-seeds and resolves the recommendation before it can be read, and that the step
can drop that container once this is fixed. It does.

### 5. Delete the knowledgebase entry

**Status:** ⬜ Not started

`docs/knowledgebase/a-reset-is-undone-by-the-next-restart.md`, and its row in the knowledgebase index.
Deleted rather than retired, per `docs/knowledgebase.md`'s retention rule and the issue's own statement:
no release ever carried this condition.

### 6. Changelog

**Status:** ⬜ Not started

`changelog.en.json`'s `unreleased` with `423` in `unreleased.issues`, and matching translated entries in
`nl` and `de` in the same commit. A `highlights` entry, because a reset that stays reset is exactly what
an operator observes.

### 7. T1

**Status:** ⬜ Not started

The application still starts. Nothing beyond startup.

### 8. T2

**Status:** ⬜ Not started

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
| 1 | ❌ | A startup after a reset seeds nothing | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_SeedsNothing` |
| 2 | ❌ | A genuinely fresh database still seeds on its first start | Unit test | `DatabaseInitializerTests.InitialiseAsync_FreshDatabase_StillSeeds` |
| 3 | ❌ | A reseed after a reset still seeds on demand | Unit test | `DatabaseInitializerTests.ReseedAsync_AfterReset_SeedsOnDemand` |
| 4 | ❌ | The reseed recommendation is still active after the restart | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_LeavesTheReseedRecommendationActive` |
| 5 | ❌ | A start after a reset takes no backup, because nothing is left to seed | Unit test | `DatabaseInitializerTests.InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup`, re-based per step 3 |
| 6 | ❌ | A start with genuine content-seed work ahead of it still backs up first | Unit test | `DatabaseInitializerTests.InitialiseAsync_ContentSeedNeeded_TakesBackup`, rebuilt on the genres-empty-with-quotes-present case |
| 7 | ❌ | Pending content-seed work is reported only where the seed would actually act | Unit test | `HasPendingContentSeedAsync` asserted through rows 5 and 6, which are the two states that separate it |
| 8 | ❌ | The baseline path seeds whatever else the baseline itself populated | Unit test | `DatabaseInitializerTests.Initialise_WithNonContentRowsOnly_StillSeeds`, re-based per step 3 |
| 9 | ❌ | No remaining test depends on a restart re-seeding | Live | `dotnet test --configuration Release --verbosity normal -m:1`, all passed |
| 10 | ❌ | Build and full test run are clean | Live | `dotnet build --configuration Release` then the run above, both `0 Warning(s)  0 Error(s)` |
| 11 | ❌ | A reset survives a restart, and the recommendation waits for the operator | Live | T2: reset, `docker restart`, then `/api/v1/quotes?pageSize=1` reports 0 and `/api/v1/notifications` still holds the undismissed `reseedRecommended` |
| 12 | ❌ | A refused content load is still reached and still reported | Live | *A startup that cannot take a backup loads nothing*, on its corrected setup |
| 13 | ❌ | The recommendation's own layout is read on one container | Live | *Every kind renders what its layout promises*, with its second container dropped |
| 14 | ❌ | The application still starts | Live | T1, Visual Studio, developer's own action |
| 15 | ❌ | T2 scope passes | Live | The smoke set plus the two documents named above |
