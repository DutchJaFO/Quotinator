# #457: The health reason for an unreadable database file misdiagnoses it as a schema-version mismatch

**Status:** In progress (step 3)
**GitHub issue:** #457
**Tiers required:** T1, T2
**Depends on:** #482, #490

---

## Next action

**Wait for #490, then start at step 3.** The plan was redone on 2026-11-14, on
top of #490, which now builds the check that tells this issue the file is unsound. Decisions A, B and C
were accepted as recommended (developer, 2026-11-14). No step after step 2 can start until #490 reaches
`Waiting for release`, because each one consumes the outcome #490 defines.

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

## Verified against the code before planning

Every claim below was read from the current code on `feature/notification-system` at `0a77be1b`, not
taken from the issue. Claims about #490 come from its issue body, because it is not built yet. Step 3
checks them again against what #490 delivers.

- **The reason comes from the generic catch** in `src/Quotinator.Api/Program.cs` (the `catch (Exception
  ex)` after `catch (Exception ex) when (IsDataDirectoryNotWritable(ex))`). Today an unreadable file
  throws from `EnableWal` (`PRAGMA journal_mode=WAL`) inside `DatabaseInitializer.InitialiseAsync`, before
  any migration or schema version is read.
- **#490 replaces that path.** Startup runs every layer before the database is opened for writing, and a
  failed layer comes back as a typed outcome naming the layer and what it found (#490, requirements 1 to
  3). Layers 1 to 4 are the SQLite header, the `-wal` header, `quick_check` and `integrity_check`. #490
  treats a failure in any of them as *not a sound copy*: a Reset sets that file aside rather than backing
  it up (requirement 5). That is the class of fault this issue's reason describes.
- **ADR 027 rules out the alternative:** *"A new recovery path that interprets an exception to decide what
  state the database is in contradicts this ADR, however narrow the case it handles."* Reading the
  outcome of #490 is the only route this issue may use. A match on error codes 26 and 11 in a `catch` is
  not allowed.
- **ADR 022 decides the shape:** the condition is detected deliberately, so it travels as an outcome on
  `DatabaseOperationResult`, the same way #305 returns `SchemaNotIntact` and #348 returns a refused
  migration.
- **ADR 004 places the wording:** `Quotinator.Data` returns a typed outcome, and the Api layer turns it
  into text, in a `*Guidance` class next to `SchemaIntegrityGuidance` and `SchemaOvershootGuidance`.
- **The remedies already exist as text.** `BackupObstacleGuidance` has a `SourceUnreadable` cause
  (*"The database file itself cannot be read: it is corrupt, truncated, or not a database"*) and
  remedies written for #482's Reset: the Reset sets the file aside in `backups/unreadable/`, or restore an
  older backup. `ResetObstacleGuidance` describes what stops that Reset. Per `CLAUDE.md`'s *search the
  whole solution before adding a helper*, the new reason reuses these strings instead of writing them a
  second time.
