# #411 — Automated-test documents no longer run as written

**Status:** In progress (step 10)
**GitHub issue:** #411
**Tiers required:** T1, T2
**Depends on:** —

---

## Next action

Step 10: share one key ring across test containers.

---

## Description

Five automated-test documents no longer run as written, found in the T2 passes of #370 and #409:

| Document | What no longer works |
|---|---|
| *A file left awaiting review raises an alert, and resolving it retires the alert* | Step 6 expects an `obsolete` alert, which nothing produces since #372; step 8's "No longer applicable" half is unreachable for the same reason; step 7 destroys the container steps 8 and 9 still use |
| *An already-reported conflict does not stage a duplicate on every reseed* | Steps 4 and 5 print empty counts: `.Count` on a single object PowerShell 5.1 has unrolled |
| *Bulk-deciding a staged batch via file export and re-import, in both wire formats* | Step 2 re-imports the curated file and expects `202`; since #373 it stages nothing and answers `200` |
| *The changelog is served from its own on-disk database, not the JSON fallback* | Step 5 reads the log as soon as health answers, before the post-restart changelog import has logged |
| *Reset wipes the entire database and does not reseed* | The cleanup leaves the `-wal`/`-shm` files DbInspector creates beside `smoke156-before.db` |

Running them for this issue found four more ways a run reports something the test did not cause (the
issue body was widened on 2026-09-19 to include them):

| Where | What it reports |
|---|---|
| *A file left awaiting review raises an alert, and resolving it retires the alert* | Step 1 writes into a bind folder an earlier run may have left, whose database it then starts against |
| *An already-reported conflict does not stage a duplicate on every reseed* | Step 3 reseeds before the restarted application answers |
| Every browser-driven document | A `CryptographicException` / `AntiforgeryValidationException` pair it did not cause: each test container has its own key ring, and the browser pane keeps a cookie from an earlier one |
| Every document that stops its container | Exception counts read after a stop include the stop's own lines |

## Decisions

