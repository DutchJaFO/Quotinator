# #327 — Smoke tests: prove startup problems degrade rather than crash

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
with `--read-only` — a technique #294 subsequently made survivable. The two sections shared a setup and
asserted opposite outcomes, so #293's own guard ("health must be 503, confirming the test actually
reached the failure state") could never hold.

This issue replaces it with a family of startup problems, each provoking a different failure at a
different stage, authored as documents in #339's `startup-and-degradation/` category.

**Two premises changed after this issue was filed**, both from #326 (2026-08-21): a pending migration
is not the deciding variable — WAL sidecar state is — and the contract is now verified in-process by
`StartupResilienceTests` for two sabotage techniques. What remains unverified is what no unit test can
emulate: a real container, a real volume, a real read-only mount.

---

## Scope change — #339 delivered requirement 1 and the first scenario (2026-08-27)

Found during this issue's own planning cross-check, and confirmed by reading the delivered document
rather than the issue that describes it. Two of #327's requirements were satisfied by #339 while it was
repairing the tests that could not fail:

- **Requirement 1** — the obsolete section and its `--read-only`-forces-a-failure technique are gone,
  and the note recording why the technique stopped working is in place as
  [`05`](../../automated-testing/startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md)'s
  *"What this replaced, and why the old version could never pass"* section.
- **Requirement 6, first scenario** — the unwritable data directory. `05` now provokes it with
  `--read-only-data`, which `scripts/testing/test-env.csx` resolves to a `:ro` mount on `/data` rather
  than `--read-only` on the root filesystem, exactly as the requirement asks. Its `Determinism` pins the
  sidecar state through `reenter`'s clean stop and records that both states were measured. Steps 1–3
  were executed 2026-08-27, reaching `503` with `SQLite Error 14` — the original incident's own code.

`05` says as much itself: *"This document now implements the first. The other two remain that issue's
to add, and its scope is worth revisiting rather than assumed."*

**What this leaves #327**, confirmed with the developer 2026-08-27:

1. Two new documents — a corrupt or truncated database file, and a schema version ahead of the running
   application.
2. Two clauses `05` does not yet assert, both #327's own requirements rather than #339's: the OpenAPI
   surface staying reachable (requirement 3), and the failure reason naming a remedy that can actually
   work (requirement 4). The reason text exists at `Program.cs:181` and `StartupResilienceTests`
   already asserts it in-process; the container scenario reads only `status`.
3. The three in-process cases from requirement 9.

Nothing was dropped — the two delivered requirements moved, they did not disappear, and rows 1 and 2 of
the verification checklist still hold them to their result.

---

## Steps

### 1. Record the scope change on the issue

**Status:** ✅ Posted 2026-08-27 — covers both scope changes, #339's delivery and the #348/#349 split

Per `process.md`'s *Scope changes and deferrals*: a comment on #327 stating what #339 delivered, so the
issue page reflects the actual scope before it is closed against it. The Scope change section above is
the plan-doc half of the same step.

### 2. Confirm the two remaining scenarios reach their own failure states

**Status:** ✅ Measured in-process 2026-08-27 — the container measurements remain outstanding

The defect this issue fixes is a test that asserted an outcome it could never reach. Do not repeat it:
establish each scenario's failure state by measurement first, then write the document around what was
observed.

**Corrupt database file.** A file whose contents are not a database reaches SQLite and is rejected:
`SQLite Error 26: 'file is not a database'`, propagating the same way #326's `SQLITE_CANTOPEN` does.
The app degrades — startup completes, `/health` is `503 unhealthy`. Distinct from #326's case, where
SQLite never reaches a file at all.

**Truncated database file.** Measured separately rather than assumed to behave like the one above,
which is why it gets its own document (developer decision, 2026-08-27). A real seeded database cut in
half (4,550,656 → 2,275,328 bytes) degrades identically at the API surface: `503 unhealthy` with the
same generic reason. **It is reachable in-process after all**, contrary to this step's first reading:
the obstacle was Windows holding the file through SQLite's connection pool, which
`SqliteConnection.ClearAllPools()` releases. Whether that earns an in-process test of its own, on top
of the container document, is step 5's question rather than a foregone conclusion — the truncation
point is a variable the container document has to pin either way.

**Schema version ahead of the application.** Reached by letting a run migrate a real database, then
recording one version beyond whatever that run reached — no literal, so no migration number enters the
test. `/health` stays `200 healthy` and the `SchemaVersionOvershoot` notification is present, exactly
as step 4 predicted.

### 3. The backup work moved to #348 and #349

**Status:** ✅ Split out and filed 2026-08-27 — #327 now depends on both

Measuring requirement 4 — not just which recovery route is *reachable* but whether it can *succeed* —
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
`CreateBackup` returns `string?` and collapses five distinct faults into two shapes — two silent skips
that let a destructive step proceed unprotected, and one `catch (Exception)` covering three different
faults with three different remedies.

