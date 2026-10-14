# #350: A schema-version overshoot runs healthy instead of degrading, on a schema whose shape is unknown

**Status:** In progress
**GitHub issue:** #350
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute the steps in order; each step section carries its own status.** The contract, where the
overshoot degrades, where the notification is raised, which components may still query a degraded
database, and what every message says are all settled below.

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

### 1. Give `DatabaseHealthState` the fault kind, signatures only

**Status:** ✅ Done, 2026-10-14. Build clean at 0 warnings, 0 errors, with no behaviour added.

**Fourteen call sites, not the four in `Program.cs`.** The required parameter found ten more in test
code that the cross-check's read of `Program.cs` could not see: six in `DatabaseHealthStateTests`
(which already exists, so step 2 adds to it rather than creating it), three in
`NotificationActionExecutorTests`, one in `DatabaseHealthGateMiddlewareTests` and one in
`BackupTestHarness`. All ten mean `ExpectedSchemaMissing`: every one of them stands for a generic
degraded startup.

`DatabaseFaultKind`, the required `MarkFailed` parameter, `ExpectedSchemaIsPresent`, and all fourteen
existing call sites stating `ExpectedSchemaMissing`.

**Signatures only, so step 2's tests compile and fail for the right reason.** The members exist here;
their behaviour does not. `ExpectedSchemaIsPresent` returns `IsHealthy`, which is exactly what the four
components read today, so every fault including an overshoot still suppresses their reads and step 2's
property test goes red. Step 5 gives it its real answer. This step changes no behaviour at all, which is
what makes the four call-site edits safe to land ahead of the tests.

### 2. Write every named test and confirm it red

**Status:** ✅ Done, 2026-10-14. Ten tests, six red on their own assertions, four controls green from
the start. Readings below.

**Red against the pre-issue build, each on an assertion rather than the build:**

| Test | Reading |
|---|---|
| `..._DegradesRatherThanServing` | `expected: ServiceUnavailable, actual: OK` |
| `..._HealthReasonNamesTheOvershoot` | `/health` answered `{"status":"healthy"}` |
| `..._HealthReasonNamesBothRemedies` | the same body, so neither remedy is named |
| `MarkFailed_SchemaAheadOfThisBuild_LeavesTheExpectedSchemaPresent` | `ExpectedSchemaIsPresent` false, since step 1 derives it from `IsHealthy` |
| `Read_SchemaVersionOvershoot_NamesBothRemediesInEachLanguage` | the German body names no `Sicherung` |
| `SchemaVersionOvershoot_IsReportedAsAFaultRatherThanACompleteSchema` | the log line says *"the schema is treated as complete"* |

**Four controls, green from the start, each red against an implementation that over-reports:**

- `..._BlazorPagesRenderDegradedUiRatherThan500` and `..._OpenApiRemainsReachableForRecovery` assert
  what a healthy build already does. Their red state is reachable only once the application degrades,
  which is the regression they are here to catch.
- `MarkFailed_ExpectedSchemaMissing_MarksItAbsent` is the control for the property's other half, red
  against an implementation that answers true for every fault.
- `..._SurfacesTheNotificationWhileDegraded` passes today because the database is healthy on an
  overshoot, so nothing suppresses the write. Its red moment falls between steps 3 and 4, and step 3
  captures the reading.

**The issue's own Failing-tests table is wrong about three of these**, which it marks `❌`: the two
reachability tests and the notification one. Per the precedent #423 set and #371 followed, the issue
body gains a paragraph above that table saying so, at `Waiting for release` with the Definition-of-done
sync.

**German needed a bare particle, not the infinitive.** `zurücksetzen` and `setzen Sie ... zurück` are
one verb, and the shipped body uses the second spelling, so a keyword of the first would have gone red
on correct prose. The per-language words are each one that can only come from that remedy's own clause.

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

### 3. Degrade on an overshoot

**Status:** ✅ Done, 2026-10-14. Four of step 2's red tests green: `DegradesRatherThanServing`,
`HealthReasonNamesTheOvershoot`, `HealthReasonNamesBothRemedies`, and both reachability controls held.

`SchemaOvershootGuidance.Reason(dataVersion, appVersion)`, and the `MarkFailed(reason,
SchemaAheadOfThisBuild)` call in `Program.cs` next to #348's. The reason states the symptom, the cause
and both remedies, in that order, which is the shape #333's sweep expects to find.