- **`Obsolete` is checked against a constructed row, in the notification document.** It has had no
  producer since #372 removed reseed truncation, and keeps its rendering only for databases that already
  hold such rows (`QuotinatorDatabaseInitializer`, the comment beside the removed producer). *Notifications
  list, dismiss, render, and drive their action* already constructs rows no producer makes, against the
  container it already stops for that purpose; a fourth row — dismissed, reason `Obsolete` — adds the
  check without adding a stop (#402). The pending-review alert document keeps its *Done* half.
- **The bulk-decide document owns its input**, as *Reviewing conflicted import actions throws nothing*
  does: bundled sources off, a base file and a conflicting file of its own, two quotes each. Two actions
  let the malformed-row step show one bad row leaving the other decided.
- **An export sent back must carry a decision for each ambiguous field.** Measured 2026-09-19: the
  unmodified export of a pending conflict carries none, and bulk-decide correctly refuses it as
  ambiguous. The round trip sets `keep` on the `quoteText` rows first — the camelCase deserialization
  the document exists for is still exercised, since the edited file goes through the same reader.
- **The changelog document waits for the import line by polling, bounded at 60 s** — never a fixed
  sleep, which `docs/testing-policy.md` rules out as an unproven delay.
- **Test containers share one key ring by default** (developer decision, 2026-09-19). `test-env.csx`
  mounts a host folder, `.claude/temp/qt-keys`, at `/data/keys`, created if missing and never removed by
  `destroy`: a cookie the browser pane got from any earlier test container then still decrypts. Measured
  2026-09-19 with probe containers: a new volume on the shared ring logged 0 lines on its first visit,
  where one with its own ring logged 2 to 7.
- **`--own-keys` opts out, and two options imply it.** `--read-only-data` keeps its keys where the
  read-only mount puts them — a writable shared ring would change what those documents test.
  `--tmpfs-data` keeps them on its tmpfs, because *A reset refuses when the disk fills during the backup,
  instead of wiping behind a 200* sizes that tmpfs to run out of space, and moving the keys off it changes the arithmetic.
- **A document that provokes the condition restores the browser before it ends.** Measured: a container
  with its own ring leaves the browser holding a cookie the shared ring cannot read, and the next
  shared-ring container logged 5 lines. Visiting a shared-ring container once replaces the cookie; that
  visit is the document's remedy proof, and the visit after it logs nothing. A separate host name does
  not isolate it: after visits to `antiforgery.localhost`, a shared-ring container visited at `localhost`
  logged 7 lines.
- **Between containers, the browser tab is closed rather than left on the old page.** A page left open
  reconnects to whatever answers on its port next. The pane cannot open `about:blank`, so the tab is
  closed and a new one opened.
- **T2 runs in two passes** (developer direction, 2026-09-19): a targeted pass proving the shared ring,
  then every other document whose environment it changed — which, since every container gains the
  mount, is every other document in the suite. Nothing touched goes untested.

---

## Steps

### 1. Run each document as written, and record where it fails

**Status:** ✅ Done — 2026-09-19, against an image built from `dec09367` (no code has changed since).
Each fails where the Description says:

| Document | As written |
|---|---|
| Pending-review alert | Step 6: `fd009b2f isDismissed=False`, `661fbc9f isDismissed=True reason=resolved`, `active alerts = 1` — no obsolete alert; after step 7, step 8's `http://localhost:19520/notifications` is unreachable |
| Already-reported conflict | Steps 4–5: `after cold start: ` and ` ->  -> ` |
| Bulk decide | Step 2: `< 200 OK`, `Expected 202, got 200.` |
| Changelog database | Step 5, three runs: one `[Changelog - Import]` line at health each time — the post-restart import had not yet logged |
| Reset | After the cleanup: `smoke156-before.db-shm`, `smoke156-before.db-wal` remain |

The reset document's cleanup line is refused by this environment's shell guard as written (a multi-path
`Remove-Item`); it was run as one `-LiteralPath` removal per path it names, which removes the same files.

Against a build of this branch, the failing steps of each of the five documents, exactly as written. Each
must fail where the Description says; a document that passes as written is not in scope.

### 2. Fix the pending-review alert document

**Status:** ✅ Done.

The fixture stages **two** conflicts (`--count 2`): the document's decision step settles one from the
notification and one from the review page, and with a single conflict only one surface could be driven —
exactly what #370's and #409's passes had to settle for. Measured before writing the expectations, against
the same image as step 1: after the discard and two reseeds, three alerts — the discarded file's original
`resolved` and a new active one for it, and the other file's original still active — with
`active alerts = 2` and `pending actions = 2`; the second reseed added nothing.

Step 6 asserts that. The resolved alert reads *Done* (new step 7). The decision step (new step 8) captures
both pending quotes and expects both batches `Applied` and `active alerts = 0`, and names the button
**Decide**, not the old **Run**. The read-only-container step moves last (new step 9), and the cleanup
removes the bind folder. The Preconditions and Determinism sections no longer promise an obsolete alert.

### 3. Add the obsolete check to the notification document

**Status:** ✅ Done — the insert names `DismissReason`, `NULL` for the three existing rows; the
Preconditions and the CHECK-constraint note name the fourth row and its column.

Step 7 inserts a fourth row — `IsDismissed = 1`, `DismissReason = 'Obsolete'` — and its count becomes
`4`. Step 8, under **All**, asserts that row's status reads *No longer applicable*.

### 4. Fix the already-reported conflict document

**Status:** ✅ Done.

`Unresolved` wraps its filtered result in `@(...)` before `.Count`.

### 5. Rewrite the bulk-decide document

**Status:** ✅ Done — run as written against the step 1 image; every step passes. Two corrections to
the design below, both measured:

- **Each batch conflicts with different quotes** — a base file of six, and three conflicting files of
  two. With the same two quotes in every batch, the second and third staged nothing: a conflict already
  awaiting review is recognised as already reported.
- **The malformed-row step reports two errors, not one**, both for the corrupted action: the rejected
  value, and `quoteText` left ambiguous because its only decision was the rejected one.

The container's log holds one `[Runtime - Exception]` line: a `FormatException` thrown by
`ImportActionFieldRowMapper.FromCsvRow` for the rejected value, which bulk-decide catches and reports as
that row's error. Under [ADR 022](../../architecture-decisions/022-exceptions-only-for-undetectable-conditions.md)
a value the code has checked is returned as an outcome, not thrown. That is code, not a document, and is
already tracked as #405, *Bulk decide reports a bad row instead of throwing for it*.

A container with `Quotinator__IncludeDefaultSources=false`; a base file and a conflicting file of two
quotes each, the second imported under `review` for each of the three batches. The JSON and CSV round
trips set `keep` on each `quoteText` row, then expect `actionsDecided=2` and `errors=0`. The
malformed-row step sets `keep` on one action's `quoteText` row and `not-a-choice` on the other's, then
expects one error naming the rejected value, one action `Decided` and one still `Pending`. The CSV
edits use `corrupt-csv-cell.csx --row`, the row found by its `Field` value.

### 6. Fix the changelog document's step 5

**Status:** ✅ Done — run as written against the step 1 image; every step passes. At health the log held
one `refreshed 126 entries` line; the second appeared after about a second of polling, also 126.

Read before the restart — after start and after the page request — the log holds no
`[Runtime - Exception]` line.

After the restart, poll the log until a second `[Changelog - Import]` line appears, for at most 60 s,
then assert it reports the same entry count as the first.

### 7. Fix the reset document's cleanup

**Status:** ✅ Done — run as written against the step 1 image; every step passes. Before the cleanup,
both copies and both pairs of sidecars were listed; after it, nothing.

The cleanup removes what `Get-ChildItem .claude/temp -Filter 'smoke156*'` lists, then lists again. A
loop over composed paths was tried first and refused by this environment's shell guard, as the original
multi-path line was in step 1.

Read before each of its three stops, the log holds no `[Runtime - Exception]` line.

The cleanup removes the `-wal`/`-shm` files beside both database copies.

### 8. T2 pass

**Status:** ✅ Done — against an image built from `7d3f164a`. Every step of every document passes as
written:

| Document | Result |
|---|---|
| Pending-review alert | 2 pending, 2 active; after the discard and two reseeds three alerts, 2 active, 2 pending; the resolved one reads *Done*; both surfaces left their batch `Applied` with `active alerts = 0`; read-only: `/notifications` and `/import-review` both `500`, `/about` and `/stats` `200` |
| Notification | Constructed rows read `Active`, `Expired`, `Dismissed`, `No longer applicable`; Cancel left 795 quotes, Confirm left 0 quotes and 0 notifications |
| Already-reported conflict | `1 -> 1 -> 1`; `AlreadyReported=2 Unchanged=2 Modify=1 Add=1` |
| Changelog | Second import line after about a second of polling, both 126 entries |
| Bulk decide, reset | As steps 5 and 7 recorded — run against the step 1 image, which differs from this one only in the bundled changelog JSON neither reads |

**Two further defects, found by this pass and fixed in the documents:**

- The pending-review alert document's step 1 wrote into a bind folder without clearing it; one left by
  an earlier run still held that run's database, so step 6 counted four alerts. Step 1 now removes the
  folder first.
- The already-reported conflict document's step 3 reseeded straight after `docker restart`, which
  failed with the connection closed. Step 2 now waits for health.

**Each container's log is read before every stop and restart**, per the index's *Read the log before
the application stops*. The pending-review alert, notification, already-reported conflict and reset
documents were first run with the log read only at the end, which counted the stops' own exceptions
with the tests'; all four were run again reading before each stop, with the results above unchanged.
What the tests themselves produced, none of which affects the app's function and each already recorded:

| Document | `[Runtime - Exception]` lines before its stops |
|---|---|
| Bulk decide | 1 — `FormatException` for the rejected value, #405 |
| Pending-review alert, notification | 2 each — a *key not found in the key ring* / antiforgery pair on the browser's first page: it still held a cookie from an earlier container on the same port (Knowledgebase: *The log reports that an antiforgery token could not be decrypted*) |
| Pending-review alert, read-only container (step 9) | 48 — `IOException`, `SqliteException`, `CryptographicException` from the read-only data directory the step creates; the known defect its step 9 describes |
| Already-reported conflict, changelog, reset | 0 |

For comparison, the notification document's one stop logged 7 of its own: two `SocketException (125)`
and five `OperationCanceledException` with the browser connected.

**This pass ran only the changed documents, not the index's full end-of-issue scope**: six of the nine
smoke tests were not run. Step 9 runs them, before any further change.

Each changed document, in full, against a build of the branch; each container's log read for
`[Runtime - Exception]` lines before every stop, restart and removal.

### 9. Run the smoke documents step 8 skipped

**Status:** ✅ Done — against the step 8 image. All six pass, each log read before its container
stopped, and each reporting no `[Runtime - Exception]` line:

| Document | Result |
|---|---|
| Baseline | `healthy`; version matches `Directory.Build.props` (`1.9.0-alpha`); random `Ok`; search `Ok` 20 matching; source-scoped 9 with 0 off-target rows; Churchill; two `NoResults` |
| Pagination contract | 795 quotes, 1507 actions, 35 audit rows; `pageSize=0` returns every row with `pageSize` equal to `totalCount`; `501` → 422 on all three; default 20; page-beyond-last → 422 on all three |
| Staged review workflow | Staged `202`, 1 pending, decide/undo/decide, apply `200`, `Applied=2` |
| Fresh seed | No zero counts, `pending=0`, duplicate checks A/B empty, `undeclared date variants = 0`, `resolvedToExisting = 25` with `unexplained no-ops = 0` and the control flagged |
| Per-file report | 5 reports with `fileName`/`entityTypes`; reset reports nothing; import's `report` singular; `removed=0` against `replacements=15`; `missingTypes=[]` |
| Startup wait page | `503 starting`, `hasDatabase=False`, self-contained refreshing page, then `healthy`/`ready` with 795 quotes; `kestrelFirst=True` |

**Step 5 of the fresh-seed document did not run**, as its own header states: it calls a `--convert`
entry point that does not exist until #400.

**One defect, fixed here:** that document's cleanup used the multi-path `Remove-Item` this environment's
shell guard refuses — the same form the reset document carried. It now removes what
`Get-ChildItem .claude/temp -Filter 'inspect-181.db*'` lists and lists again; the run left all three
files, sidecars included.

The six the end-of-issue scope requires and step 8 did not run, against the same build, each log read
before its container stops:

- *Baseline — health, version, random and search respond correctly*
- *The pagination contract holds live on every paginated endpoint*
- *The staged review → decide → apply workflow, end to end*
- *A fresh seed resolves every bundled file with nothing left pending*
- *Every seed and import surface reports per-file, per-entity-type counts*
- *Kestrel serves a wait page during initialisation instead of appearing dead*

They run before the key-ring change, so a failure here belongs to the build rather than to it. A
document that fails as written is fixed in this issue, like the five the Description names.

### 10. Share one key ring across test containers

**Status:** ⬜ Not started

`test-env.csx`, on `create` and `reenter`: resolve `.claude/temp/qt-keys` against the working directory,
create it if missing, and add `-v <that path>:/data/keys` — unless `--own-keys`, `--read-only-data` or
`--tmpfs-data` is given. `destroy` never touches the folder. The header documents `--own-keys` and says
which options imply it and why.

Probe before relying on it, each read from the container's own log before it is stopped:

- a container with and one without the shared ring, started at the same time on an empty `qt-keys`,
  each visited in the browser afterwards — both log nothing, so two writers to one ring are safe;
- the shared ring inside a `--read-only` root filesystem container — the mount is writable and the
  container healthy.

### 11. Write the antiforgery document

**Status:** ⬜ Not started

`api-surface/06-a-cookie-from-another-key-ring-is-replaced.md`, titled *A cookie from another key ring
is replaced on the first page, and logged only then*. The browser tab is closed and reopened between
containers; each log is read before its container is stopped.

1. Container A on the shared ring; visit `/notifications` twice. The first visit is not asserted — it
   depends on what the browser held — the second logs nothing.
2. Container B, a new volume on the shared ring; visit once — logs nothing. This is the core claim, and
   the red case: before step 10 there is no shared ring, and B logs the pair.
3. Container C with `--own-keys`; visit twice — the first logs the `CryptographicException` /
   `AntiforgeryValidationException` pair, measured in this step for the exact set; the second logs
   nothing.
4. Container D on the shared ring; visit twice — the first logs the pair once, the second nothing. This
   restores the browser for every later test, and is the Knowledgebase entry's remedy, exercised.

**Red first:** run against the script as it was before step 10 — step 2 logs the pair.

### 12. Assert a clean log in the browser-driven documents

**Status:** ⬜ Not started

*Notifications list, dismiss, render, and drive their action* reads the log before its step 7 stop and
expects no `[Runtime - Exception]` line; *A file left awaiting review raises an alert, and resolving it
retires the alert* does the same before its step 3 restart and again before step 9. Both close the
browser tab before any stop, so no page reconnects.

**Red:** both were measured before step 10 in step 8's rerun — 2 lines each, the antiforgery pair.

### 13. Update the automated-testing index

**Status:** ⬜ Not started

- The `test-env.csx` options table gains `--own-keys`.
- *Read the log before the application stops* gains the browser rule: a test that provokes the
  antiforgery pair uses `--own-keys` and ends by restoring the browser; `qt-keys` is never deleted.
- `api-surface/` lists the new document.

### 14. T2, targeted pass — prove the shared ring

**Status:** ⬜ Not started

Against a build of the branch, each document in full, each log read before every stop:

| Why | Document |
|---|---|
| New | *A cookie from another key ring is replaced on the first page, and logged only then* |
| Browser-driven | *Notifications list, dismiss, render, and drive their action*; *A file left awaiting review raises an alert, and resolving it retires the alert*; *A running notification action says so, and cannot be started twice*; *Degraded-state pages survive a genuine migration failure* |
| `--bind` | *The changelog is served from its own on-disk database, not the JSON fallback* |
| `reenter`, `--read-only` root | *Migration replay survives an environment where only the data directory is writable* |
| `--read-only-data`, `--bind`, `reenter` | *A reset refuses when the backup folder cannot be written, and stops re-offering the override* |
| `--tmpfs-data` | *A reset refuses when the disk fills during the backup, instead of wiping behind a 200* |
| Smoke set | The six of step 9, re-run because the key ring changed their environment too |

### 15. T2, second pass — everything else the change touched

**Status:** ⬜ Not started

Every other document in `docs/automated-testing/`: every container gains the mount, so every document's
environment changed. A document found broken is fixed in this issue and run again.

### 16. T1 pass

**Status:** ⬜ Not started

The developer starts the application in Visual Studio.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The pending-review alert document asserts what a reseed now produces and runs in its written order | Live (T2) | *A file left awaiting review raises an alert, and resolving it retires the alert* passes every step, in order |
| 2 | ✅ | An obsolete alert reads *No longer applicable* | Live (T2) | *Notifications list, dismiss, render, and drive their action*, steps 7 and 8, with the fourth row |
| 3 | ✅ | The already-reported conflict document prints its counts | Live (T2) | *An already-reported conflict does not stage a duplicate on every reseed*, steps 4 and 5 print `1 -> 1 -> 1` |
| 4 | ✅ | The bulk-decide document stages batches of its own and runs its round trips | Live (T2) | *Bulk-deciding a staged batch via file export and re-import, in both wire formats* passes every step |
| 5 | ✅ | The changelog document reads the import line once it is written | Live (T2) | *The changelog is served from its own on-disk database, not the JSON fallback*, step 5 |
| 6 | ✅ | The reset document leaves nothing in `.claude/temp` | Live (T2) | *Reset wipes the entire database and does not reseed*, then `Get-ChildItem .claude/temp -Filter 'smoke156*'` lists nothing |
| 7 | ✅ | The pending-review alert document runs against a fresh database whatever an earlier run left | Live (T2) | Step 1 run over a leftover bind folder: step 6 lists three alerts |
| 8 | ✅ | The already-reported conflict document reseeds only once the application answers | Live (T2) | Step 3 as written: the reseed succeeds |
| 9 | ❌ | A new test container on the shared ring reads the browser's cookie | Live (T2) | *A cookie from another key ring is replaced on the first page, and logged only then*, step 2: no `[Runtime - Exception]` line; red before step 10 |
| 10 | ❌ | Provoking the pair is possible on demand, and the remedy restores the browser | Live (T2) | Same document, steps 3 and 4: the pair once, then nothing |
| 11 | ❌ | The browser-driven documents log no exception they did not cause | Live (T2) | *Notifications list, dismiss, render, and drive their action* before its step 7 stop, and *A file left awaiting review raises an alert, and resolving it retires the alert* before step 3 and step 9: no `[Runtime - Exception]` line |
| 12 | ❌ | Every document whose environment changed still passes | Live (T2) | Steps 14 and 15: every document in the suite passes as written |
| 13 | ❌ | A container's log is read before the application stops | Review | The index's *Read the log before the application stops*, and every document run in steps 14 and 15 read that way |
| 14 | ✅ | The smoke documents step 8 skipped pass | Live (T2) | Step 9: each of the six passes as written |