- **`CheckSetAsideReadiness()`** (#482, private in `DatabaseInitializer`) reports whether a Reset could
  move the file aside. Only `ResetAsync` calls it today.
- **Existing tests hold part of the current wording.**
  `StartupResilienceTests.Startup_DatabaseFileCorrupt_HealthReasonNamesARemedyThatCanWork` and
  `Startup_DatabaseFileTruncated_HealthReasonNamesARemedyThatCanWork` assert the substring `deleting the
  database file`. Their doc comment says *"which remedy the reason leads with for this fault is #457's"*.
- **Documents name this issue as the owner of the reason:** `docs/knowledgebase/the-database-file-cannot-be-read.md`
  (its Symptom is the generic text), T2 documents `startup-and-degradation/08` and `09` (*Observed effect*),
  #305's plan (*"The generic catch's wording is #457's"*), and #327's plan (blocked by #457).

---

## How it is built

1. #490's check fails at one of layers 1 to 4 during startup, and `InitialiseAsync` returns that outcome
   without opening the database for writing.
2. For those layers only, `InitialiseAsync` also runs `CheckSetAsideReadiness()` and carries any obstacle
   on the same `DatabaseOperationResult`, through the `ResetObstacle` property it already has. Nothing is
   written.
3. `StartupDegradationReasons.For(...)` maps a layer 1 to 4 outcome to `DatabaseUnsoundGuidance.Reason(...)`:
   - one cause sentence for the layer that failed;
   - then the shared remedies, which leave out the Reset when an obstacle was found;
   - then the Knowledgebase link.

   `Program.cs` logs it and calls `MarkFailed`, as it does for every other reason. No notification is
   written, because there is no sound database to write it into.
4. The generic catch stays as a backstop for what no layer foresees, as ADR 027 requires, with its own
   wording corrected (decision 4).

### DRY and SOLID (developer direction, 2026-11-14)

- **One copy of every operator-facing sentence.** The unsound-file cause and the Reset and backup
  remedies already exist in `BackupObstacleGuidance` (`SourceUnreadable`), and the set-aside obstacles in
  `ResetObstacleGuidance`. `DatabaseUnsoundGuidance` calls these. It does not repeat their text. Where a
  sentence is `private` today, it becomes `internal` instead of being copied. Each layer's cause sentence
  is written once and shared with #491's report, which shows *the cause and remedy text the degraded
  reason uses* (#491, requirement 4). The generic reason moves out of the inline `const` in `Program.cs`
  into a guidance class too.
- **One check.** This issue writes no check. It reads #490's outcome, the same one a Reset and the
  integrity endpoint read.
- **Single responsibility: choosing the reason is separate from applying it.** `StartupDegradationReasons.For(...)`
  returns the first applicable reason, or `null`, and `Program.cs` applies it once. #490 creates it
  (decision C), and this issue adds one case.
- **Open/closed and dependency direction.** `Quotinator.Data` gains nothing from this issue beyond setting
  `ResetObstacle` on a path that already returns `DatabaseOperationResult`. It knows nothing about wording
  (ADR 004).
- **Interface segregation.** No interface is widened: `IDatabaseInitializer.InitialiseAsync` already
  returns `DatabaseOperationResult`.

---

## Decisions

### Accepted 2026-11-14 (first plan)

Each is a mismatch between the issue (filed 2026-10-23, before #482 and #305 landed) and the code and
documents as they now stand, raised per `process.md`, Planning step 3. All five were accepted as
recommended by the developer.

1. **When the check runs.** *Superseded by #490.* Accepted as *"before anything else, on every
   startup"*. The developer then widened it: every layer runs at every startup, and a setting selects the
   faster check with its risks stated. Both the timing and the measured cost are #490's.
2. **Which remedies the reason names, and in what order:**
   - (a) the Reset, which sets the file aside in `backups/unreadable/` and replaces it with an empty
     database;
   - (b) restore an older backup in place of the file, then restart;
   - (c) stop the application, delete the file, and restart.

   (a) and (b) reuse `BackupObstacleGuidance`'s `SourceUnreadable` remedies. (c) keeps the phrase
   `deleting the database file`, so the two existing `…NamesARemedyThatCanWork` tests stay meaningful.
   *Unchanged.*
3. **An unsound file in a data directory that cannot be written.** The reason leaves the Reset out and
   names the obstacle using `ResetObstacleGuidance`'s cause text, so it never leads with a remedy that
   would fail. *Unchanged.*
4. **The generic catch's own wording** is in scope. It is reworded to say that initialisation failed for a
   cause this build does not recognise, to point to the log, and to keep the Reset and delete remedies.
   *Unchanged. After #490 it matters more, not less, because the generic catch is then only reached by
   what no layer foresaw.*
5. **The Knowledgebase link:** add `KnowledgebaseLinks.TheDatabaseFileCannotBeRead`, end the reason with
   it, and rewrite the entry. *Unchanged. Decision B decides what the entry covers.*

### Raised by the re-plan, accepted 2026-11-14

All three were accepted as recommended by the developer.

- **A. Where #490 ends and this issue starts.** #490's requirements 3 and 9 say startup degrades *"with a
  reason specific to the layer that failed"*, and every new reason gets a Knowledgebase entry. Read
  literally, that includes the layer 1 to 4 reasons this issue exists to write.
  - *Recommended:* this issue owns the reason, remedies and Knowledgebase entry for layers 1 to 4, the
    layers that mean *not a sound copy*. #490 owns the check, the outcome, the startup wiring and the
    reasons for layers 0 and 5. Layer 0 extends #326's existing reason, and layer 6 keeps #305's. Until
    this issue lands on the same branch, a layer 1 to 4 failure keeps the generic reason, which is no
    worse than today.
  - This needs a comment on #490 recording the boundary.
  - *Alternative:* #490 writes every reason, and this issue shrinks to decisions 3 and 4 plus the
    Knowledgebase entry.
- **B. One cause for all four layers, or one per layer.** The first plan had a single *cannot be read*
  cause. With #490, the application can tell more than that:
  - layer 1: not a database, or shorter than its header says;
  - layer 2: the `-wal` file is damaged;
  - layer 3: pages are damaged;
  - layer 4: the file reads, but an index disagrees with its table.

  The last case is not *cannot be read*. *Recommended:* one cause sentence per layer, followed by the
  shared remedies. The developer's direction of 2026-10-23 asks for the reason to be as close to the
  cause as the application can tell. All four layers take the same remedies, because #490 sets aside a
  file that fails any of them. The Knowledgebase entry keeps its file name, and its Symptom and Cause list
  each layer. *Alternative:* one shared cause for all four, which is shorter but says *cannot be read* of
  a file that can be.
- **C. Which issue creates `StartupDegradationReasons`.** The first plan extracted it here. But #490 now
  lands first and adds cases of its own (layers 0 and 5), which would grow the `if` chain in `Program.cs`
  before this issue removes it. *Recommended:* the extraction moves to #490. It moves the #348, #350 and
  #305 cases unchanged and adds its own, and this issue adds the layer 1 to 4 case first in the order.
  This needs a comment on #490 and drops a scope change from this issue. *Alternative:* keep it here, and
  #490 adds its cases to `Program.cs` for this issue to move.

---

## Steps

### 1. Confirm decisions 1 to 5

**Status:** ✅ Done

All five accepted as recommended (developer, 2026-11-14). Decision 1 is now superseded by #490.

### 2. Confirm decisions A, B and C, and post the matching comments

**Status:** ✅ Done

Decisions A, B and C were accepted (developer, 2026-11-14). The scope comment on #457 and the boundary
comment on #490 were posted on 2026-11-14, after approval.

### 3. Wait for #490, then read what it delivered

**Status:** ⬜ Not started

Once #490 is at `Waiting for release`, read its outcome type and its tests, and record here:

- the type and members this issue maps;
- which layer the two existing sabotages fail at: the 34-byte text file used by
  `FactoryWithCorruptDatabase`, and the half-length database used by `FactoryWithTruncatedDatabase`.
  Read this from #490's tests or a startup log, not assumed. Both are expected to fail at layer 1.

Adjust steps 5 to 8 to the names that actually exist.

### 4. Add the touched files to the IDE0008/IDE0090 scoped lists

**Status:** ⬜ Not started

Each file this issue creates or edits goes into `.editorconfig`'s scoped sections the moment it is first
touched (`CLAUDE.md`, *Zero-warnings policy and boyscout rules*). Expected files:

- `Program.cs`, `DatabaseInitializer.cs` and `StartupDegradationReasons.cs`;
- the new `DatabaseUnsoundGuidance.cs` and `InitialisationFailureGuidance.cs`;
- `BackupObstacleGuidance.cs`, `ResetObstacleGuidance.cs` and `KnowledgebaseLinks.cs`;
- `StartupResilienceTests.cs`, and the new Core and guidance test files.

### 5. Write the failing tests and confirm them red

**Status:** ⬜ Not started

Every test in *Verification* rows 1 to 8 is written and run against the code as #490 left it, before any
production change. Each must fail for the reason it names, not because something does not compile.

- **Api, `StartupResilienceTests`:**
  - the issue's two tests: the reason names what is wrong with the file and does not mention the schema
    version;
  - with decision 3, an unsound file whose set-aside folder cannot be created: the reason does not offer
    the Reset. Use the fixture that `Startup_DatabaseFileCorrupt_ResetWhenTheSetAsideFolderCannotBeCreated_…`
    already uses;
  - with decision 4, the generic reason no longer claims a schema-version mismatch.
- **Core, `DatabaseInitializerTests.UnreadableStartup.cs`** (a new part of the existing partial class, next
  to `.UnreadableReset.cs`): an unsound file whose set-aside folder cannot be created reports the
  obstacle, and an unsound file in a writable directory reports none.
- **Api, `DatabaseUnsoundGuidanceTests`:**
  - each layer's cause;
  - the remedies in the decided order;
  - the Knowledgebase link;
  - the Reset left out when there is an obstacle.
- **Api, `StartupDegradationReasonsTests`:** a layer 1 to 4 outcome maps to this reason, ahead of every
  other case.

### 6. Carry the set-aside obstacle on the startup outcome

**Status:** ⬜ Not started

In `InitialiseAsync`, when #490's check fails at layers 1 to 4, run `CheckSetAsideReadiness()` and set
`ResetObstacle` on the result. `QuotinatorDatabaseInitializer` must not run its content-load hooks on this
path: confirm this by reading `RunInitialisedHookAsync`'s callers.

### 7. Write the reason and map it at startup

**Status:** ⬜ Not started

- Create `src/Quotinator.Api/Startup/DatabaseUnsoundGuidance.cs`, with one cause per layer (decision B).
  It reuses `BackupObstacleGuidance`'s `SourceUnreadable` remedies and `ResetObstacleGuidance`'s causes,
  without copying either.
- Add the `KnowledgebaseLinks` constant (decision 5).
- Add the case to `StartupDegradationReasons` first in the order, because a file that fails layers 1 to 4
  skips every later check. #490 creates the class (decision C).
- `Program.cs` logs with a `[Database - Startup]` line that follows the `LogStartupSchemaNotIntact`
  pattern.

### 8. Reword the generic reason

**Status:** ⬜ Not started

Move the generic catch's text out of `Program.cs` into `InitialisationFailureGuidance` and rewrite it per
decision 4.

### 9. Update the tests whose comments or assertions this changes

**Status:** ⬜ Not started

- The two `…HealthReasonNamesARemedyThatCanWork` tests: rewrite their doc comment, because the question
  it leaves to #457 is now answered, and check that the assertion still matches decision 2's wording.
- Search `tests/` for any other assertion on the generic reason's text.

### 10. Update the automated documents and the Knowledgebase

**Status:** ⬜ Not started

- `startup-and-degradation/08` and `09`:
  - step 3 also asserts the reason, using `TestProbe json --contains` (or the equivalent check the tool
    already has, to be confirmed) for the layer's cause;
  - *Observed effect* is rewritten to the new reason, and the paragraph handing the misdiagnosis to #457
    is removed;
  - both are confirmed red against a canary (`quotinator:canary-457`, built from the commit before this
    issue's first commit), then the container, image and worktree are removed.
- `docs/knowledgebase/the-database-file-cannot-be-read.md`: Symptom, Cause and Notes are rewritten for
  each of layers 1 to 4 (decision B). The *same reason for two conditions* framing no longer holds.
- `docs/automated-testing/README.md`'s *Observed effects awaiting measurement* list gains both documents
  until step 14 measures them.

### 11. Leave no contradiction in the documentation

**Status:** ⬜ Not started

Search `docs/`, `CLAUDE.md`, `addon/DOCS.md` and `addon-beta/DOCS.md` for the generic reason's text and
for *schema version doesn't match*, and correct each one that describes an unsound file. Endpoint
`[Description]`s that quote the reason are included.

### 12. Boyscout pass and full build

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings and 0 errors. IDE0008/IDE0090 fixes are applied to the files from step 4 only.

### 13. T1

**Status:** ⬜ Not started

This is the developer's step. In Visual Studio, start the app over a data directory holding a corrupt
`quotinatordata.db`, then:

1. read `/api/v1/health` and the degraded home page;
2. press *Reset the database*;
3. confirm the app returns to healthy.

### 14. T2

**Status:** ⬜ Not started

Run `08` and `09` in full against `quotinator:local` built from this branch, plus the smoke set
(`docs/automated-testing/README.md`, *end of an issue*). Record *Observed effect* with the date and the
commit.

### 15. Changelog

**Status:** ⬜ Not started

Add an `unreleased` entry in `changelog.en.json`, `nl.json` and `de.json`, carrying issue 457. This is a
user-facing highlight: the health message now says what is wrong with the database file, and what to do.

---

## Scope changes

Recorded on the issue in a matching comment (step 2):

- **An unsound file in a data directory that cannot be written** (decision 3). The reason leaves out the
  Reset when the file could not be set aside, and names what stops it. Verification row 5.
- **The generic initialisation-failure reason** (decision 4), handed to this issue by #305's plan. It no
  longer claims a schema-version mismatch. Verification row 6.
- **Detection moved to #490** (2026-11-14). This issue no longer runs a check of its own. It consumes
  #490's layered outcome, and #457 now depends on #490.
- **One cause per layer** (decision B). The issue as filed speaks of a file that *cannot be read*. Layer
  4 finds a file that can be read but is not sound, so the reason names each case.
- **`StartupDegradationReasons`** is created by #490 (decision C), not here. This issue adds its case to
  it.

The remedies (decision 2) differ from the issue's *replace or delete the file, or restore a backup*: the
Reset leads, since #482 made it work on an unreadable file. This changes the wording of the issue's
expected behaviour, not its scope.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A corrupt file's health reason names what is wrong with the file, and does not diagnose a schema-version mismatch | Unit test | `StartupResilienceTests.Startup_DatabaseFileCorrupt_HealthReasonNamesTheFault` |
| 2 | ❌ | A truncated file's health reason does the same | Unit test | `StartupResilienceTests.Startup_DatabaseFileTruncated_HealthReasonNamesTheFault` |
| 3 | ❌ | *(decision B)* Each of layers 1 to 4 has its own cause, followed by the remedies in the decided order and the Knowledgebase link | Unit test | `DatabaseUnsoundGuidanceTests.Reason_EachLayer_NamesItsOwnCause`, `…Reason_NamesTheResetThenABackupThenDeletingTheFile`, `…Reason_EndsWithTheKnowledgebaseLink` |
| 4 | ❌ | A layer 1 to 4 outcome maps to this reason ahead of every other startup case | Unit test | `StartupDegradationReasonsTests.For_UnsoundFile_ReturnsTheUnsoundReasonFirst` |
| 5 | ❌ | *(decision 3)* With the set-aside folder unusable, the startup outcome carries the obstacle, and the reason leaves out the Reset and names the obstacle | Unit test | `DatabaseInitializerTests.InitialiseAsync_UnsoundFile_SetAsideFolderCannotBeCreated_ReportsTheResetObstacle`; `DatabaseUnsoundGuidanceTests.Reason_WithAResetObstacle_LeavesTheResetOut`; `StartupResilienceTests.Startup_DatabaseFileCorrupt_SetAsideFolderCannotBeCreated_HealthReasonDoesNotOfferTheReset` |
| 6 | ❌ | *(decision 4)* The generic reason no longer claims a schema-version mismatch | Unit test | `StartupResilienceTests.Startup_UnrecognisedInitialisationFailure_HealthReasonDoesNotClaimASchemaMismatch` |
| 7 | ❌ | The existing refused-migration, overshoot and schema-integrity reasons are unchanged | Unit test | Existing #348, #350 and #305 `StartupResilienceTests` stay green |
| 8 | ❌ | The Reset the reason names still recovers an unsound file | Unit test | Existing `Startup_DatabaseFileCorrupt_ResetRecoversToHealthy` and `…_RepairActionRecoversToHealthy` stay green |
| 9 | ❌ | Live: a corrupt file degrades with this reason and recovers through the Reset | Live (T2) | `docs/automated-testing/startup-and-degradation/08` steps 3 and 5 pass on this build and fail at step 3 on `quotinator:canary-457` |
| 10 | ❌ | Live: a truncated file does the same | Live (T2) | `docs/automated-testing/startup-and-degradation/09` steps 3 and 5, same canary |
| 11 | ❌ | The full build and test suite are clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`: 0 warnings, 0 errors, all passed |
| 12 | ❌ | T1: the degraded page shows the new reason, and its Reset recovers | Live (T1) | Step 13, confirmed by the developer |
