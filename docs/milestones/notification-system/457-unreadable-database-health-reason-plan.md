# #457: The health reason for an unreadable database file misdiagnoses it as a schema-version mismatch

**Status:** In progress (step 6)
**GitHub issue:** #457
**Tiers required:** T1, T2
**Depends on:** #482, #490

---

## Next action

**Execute from step 6.** No question is open. #490 reached `Waiting for release` on 2026-11-17. The plan
was reviewed against what it delivered on 2026-11-18 at `a004340f` (*Reviewed against #490's delivery*
below), and decisions D and E, which that review raised, were accepted as recommended the same day
(developer, 2026-11-18). Every choice the steps leave to execution is already made in the step itself:
which fixture proves the truncated case is decided by a measurement in step 6, with both outcomes'
consequences written down (developer, 2026-11-18: *"if that test doesn't get the results we need then we
add another test"*).

---

## What this fixes

A database file SQLite cannot read, whether it is not a database at all or a real one cut in half,
degrades the application with the generic initialisation-failure reason:

> Database initialisation failed. This often means the database's recorded schema version doesn't match
> its actual on-disk schema (e.g. after an interrupted upgrade). Resolve with an explicit database Reset
> (POST /api/v1/admin/database/reset) or by stopping the app, deleting the database file, and restarting.

That diagnosis is wrong for this fault. The schema version is not the problem, because nothing in the
file can be read. The reason should say what is actually wrong with the file, tell that apart from a
schema problem, and name the remedies that work for it (developer direction, 2026-10-23: the reason should
be as close to the actual cause as the application can tell).

**Not this issue:**
- Detecting the fault: the layered check, running it at startup before the database is opened for
  writing, the `Fast` setting and what each layer costs are #490.
- The six admin endpoints that answer `500` while degraded are #453.
- The counts that read `0` under *last known-good state* are #428.
- Listing, downloading and removing a set-aside file is #481.
- Restoring a backup from inside the application is #444.
- Showing the check's report on a page is #491, and retrying startup without a restart is #492.
- How a Reset routes a file the Storage layer could not open (set aside, not backed up) is #490's
  `ResetDatabaseCheck` and ADR 025. This issue only says, in the reason, what the Reset will do (decision E).

---

## Why the plan was redone (2026-11-14)

The first version of this plan had startup call #482's `IsDatabaseFileReadable()` (one `PRAGMA
quick_check`) and report a single *unreadable* outcome. While it was being planned, three things came out:

- SQLite errors can still reach the generic catch after that check, because `quick_check` does not
  check indexes against their tables, and the `immutable` open ignores the `-wal` file.
- `quick_check` had been chosen in #482's design, but the developer was never asked to decide it.
- The developer decided that every database is checked in layers, cheapest first, at startup, before a
  Reset and on demand, with a setting that selects a faster check and states its risks. That work is
  filed as #490.

So this issue no longer builds or calls a check of its own. It consumes #490's outcome and turns the
layers meaning *the file is unsound* into a reason with remedies that work.

---

## Reviewed against #490's delivery (2026-11-18, `a004340f`)

The 2026-11-14 plan was written from #490's issue body, before #490 was built. Each point below was read
from the code as #490 left it, and the steps below are corrected to match.

- **Layers are named, not numbered** (`Quotinator.Data.Enums.DatabaseCheckLayer`): `Storage`, `Format`,
  `Journal`, `Pages`, `Indexes`, `References`, `Schema`. The earlier plan's *layers 1 to 4* are **Format,
  Journal, Pages and Indexes**: the layers `ResetDatabaseCheck.IsSoundCopy` treats as the file's own. This
  plan calls them *the file layers*.
- **Where the reason is chosen.** `StartupDegradationReasons` exists (#490, decision C). A check that stopped
  startup already wins first, through `StoppingLayer`, and `ForLayer` turns the failing layer into a reason.
  `ForLayer` sends the file layers, and a Storage `FileUnreadable` fault, to `InitialisationFailed`. **The
  change this issue makes is inside `ForLayer`**, not a new case at the head of `For`.
- **`ForLayer` has two callers.** `For` at startup, and `ForCheck`, which `GET /api/v1/admin/database/integrity`
  calls (`AdminEndpoints.cs`) to degrade the application when a check fails after startup (#490, row 27).
  Whatever this issue changes in `ForLayer` reaches both. Decision D.
- **The startup sequence moved** out of `Program.cs` into `Startup/DatabaseInitialisationSequence.cs` (#490,
  step 17). Its generic `catch (Exception ex)` remains the backstop ADR 027 requires.
- **The generic text is no longer in `Program.cs`.** It is `StartupDegradationReasons.InitialisationFailed`.
  Step 9 rewords it there, in place, rather than moving it out of `Program.cs`.
- **Logging is already done.** #490 replaced the per-reason log methods with one `LogStartupDegraded`, and
  `StartupDatabaseCheck.Log` already logs the failing layer and the engine's finding at Error. The earlier
  plan's `[Database - Startup]` log line (step 7) is dropped.
- **An unwritable data directory never reaches this issue's reason.** Under #490's decision C, Storage fails
  first with `DirectoryNotWritable`, and startup gives #326's reason, whatever the reading layers then find.
  Of `ResetObstacle`'s three members, only `SetAsideFolderNotWritable` can accompany a file-layer failure
  when the reason is written. `SetAsideMoveFailed` is found only by a Reset attempting the move.
- **`CheckSetAsideReadiness()` writes.** It creates `backups/unreadable/` and writes a probe file in it and
  in the data directory. The earlier plan's *nothing is written* was wrong. The folder is the one a Reset
  would create anyway, and the probe is removed, so this is harmless, but the plan no longer claims it.
- **Two shared sentences say *cannot be read* unconditionally.** `ResetObstacleGuidance.Cause(SetAsideFolderNotWritable)`
  begins *"The database file cannot be read"*, and `BackupObstacleGuidance`'s `SourceUnreadable` remedy speaks
  of *"the unreadable file"*. Reused verbatim, they would say *cannot be read* of an Indexes failure, which
  decision B rules out. Step 8 splits them so the folder sentence and the Reset sentence can be reused alone,
  still with one copy each.
- **The Knowledgebase entry is half rewritten.** #490 changed `the-database-file-cannot-be-read.md` to describe
  three conditions sharing the generic reason, and the layered Reset. Its Symptom is still the generic text.
- **Tests that assert the generic reason today** and change with this issue:
  - `StartupDegradationReasonsTests.For_FileLayerFailed_IsTheGenericReason` (four rows) and
    `…For_StorageFoundTheFileUnreadable_IsTheGenericReason`; `…For_EveryNewReason_IsDistinct` gains the new
    reasons;
  - `StartupResilienceTests.Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer`;
  - `AdminDatabaseIntegrityEndpointTests.Check_LayerFailsAfterStartup_DegradesTheApplication`;
  - the two `…HealthReasonNamesARemedyThatCanWork` tests, whose doc comment leaves the remedy order to #457.
- **Which layer each fixture fails at:**
  - `FactoryWithCorruptDatabase` (34 bytes of text) fails at **Format**: #490's row 13
    (`DatabaseInitializerTests.InitialiseAsync_NotADatabase_ReturnsTheFormatOutcomeWithoutOpeningForWrite`).
  - `FactoryWithTruncatedDatabase` (a migrated database cut to half its length) is **not yet measured**.
    #490's `Check_TruncatedDatabase_FailsAtPagesAndRunsNoLaterLayer` uses a fixture whose in-header size is
    valid. ADR 028 records that when the in-header size is invalid, SQLite takes the size from the file and
    no documented check guarantees a truncation is found. This fixture's header was not built to be valid, so
    it may fail at Pages, at another layer, or pass the check and reach the generic catch. Step 6 measures it
    and states what follows from each result.
- **A directory at the database path is not a `FileUnreadable` fault.** `SqliteCheckSubject.FileExists` is
  `File.Exists`, which is `false` for a directory, so Storage reports nothing to check and the failure reaches
  the `IsNotWritable` backstop. The existing `FactoryWithUnopenableDatabase` therefore cannot prove decision E;
  step 6 uses a lock instead.

---

## How it is built

1. #490's check fails at a file layer, or at Storage with `FileUnreadable`, at startup or on demand.
2. The caller asks `IResetReadiness.CheckSetAsideReadiness()` for the obstacle, only for those failures
   (decision D). `DatabaseInitializer` implements it with the method it already has, and both startup's
   `DatabaseInitialisationSequence` and the integrity endpoint receive it by DI.
3. `StartupDegradationReasons.ForLayer(failure, obstacle)` returns `DatabaseUnsoundGuidance.Reason(failure, obstacle)`
   for those failures:
   - one cause sentence for the layer that failed (decision B), or for the file that could not be opened
     (decision E);
   - then the remedies in the decided order (decision 2, or decision E's order for a file that could not be
     opened), leaving out the Reset when there is an obstacle (decision 3);
   - then the Knowledgebase link.

   The sequence logs and degrades as it does for every other reason. No notification is written, because
   there is no sound database to write it into.
4. The generic catch stays as a backstop for what no layer foresees, as ADR 027 requires, with its own
   wording corrected (decision 4).

### DRY and SOLID (developer direction, 2026-11-14)

- **One copy of every operator-facing sentence.** The Reset and backup remedies already exist in
  `BackupObstacleGuidance` (`SourceUnreadable`), and the set-aside obstacles in `ResetObstacleGuidance`.
  `DatabaseUnsoundGuidance` calls these and does not repeat their text. Where a sentence is `private` today it
  becomes `internal`, and where it carries a claim that does not hold for every caller (*cannot be read*), it
  is split at the source rather than copied with the claim removed. Each layer's cause sentence is written
  once and shared with #491's report (#491, requirement 4).
- **One check.** This issue writes no check. It reads #490's outcome, the same one a Reset and the
  integrity endpoint read.
- **One mapping, two callers.** `ForLayer` already serves startup and the on-demand check, so one change
  gives both the same reason for the same fault (decision D).
- **Interface segregation.** `IResetReadiness` is a new, narrow interface. `IDatabaseInitializer` is not
  widened, and `IDatabaseInitializer.InitialiseAsync` is not changed.
- **Dependency direction.** `Quotinator.Data` exposes a readiness outcome and knows no wording (ADR 004).

---

## Decisions

### Accepted 2026-11-14 (first plan)

Each is a mismatch between the issue (filed 2026-10-23, before #482 and #305 landed) and the code and
documents as they then stood, raised per `process.md`, Planning step 3. All five were accepted as
recommended by the developer.

1. **When the check runs.** *Superseded by #490.* Every layer runs at every startup, and a setting selects
   the faster check with its risks stated. Both the timing and the measured cost are #490's.
2. **Which remedies the reason names, and in what order:**
   - (a) the Reset, which sets the file aside in `backups/unreadable/` and replaces it with an empty
     database;
   - (b) restore an older backup in place of the file, then restart;
   - (c) stop the application, delete the file, and restart.

   (a) and (b) reuse `BackupObstacleGuidance`'s `SourceUnreadable` remedies. (c) keeps the phrase
   `deleting the database file`, so the two existing `…NamesARemedyThatCanWork` tests stay meaningful.
   Decision E sets a different order for a file that could not be opened.
3. **An unsound file whose Reset cannot proceed.** The reason leaves the Reset out and names the obstacle
   using `ResetObstacleGuidance`'s text, so it never leads with a remedy that would fail. *Narrowed by the
   2026-11-18 review:* only `SetAsideFolderNotWritable` can reach this reason, because an unwritable data
   directory fails Storage first and takes #326's reason.
4. **The generic catch's own wording** is in scope. It is reworded to say that initialisation failed for a
   cause this build does not recognise, to point to the log, and to keep the Reset and delete remedies.
   After #490 it is reached only by what no layer foresaw.
5. **The Knowledgebase link:** add `KnowledgebaseLinks.TheDatabaseFileCannotBeRead`, end the reason with
   it, and rewrite the entry. Decision B decides what the entry covers.

### Raised by the re-plan, accepted 2026-11-14

All three were accepted as recommended by the developer.

- **A. Where #490 ends and this issue starts.** This issue owns the reason, remedies and Knowledgebase entry
  for the file layers. #490 owns the check, the outcome, the startup wiring and the reasons for Storage's
  `DirectoryNotWritable`, References and an inconclusive check. Recorded on both issues.
- **B. One cause per layer:**
  - Format: not a database of this engine, by its header;
  - Journal: the write-ahead file beside it is damaged or does not belong to it;
  - Pages: pages are damaged, or missing;
  - Indexes: the file reads, but an index disagrees with its table, or a unique constraint does not hold.

  The last is not *cannot be read*. All four take the same remedies, because a Reset sets aside a file that
  fails any of them. The Knowledgebase entry keeps its file name, and its Symptom and Cause list each layer.
- **C. `StartupDegradationReasons` is created by #490.** Done: #490 moved the #348, #350 and #305 cases into
  it unchanged.

### Raised by the review against #490's delivery, accepted 2026-11-18

Both were accepted as recommended by the developer.

- **D. The on-demand check gives the same reason, with the same obstacle.** #490 made the integrity endpoint
  degrade the application through the same `ForLayer` (row 27), but that path has a `DatabaseCheckResult`
  only, with no `ResetObstacle`. Carrying the obstacle on `InitialiseAsync`'s result, as the earlier plan did,
  would keep decision 3 at startup and break it on demand.
  - *Accepted:* a narrow new interface, `IResetReadiness`, in `Quotinator.Data`, implemented by
    `DatabaseInitializer` with its existing `CheckSetAsideReadiness()`. Startup and the endpoint each ask it
    only when the failure is one this issue words, and `ForLayer` takes the obstacle as a parameter. The
    earlier step 6, setting `ResetObstacle` inside `InitialiseAsync`, is dropped, and `Quotinator.Data`'s
    startup path stays as #490 left it.
  - *Rejected:* the earlier plan's obstacle on the startup result only, which offers an unchecked Reset on
    demand; or this issue's reason at startup only, which gives one fault two reasons depending on when it
    was found.
- **E. A file the Storage layer could not open gets its own cause and remedies.** `StorageLayer` reports
  `FileUnreadable` when opening the file throws `IOException` or `UnauthorizedAccessException`: wrong
  permissions, a lock held by another process, or a directory at the database path. That is not damage, and
  the file may be sound. Today it takes the generic reason, which leads with a Reset that sets the file aside.
  - *Accepted:* in this issue, with its own cause sentence, and remedies in this order:
    - restore read access to the file, or stop whatever holds it, then restart;
    - restore an older backup in place of the file, then restart;
    - the Reset, saying that it sets aside a file that may be intact, and replaces it with an empty database.

    The Reset's routing is unchanged (*Not this issue*). The reason says what it does rather than changing it.
  - *Rejected:* the damaged-file cause for this fault, which repeats this issue's bug for it; or a separate
    issue alongside #455 and #456.

---

## Steps

### 1. Confirm decisions 1 to 5

**Status:** ✅ Done

All five accepted as recommended (developer, 2026-11-14). Decision 1 is superseded by #490.

### 2. Confirm decisions A, B and C, and post the matching comments

**Status:** ✅ Done

Accepted (developer, 2026-11-14). The scope comment on #457 and the boundary comment on #490 were posted on
2026-11-14, after approval.

### 3. Read what #490 delivered

**Status:** ✅ Done

Done 2026-11-18: *Reviewed against #490's delivery* above, and decisions D and E, accepted the same day.
The one measurement left, which layer the in-process truncated fixture fails at, belongs with the test that
depends on it and moved to step 6.

### 4. Record decisions D and E on the issue

**Status:** ✅ Done

Posted 2026-11-18, after approval: the comment, and the issue body's *Failing tests* table renamed to
`…HealthReasonNamesTheFault`.

Draft a comment on #457 covering decisions D and E and the corrected step list, show it, and post it after
approval. Update the issue's *Failing tests* table to the names in *Verification* rows 1 and 2, which
replace the issue's `…HealthReasonSaysTheFileCannotBeRead`: decision B means an Indexes failure is not
*cannot be read*, so the tests assert that the reason names the fault.

### 5. Add the touched files to the IDE0008/IDE0090 scoped lists

**Status:** ✅ Done

Done 2026-11-18. Every existing file below was already in both lists from earlier issues; the four new
files (`IResetReadiness.cs`, `DatabaseUnsoundGuidance.cs`, `DatabaseInitializerTests.ResetReadiness.cs`,
`DatabaseUnsoundGuidanceTests.cs`) were added to both, ahead of being created.

Each file this issue creates or edits goes into `.editorconfig`'s scoped sections the moment it is first
touched (`CLAUDE.md`, *Zero-warnings policy and boyscout rules*). Expected files:

- `StartupDegradationReasons.cs`, `DatabaseInitialisationSequence.cs`, `AdminEndpoints.cs`, `Program.cs`
  (DI registration only) and `DatabaseInitializer.cs`;
- the new `IResetReadiness.cs` and `DatabaseUnsoundGuidance.cs`;
- `BackupObstacleGuidance.cs`, `ResetObstacleGuidance.cs` and `KnowledgebaseLinks.cs`;
- `StartupResilienceTests.cs`, `StartupDegradationReasonsTests.cs`, `AdminDatabaseIntegrityEndpointTests.cs`,
  and the new test files.

### 6. Write the failing tests and confirm them red

**Status:** ⬜ Not started

Red first means signatures first: `IResetReadiness`, the new `ForLayer` parameter and
`DatabaseUnsoundGuidance` are added with no behaviour, so each test compiles and fails on its assertion.

- **Api, `StartupResilienceTests`:**
  - the issue's two tests (rows 1 and 2): the reason names what is wrong with the file and does not mention
    the schema version;
  - **before writing row 2's test, measure** the layer `FactoryWithTruncatedDatabase` fails at, by asserting
    `DatabaseHealthState.StartupCheck.FirstFailure.Layer` in a test, not from assumption:
    - *it fails at a file layer:* row 2's test uses it as it is, and asserts that layer's cause;
    - *it does not* (it passes the check and reaches the generic catch, or fails elsewhere): keep that test,
      asserting what it measured, since it then proves the backstop or that other layer; **and add another
      test** for row 2, `Startup_DatabaseFileTruncatedWithAValidHeader_HealthReasonNamesTheFault`, whose
      fixture checkpoints the migrated database and cuts it with `DocumentedFormatFixtures.TruncateToHalf`,
      which asserts the in-header size is valid first, as #490's row 3 does. The same rule applies to T2 `09`
      in step 11;
  - decision 3, with `FactoryWithCorruptDatabase(blockTheSetAsideFolder: true)`: the reason does not offer
    the Reset and names the obstacle (row 5);
  - decision E: a file the Storage layer cannot open gets its own cause, with the Reset last (row 7). The
    fixture is a valid database the test holds open with `FileShare.None` for the factory's lifetime:
    `StorageLayer` opens with `FileShare.ReadWrite | FileShare.Delete`, so the open fails with an
    `IOException`, a sharing violation on Windows and the .NET advisory lock on Linux, which Microsoft
    documents for `FileShare` between .NET handles. Not a directory at the path (see the review above), and
    not Unix permissions, which a root test runner ignores;
  - decision 4, the generic reason no longer claims a schema-version mismatch (row 6);
  - `Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer` asserts the Indexes cause rather than the
    generic reason.
- **Api, `AdminDatabaseIntegrityEndpointTests`** (decision D):
  - `Check_LayerFailsAfterStartup_DegradesTheApplication` asserts the Indexes cause;
  - a new test: a file layer failing after startup, with the set-aside folder blocked, degrades without
    offering the Reset (row 4).
- **Core, `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.ResetReadiness.cs`** (a new part of
  the existing partial class, next to `.UnreadableReset.cs`):
  `CheckSetAsideReadiness` through `IResetReadiness` reports `SetAsideFolderNotWritable` when the folder
  cannot be created, and nothing in a writable directory.
- **Api, `DatabaseUnsoundGuidanceTests`:**
  - each file layer's cause, and decision E's;
  - the remedies in the decided order, for both orders;
  - the Knowledgebase link;
  - the Reset left out when there is an obstacle;
  - an Indexes cause that does not say *cannot be read*, including through the reused obstacle and Reset
    sentences.
- **Api, `StartupDegradationReasonsTests`:** the four file-layer rows and the `FileUnreadable` test assert the
  new reason, and `For_EveryNewReason_IsDistinct` includes it.

### 7. Expose set-aside readiness as its own interface

**Status:** ⬜ Not started

- Add `Quotinator.Data.Database.IResetReadiness` with `ResetObstacle? CheckSetAsideReadiness()`, and have
  `DatabaseInitializer` implement it with the method it already has, made public.
- Register it in `Program.cs` as the same singleton the main `IDatabaseInitializer` resolves to, not a second
  instance (`CLAUDE.md`, *Dependency injection policy*). `NoOpDatabaseInitializer` implements it returning
  `null`, so endpoint tests stay database-free.
- Confirm by reading `ResetAsync` and `RebuildAsync` that their existing calls are unchanged.

### 8. Write the reason and map it

**Status:** ⬜ Not started

- Split the two shared sentences that claim *cannot be read* (see the review above) at their source, so the
  folder obstacle and the Reset remedy can be used alone. One copy each remains.
- Create `src/Quotinator.Api/Startup/DatabaseUnsoundGuidance.cs`: one cause per file layer (decision B), the
  `FileUnreadable` cause (decision E), the two remedy orders, and the Knowledgebase link (decision 5).
- Add `KnowledgebaseLinks.TheDatabaseFileCannotBeRead`.
- `StartupDegradationReasons.ForLayer` takes the obstacle and returns this reason for the file layers and
  `FileUnreadable`. `For` and `ForCheck` take the obstacle and pass it on.
- `DatabaseInitialisationSequence` and the integrity endpoint ask `IResetReadiness` only when the failing
  layer is one this reason covers, so a healthy start and every other failure write no probe.

### 9. Reword the generic reason

**Status:** ⬜ Not started

Rewrite `StartupDegradationReasons.InitialisationFailed` per decision 4, in place. It stays a constant where
#490 put it, beside the other two reasons #490 owns there. No `InitialisationFailureGuidance` class is
created: the rewording is one sentence and two remedies already written as text in the same constant.

### 10. Update the tests whose comments or assertions this changes

**Status:** ⬜ Not started

- The two `…HealthReasonNamesARemedyThatCanWork` tests: rewrite their doc comment, because the question
  it leaves to #457 is now answered, and check that the assertion still matches decision 2's wording.
- The tests listed in the review above, beyond those step 6 already rewrites.
- Search `tests/` for any other assertion on `InitialisationFailed` or its text.

### 11. Update the automated documents and the Knowledgebase

**Status:** ⬜ Not started

- `startup-and-degradation/08` and `09`:
  - step 3 also asserts the reason, with `TestProbe json --contains reason=<the layer's cause>`
    (`JsonExpectationParser` already supports `--contains`);
  - `09` follows step 6's rule: if its container truncation does not fail at a file layer, it records what
    it does reach, and a new document beside it truncates with a valid in-header size and asserts the Pages
    cause;
  - *Observed effect* is rewritten to the new reason, and the paragraph handing the misdiagnosis to #457
    is removed;
  - both are confirmed red against a canary (`quotinator:canary-457`, built from the commit before this
    issue's first commit), then the container, image and worktree are removed.
- `docs/knowledgebase/the-database-file-cannot-be-read.md`: the Symptom becomes the new reasons, the Cause
  lists each file layer and the file that could not be opened (decisions B and E), and the generic reason's
  *three conditions* framing is removed.
- `docs/automated-testing/README.md`'s *Observed effects awaiting measurement* list gains both documents
  until step 15 measures them.

### 12. Leave no contradiction in the documentation

**Status:** ⬜ Not started

Search `docs/`, `CLAUDE.md`, `addon/DOCS.md` and `addon-beta/DOCS.md` for the generic reason's text and
for *schema version doesn't match*, and correct each one that describes an unsound file. Endpoint
`[Description]`s that quote the reason are included, the integrity endpoint's among them.

### 13. Boyscout pass and full build

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings and 0 errors. IDE0008/IDE0090 fixes are applied to the files from step 5 only.

### 14. T1

**Status:** ⬜ Not started

This is the developer's step. In Visual Studio, start the app over a data directory holding a corrupt
`quotinatordata.db`, then:

1. read `/api/v1/health` and the degraded home page;
2. press *Reset the database*;
3. confirm the app returns to healthy.

### 15. T2

**Status:** ⬜ Not started

Run `08` and `09` in full against `quotinator:local` built from this branch, plus the smoke set
(`docs/automated-testing/README.md`, *end of an issue*). Record *Observed effect* with the date and the
commit.

### 16. Changelog

**Status:** ⬜ Not started

Add an `unreleased` entry in `changelog.en.json`, `nl.json` and `de.json`, carrying issue 457. This is a
user-facing highlight: the health message now says what is wrong with the database file, and what to do.

---

## Scope changes

Recorded on the issue in a matching comment (step 2, and step 4 for D and E):

- **An unsound file whose Reset cannot proceed** (decision 3). The reason leaves out the Reset when the file
  could not be set aside, and names what stops it. Verification row 5.
- **The generic initialisation-failure reason** (decision 4), handed to this issue by #305's plan. It no
  longer claims a schema-version mismatch. Verification row 6.
- **Detection moved to #490** (2026-11-14). This issue no longer runs a check of its own. It consumes
  #490's layered outcome, and #457 now depends on #490.
- **One cause per layer** (decision B). The issue as filed speaks of a file that *cannot be read*. The
  Indexes layer finds a file that can be read but is not sound, so the reason names each case, and the
  failing tests are renamed from `…SaysTheFileCannotBeRead` to `…NamesTheFault`.
- **`StartupDegradationReasons`** is created by #490 (decision C), not here.
- **The reason also applies when a check after startup degrades the application** (decision D), with the
  same obstacle. Verification row 4.
- **A file that could not be opened** (decision E) gets its own cause, with access restored first and the
  Reset last. Verification row 7.

The remedies (decision 2) differ from the issue's *replace or delete the file, or restore a backup*: the
Reset leads for a damaged file, since #482 made it work on an unreadable one. This changes the wording of
the issue's expected behaviour, not its scope.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A corrupt file's health reason names what is wrong with the file, and does not diagnose a schema-version mismatch | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_HealthReasonNamesTheFault` |
| 2 | ❌ | A truncated file's health reason does the same | Unit test | `StartupResilienceTests.Startup_DatabaseFileTruncated_HealthReasonNamesTheFault`, or, if step 6 measures that fixture missing every file layer, `…Startup_DatabaseFileTruncatedWithAValidHeader_HealthReasonNamesTheFault` beside it |
| 3 | ❌ | *(decision B)* Each file layer has its own cause, followed by the remedies in the decided order and the Knowledgebase link; an Indexes cause never says *cannot be read* | Unit test | `DatabaseUnsoundGuidanceTests.Reason_EachLayer_NamesItsOwnCause`, `…Reason_NamesTheResetThenABackupThenDeletingTheFile`, `…Reason_EndsWithTheKnowledgebaseLink`, `…Reason_Indexes_DoesNotSayTheFileCannotBeRead`; `StartupDegradationReasonsTests.For_FileLayerFailed_IsTheUnsoundReason`; `StartupResilienceTests.Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer` |
| 4 | ❌ | *(decision D)* A file layer failing after startup degrades with the same reason, and leaves out the Reset when the set-aside folder is unusable | Unit test | `AdminDatabaseIntegrityEndpointTests.Check_LayerFailsAfterStartup_DegradesTheApplication`, `…Check_LayerFailsAfterStartup_SetAsideFolderBlocked_DoesNotOfferTheReset`; `StartupDegradationReasonsTests.ForCheck_FileLayerFailed_IsTheUnsoundReason` |
| 5 | ❌ | *(decision 3)* With the set-aside folder unusable, readiness reports the obstacle, and the startup reason leaves out the Reset and names the obstacle | Unit test | `DatabaseInitializerTests.CheckSetAsideReadiness_FolderCannotBeCreated_ReportsTheObstacle`, `…CheckSetAsideReadiness_WritableDirectory_ReportsNone`; `DatabaseUnsoundGuidanceTests.Reason_WithAResetObstacle_LeavesTheResetOut`; `StartupResilienceTests.Startup_DatabaseFileCorrupt_SetAsideFolderCannotBeCreated_HealthReasonDoesNotOfferTheReset` |
| 6 | ❌ | *(decision 4)* The generic reason no longer claims a schema-version mismatch | Unit test | `StartupResilienceTests.Startup_UnrecognisedInitialisationFailure_HealthReasonDoesNotClaimASchemaMismatch` |
| 7 | ❌ | *(decision E)* A file the Storage layer cannot open gets its own cause, with access restored first and the Reset last, described as setting aside a file that may be intact | Unit test | `DatabaseUnsoundGuidanceTests.Reason_FileCouldNotBeOpened_RestoresAccessFirstAndResetsLast`; `StartupDegradationReasonsTests.For_StorageFoundTheFileUnreadable_IsTheCouldNotBeOpenedReason`; `StartupResilienceTests.Startup_DatabaseFileCannotBeOpened_HealthReasonRestoresAccessFirst` |
| 8 | ❌ | The existing refused-migration, overshoot, schema-integrity, data-directory and inconclusive reasons are unchanged | Unit test | Existing #348, #350, #305, #326 and #490 `StartupResilienceTests` and `StartupDegradationReasonsTests` stay green |
| 9 | ❌ | The Reset the reason names still recovers an unsound file | Unit test | Existing `Startup_DatabaseFileCorrupt_ResetRecoversToHealthy` and `…_RepairActionRecoversToHealthy` stay green |
| 10 | ❌ | Live: a corrupt file degrades with this reason and recovers through the Reset | Live (T2) | `docs/automated-testing/startup-and-degradation/08` steps 3 and 5 pass on this build and fail at step 3 on `quotinator:canary-457` |
| 11 | ❌ | Live: a truncated file does the same | Live (T2) | `docs/automated-testing/startup-and-degradation/09` steps 3 and 5, same canary |
| 12 | ❌ | The full build and test suite are clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: 0 warnings, 0 errors, all passed |
| 13 | ❌ | T1: the degraded page shows the new reason, and its Reset recovers | Live (T1) | Step 14, confirmed by the developer |