**[#348](https://github.com/DutchJaFO/Quotinator/issues/348)** — every backup attempt reports which of
the five obstacles it hit; every call site can report it; Reset refuses rather than running unprotected;
an override lets the operator accept responsibility, logged and audited so nobody hunts for a backup
that was never made. It also replaces the storage arithmetic, which decides a hard yes/no from an
estimate SQLite's page-level copy does not guarantee, with a two-level quota: an operating quota
(default 90%) and an absolute ceiling, the reserve between them available only by explicit override.

**[#349](https://github.com/DutchJaFO/Quotinator/issues/349)** — `GET`/`DELETE /admin/backups` and
`GET /admin/backups/status`, tagged `Backup`, so the remedy for a full backup folder is an in-app action
carrying authorization and an audit trail rather than a manual file deletion. Restoring a backup is
recorded there as future work, deliberately out of scope.

**What stays here.** The corrupt-database and truncated-database documents are written against the
behaviour #348 delivers, which is why they wait on it. Nothing else in #327 depends on either issue —
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

No existing test puts a database into an overshoot state — `notifications-and-changelog/02` mentions
overshoot in prose only — so this is new coverage regardless.

### 5. Extend `StartupResilienceTests` in-process, red before green

**Status:** ✅ All three written and green; two of the three shown able to fail

For the sabotage techniques that are deterministic and cross-platform, additional to the container
scenarios and never a replacement — the environmental tier is what proves the behaviour on a real
mount.

Both techniques look reachable the same way #326's already are, by pointing the real
`SqliteConnectionFactory` at a sabotaged path rather than by throwing from a fake: garbage bytes at the
database path for the corrupt case, and a counter bumped between two runs over one data directory for
the overshoot case. Step 2's measurement confirms that before the tests are written; anything that
turns out not to be reachable in-process drops to T2-only and is recorded here.

**A test that passes the moment it is written has not been shown to work.** All three passed on their
first run, which is what #326 already having delivered the behaviour looks like — so each assertion was
put to a negative control rather than accepted:

- `Startup_SchemaVersionAheadOfApplication_...` — recording the version *level* with the database rather
  than one beyond it fails the test on its notification assertion, as it must.
- `Startup_DatabaseFileCorrupt_HealthReportsUnhealthy...` — the same factory over a non-corrupt data
  directory answers `200 healthy` (measured as step 3's control row), so the `503` assertion
  discriminates.
- `Startup_DatabaseFileCorrupt_EntersDegradedStateInsteadOfCrashing` — **not shown able to fail, and
  said so rather than implied.** Its assertion fails only if the process dies during startup, which
  nothing available can provoke on demand; it is a regression guard on the never-crash contract. What
  *is* established is that the sabotage lands, since its sibling's `503` and the logged `SQLite Error
  26` both come from the same run.

### 6. Close `05`'s two unasserted clauses

**Status:** ✅ Added and verified live 2026-08-27 — full T2 pass on `05`, all four steps

Requirement 3's OpenAPI clause and requirement 4's remedy clause, added to the existing document rather
than to a new one. Neither changes what `05` provokes, so its measured failure state stands.

Run against a freshly built `quotinator:local`, seeded from a real `1.8.2` container (799 quotes):
`503 unhealthy` with `sqliteError14=3`, `reasonNamesTheFault=True`, `reasonNamesAWorkableRemedy=True`,
and `/openapi/v1.json` at `200`.

**Step 4 was executed too, which #339 never did** — it had only established the assertions were
automatable. All three pages rendered as specified, no page matched the stack-trace pattern, and the
console carried six errors, every one a `503`. **No screenshot was captured**: the Browser pane is not
displayed in this session, so the page never composites frames. The document states its step-4
assertions as DOM reads and those all passed, so the document's own requirement is met — but this is
not visual confirmation and is not recorded as such.

### 7. Write the degradation contract into the corrupt-database document

**Status:** ⬜ Not started

Process stays alive; `/health` reports unhealthy rather than being unreachable; the Blazor pages render
degraded UI rather than 500; the OpenAPI surface stays reachable.

### 8. State the recovery route, and whether it can actually succeed

**Status:** ⬜ Not started

An unwritable data directory cannot be repaired by a Reset — a Reset writes too. #326 added a distinct
failure reason for exactly that, because the generic one misdirects the operator. A corrupt database is
the opposite case: a Reset both is reachable and would work, which is a different claim and must be
stated as one.

"Reachable" and "would succeed" are different claims. #326's own test asserts only that the admin
request reaches its handler rather than being answered by the health gate.

### 9. Make each scenario independent

**Status:** ⬜ Not started

Each creates, seeds, and destroys its own container, volume and port, and depends on no state another
one left behind. #339's `EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother`
enforces the port half mechanically.

### 10. Assert no migration number or schema version anywhere

**Status:** ⬜ Not started

The suite's existing rule. Counts move whenever any milestone adds a migration, and a hardcoded number
goes stale on its own and gets "fixed" by editing the number rather than by anyone checking what
happened.

This bites hardest on the overshoot document, whose whole subject is a version counter. It states the
relationship — recorded version exceeds known migrations — never the two numbers.

### 11. Fill in #339's template fields and register both documents in the index

**Status:** ⬜ Not started

Including Determinism — step 2's measurements are exactly what that field records — the environment
profile, and the per-step expected results. The index's category table and the guard tests that keep it
from drifting are part of this step, not a follow-up to it.

### 12. Propose the smoke-set designation

**Status:** ⬜ Partly done — `06` proposed as `Smoke: no` from its own run; the other two follow when they exist

Deferred to after the runs by developer decision (2026-08-27), rather than proposed up front: the
question is whether either failure is basic enough that its breaking would invalidate other results,
and the runs are what answer that. Every document carries a `Smoke:` value regardless — the field cannot
be left blank, since the template check and `SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` both
require it.

**`06` — proposed `no`, with the run behind it.** The smoke set answers *does this container
fundamentally work*, and a test belongs in it when its failure would invalidate most other results.
`06`'s run showed the opposite: the application is entirely healthy under an overshoot — `/health` at
`200`, quote endpoints serving normally — so nothing else in the suite depends on this test passing. It
also needs a hand-built state (a stopped container plus a SQL insert) that no other test's setup
produces, which is the shape of a targeted scenario rather than a baseline check. Matches `01`, `02`,
`04` and `05`, all `no`; only `03` (the wait page) is in the set for this category.

### 13. Run the new documents and the smoke set

**Status:** ⬜ Not started

Per the index's *When to run what*: at the end of an issue, the designated smoke set plus the tests
relevant to this issue. Relevant here means all of `startup-and-degradation/`, since `05` changes and
the two new documents sit beside it.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The obsolete section and its `--read-only`-forces-a-failure technique are gone, with a note recording why it stopped working | Live | Delivered by #339; re-confirmed here rather than assumed. `05`'s *"What this replaced"* section is present, and the technique appears nowhere as a way to force a failure. Re-read 2026-08-27 |
| 2 | ✅ | #294's test still uses `--read-only` and still asserts success | Live | `04` is unchanged by this issue and still asserts a healthy upgrade under a read-only root filesystem |
| 3 | ❌ | Each scenario is a separate document in `startup-and-degradation/` | Live | `05` (delivered by #339), plus the two this issue adds |
| 4 | ❌ | Every degrading scenario asserts process alive, `/health` unhealthy, pages 200, OpenAPI reachable | Live | **`05`: all four verified live 2026-08-27** — `503 unhealthy`, `/` + `/stats` + `/notifications` at 200, `/openapi/v1.json` at 200. Stays ❌ until the corrupt-database document exists and asserts the same four |
| 5 | ❌ | Each scenario states the reachable recovery route and whether it can succeed | Live | **`05` done**: its Observed effect states the route is reachable but a Reset cannot fix it, and step 2 asserts the reason names `Restore write access` — confirmed `True` live. Stays ❌ until the other documents do the same |
| 6 | ❌ | Scenarios are independent — own container, volume, port, seed and teardown | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` covers the port half; the rest by reading each document's `create`/`destroy` pair |
| 7 | ❌ | Corrupt/truncated database scenario reaches its failure state | Live | Confirmed degraded by a step that checks it, not inferred from the recipe |
| 8 | ✅ | Overshoot scenario asserts healthy plus the overshoot notification, not the degraded contract | Live | [`06`](../../automated-testing/startup-and-degradation/06-schema-version-ahead-of-the-application.md) — run end to end 2026-08-27: `/health` `200`, `found=True`, `type=actionrequired`, `bodyNamesTheRemedy=True`, and the detection line in the log |
| 9 | ❌ | No scenario asserts a migration number or schema version | Live | `06` holds: it inserts `MAX(Version) + 1` rather than a literal, and the versions in its Observed effect are marked as observed output, not assertions. Stays ❌ until the other two documents exist |
| 10 | ✅ | In-process cases extend `StartupResilienceTests`, and each is shown able to fail | Unit test | `Startup_DatabaseFileCorrupt_EntersDegradedStateInsteadOfCrashing`, `Startup_DatabaseFileCorrupt_HealthReportsUnhealthyRatherThanBeingUnreachable`, `Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot` — 12/12 green. Two shown able to fail by negative control; the first is a regression guard nothing can provoke on demand, recorded as such in step 5 rather than implied |
| 11 | ❌ | Each new document carries #339's template fields including Determinism and its smoke designation | Unit test | `EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile`, `EveryAutomatedTestingStep_CarriesItsOwnExpectedResult`, `EveryAutomatedTestingCodeBlock_IsPowerShell`, plus a field check against the template |
| 12 | ❌ | Both documents are linked from the index and every link resolves | Unit test | `EveryAutomatedTestingDocument_IsLinkedFromTheIndex`, `EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument`, `EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument` |
| 13 | ❌ | The smoke designation is proposed from what the runs showed, and approved | Live | Proposal recorded in step 11 with the run that justifies it; developer decision recorded here |
| 14 | ✅ | The scope change is recorded on the GitHub issue, so the spec reflects what #327 actually delivered | Live | Comment posted 2026-08-27 covering both scope changes — what #339 delivered, and the split into #348/#349 |
