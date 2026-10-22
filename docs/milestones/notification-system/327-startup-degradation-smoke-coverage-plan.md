# #327: Smoke tests that prove startup problems degrade rather than crash

**Status:** In progress
**GitHub issue:** #327
**Tiers required:** T1, T2
**Depends on:** #326, #339, #348

---

## Description

The application must never crash; the worst acceptable outcome of a startup problem is a degraded UX
plus an OpenAPI surface that still allows recovery. That is a feature, and the environmental tier does
not verify it.

What existed instead was a section reproducing one historical incident (#293), which forced its failure
with `--read-only`, a technique #294 subsequently made survivable. The two sections shared a setup and
asserted opposite outcomes, so #293's own guard ("health must be 503, confirming the test actually
reached the failure state") could never hold.

This issue replaces it with a family of startup problems, each provoking a different failure at a
different stage, authored as documents in #339's `startup-and-degradation/` category.

**Two premises changed after this issue was filed**, both from #326 (2026-08-21): a pending migration
is not the deciding variable; WAL sidecar state is, and the contract is now verified in-process by
`StartupResilienceTests` for two sabotage techniques. What remains unverified is what no unit test can
emulate: a real container, a real volume, a real read-only mount.

---

## Scope change: #339 delivered requirement 1 and the first scenario (2026-08-27)

Found during this issue's own planning cross-check, and confirmed by reading the delivered document
rather than the issue that describes it. Two of #327's requirements were satisfied by #339 while it was
repairing the tests that could not fail:

- **Requirement 1**: the obsolete section and its `--read-only`-forces-a-failure technique are gone,
  and the note recording why the technique stopped working is in place as
  [`05`](../../automated-testing/startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md)'s
  *"What this replaced, and why the old version could never pass"* section.
- **Requirement 6, first scenario**: the unwritable data directory. `05` now provokes it with
  `--read-only-data`, which `scripts/testing/test-env.csx` resolves to a `:ro` mount on `/data` rather
  than `--read-only` on the root filesystem, exactly as the requirement asks. Its `Determinism` pins the
  sidecar state through `reenter`'s clean stop and records that both states were measured. Steps 1 to 3
  were executed 2026-08-27, reaching `503` with `SQLite Error 14`, the original incident's own code.

`05` says as much itself: *"This document now implements the first. The other two remain that issue's
to add, and its scope is worth revisiting rather than assumed."*

**What this leaves #327**, confirmed with the developer 2026-08-27:

1. Two new documents, a corrupt or truncated database file, and a schema version ahead of the running
   application.
2. Two clauses `05` does not yet assert, both #327's own requirements rather than #339's: the OpenAPI
   surface staying reachable (requirement 3), and the failure reason naming a remedy that can actually
   work (requirement 4). The reason text exists at `Program.cs:181` and `StartupResilienceTests`
   already asserts it in-process; the container scenario reads only `status`.
3. The three in-process cases from requirement 9.

Nothing was dropped (the two delivered requirements moved, they did not disappear), and rows 1 and 2 of
the verification checklist still hold them to their result.

---

## Steps

### 1. Record the scope change on the issue

**Status:** ✅ Posted 2026-08-27, covers both scope changes, #339's delivery and the #348/#349 split

Per `process.md`'s *Scope changes and deferrals*: a comment on #327 stating what #339 delivered, so the
issue page reflects the actual scope before it is closed against it. The Scope change section above is
the plan-doc half of the same step.

### 2. Confirm the two remaining scenarios reach their own failure states

**Status:** ✅ Measured in-process 2026-08-27, the container measurements remain outstanding

The defect this issue fixes is a test that asserted an outcome it could never reach. Do not repeat it:
establish each scenario's failure state by measurement first, then write the document around what was
observed.

**Corrupt database file.** A file whose contents are not a database reaches SQLite and is rejected:
`SQLite Error 26: 'file is not a database'`, propagating the same way #326's `SQLITE_CANTOPEN` does.
The app degrades, startup completes, `/health` is `503 unhealthy`. Distinct from #326's case, where
SQLite never reaches a file at all.

**Truncated database file.** Measured separately rather than assumed to behave like the one above,
which is why it gets its own document (developer decision, 2026-08-27). A real seeded database cut in
half (4,550,656 → 2,275,328 bytes) degrades identically at the API surface: `503 unhealthy` with the
same generic reason. **It is reachable in-process after all**, contrary to this step's first reading:
the obstacle was Windows holding the file through SQLite's connection pool, which
`SqliteConnection.ClearAllPools()` releases. Whether that earns an in-process test of its own, on top
of the container document, is step 5's question rather than a foregone conclusion, the truncation
point is a variable the container document has to pin either way.

**Schema version ahead of the application.** Reached by letting a run migrate a real database, then
recording one version beyond whatever that run reached, no literal, so no migration number enters the
test. `/health` stays `200 healthy` and the `SchemaVersionOvershoot` notification is present, exactly
as step 4 predicted.

### 3. The backup work moved to #348 and #349

**Status:** ✅ Split out and filed 2026-08-27, #327 now depends on both

Measuring requirement 4, not just which recovery route is *reachable* but whether it can *succeed*,
turned up a defect larger than this issue's own subject, and the developer's direction turned it from a
patch into a feature. Both were split out (developer decision, 2026-08-27) while staying inside this
milestone.

**What the measurement found.** All three rows from one run, the healthy row as the negative control:

| Database | `/health` | `POST /admin/database/reset` |
|---|---|---|
| Corrupt (not a database) | `503 unhealthy` | **500**, unhandled |
| Truncated (real, cut in half) | `503 unhealthy` | **500**, unhandled |
| Healthy | `200 healthy` | `200`, full stats body |

The `503` reason tells the operator to run exactly the Reset that returns the `500`. Underneath it,
`CreateBackup` returns `string?` and collapses five distinct faults into two shapes, two silent skips
that let a destructive step proceed unprotected, and one `catch (Exception)` covering three different
faults with three different remedies.

**[#348](https://github.com/DutchJaFO/Quotinator/issues/348)**: every backup attempt reports which of
the five obstacles it hit; every call site can report it; Reset refuses rather than running unprotected;
an override lets the operator accept responsibility, logged and audited so nobody hunts for a backup
that was never made. It also replaces the storage arithmetic, which decides a hard yes/no from an
estimate SQLite's page-level copy does not guarantee, with a two-level quota: an operating quota
(default 90%) and an absolute ceiling, the reserve between them available only by explicit override.

**[#349](https://github.com/DutchJaFO/Quotinator/issues/349)**: `GET`/`DELETE /admin/backups` and
`GET /admin/backups/status`, tagged `Backup`, so the remedy for a full backup folder is an in-app action
carrying authorization and an audit trail rather than a manual file deletion. Restoring a backup is
recorded there as future work, deliberately out of scope.

**What stays here.** The corrupt-database and truncated-database documents are written against the
behaviour #348 delivers, which is why they wait on it. Nothing else in #327 depends on either issue,
`05` is already closed out, the overshoot document is independent, and the three in-process tests are
green.

### 4. Establish the overshoot scenario's real contract

**Status:** ✅ Confirmed in-process, then verified live in a container by [`06`](../../automated-testing/startup-and-degradation/06-schema-version-ahead-of-the-application.md)

A database whose recorded version is ahead of the build is **not** a degradation case and must not
assert the degraded contract. `DatabaseInitializer` detects it deliberately and continues
(`DatabaseInitializer.cs:757`); `Program.cs:1064` gates the notification on
`dbHealth.IsHealthy && dbInitializer.SchemaVersionOvershootDetected`. Per #289 this is only reachable
after a migration squash, where the schema is complete and only the counter is stale.

Its contract is: process alive, `/health` **healthy**, overshoot notification present. Asserting 503
here would either fail or get "fixed" by changing correct behaviour.

No existing test puts a database into an overshoot state: `notifications-and-changelog/02` mentions
overshoot in prose only, so this is new coverage regardless.

### 5. Extend `StartupResilienceTests` in-process, red before green

**Status:** ✅ All three written and green; two of the three shown able to fail

For the sabotage techniques that are deterministic and cross-platform, additional to the container
scenarios and never a replacement: the environmental tier is what proves the behaviour on a real
mount.

Both techniques look reachable the same way #326's already are, by pointing the real
`SqliteConnectionFactory` at a sabotaged path rather than by throwing from a fake: garbage bytes at the
database path for the corrupt case, and a counter bumped between two runs over one data directory for
the overshoot case. Step 2's measurement confirms that before the tests are written; anything that
turns out not to be reachable in-process drops to T2-only and is recorded here.

**A test that passes the moment it is written has not been shown to work.** All three passed on their
first run, which is what #326 already having delivered the behaviour looks like, so each assertion was
put to a negative control rather than accepted:

- `Startup_SchemaVersionAheadOfApplication_...`, recording the version *level* with the database rather
  than one beyond it fails the test on its notification assertion, as it must.
- `Startup_DatabaseFileCorrupt_HealthReportsUnhealthy...`, the same factory over a non-corrupt data
  directory answers `200 healthy` (measured as step 3's control row), so the `503` assertion
  discriminates.
- `Startup_DatabaseFileCorrupt_EntersDegradedStateInsteadOfCrashing`, **not shown able to fail, and
  said so rather than implied.** Its assertion fails only if the process dies during startup, which
  nothing available can provoke on demand; it is a regression guard on the never-crash contract. What
  *is* established is that the sabotage lands, since its sibling's `503` and the logged `SQLite Error
  26` both come from the same run.

### 6. Close `05`'s two unasserted clauses

**Status:** ✅ Added and verified live 2026-08-27, full T2 pass on `05`, all four steps

Requirement 3's OpenAPI clause and requirement 4's remedy clause, added to the existing document rather
than to a new one. Neither changes what `05` provokes, so its measured failure state stands.

Run against a freshly built `quotinator:local`, seeded from a real `1.8.2` container (799 quotes):
`503 unhealthy` with `sqliteError14=3`, `reasonNamesTheFault=True`, `reasonNamesAWorkableRemedy=True`,
and `/openapi/v1.json` at `200`.

**Step 4 was executed too, which #339 never did**: it had only established the assertions were
automatable. All three pages rendered as specified, no page matched the stack-trace pattern, and the
console carried six errors, every one a `503`. **No screenshot was captured**: the Browser pane is not
displayed in this session, so the page never composites frames. The document states its step-4
assertions as DOM reads and those all passed, so the document's own requirement is met, but this is
not visual confirmation and is not recorded as such.

### 7. Scope cross-check: #348 delivered both scenarios, and the developer chose separate documents anyway

**Status:** ✅ Decided 2026-10-21

The second time this issue's own scope had to be re-read against what another issue shipped, and the
same method found it: read the delivered documents, not the issues that describe them. #348's
[`backup/01`](../../automated-testing/backup/01-refuses-a-reset-when-the-source-cannot-be-read.md) and
[`backup/02`](../../automated-testing/backup/02-refuses-a-reset-when-the-database-is-truncated.md)
already provoke exactly the two faults requirement 6 names, each with its own container, bind directory
and port, asserting no migration number, and both prove the named remedy resolves the fault. Both carry
`Traces to: #348, #327`.

What neither asserts is two clauses of requirement 3: the degraded pages and the OpenAPI surface.
Measured 2026-10-21 before proposing anything, since the clauses had to be measured wherever they
landed: in both states `/health` is `503`, `/`, `/stats` and `/notifications` are `200`,
`/openapi/v1.json` is `200`, and the reset refuses with `409 SourceUnreadable`.

**The developer chose separate documents in `startup-and-degradation/`, per requirement 2 as written**,
over adding the two clauses to `backup/01` and `backup/02` the way step 6 added them to `05`. The two
pairs therefore provoke the same state and assert different contracts: `backup/01` and `02` assert the
Reset path, `08` and `09` assert the startup contract, and each document's
*Explicitly not covered here* names the other so neither reads as the whole story.

### 8. Write `08` and `09`, and run both end to end

**Status:** ✅ Written and run 2026-10-21, all seven steps of each

[`08`](../../automated-testing/startup-and-degradation/08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md)
(a file that is not a database) and
[`09`](../../automated-testing/startup-and-degradation/09-a-truncated-database-degrades-instead-of-crashing.md)
(a real database cut in half), ports 18408 and 18409, each on its own bind directory.

Seven steps each: seed, sabotage with the sabotage confirmed, start and confirm it degraded rather than
died, the degraded pages and the spec, every database-reading endpoint gated, the recovery route
reachable and what it offers, and the remedy applied with a `200` to prove it works.

**`docker inspect -f '{{.State.Running}}'` is the never-crash assertion**, and it is in step 3 beside
the `503` rather than inferred from the `503` answering at all. Both read `true`.

**Running them found two things the writing did not.** `docker inspect` prints lowercase `true`, not
`True`, so both documents' step 3 said the wrong thing until the run corrected them. And a regex
holding a backslash cannot sit inside an interpolated PowerShell string: `"$($body -match 'at
[A-Za-z.]+\(')"` is a parse error on the escaped parenthesis in Windows PowerShell 5.1, not a failed
match. Both documents assign the pattern to its own variable, and both record the reason in
*Determinism*.

### 9. Assert the gating rule the developer added mid-issue

**Status:** ✅ Measured and asserted 2026-10-21

**"When health indicates a problem then we should not expect sane results from any other endpoint that
queries the database"** (developer direction, 2026-10-21). This arrived while `08` was being written
and it is now step 5 of each document: `/api/v1/quotes`, `/api/v1/quotes/random`,
`/api/v1/quotes/search`, `/api/v1/stats`, `/api/v1/notifications` and `/api/v1/masterdata/sources`,
all asserted `503`, all measured `503` in both states.

It is also an index rule rather than two documents' private habit, under
*A degraded application is not expected to answer from its database*: the pages and the recovery
surface are the deliberate exceptions, and a document asserts both halves.

**It immediately found a defect, which is why it is a rule worth having.** `GET /api/v1/version`
answers `200` with `"status":"ready"` and every count at `0` on a database that could not be opened,
measured on both documents' states. `03`'s step 1 records the rule it breaks, from #419: that endpoint
is gated *while starting* precisely because "a caller could otherwise reach for it as a readiness
endpoint in `/health`'s place". Measured the same day as the control, during startup it answers `503`
with the *"Quotinator is starting up"* problem-details body, carrying no `status` and no `database`
block. So the gate exists and works, and the degraded state is not behind it.

**Neither document asserts anything about `/version`, in either direction**, and both say so.
Asserting the `200` would write the gap into the suite as though it were the contract, which is this
issue's own founding defect; asserting a `503` would fail against the build they were measured on. The
finding is recorded in `08`'s Observed effect and in the index rule, and belongs to its own issue
rather than to this one: gating an endpoint is an application change, not test coverage. It carries no
row in the verification checklist below for that reason, since a row this issue cannot close is a row
that makes the plan hold its own approval gate.

### 10. Fix `03`, whose step 1 command asserted the opposite of its own expectation

**Status:** ✅ Fixed 2026-10-21

Found while checking whether any existing document contradicted the new gating rule, which is the
check #327 exists to make. `03`'s step 1 Expected says *"`/version` returns `503`. It is gated like
everything else (#419)"*, while its own command read `--expect 200` and then parsed `.status` off the
response. The step could not pass: the command and the expectation above it asserted opposite things.

The prose is the correct half, confirmed by measurement, so the command now reads `--expect 503
--status` and the `.status` parse is gone. The document records what it used to say and that #327 found
it, so the next reader sees a corrected contradiction rather than a silent edit.

**This is the same defect class as #293 and #294**, the pair whose contradiction created this issue: a
document asserting an outcome the build does not produce. It survived #339's template work because
nothing mechanical reads a step's command against its stated expectation, and nothing can.

### 11. Propose the smoke designation

**Status:** ⬜ Proposed, awaiting the developer's decision

**Both `08` and `09`: proposed `no`.** The smoke set answers *does this container fundamentally work*,
and a test belongs in it when its failure would invalidate most other results. Neither qualifies: each
needs a hand-sabotaged database file that no other test's setup produces, and a healthy container is
unaffected by whether either passes. Matches `01`, `02`, `04`, `05` and `06`, all `no`; only `03`, the
wait page, is in the set for this category.

Both documents already carry `Smoke: no`, since the field cannot be left blank and
`SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` holds the index's table to it.

### 12. Run the category and the smoke set

**Status:** ✅ All 17 documents run 2026-10-21, against `quotinator:local` at `80324161`

Per the index's *When to run what*: the designated smoke set plus everything relevant to this issue,
which is all of `startup-and-degradation/` since `03` changed and `08` and `09` are new. Seventeen
documents, every step of each, with one exception recorded below.

**Thirteen passed outright**: `startup-and-degradation/` 01, 04, 05, 07, 08, 09, and the smoke
documents `api-surface/01`, `api-surface/02`, `database-lifecycle/03`,
`import-and-staged-actions/01`, `import-and-staged-actions/19`, `notifications-and-changelog/01`,
`notifications-and-changelog/07`.

**Four needed repair, and not one of them was an application defect.** Three were documents asserting
what the build does not do, which is this issue's own subject arriving from the other direction, and
one was the blast radius of #428's fix landing earlier the same day.

**`02`, steps 4 and 5.** Step 4 expected a backup on the restart after a Reset and `backups=2`; the
whole container log holds one backup pair, the Reset's own, and the restart logged only `schema is up
to date`. Startup seeds a database it *created*, not one that merely holds no rows, so the empty
database a Reset leaves is not re-seeded, nothing is written and nothing is at risk. That is #156's
intent holding, so the document was corrected rather than the application. Step 5 then fell with it:
its witness was "a non-zero quote count, because the refused reset changed nothing", and the count is
already `0`, so it could not tell a refusal from a reset that ran. It now counts `reset complete`
lines, which only the rebuilding path writes.

**`03` and `14` were [#441](https://github.com/DutchJaFO/Quotinator/issues/441)'s already, and this
sweep re-found them instead of reading the issue.** Recorded as a process failure rather than smoothed
over, because the cost is real: four of the six items #441 filed on 2026-10-10 are the ones this run
reported as new findings on 2026-10-21, with the same measurements.

| Re-found here | #441, twelve days earlier |
|---|---|
| `03` step 1, `/version` expecting `200` | items 4 and 5: `503` is correct per #419 |
| `03` step 1, `autoRefresh=False` | with `Accept: text/html`: `503`, `text/html; charset=utf-8`, `autoRefresh=True`, `externalAssets=0` |
| `03` step 2's two blank accessors | item 6: `application/problem+json` and `Retry-After: 2` |
| `14` step 5 | items 1 to 3, including that it can never fail |

The corrections stand, now credited to #441 in each document, and `http.csx` gained the `--accept`
flag #441's own correction column prescribes. **Two things about them are worth stating plainly.**
Rewriting two steps to PowerShell 5.1 forms treats the edition question as settled, and #441's first
item is the developer deciding it; if the suite targets 7, the fix is to install it rather than to
reword every step. And `14` step 5 is still blocked whatever the edition: its loop reads
`$manifest.sources` where the manifest's array is `files`, and `--convert` does not exist on
`src/Quotinator.Api`. This plan's first wording said that step was merely the developer's to run,
which read as repaired when it is not; the document now states both faults and leaves them to #441.

`project_powershell_edition_is_5_1` had already written down the rule that was broken here: report the
document defect, and do not fix it as part of an unrelated issue. The note was read and the issue was
not opened, which is the gap: checking for an existing issue before repairing a document is the step
that was skipped, and it was done correctly for the admin endpoints the same day, where no issue
existed and #453 was filed.

**`06` step 7 and `backup/07` step 4, both broken by #428's fix.** Each read a database value from
`/api/v1/version` while the container was degraded, which that endpoint now correctly refuses. Found by
reading every `api/v1/version` use in the suite once `06` threw; `backup/03`'s two reads are healthy on
both sides and unaffected. `06` now separates Cancel from Confirm on `/health`, the only channel that
answers while degraded, and `backup/07` reads the recorded version out of the database file with
`DbInspector`, which is the stronger instrument for a claim about what the file holds. Everything else
in both steps passed, `06`'s modal repair included: `resetButtons=1` on the four interactive pages and
`0` on `/about`, the three button sets exactly as specified, and after Confirm `/health` `200`,
`quotes` `0` and the thrown count unchanged, so no reload and none of #445's WebSocketExceptions.

**`14` is the one document no run can complete**, and the detail is in the table above: its step 5
iterates an array the manifest does not have and calls a `--convert` argument that does not exist, so
it is blocked for a developer as much as for an assistant, and #441 owns both faults. The `??` that
stopped the block even parsing is corrected; nothing else about the step is. Steps 1 to 4 pass, and
step 6 was run far enough to read `resolvedToExisting = 25`, with its classification assertions
**not** evaluated.

**Two documents will need rewording when #453 lands**, neither wrong today. `06` step 4 probes
`seed/preview` as a proxy for the admin surface being reachable, measured `200` with real data in the
overshoot state because the database is readable there; #453 gates it. `01` step 5 explains its `401`
by the gate exempting `/api/v1/admin/*` entirely, which stops being the reason.

**One correction to an earlier record:** `scripts/testing/capture-page.csx` drives headless Edge and
does produce real screenshots in an assistant's session. `05`'s note that none was possible was about
the Browser pane, not this script; `06` and `notifications-and-changelog/01` wrote thirteen between
them during this sweep.

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The obsolete section and its `--read-only`-forces-a-failure technique are gone, with a note recording why it stopped working | Live | Delivered by #339; re-confirmed here rather than assumed. `05`'s *"What this replaced"* section is present, and the technique appears nowhere as a way to force a failure. Re-read 2026-08-27 |
| 2 | ✅ | #294's test still uses `--read-only` and still asserts success | Live | `04` is unchanged by this issue and still asserts a healthy upgrade under a read-only root filesystem |
| 3 | ✅ | Each scenario is a separate document in `startup-and-degradation/` | Live | `05` (delivered by #339), `08` and `09`, the latter two written and run end to end 2026-10-21. The developer chose separate documents over adding the clauses to `backup/01` and `02`, recorded in step 7 |
| 4 | ✅ | Every degrading scenario asserts process alive, `/health` unhealthy, pages 200, OpenAPI reachable | Live | All three verified live. `05` 2026-08-27; `08` and `09` 2026-10-21, each reading `docker inspect` `true` beside the `503` so the never-crash half is asserted rather than inferred, then `/`, `/stats`, `/notifications` and `/openapi/v1.json` all `200` |
| 5 | ✅ | Each scenario states the reachable recovery route and whether it can succeed | Live | `05`: reachable, and a Reset cannot fix it. `08` and `09`: the reset reaches its handler and answers `409`, so reachable but not workable as offered, and step 7 of each proves the remedy it names by returning the container to `200` |
| 6 | ✅ | Scenarios are independent, own container, volume, port, seed and teardown | Unit test | `EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` green over 18408 and 18409; each document creates, seeds, sabotages and destroys its own container and bind directory, read in its Steps and Cleanup |
| 7 | ✅ | Corrupt/truncated database scenario reaches its failure state | Live | Confirmed by a step rather than inferred: step 2 of each reads the sabotaged file back (34 bytes; 4,689,920 to 2,344,960) and step 3 ends the run on `--expect 503`, so steps 4 to 6 cannot pass against a healthy container |
| 8 | ✅ | Overshoot scenario asserts healthy plus the overshoot notification, not the degraded contract | Live | [`06`](../../automated-testing/startup-and-degradation/06-schema-version-ahead-of-the-application.md), run end to end 2026-08-27: `/health` `200`, `found=True`, `type=actionrequired`, `bodyNamesTheRemedy=True`, and the detection line in the log |
| 9 | ✅ | No scenario asserts a migration number or schema version | Live | `06` inserts `MAX(Version) + 1` rather than a literal; `08` and `09` assert no version or migration count anywhere, their only numbers being the sabotaged file sizes, which are readings rather than assertions |
| 10 | ✅ | In-process cases extend `StartupResilienceTests`, and each is shown able to fail | Unit test | `Startup_DatabaseFileCorrupt_EntersDegradedStateInsteadOfCrashing`, `Startup_DatabaseFileCorrupt_HealthReportsUnhealthyRatherThanBeingUnreachable`, `Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot`, 12/12 green. Two shown able to fail by negative control; the first is a regression guard nothing can provoke on demand, recorded as such in step 5 rather than implied |
| 11 | ✅ | Each new document carries #339's template fields including Determinism and its smoke designation | Unit test | The eight template guards green over both, 52/52 in `RepositoryStructureTests` 2026-10-21, including the section set, their order, the environment profile, the per-step expected results, the PowerShell check, the determinism field and the measured Observed effect |
| 12 | ✅ | Both documents are linked from the index and every link resolves | Unit test | `EveryAutomatedTestingDocument_IsLinkedFromTheIndex`, `EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument` and `EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument` green; both added to the index category table and to `Quotinator.slnx`, which `DocsMarkdownFiles_OnDisk_AreAllInSlnx` caught as missing first |
| 13 | ❌ | The smoke designation is proposed from what the runs showed, and approved | Live | `06` approved `no`. `08` and `09` proposed `no` in step 11 with the reasoning from their runs, and the runs in step 12 bear it out: a healthy container is unaffected by either, and each needs a hand-sabotaged file no other setup produces. Awaiting the developer |
| 14 | ✅ | The scope change is recorded on the GitHub issue, so the spec reflects what #327 actually delivered | Live | Comment posted 2026-08-27 covering both scope changes, what #339 delivered, and the split into #348/#349 |
| 15 | ✅ | A degraded application answers no database read, and every degrading document asserts it | Live | Developer direction 2026-10-21, now an index rule under *A degraded application is not expected to answer from its database*. Step 5 of `08` and `09` asserts six endpoints at `503`, all six measured `503` in both states |
| 16 | ✅ | No existing document in this category contradicts the gating rule | Live | Checked across the suite. `03`'s step 1 did, asserting `--expect 200` where its own Expected said `503` per #419, so the step could not pass; corrected to `--expect 503 --status` 2026-10-21, with what it used to say recorded in the document |
| 17 | ✅ | The category and the smoke set are run, and every document that cannot pass is repaired rather than left | Live | Step 12: all 17 documents run 2026-10-21, 13 passing outright. `02` and, for #428, `06` and `backup/07` repaired here and re-run to green. `03` and `14` were #441 findings this run re-found rather than read: the corrections stand, credited to #441 in each document, and `14` step 5 stays blocked on two faults #441 owns. Build clean and 4,626 tests green |