**Checked after the refused-migration case, never before it.** `MarkFailed` is first-wins, and a
refusal leaves the schema *behind* this build, where a live query may throw. That is the stricter
fault, so it has to win when a database manages both at once (one side overshooting while the other
has pending work).

**The overshoot needed its own log message.** `LogStartupDatabaseInitFailed` takes a non-nullable
`Exception`, and there is no exception here: the condition was detected, and per ADR 022 a detected
condition is an outcome rather than something thrown. `LogStartupSchemaOvershoot` mirrors
`LogStartupMigrationRefused`, which exists for the same reason one issue earlier.

**`..._SurfacesTheNotificationWhileDegraded` is now red**, which is the reading step 2 deferred to
here: `expected: not empty, actual: empty`, because the `dbHealth.IsHealthy &&` gate suppresses the
write the moment the overshoot degrades. Step 4 makes it green.

**One wording correction, found by its own test.** The reason first read *"by restoring a backup"*,
and `Assert.Contains("restore", ...)` does not match `restoring`. The clause became the imperative
*"or restore a backup ... and restart"*, which is also parallel with the Reset clause it follows.

### 4. Move the notification off the health gate

**Status:** ✅ Done, 2026-10-14. `..._SurfacesTheNotificationWhileDegraded` green: the row is written
to the overshot database and read back through `INotificationReader`, so the write itself survives a
schema this build cannot vouch for.

Drop the `dbHealth.IsHealthy &&` clause. The comment above the block currently explains the overshoot as
a squash artefact, and has to say what the state actually is, including why the write is attempted
against a schema of unknown shape and why its failure is tolerated.

### 5. Let the four diagnostic surfaces read a queryable database

**Status:** ✅ Done, 2026-10-14. Every test in this issue's own set green, with
`DatabaseHealthGateMiddlewareTests` run alongside to confirm the request gate still answers from
`IsHealthy`: 38 passed, 0 failed.

`ExpectedSchemaIsPresent` stops deriving from `IsHealthy` and answers from the fault kind instead, which
is the one behavioural change of this step, and `Notifications`, `NotificationSummary`, `ImportReview`
and `DatabaseStatsSummary` gate on it. Each one's gate comment names the #326 or #293 reason it exists
for, so each comment gains the distinction rather than losing it: the query is skipped because the
schema may be missing what the query names, not because the application is degraded.

### 6. Rewrite the messages

**Status:** ✅ Done, 2026-10-14. `SchemaVersionOvershoot_IsReportedAsAFaultRatherThanACompleteSchema`,
`Read_SchemaVersionOvershoot_NamesBothRemediesInEachLanguage` and `TranslationCompletenessTests` all
green, with #289's own two overshoot tests still passing on their original assertions.

**Dutch said the opposite of what it meant.** The shipped body rendered the Reset as *"Zet de
database terug"*, which in Dutch reads as restoring it. With a restore now named alongside, the two
remedies would have been one sentence apart in the same words, so the Reset became *"Bouw de database
opnieuw op"* (rebuild) and the restore *"zet een back-up terug"*.

The log template, the notification body in all three locale files, the detection comment in
`DatabaseInitializer.cs`, and #289's test summary. Nothing left claiming the schema is complete or the
application normal; both remedies named wherever one is named today.

### 7. Rewrite every document this reversal invalidates

**Status:** ✅ Done, 2026-10-14 (written; both documents run in step 9).
`RepositoryStructureTests` green, so the index, the links and the solution file still agree.

**`startup-and-degradation/06`** asserted `/health` `200` and told the reader not to "fix" a `503` by
relaxing the expectation, advice that is exactly backwards once this lands, and carried a
superseded-contract banner (with a stray `>:` on its last line) that went with the rewrite. It now
asserts the same contract as its siblings, across seven steps: degrade, the reachable surface, the
notification read from the page with a `capture-page.csx` screenshot (following `backup/06` and
`notifications-and-changelog/15`), the log, and a Reset that actually clears it. Its `Determinism`
gained the reasoning this plan records, and two new entries: the second start waits for *listening*
rather than healthy, and the notification is read from the page because `/api/v1/notifications` is
gated while degraded by design.

**Step 2 now stops the container rather than destroying it**, and step 3 is a `reenter` rather than a
second `create`. `test-env.csx` records both reasons itself: `rm -f` is a SIGKILL that leaves the
`-wal` sidecars behind, so a row written from the host lands beside a journal the next startup still
replays, and a second startup over existing data is what `reenter` is for.

