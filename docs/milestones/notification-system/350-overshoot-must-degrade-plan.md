# #350: A schema-version overshoot runs healthy instead of degrading, on a schema whose shape is unknown

**Status:** Planning
**GitHub issue:** #350
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute from step 1.** The contract, where the overshoot degrades, where the notification is raised,
which components may still query a degraded database, and what every message says are all settled below.

---

## Description

When a database's recorded schema version is ahead of the running build, the application reports itself
healthy and serves normally. That is a foot gun: an overshoot means this build does not know what the
missing migrations did, whether they added, altered or removed things it does not expect, so the schema
is of *unknown* shape, not a known-good one.

Confirmed live, 2026-10-13: `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot`
(#327's test, which asserts the behaviour this issue reverses) passes green against the current build.
`/health` answers `200` and the quote endpoints serve.

---

## Why this reverses a deliberate decision

Recorded here rather than argued with in code comments, so there is one account rather than two.

#289 introduced the behaviour on the premise that an overshoot is only ever a stale counter on a
complete schema, which is what a migration squash leaves behind. A squash is one way to reach an
overshoot, not the only way, and nothing distinguishes it from a database that ran migrations this build
has never seen. The counter-argument is not that the squash case is wrong: it is that the application
cannot *detect* which case it is in, and the safe reading of an unknown schema is not "assume complete".
Degrading costs an operator a Reset or a restore; serving from an unknown schema risks writing against a
shape the build does not understand.

Developer direction, 2026-08-27:

> *"An overshoot is a foot gun. We don't know if the missing migrations added or removed things we did
> not expect. As such we treat it as a failure that has to be fixed before we allow anything other than
> degraded UX."*

---

## Design

### The contract

The same contract as every other degradation in `startup-and-degradation/`: the process stays alive,
`/health` answers `503` with a reason naming the fault, the Blazor pages render degraded UI rather than
`500`, the OpenAPI surface and the admin routes stay reachable, and every other request is answered
`503` by `DatabaseHealthGateMiddleware`. The overshoot stops being this category's one exception.

No middleware change is needed for that surface. `DatabaseHealthGateMiddleware`'s exempt list already
carries `/api/v1/health`, `/api/v1/version`, `/api/v1/admin`, `/openapi`, `/scalar`, the Blazor page
routes and the Blazor circuit, which is exactly the set requirement 6 asks to be reachable.

### Where the overshoot marks the database unhealthy

In `Program.cs`, immediately after `await dbInitializer.InitialiseAsync()` returns, alongside the
refused-migration check #348 already degrades from, reading `dbInitializer.SchemaVersionOvershootDetected`
and calling `dbHealth.MarkFailed(...)`.

Not inside `DatabaseInitializer`: `DatabaseHealthState` is a `Quotinator.Api` type that
`Quotinator.Data` cannot see (ADR 004), and per ADR 022 a condition the code has already detected is
reported as an outcome for the caller to act on, never thrown. The flag is that outcome and the consumer
decides the response, so `SchemaVersionOvershootDetected` keeps its current meaning and its current
detection site.

### Degraded has two meanings, and only one of them stops a query

`DatabaseHealthState` carries a single `IsHealthy` bool today, and nine places read it for two different
questions. Three ask *is the application degraded* and must keep reading `IsHealthy`, since an overshoot
is degraded: `Home.razor` and `RestApi.razor` show the startup error layout from it, and `NavMenu.razor`
marks the degraded state. Four ask *may I run a live query*, and that is the question an overshoot
answers differently from every fault the flag was built for:

| Component | What its gate skips | Why the gate exists |
|---|---|---|
| `Notifications.razor.cs` | the whole notification list | a live query throws `SQLITE_CANTOPEN` past `NotificationReader`'s own catch (#326) |
| `NotificationSummary.razor.cs` | the startup-modal summary | the same read, from the modal (#326) |
| `ImportReview.razor.cs` | the staged-action list | the same read, from the review page (#326) |
| `DatabaseStatsSummary.razor.cs` | its file-history query only, never the counts | `Import_FileResource`/`Import_Batch` may not exist yet after a failed migration (#293) |

Every one of those reasons is a schema that may be *missing* what this build expects. An overshot
database has everything this build expects plus unknown extra, so the query runs. Leaving these four on
`IsHealthy` would suppress the notification explaining the degradation exactly as effectively as the
`Program.cs` gate requirement 3 removes, one layer down.

So `DatabaseHealthState` records which kind of fault it holds, and derives from it the one question
these four ask:

- `DatabaseFaultKind` (`src/Quotinator.Api/Enums/`, per ADR 016): `ExpectedSchemaMissing` for a fault
  where a query written against this build's schema cannot be expected to execute, and
  `SchemaAheadOfThisBuild` for the overshoot.
- `MarkFailed(string reason, DatabaseFaultKind kind)`: the kind is a required parameter, so a fault
  added later cannot inherit an answer by default. The four existing call sites all pass
  `ExpectedSchemaMissing`, which is what they mean today.
- `ExpectedSchemaIsPresent`, true while healthy and false only for `ExpectedSchemaMissing`. The four
  components above gate on this; nothing else changes.

A second `MarkFailed` overload, or a separate `MarkSchemaAheadOfThisBuild` method, was the smaller
change and loses on exactly that point: nothing would force the next fault's author to decide which of
the two classes it belongs to, which is the mistake this issue is fixing one layer up.

### What may be read from an untrusted database, and what may not

Developer direction, 2026-10-13:

> *"Note that due to an overshoot we cannot trust the database itself as a source. That is why a reset
> or a restore from backup are our only options."*

That is why the remedy set is exactly two, and why no third option (true up the counter, continue
anyway) is offered or built: neither would make the content trustworthy. It also sets what the four
components above are allowed to be: diagnostic surfaces, read so an operator can see what the
application thinks is wrong, never a claim that the content is authoritative. Quote content stays
gated by the middleware, which is the only thing a consumer would take as authoritative.

### How the notification survives the health gate

The gate at `Program.cs:1163` loses its `dbHealth.IsHealthy &&` clause and reads
`if (dbInitializer.SchemaVersionOvershootDetected)` alone. The block already has its own `try`/`catch`,
which is what makes this safe to attempt: the `INSERT` runs against a notifications table whose shape
this build cannot vouch for, so a failure is logged and startup continues rather than taking the
degraded UI down with it.

This is the opposite of #348's deliberate decision not to notify, and the difference is the direction of
the mismatch. A refused migration leaves the schema *behind* this build, so the write is known to target
an unmigrated table; an overshoot leaves it *ahead*, where the table may well be intact, and the
notification is the only record of the fault that outlives the restart.

The notification already carries the `DatabaseReset` trigger, and `NotificationActionExecutor` neither
refuses while unhealthy nor needs to change: it runs the Reset and calls `MarkHealthy()` afterwards. So
once the page shows the notification, its own button is a working route out of the degraded state.

### Two remedies, not one

An overshoot is resolvable by a database Reset **or** by restoring a backup taken before the version
moved ahead (developer direction, 2026-08-27). The second matters when the operator wants their data
back rather than a clean rebuild. Both the health reason and the notification body name both.

[#349](https://github.com/DutchJaFO/Quotinator/issues/349) records a restore endpoint as future work.
Naming restore here is still correct: an operator holding a pre-overshoot backup can restore it by hand
today, and per the issue's own scope boundary, if that ordering is wrong the answer is to pull restore
forward, not to trim the remedy text to match what happens to be implemented.

### Where each message lives

The health reason is operator-facing JSON on `/health` and text on the degraded page, English only, like
`DataDirectoryNotWritableReason` and `BackupObstacleGuidance.MigrationRefusedReason` before it. It
carries two version numbers, so it is a method rather than a `const`:
`SchemaOvershootGuidance.Reason(int dataVersion, int appVersion)` in `src/Quotinator.Api/Startup/`,
following the guidance-class precedent. `StartupErrorModal` renders whatever
`DatabaseHealthState.FailureReason` holds, so this text is also what an operator reads on the degraded
page.

The notification title and body stay in `UI.en-GB.json`, `UI.nl.json` and `UI.de.json` with their
existing `{0}`/`{1}` substitution, updated in all three files in the same commit.

---

## Cross-check against authoritative sources

Seven findings against the issue body and the code as it stands, 2026-10-13.

### The notification body no longer makes the claim the issue quotes

The issue's *Actual behaviour* quotes a body ending *"The schema itself is complete and the app is
working normally; running a database Reset ... will true up the version bookkeeping"*. #319 (commit
`42325d68`) already replaced it. The shipped English body reads:

> This database's recorded schema version (data v{0}, app v{1}) is ahead of what this build knows how to
> apply. Reset the database from the admin endpoints to rebuild it at this build's own schema version.

So requirement 5 is **already satisfied for the notification**, and what the notification still lacks is
requirement 4's second remedy. The unprovable claim survives in exactly two places, both in scope here:
`LogMessages.LogSchemaVersionOvershoot`'s template (*"the schema is treated as complete"*) and the
comment above the detection in `DatabaseInitializer.cs`, which states #289's premise as fact.

### The page-level health gate is in scope

**Decided (developer, 2026-10-13): in scope.** The gate is described under *Degraded has two meanings*
above. Raised because requirement 3 names only the `Program.cs` gate, while four components hold the
same suppression one layer down, so fixing only the named one would leave the notification written and
invisible. The alternative considered was a separate issue covering the four components; it lost because
#350's own requirement 3 is not met while the operator cannot see the notification.

Scope checked against the components themselves rather than the issue: four gates skip a read, not
three, and `DatabaseStatsSummary`'s gate covers only its file-history query, since its counts are an
in-memory snapshot that never queries.

### This plan names three tests the issue's table does not

Requirement 4 (both remedies) has no row of its own for the notification body, and the issue's table
predates the page-gate decision above. Rows 3, 8, 10 and 12 of the verification table carry them: the
health reason's remedies, the fault-kind property, the body's remedies in all three languages, and the
page actually listing the notification while degraded.

### Page rendering has no unit-test home, so the property is asserted instead

`NotificationTableTests` records that this project has no Blazor component-rendering infrastructure (no
bUnit), which is why its own assertions are over mapping methods rather than rendered components. So
*the page shows the notification* is asserted at the two points that can carry it: `DatabaseHealthState`
exposing `ExpectedSchemaIsPresent` for an overshoot (unit), and document 06 reading the live page with a
screenshot (T2). The other three components share that one property and have no separate assertion;
`ImportReview` in particular has nothing to show in this scenario, since an overshoot stages no actions.

### #289's own unit test keeps its assertions and loses its summary's premise

`DatabaseInitializerTests.InitialiseAsync_RecordedVersionExceedsKnownMigrations_TreatsAsUpToDateAndFlagsOvershoot`
stays, assertions untouched: the Data-level contract genuinely does not change, since the method still
returns without throwing and still reports the recorded higher version. Its XML summary says the state
is *"treated as already-complete, not an error"*, which is the reversed premise, and is corrected.

### No diagnostic code is allocated here

Checked against [#333](https://github.com/DutchJaFO/Quotinator/issues/333) requirement 8, which is open:
it owns allocating codes for the messages that shipped before it, naming #289's among them. So the texts
this issue rewrites gain no `QTN-` code and no knowledgebase entry; #333 adds both, to whatever the text
says by then.

### The suite index describes the old behaviour in a second place

`docs/automated-testing/README.md` carries the overshoot twice: the row linking document 06, and the
*"Downgrade is deliberately not covered yet"* decision, which reasons from *"already a handled degraded
state rather than a crash: `DatabaseInitializer` sets `SchemaVersionOvershootDetected`, logs it, and
surfaces it as a notification"*. Both are updated. The document keeps its filename: the number is
positional, and two repository tests hold the index's links to files that exist.

---

## Steps

### 1. Write every named test and confirm it red

**Status:** ⬜ Not started

The six `StartupResilienceTests` methods and the one `DatabaseInitializerTests` method the issue names,
plus the three from the cross-check above.

`Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot` is **removed, not edited**.
Its name states the old contract, and flipping an assertion in place leaves a test whose name lies about
what it checks. Its `RecordASchemaVersionAheadOfThisBuild` helper and `FactoryFor` stay: the new tests
build the same state the same way.

The overshoot is reached the way that helper already does it, by letting a real build migrate a fresh
directory and then recording one version past `QuotinatorMigrations.All.Count`. A bumped counter on an
empty directory sets the same flag from a database that never migrated at all, which is a different
state.

### 2. Give `DatabaseHealthState` the fault kind

**Status:** ⬜ Not started

`DatabaseFaultKind`, the required `MarkFailed` parameter, `ExpectedSchemaIsPresent`, and the four
existing call sites in `Program.cs` stating `ExpectedSchemaMissing`. No behaviour changes in this step:
every fault that exists today is `ExpectedSchemaMissing`, so the derived property is false in exactly
the cases `IsHealthy` was false before.

### 3. Degrade on an overshoot

**Status:** ⬜ Not started

`SchemaOvershootGuidance.Reason(dataVersion, appVersion)`, and the `MarkFailed(reason,
SchemaAheadOfThisBuild)` call in `Program.cs` next to #348's. The reason states the symptom, the cause
and both remedies, in that order, which is the shape #333's sweep expects to find.

### 4. Move the notification off the health gate

**Status:** ⬜ Not started

Drop the `dbHealth.IsHealthy &&` clause. The comment above the block currently explains the overshoot as
a squash artefact, and has to say what the state actually is, including why the write is attempted
against a schema of unknown shape and why its failure is tolerated.

### 5. Let the four diagnostic surfaces read a queryable database

**Status:** ⬜ Not started

`Notifications`, `NotificationSummary`, `ImportReview` and `DatabaseStatsSummary` gate on
`ExpectedSchemaIsPresent`. Each one's gate comment names the #326 or #293 reason it exists for, so each
comment gains the distinction rather than losing it: the query is skipped because the schema may be
missing what the query names, not because the application is degraded.

### 6. Rewrite the messages

**Status:** ⬜ Not started

The log template, the notification body in all three locale files, the detection comment in
`DatabaseInitializer.cs`, and #289's test summary. Nothing left claiming the schema is complete or the
application normal; both remedies named wherever one is named today.

### 7. Rewrite `startup-and-degradation/06` and both index entries

**Status:** ⬜ Not started

It currently asserts `/health` `200` and tells the reader not to "fix" a `503` by relaxing the
expectation, advice that is exactly backwards once this lands, and it carries a superseded-contract
banner (with a stray `>:` on its last line) that goes with the rewrite. Its `Determinism` section gains
the reasoning above: the unknown schema is the fault, the stale counter only its symptom. It gains a
notifications-page step with a `capture-page.csx` screenshot, following `backup/06` and
`notifications-and-changelog/15`. The suite index needs the row title and the downgrade passage.

`docs/knowledgebase/degraded-startup-shows-every-count-as-zero.md` is checked against what that pass
actually observes on an overshoot, and corrected only if the entry's claim no longer holds for it. Its
stated cause is a startup that never completes, which is not this fault, so the pass decides it rather
than this plan.

### 8. Boyscout pass and solution file

**Status:** ⬜ Not started

Per `checklist.md`: every touched file into `.editorconfig`'s scoped `IDE0008` and `IDE0090` sections
with both conversions run to a stable count, `[Subsystem - Phase]` on any log line in a touched file
that lacks one, and no em or en dash left in any file this issue touches. This plan doc is already in
`Quotinator.slnx`, and the rewritten T2 document keeps its path, so nothing new is added there.

### 9. Full suite, T1 and T2

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both at zero warnings. Then the T2 smoke set plus every document this issue touches, against a Docker
image, with document 06 confirmed red against a pre-issue build first.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | An overshoot does not serve quote content, and the process survives it | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_DegradesRatherThanServing`: a quote endpoint answers `503 unavailable` and `StartupPhaseState.IsComplete` is true |
| 2 | ❌ | `/health` reports unhealthy with a reason naming the overshoot | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_HealthReasonNamesTheOvershoot`: `503`, body contains `unhealthy` and names the recorded schema version |
| 3 | ❌ | The reason names a Reset and a restore, not only the Reset | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_HealthReasonNamesBothRemedies` |
| 4 | ❌ | Every Blazor route the gate exempts renders rather than `500` | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_BlazorPagesRenderDegradedUiRatherThan500`, one `DataRow` per exempt route |
| 5 | ❌ | The OpenAPI surface stays reachable | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_OpenApiRemainsReachableForRecovery`: `/openapi/v1.json` answers `200` |
| 6 | ❌ | The remedy the reason names is reachable while degraded | Unit test | Same test: an admin route reaches its handler rather than the gate's `unavailable` body, so `POST /api/v1/admin/database/reset` is callable |
| 7 | ❌ | The notification is raised although the database is unhealthy | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_SurfacesTheNotificationWhileDegraded`, read through `INotificationReader` from the factory's services |
| 8 | ❌ | An overshoot leaves a queryable database; every other fault does not | Unit test | `DatabaseHealthStateTests.SchemaAheadOfThisBuild_LeavesTheExpectedSchemaPresent` and `.ExpectedSchemaMissing_MarksItAbsent` |
| 9 | ❌ | No message claims the schema is complete or the application normal | Unit test | `DatabaseInitializerTests.SchemaVersionOvershoot_IsReportedAsAFaultRatherThanACompleteSchema`, over `CapturingLogger<DatabaseInitializer>.Messages` |
| 10 | ❌ | The notification body names both remedies in all three languages | Unit test | `NotificationTranslationSourceTests.Read_SchemaVersionOvershoot_NamesBothRemediesInEachLanguage` |
| 11 | ❌ | Both notification strings still exist in all three languages | Unit test | `TranslationCompletenessTests` |
| 12 | ❌ | The notifications page lists the overshoot notification while degraded | Live | `docs/automated-testing/startup-and-degradation/06-schema-version-ahead-of-the-application.md`, its notifications-page step, with the `capture-page.csx` screenshot it names |
| 13 | ❌ | The live overshoot degrades, explains itself, and the remedy is reachable | Live | The same document, rewritten, confirmed red against a pre-issue build first |
| 14 | ❌ | T1: the application still starts | Live | Developer starts `Quotinator.Api` in Visual Studio and confirms it starts without error |
