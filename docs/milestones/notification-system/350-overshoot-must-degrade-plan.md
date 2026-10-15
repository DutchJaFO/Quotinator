# #350: A schema-version overshoot runs healthy instead of degrading, on a schema whose shape is unknown

**Status:** Waiting for release
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

### Degraded means untrusted, and nothing is read from an untrusted database

Developer direction, 2026-10-13:

> *"Note that due to an overshoot we cannot trust the database itself as a source. That is why a reset
> or a restore from backup are our only options."*

That is why the remedy set is exactly two, and why no third option (true up the counter, continue
anyway) is offered or built: neither makes the content trustworthy. It also settles what the degraded
surfaces may show, which is nothing that came out of that database. Four components hold a read gate,
and all four keep asking `IsHealthy`:

| Component | What its gate skips | Why the gate exists |
|---|---|---|
| `Notifications.razor.cs` | the whole notification list | a live query throws `SQLITE_CANTOPEN` past `NotificationReader`'s own catch (#326) |
| `NotificationSummary.razor.cs` | the startup-modal summary | the same read, from the modal (#326) |
| `ImportReview.razor.cs` | the staged-action list | the same read, from the review page (#326) |
| `DatabaseStatsSummary.razor.cs` | its file-history query only, never the counts | `Import_FileResource`/`Import_Batch` may not exist yet after a failed migration (#293) |

Each was written for a schema that may be *missing* what this build expects, and an overshoot is the
opposite case: the query would execute. **That difference is not a reason to read.** Whether a query
runs says nothing about whether its answer can be trusted, and this build cannot vouch for the shape of
what it would return. The `Scope changes` section above records the round where that was built the
other way and reversed.

**So the explanation an operator gets cannot be a row.** `StartupErrorModal` renders
`DatabaseHealthState.FailureReason`, which is in-memory state: it is there whatever the database turns
out to hold, and it is there even if the notification's own `INSERT` never landed.

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

**Restore has no route yet, and that is tracked rather than papered over.** The issue's own scope
boundary anticipated this: if naming a remedy nothing implements is the wrong order, restore gets pulled
forward rather than the remedy text trimmed to match. The developer chose the first on 2026-10-14, a
Reset button now and restore as its own issue, filed as
[#444](https://github.com/DutchJaFO/Quotinator/issues/444) in the Startup and degraded experience
milestone. Naming it here stays correct meanwhile: an operator holding a pre-overshoot backup can put it
back by hand today.

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

## Scope changes

### The degraded surfaces read nothing from an overshot database (developer, 2026-10-14)

**Reversed after T1.** The cross-check finding below settled that the four read gates should
distinguish *"cannot be read"* from *"readable, of unrecognised version"*, so the notification
explaining the overshoot would stay visible. Built that way and run live, the developer read the
result and rejected it:

> *"statistics was accessible: data is not to be trusted! import review: ditto. notifications: why did
> you expect this to be usable at all? data is not to be trusted. We cannot assume that the
> notification informing us about the overrun is available at all."*

**The distinction was the wrong one.** Whether a query *executes* says nothing about whether its answer
can be trusted, and an overshot schema's content is untrusted either way. The third sentence is the
decisive one: the notification is a row in a table this build cannot vouch for, so the design must not
depend on reading it back, and this build cannot even assume the `INSERT` landed.

**What changed back:** `DatabaseFaultKind`, `ExpectedSchemaIsPresent` and the `MarkFailed` kind
parameter are gone, and `Notifications`, `NotificationSummary`, `ImportReview` and
`DatabaseStatsSummary` gate on `IsHealthy` again, as they did before this issue. One flag is the whole
answer.

**What did not change:** the notification is still written, still off the health gate. The issue's own
requirement 3 asks for that, and the correction is about depending on it rather than producing it. What
explains the degraded state to an operator is `StartupErrorModal`'s reason, which is in-memory state
and cannot go missing with the schema.

### The counts in the degraded modal are #428's, and this path sharpens them

`DatabaseStatsSummary`'s counts are not gated by health at all, so the degraded modal introduces ten
real numbers with *"The database was left at its last known-good state:"* while the schema they came
from is one this build cannot vouch for.
[#428](https://github.com/DutchJaFO/Quotinator/issues/428) already owns withholding them, and its
expected behaviour covers this path verbatim: *"While the database is degraded, no count is shown on
either surface."* It sits in the **Startup and degraded experience** milestone rather than this one.

Raised rather than taken: this issue makes the case stronger, because the numbers here are populated
and plausible rather than the zeros #428 was filed about, which is the more convincing wrong answer.

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

### The page-level health gate was raised here, and the answer is in Scope changes

Raised because requirement 3 names only the `Program.cs` gate, while four components hold a read gate
one layer down. It was first decided *in scope*, built so those four would read an overshot database,
and then reversed by the developer after T1: see `Scope changes` above. What stands is that all four
keep gating on `IsHealthy`, and the operator's explanation comes from the reason text rather than from
a row.

Scope checked against the components themselves rather than the issue: four gates skip a read, not
three, and `DatabaseStatsSummary`'s gate covers only its file-history query. Its counts never query at
all, which is why they survive the gate and why #428 rather than this issue removes them.

### This plan names three tests the issue's table does not

Requirement 4 (both remedies) has no row of its own for the notification body, and the issue's table
names nothing for the health-state behaviour. Rows 3, 8 and 10 of the verification table carry them:
the health reason's remedies, an overshoot degrading like any other fault, and the body's remedies in
all three languages.

### Page rendering has no unit-test home, so the live document carries it

`NotificationTableTests` records that this project has no Blazor component-rendering infrastructure (no
bUnit), which is why its own assertions are over mapping methods rather than rendered components. So
*what the degraded pages show* is asserted in document 06, reading the live modal and the live
notifications page with screenshots. The unit tier carries what it can: that the pages answer `200`
rather than `500`, and that an overshoot degrades like any other fault.

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

**Status:** ↩️ Reverted, 2026-10-14. Built, then undone by the `Scope changes` decision above;
`DatabaseHealthState` carries one flag again, as it did before this issue.

What this step added and later removed: `DatabaseFaultKind`, a required `MarkFailed` kind parameter,
and `ExpectedSchemaIsPresent`. Kept from it, because the reversal did not touch them: the
`DatabaseHealthStateTests` method asserting that an overshoot degrades like any other fault, and the
reading below.

**Fourteen call sites, not the four in `Program.cs`.** The required parameter found ten more in test
code that the cross-check's read of `Program.cs` could not see: six in `DatabaseHealthStateTests`
(which already exists, so step 2 adds to it rather than creating it), three in
`NotificationActionExecutorTests`, one in `DatabaseHealthGateMiddlewareTests` and one in
`BackupTestHarness`. Worth keeping even though the parameter is gone: a signature change to this type
reaches four times as far as `Program.cs` suggests, which is what made reverting it cheap to get wrong.

### 2. Write every named test and confirm it red

**Status:** ✅ Done, 2026-10-14. Ten tests, six red on their own assertions, four controls green from
the start. Readings below.

**Red against the pre-issue build, each on an assertion rather than the build:**

| Test | Reading |
|---|---|
| `..._DegradesRatherThanServing` | `expected: ServiceUnavailable, actual: OK` |
| `..._HealthReasonNamesTheOvershoot` | `/health` answered `{"status":"healthy"}` |
| `..._HealthReasonNamesBothRemedies` | the same body, so neither remedy is named |
| `MarkFailed_SchemaAheadOfThisBuild_...` | red as the property test the reversal later replaced; the method that stands asserts the degradation, not a second flag |
| `Read_SchemaVersionOvershoot_NamesBothRemediesInEachLanguage` | the German body names no `Sicherung` |
| `SchemaVersionOvershoot_IsReportedAsAFaultRatherThanACompleteSchema` | the log line says *"the schema is treated as complete"* |

**Four controls, green from the start, each red against an implementation that over-reports:**

- `..._BlazorPagesRenderDegradedUiRatherThan500` and `..._OpenApiRemainsReachableForRecovery` assert
  what a healthy build already does. Their red state is reachable only once the application degrades,
  which is the regression they are here to catch.
- `MarkFailed_ExpectedSchemaMissing_MarksItAbsent` was the control for that property's other half.
  It went with the property when the reversal removed it.
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

### 5. Keep the four diagnostic surfaces from reading an untrusted database

**Status:** ✅ Done, 2026-10-14, after being built the other way round first.

This step originally moved `Notifications`, `NotificationSummary`, `ImportReview` and
`DatabaseStatsSummary` onto `ExpectedSchemaIsPresent` so they would read an overshot database. The
developer rejected that result at T1 (see `Scope changes`), and all four went back to `IsHealthy`.

What they gained instead is the second reason, written at each gate: the query is skipped not only
because the schema may be missing what the query names, but because its answer cannot be trusted when
the schema's shape is unknown. `DatabaseStatsSummary`'s comment also records that its counts are not
behind this gate at all and that #428 owns them.

Confirmed live on the final build: the degraded modal renders its reason and `notificationRows=0`, and
the notifications page reads `rows=0` with *No notifications yet.*

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

### 9. Give the degraded page a repair control of its own

**Status:** ✅ Done, 2026-10-14. Red first on `RefusedRepair_NamesTheObstacleThatStoppedIt`
(*expected value to not be null*), then green, then driven live.

**Found by the developer at T1, and created by step 5's reversal.** The only UI control for a Reset
while degraded was the overshoot notification's own action button, and once the notifications summary
stopped rendering, the modal was left with nothing but `Continue`: the reason named a route the
operator could not take without an API client.

> *"when the app is in a degraded state the user needs to have options to repair. that is why we offer
> the reset database and/or restore from backup options are for. the backup could be local or uploaded
> by the user"* (developer, 2026-10-14)

`DatabaseRepairActions` offers `Reset the database`, arming to `Confirm`/`Cancel` the same way the
Notifications page's action does, and runs it through `INotificationActionExecutor` so the sequence
(back up, rebuild, restore health, re-record the version, re-run the condition checks) stays in one
place rather than being written a second time. Everything it needs is in-memory state or the executor,
so it does not depend on the untrusted database in any way. On success the page reloads into the normal
UI; a refusal is reported in place, since a Reset that cannot take a backup changes nothing.

**It went on the modal first, and that was not enough.** Developer, 2026-10-14, after driving it:
*"The reset option was only available on the modal dialog."* The modal is dismissed once, with
`Continue`, which navigates to `/rest-api` and leaves the operator reading a reason that names a remedy
they cannot run. So the control is its own component, and `DegradedDatabaseBanner` puts it with the
reason on every degraded page that can carry an interactive control: `/rest-api`, `/notifications`,
`/import-review` and `/stats`. `/rest-api` and `/stats` gained `@rendermode InteractiveServer` for it,
the first because it is where `Continue` lands and the second because it is where database-derived
numbers are read while the database is untrusted.

`/about` carries none and stays static SSR: it holds no database content, and the nav reaches
`/rest-api` from anywhere.

**The repair ended in a forced page reload, and that cost four errors per success.** Developer,
2026-10-15: *"it is obvious you caused the exceptions so we fix those now."* Measured first, because
the mechanism matters: a plain `location.reload()` on any interactive page, with no reset involved at
all, takes a container from `thrown=0` to `thrown=4`, every one a `WebSocketException` at `Error`. The
circuit is torn down without a close handshake. So the reload was not writing its own exceptions, it
was triggering the ones any reload writes, and a repair that worked still put four error lines into
the log an operator reads to find real faults.

`DatabaseHealthState` now raises `Changed` when `IsHealthy` actually moves, and the surfaces that
render health state subscribe: `DegradedDatabaseBanner`, `NavMenu`, `Home` and `RestApi`, each
unsubscribing on dispose because the state is a singleton that outlives them. The repair re-renders
them instead of reloading the page. Measured after: `thrown=0` before the repair and `0` after, the
banner and button gone in place, and on Home the started-with-a-problem modal replaced by the ready
one.

**`NavMenu`'s badge lags by one page load, and is left that way on purpose.** It renders in the static
layout rather than in a page's interactive island, so its subscription cannot re-render it; every page
loaded after a repair reads the badge correctly. Giving the nav its own render mode would close the
gap, and `CLAUDE.md` records the language selector inside it as working deliberately without a Blazor
circuit, so that is a decision about a documented constraint rather than a line to add in passing.

`INotificationActionExecutor`'s own summary said it is invoked *"never from the read-only startup-modal
summary"*. That was true of the summary, and still is: the action belongs to the modal around it, not
to a notification row inside it.

**The labels are reused, not duplicated**: `NotificationsResetActionButton`,
`NotificationsConfirmActionButton`, `NotificationsCancelActionButton` and `NotificationsExecutingLabel`
already exist in all three locales, and a second key holding the same words is what the string
centralisation policy exists to prevent. The refusal text is English, like the reason above it.

**Restore is not built here.** The developer chose a Reset button now with restore as its own issue
(2026-10-14), filed as [#444](https://github.com/DutchJaFO/Quotinator/issues/444): `BackupEndpoints`
today lists, creates, downloads and deletes backups, and there is no restore endpoint and no upload
path, so restore stays a named remedy an operator performs by hand.

### 10. Full suite, T1 and T2

**Status:** ✅ Done, 2026-10-14. Full suite, T2 and T1 all green, re-run after the reversal and the repair control.

**T1, 2026-10-14:** a standard migration start in Visual Studio, on a database at data v3 and app v5.
The pre-migration backup was taken, 25 Data and 4 App migrations applied to v28 and v9, the banner
reported its counts, and the application reached ready with no error and no degraded reason, which is
the path this issue leaves untouched.

**Full suite: 4,555 tests across 11 projects, all passed, 0 warnings, 0 errors**, under
`--configuration Release --verbosity normal -m:1`, run three times: once on the original build, once
after the reversal, and once after the repair control.

**`startup-and-degradation/06` was confirmed red against the pre-issue build first.** A worktree at
`a3a8c787` (this branch's parent) built as `quotinator:pre350`, and the rewritten document's step 3
read `Expected 503, got 200` on it, with the quote endpoint serving and the log carrying *the schema is
treated as complete*. Image, container and worktree torn down afterwards.

**Then green against `quotinator:local`, all seven steps**, re-run whole on the final build after
the reversal and the repair control, with the readings written into the document's own Observed effect.
The degraded modal names the overshoot and both remedies with `notificationRows=0`, the notifications
page reads `rows=0` with *No notifications yet.*, and the modal's own `Reset the database` takes
`/health` from `503` to `200` with `quotes` from `795` to `0`.

**`notifications-and-changelog/14` re-run from step 1.** Steps 1 and 2 (healthy rendering of five
kinds) green unchanged; steps 4 to 8, the ones this issue's change reaches, all green on the rewritten
waits: `/health` `503` after the overshoot, the fifth kind read from the page once the overshoot is cleared, since nothing is shown from it while it stands,
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
| 8 | ✅ | An overshoot degrades like any other fault, with no second notion of health | Unit test | `DatabaseHealthStateTests.MarkFailed_SchemaAheadOfThisBuild_DegradesLikeAnyOtherFault` |
| 9 | ✅ | No message claims the schema is complete or the application normal | Unit test | `DatabaseInitializerTests.SchemaVersionOvershoot_IsReportedAsAFaultRatherThanACompleteSchema`, over `CapturingLogger<DatabaseInitializer>.Messages` |
| 10 | ✅ | The notification body names both remedies in all three languages | Unit test | `NotificationTranslationSourceTests.Read_SchemaVersionOvershoot_NamesBothRemediesInEachLanguage` |
| 11 | ✅ | Both notification strings still exist in all three languages | Unit test | `TranslationCompletenessTests` |
| 12 | ✅ | Nothing read from the untrusted database is shown, and the reason explains the state instead | Live | `docs/automated-testing/startup-and-degradation/06-schema-version-ahead-of-the-application.md` step 5: modal reason names the overshoot and both remedies with `notificationRows=0`, notifications page `rows=0` |
| 13 | ✅ | A refused repair names the obstacle that stopped it | Unit test | `DatabaseRepairActionsTests.RefusedRepair_NamesTheObstacleThatStoppedIt` and `.SuccessfulRepair_SaysNothing` |
| 14 | ✅ | A repair tells the rendered page, so it costs no page reload and no log lines | Unit test + Live | `DatabaseHealthStateTests.MarkHealthy_AfterAFailure_RaisesChanged`, `.MarkFailed_RaisesChanged`, `.MarkFailed_Twice_RaisesChangedOnce`; `startup-and-degradation/06` step 7 reads `thrown` unchanged across the repair |
| 15 | ✅ | Every degraded page that can carry a control offers the repair, not just the modal | Live | `startup-and-degradation/06` step 7: `resetButtons=1` on `/rest-api`, `/notifications`, `/import-review` and `/stats`; Cancel leaves `quotes` at `795`, Confirm from either surface takes `/health` to `200` with `quotes` at `0` |
| 16 | ✅ | The live overshoot degrades, explains itself, and the remedy is reachable | Live | The same document, rewritten, confirmed red against a pre-issue build first |
| 17 | ✅ | T1: the application still starts | Live | Developer started `Quotinator.Api` in Visual Studio, 2026-10-14: a standard migration start, backup taken, Data v3 to v28 and App v5 to v9 applied, ready with no errors |