**`notifications-and-changelog/14` broke on this change and is fixed here, not later.** Found while
sweeping the suite for other documents describing the old behaviour: its step 4 triggers an overshoot
deliberately (the version bump is the only trigger for that notification kind) and then waited for
`/health` `200` and read `/api/v1/notifications`. Both now fail by design, the first by timing out.
Steps 4 and 5 wait for `503`, and step 4 reads its fifth kind from the page, which its own next
command already did. Nothing else in that document needed touching: steps 5 and 6 read pages, and
step 7's Reset restores health before the API is used again.

**The suite index** needed the row title and the *"Downgrade is deliberately not covered yet"*
decision, which reasoned from the old behaviour.

**`execute-sql.csx`'s own output is quoted verbatim, em dash included.** Both documents expect
`OK — 1 row(s) affected.`; the dash rule keeps a quote faithful to a source this issue does not
change, and a doc rewriting it to `OK,` would no longer match what a reader sees.

`docs/knowledgebase/degraded-startup-shows-every-count-as-zero.md` is checked against what step 9
actually observes. Its stated cause is a startup that never completes, and an overshoot completes
`InitialiseAsync` normally, so the counts in the banner should be real rather than zero. If that is
what the pass shows, the entry needs a line saying the zeros do not follow from the startup-problem
message alone.

### 8. Boyscout pass and solution file

**Status:** ✅ Done, 2026-10-14. Full rebuild at 0 warnings, 0 errors.

Six files joined `.editorconfig`'s scoped `IDE0008` section and seven its `IDE0090` section, and both
conversions ran to a stable count, with the `IDE0028` follow-on they exposed fixed in the same pass.
`[Subsystem - Phase]` holds on both new log messages (`[Server]` and `[Database - Init]`), and no
`Sql.*` class was touched, so the `nameof` rule has nothing to apply to. Nothing was added to
`Quotinator.slnx`: the two new source files live inside a project, and the rewritten T2 document kept
its path.

**An incremental build hides a newly scoped warning.** After the `var` pass reported zero `IDE0008`,
`dotnet build` showed 0 warnings twice over, and `--no-incremental` then surfaced 18: analyzers do not
re-run for a project MSBuild considers up to date. Every warning count in this step was taken from a
full rebuild.

**Forty of the 41 em dashes in the touched files became prose punctuation**, reviewed line by line
rather than substituted; `ImportReview.razor.cs` alone held 22. Two replacements were wrong on the
first pass, both mine rather than the rule's, and worth naming: writing a file back through PowerShell
added a BOM to a file that had none and stripped one from a file that had it, and a blanket pass turned
a box-drawing separator (`──`, U+2500, not a dash at all) into `==`. Both restored; only the en dash
inside `rows 14–16` on that line needed changing.

**Four dashes stay, each a stated exception.** Three are the same verbatim quote of
`execute-sql.csx`'s own output in the two T2 documents and in this plan, which the dash rule keeps
faithful to a source this issue does not change. The fourth is `DatabaseStatsSummary`'s `"—"` for an
unknown file origin: a rendered display placeholder rather than prose, and the project keeps the same
character in `LocalTimestamp.Absent` as its own constant for an absent value. Changing it is a UI
convention change across every surface that constant reaches, which is wider than this issue, so it is
raised rather than decided here.

### 9. Full suite, T1 and T2

**Status:** 🔄 In progress. T2 complete; T1 is the developer's own step and is the one row left.

**Full suite: 4,554 tests across 11 projects, all passed, 0 warnings, 0 errors**, under
`--configuration Release --verbosity normal -m:1`.

**`startup-and-degradation/06` was confirmed red against the pre-issue build first.** A worktree at
`a3a8c787` (this branch's parent) built as `quotinator:pre350`, and the rewritten document's step 3
read `Expected 503, got 200` on it, with the quote endpoint serving and the log carrying *the schema is
treated as complete*. Image, container and worktree torn down afterwards.

**Then green against `quotinator:local`, all seven steps**, with the readings written into the
document's own Observed effect. The notifications page renders in full while degraded, overshoot row
first, and `POST /admin/database/reset` recovers the application in the same process.

**`notifications-and-changelog/14` re-run from step 1.** Steps 1 and 2 (healthy rendering of five
kinds) green unchanged; steps 4 to 8, the ones this issue's change reaches, all green on the rewritten
waits: `/health` `503` after the overshoot, the fifth kind read from the page because the API is gated,
the popup rendering every kind on a degraded start, line breaks `pre-line` and honoured, and step 7's
Reset restoring health and producing the sixth kind. Step 3 (a detail dialog on a healthy app) was not
re-run: nothing in this issue reaches it.

**The nine smoke documents, all green**: `api-surface/01` and `02`, `database-lifecycle/03`,
`startup-and-degradation/03`, `notifications-and-changelog/01` and `07`,
`import-and-staged-actions/01`, `14` and `19`. `notifications-and-changelog/01` included its browser
steps: the four constructed rows read `Active`, `Expired`, `Dismissed` and `No longer applicable`, and
the action button's Cancel path left `quotes` at `795` where Confirm drove it to `0`.

**`startup-and-degradation/03` has two defects of its own, in its commands rather than in the
application.** Its step 1 fetches `/version` with `--expect 200` while its own Expected text says
`503`, so the command fails on correct behaviour; and it reads `/` through `http.csx`, which sends no
`Accept` header, then asserts HTML properties of the body. Measured: with no `Accept` the wait page is
`application/problem+json`, and with `Accept: text/html` it is the HTML page with `autoRefresh=True`
and no external assets, which is what the document means. The application is right on every claim the
document makes. Not fixed here: the document belongs to #419, and a breach in a file this issue does
not touch is surfaced rather than cleaned up in passing.

**Every exception in every log was accounted for.** `SocketException` on each container stop, which
`transport-connection-cancelled-during-a-request` covers, and four `WebSocketException`s from one
capture of mine that failed while writing its PNG, which document 14 already names as four per failed
capture. None from the application's own paths.

**The knowledgebase entry was corrected from what the pass observed**, as planned: an overshoot
completes `InitialiseAsync`, so the Statistics page and the startup banner both report the real `795`
rather than the zeros that entry describes.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | An overshoot does not serve quote content, and the process survives it | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_DegradesRatherThanServing`: a quote endpoint answers `503 unavailable` and `StartupPhaseState.IsComplete` is true |
| 2 | ✅ | `/health` reports unhealthy with a reason naming the overshoot | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_HealthReasonNamesTheOvershoot`: `503`, body contains `unhealthy` and names the recorded schema version |
| 3 | ✅ | The reason names a Reset and a restore, not only the Reset | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_HealthReasonNamesBothRemedies` |
| 4 | ✅ | Every Blazor route the gate exempts renders rather than `500` | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_BlazorPagesRenderDegradedUiRatherThan500`, one `DataRow` per exempt route |
| 5 | ✅ | The OpenAPI surface stays reachable | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_OpenApiRemainsReachableForRecovery`: `/openapi/v1.json` answers `200` |
| 6 | ✅ | The remedy the reason names is reachable while degraded | Unit test | Same test: an admin route reaches its handler rather than the gate's `unavailable` body, so `POST /api/v1/admin/database/reset` is callable |
| 7 | ✅ | The notification is raised although the database is unhealthy | Unit test | `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_SurfacesTheNotificationWhileDegraded`, read through `INotificationReader` from the factory's services |
| 8 | ✅ | An overshoot leaves a queryable database; every other fault does not | Unit test | `DatabaseHealthStateTests.SchemaAheadOfThisBuild_LeavesTheExpectedSchemaPresent` and `.ExpectedSchemaMissing_MarksItAbsent` |
| 9 | ✅ | No message claims the schema is complete or the application normal | Unit test | `DatabaseInitializerTests.SchemaVersionOvershoot_IsReportedAsAFaultRatherThanACompleteSchema`, over `CapturingLogger<DatabaseInitializer>.Messages` |
| 10 | ✅ | The notification body names both remedies in all three languages | Unit test | `NotificationTranslationSourceTests.Read_SchemaVersionOvershoot_NamesBothRemediesInEachLanguage` |
| 11 | ✅ | Both notification strings still exist in all three languages | Unit test | `TranslationCompletenessTests` |
| 12 | ✅ | The notifications page lists the overshoot notification while degraded | Live | `docs/automated-testing/startup-and-degradation/06-schema-version-ahead-of-the-application.md`, its notifications-page step, with the `capture-page.csx` screenshot it names |
| 13 | ✅ | The live overshoot degrades, explains itself, and the remedy is reachable | Live | The same document, rewritten, confirmed red against a pre-issue build first |
| 14 | ❌ | T1: the application still starts | Live | Developer starts `Quotinator.Api` in Visual Studio and confirms it starts without error |
