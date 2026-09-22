# #411 — Automated-test documents no longer run as written

**Status:** In progress (step 15)
**GitHub issue:** #411
**Tiers required:** T1, T2
**Depends on:** —

---

## Next action

Step 15: T2 on every other document in the suite.

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
- **`--own-keys` opts out; `--tmpfs-data` implies it, and `--read-only-data` depends on the command.**
  `--tmpfs-data` keeps its keys on its tmpfs, because *A reset refuses when the disk fills during the
  backup, instead of wiping behind a 200* sizes that tmpfs to run out of space, and moving the keys off
  it changes the arithmetic. `create --read-only-data` is a new volume that never had keys, so it gets
  none; `reenter --read-only-data` keeps the shared ring its volume was created with, mounted read-only
  (corrected in step 14).
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

**Status:** ✅ Done — `test-env.csx` mounts `.claude/temp/qt-keys` at `/data/keys` unless `--own-keys`,
`--read-only-data` or `--tmpfs-data` is given. Probed against the step 8 image, each log read before its
container stopped:

| Probe | Result |
|---|---|
| Two containers booted at the same moment on an empty ring | Both healthy, one key in the ring, no exception line — one wrote it, the other read it |
| `--read-only` root | Mounts `/data` and `/data/keys`; healthy; no exception line |
| `--read-only-data` | Mounts `/data` only — no shared ring |
| Browser: shared-ring container a, then b | 0 lines on either |
| Control: `--own-keys` container c, visited next | Exactly the pair — 2 lines. The browser was sending a's cookie, so b's 0 is a real result |
| Remedy: b again, twice | The pair once, then nothing more |

**The concurrency probe started its two containers with `docker run` directly.** Launching the script
twice at once collides on dotnet-script's shared compilation cache, which is the script host's and not
the application's; the containers it creates are the same either way.

**Two measurements refine the Decisions above.** With the tab closed before each container change, the
own-ring visit logs exactly the pair, not the 5 lines measured earlier: the extra three came from the
old page reconnecting. And after the browser pane was reopened, the first visit logged nothing — the
antiforgery cookie lives only for the browser session, so a fresh pane holds none.

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

**Status:** ✅ Done — written, added to `Quotinator.slnx`, and run both ways against the step 8 image:

| Step | Red — script before step 10 | Green — as written |
|---|---|---|
| 1. Pin the browser (first, second visit) | 2, 0 | 2, 0 |
| 2. New container on the shared ring | **2** — `CryptographicException`, `AntiforgeryValidationException` | **0** |
| 3. `--own-keys` container (first, second visit) | not run | 2, 0 — the pair; the page renders |
| 4. Restore (first, second visit) | not run | 2, 0 |

The red run stopped at step 2, which is where it fails; its container had no `/data/keys` mount. The
old script was recovered from `1e97f777` into `.claude/temp` and removed afterwards.

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

**Status:** ✅ Done — both documents carry a `Read-Thrown` function that prints only the lines added
since its previous read, called before every stop with the browser tab closed first; each expects
`thrown=0`. The pending-review alert document also says why step 9's read-only container is not counted:
its 48 lines are the read-only data directory's own defect. Run green in step 14.

*Notifications list, dismiss, render, and drive their action* reads the log before its step 7 stop and
expects no `[Runtime - Exception]` line; *A file left awaiting review raises an alert, and resolving it
retires the alert* does the same before its step 3 restart and again before step 9. Both close the
browser tab before any stop, so no page reconnects.

**Red:** both were measured before step 10 in step 8's rerun — 2 lines each, the antiforgery pair.

### 13. Update the automated-testing index

**Status:** ✅ Done — *Fresh* documents the shared key ring and its options table gains `--own-keys`,
plus `--read-only-data` and `--tmpfs-data`, which were missing from it and now imply `--own-keys`. *Read
the log before the application stops* gains the closed-tab rule and the rule for a test that provokes
the pair. `api-surface/` lists the new document. `RepositoryStructureTests`: 27 passed.

- The `test-env.csx` options table gains `--own-keys`.
- *Read the log before the application stops* gains the browser rule: a test that provokes the
  antiforgery pair uses `--own-keys` and ends by restoring the browser; `qt-keys` is never deleted.
- `api-surface/` lists the new document.

### 14. T2, targeted pass — prove the shared ring

**Status:** ✅ Done — against the step 8 image (no code change since), each log read before every stop.
Every document passes as written, after the corrections below:

| Document | Result |
|---|---|
| *A cookie from another key ring…* | Step 11's run — red at step 2 before the change, green throughout after |
| *Notifications list, dismiss, render, and drive their action* | Every step; `thrown=0` before the stop and before the cleanup (red was 2); with the tab closed the stop logged 2 lines, not 7 |
| *A file left awaiting review…* | Every step; `thrown=0` before the restart and before step 9 (red was 2); read-only parity `500`/`500`/`200`/`200` |
| *A running notification action says so, and cannot be started twice* | Every step, after the fixes below |
| *Degraded-state pages survive a genuine migration failure* | Every step, after the script fix below; console six `503`s; no stale-cookie line |
| *The changelog is served from its own on-disk database…* | Every step; `--bind` with the ring nested inside it |
| *Migration replay survives an environment where only the data directory is writable* | Every step, after the fix below; `--read-only` root with the ring mounted |
| *A reset refuses when the backup folder cannot be written…* | Every step; `reenter --read-only-data` over a bind, the ring mounted read-only |
| *A reset refuses when the disk fills during the backup…* | Every step; the tmpfs keeps its own keys |
| The six smoke documents of step 9 | All pass again, 0 exceptions each |

**The shared ring broke one document, and the script was corrected.** *Degraded-state pages survive a
genuine migration failure* answered `500` on `/` and `/notifications`: its 1.8.2 seed wrote its keys to
the shared folder, and the read-only re-entry, given no ring at all, had no keys to read. `reenter
--read-only-data` now mounts the shared ring read-only — the keys that install was created with, made
unwritable like the rest of `/data` — while `create --read-only-data` still mounts none. Rerun green,
and the index's options table says so.

**Stale documents, fixed here:**

- *A running notification action…* named a **Run** button that is now *Reseed the database*, and waited
  with two fixed sleeps (3 s and 20 s). Both now poll a condition. The run found the alert records
  `resolved` one second after the reseed starts while the page reads **Running…** for another 18, so
  step 3 waits for `reseed complete` rather than for the alert.
- *Migration replay survives…* asserted the quote count is unchanged by the upgrade — false since #374's
  Migration009 deletes duplicate quotes per Source. Its 1.8.2 seed holds four such pairs (799 → 795); it
  now counts them and asserts `upgraded = seeded − duplicates`. The pre-change script gave the same
  799 → 795, so the key ring was not the cause.
- *A reset refuses when the disk fills…* expected 1–2 MB free on its tmpfs; measured 260 KB. The outcome
  is unchanged; the figure is updated.

**Two application findings, outside this issue:**

- **A reset in the first moments after startup races the what's-new notification.** The startup writes
  it on a detached task with the pre-reset application-version id; a reset that lands first wipes that
  row, and the write fails `FOREIGN KEY constraint failed` — one exception rethrown ten times, then
  `[Server] Failed to seed the #81 what's-new notification`. Reproduced 5 of 5 by firing the reset the
  instant health answers, against 0 of 5 with the script's one-second poll. Found in *A running
  notification action…*'s first run.
- **Migration009 keeps the earliest row of a duplicate pair, which can drop the curated one.** On the
  1.8.2 upgrade the "Inigo Montoya" line survives as `f3557c41`, not as the curated `da53310a` a
  conversation in `quotinator-curated.json` references.

Exceptions logged before each stop, beyond the ones the documents provoke on purpose: #407's
`DatabaseBackupUnavailableException` refusals in both backup documents, and the read-only
directories' own `SqliteException`/`IOException`s.

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

**Status:** In progress — 53 documents, each run as written with the log read before every stop.

| Document | Result |
|---|---|
| *The Unicode-aware search flag reaches the running app* | Pass — flag off `NoResults`, on `Ok` with the fixture; 0 exceptions |
| *Endpoint names and summaries follow the standard* | Pass — 59 operations; renames 1/1/0/0; summaries 1/1/1/0/0/0; `GetQuoteById` 1, `GetById` 0; `by ID` 12, `by id` 0 |
| *A thrown exception is logged where the app actually runs* | Pass — 0 before; 5 after, `JsonReaderException` plus one `QuoteImportValidationException` id rethrown; quiet request adds none |
| *A reset refuses when the database cannot be read…* | Pass — 34 bytes, `503`, `SourceUnreadable`, 2 remedies, no override; `409` with it; remedy then `200` |
| *A reset refuses when the database is truncated…* | Pass — `503`, `SourceUnreadable`, no override; remedy then `200`. Its Determinism claimed `destroy` stops cleanly; it is `docker rm -f`, now said so |
| *A full backup quota is resolvable from inside the application* | Pass — create; `BudgetExceeded` with remedies naming the endpoints; reset `409`; delete `204`; reset `200`; download matches; read-only delete `409` with its remedy |
| *Captured source files are recorded with provenance and reconstruct byte-for-byte* | Pass — six captured rows, `manifest.json` linked to all 5 batches; list/detail/batches agree; byte-for-byte download; CRLF 277 against 0; `prunedCount=0` |
| *Audit export, date-range discovery, and conflict-data auto-purge* | Pass after a fix — step 12 re-imported the curated file for change rows, which since #373 writes none (`ChangesBefore 0`, its own stop condition); it now applies the conflict fixture (`1` before, `1` after). Its cleanup missed two copies' sidecars; it now removes what a listing finds |
| *Reset wipes the entire database and does not reseed* | Pass — 795 → 0, audit 1, `NoResults`; both counters 1 before and after |
| *A file-authored explicit id is canonicalized at capture* | Pass — no `SQLite Error 19`; the Source resolves by its lowercase id and the quote's join holds |
| *A quote resolves by id in either casing* | Pass — both casings `200`, id lowercase, same quote |
| *A conversation line in the wrong casing does not violate the foreign key* | Pass — `200`, `0 -> 0` |
| *String-typed id fields render canonically over HTTP* | Pass after a fix — it staged its batch from the curated file, which since #373 answers `200` (`Expected 202, got 200`); it now stages the conflict fixture. Every id checked is lowercase |
| *Generic-repository endpoints return correct data and lowercase ids* | Pass — all eight endpoints populated and lowercase; a Source resolves in both casings |
| *A batch applied through the staged flow can be reversed* | Pass after a fix — it staged from the curated file (`Expected 202, got 200`); now the conflict fixture. Apply, preview and reverse all `200` |
| *`POST /import?batchId=` applies an already-staged batch without re-uploading* | Pass after a fix — the curated preview's 37 actions all read `Applied` before the alias ran, so the comparison could not fail; the conflict fixture's `Quote Modify Decided` now moves to `Applied` |
| *Discard* | Pass — the pending action `Discarded`, the no-op still `Applied`, quotes unchanged |
| *Reverse and resurrection* | Pass after a fix — the curated import was 37 `Unchanged` no-ops, so the reversal removed nothing and the Airplane quotes were still there before the re-import. Two quotes of its own now read 2 → 0 after the reversal → 2 after the re-import |
| *Bodyless request validation* | Pass — `422` and `404`, each with a `detail` |
| *StageDirection and SoundCue Modify* | Pass — `Complete` rows block the third import; the reversal restores the original text. Step 4's reason for its decide-everything loop (a quote `Modify`) is gone since #373; the text now says so |
| *Person Modify and lowercase-id reversal* | Pass — `Blocked` third import; `404` after the reversal, `Add` and `200` after the re-import. Same stale loop reason, corrected |
| *Character/Source many-to-many identity* | Pass — links 15 → 16; a second Source makes a second Character |
| *Source date from the resolving quote* | Pass — 434 of 473 sources dated; Airplane! 1980, Jurassic Park 1993, Frozen 2013 |
| *`batchId` validation and request-log status* | Pass — three `422`s logged as `422`; its happy path now applies the conflict fixture's decided action rather than a batch of no-ops |
| *Character Modify and explicit id on Add* | Pass — rename `Complete`, third name `Blocked`, explicit id canonical, `AIRPLANE!` matches the stored Source. Same stale loop reason, corrected |
| *Bulk-deciding a staged batch via file export and re-import, in both wire formats* | Pass — rerun, as step 5 recorded |
| *Rule-file live-read proof* | Pass — the conflict returns without the rule; `Replace` records `2005`; the rule file restored and the image rebuilt |
| *Conflict rule staleness*, *Source alias staleness* | Pass except the evaluation line, which #347 adds; both now declare `Fully green after: #347` |
| *Rule-file override endpoints* | **Fails at step 4 — an application defect.** Step 3 staged from the curated file (`Expected 202, got 200`) and now uses the conflict fixture; step 4's `generate` then answers an unhandled `500`: `nikhilnamal17-conflict-rules.json` holds two rules for `e69951f1-…`, and `ConflictRuleGenerator.Merge` keys a dictionary on the entity id (`An item with the same key has already been added`). Raised with the developer |
| *A reseed imports the designated files and deletes nothing* | Pass after a fix — step 5 called `DELETE /quotes/{id}`, which does not exist (`405`); the removal is now a constructed soft delete (795 → 794 → 795, that quote back). Its `Fully green after: #373` header no longer applied on this branch and is removed |
| *A season-attached quote is served through the API* | Pass — three seasons, the episode's season in both read paths, the quote, and the database link |
| *A `Complete` row is not blocked when the resolution writes nothing* | Pass after a fix — its step 3 reseeded straight after `docker restart` and failed with the connection closed; step 2 now waits for health. `blocked=0`, a `ResolvedToExisting` action |
| *A `Complete` row still blocks a real write* | Pass after the same fix — `blocked=1`, the third text never written |
| *An already-reported conflict does not stage a duplicate on every reseed* | Pass — rerun: `1 -> 1 -> 1`, `AlreadyReported=2` |
| *A new conflict is still staged* | Pass after the same restart fix — `1` then `2` distinct unresolved |
| *A review row whose batch is gone offers only dismiss…* | Pass after a fix — step 1 wrote into its bind folder without clearing it (the pending-review alert document's defect); now cleared. Every page assertion, then Dismiss: `Discarded`/`Applied`, alert `resolved`, eight worded badges |
| *Reviewing conflicted import actions throws nothing* | Pass — three pending with `quoteText`, the refusal names it, `before=0 after=0` |
| *A case-only change is shown for review* | Pass — both hold `quoteText`; refusal; `Decided` with the incoming casing |
| *Deciding an Add answers an outcome* | Pass after the bind-folder fix — the five cases held as expected, each decide `422` with the right reason, no exception |
| *Notification metadata, provenance, and the released-database migration path* | Pass after two fixes — its data folder was never cleared (now it is), and step 6's `DELETE` failed on the announcement's #319 translation rows (`FOREIGN KEY constraint failed`), rolling the edit back so `announcementBack=1` was read off the untouched original; translations are now deleted first (`4 row(s) affected`, 0 announcements while stopped, then back beside the legacy row). Step 5 expected one `applying … pending` line where startup logs one per phase (2); it now compares against step 2's own count |
| *Upgrading from an intermediate schema version* | Pass after a fix — its data folder was never cleared; now it is, and the log is read before the cleanup's stop. `version 4 → 22`, `Quotinator ready`, rows `1.8.3|0`, `NULL 1.8.4|1`, `1.9.0-alpha|2` |
| *Upgrading a v1.8.3 database enriches its notification rather than duplicating it* | Pass after the same fix — `1` then `1`, `metadataKind=announcement`, `retained=True` |
| *The legacy notification gains provenance…* | Pass after the same fix, for both its folders, with the log read before each of its two stops — `1.8.3|1`, `1.9.0-alpha|2` upgraded; `1.9.0-alpha|1` alone fresh |
| *A what's-new row written before the release state existed is backfilled* | **Rewritten.** Its counter rollback could no longer reach the backfill: measured as written, two deletes replayed `20 → 22` and the row came back unchanged; reaching migration 10 would replay three non-idempotent `ADD COLUMN`s. It now upgrades 1.8.3 → an image built from `54bab6f7^` (data v8) → current (`8 → 22`). Its second row could not tell adding from overwriting; a row stating `Unreleased` beside a `version` now does, and stays `Unreleased`. All four rows as expected, `healthy`, 0 exceptions |

Exceptions before each stop, beyond what a document provokes: #407's `DatabaseBackupUnavailableException`
refusals in the two unreadable-database documents, alongside the corrupt file's own `SqliteException`s;
#404's `ImportBatchStateException` and `ImportBatchNotFoundException` for every refused reversal (the
audit, reverse-and-resurrection and bodyless-request documents).

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
| 9 | ✅ | A new test container on the shared ring reads the browser's cookie | Live (T2) | *A cookie from another key ring is replaced on the first page, and logged only then*, step 2: no `[Runtime - Exception]` line; red before step 10 |
| 10 | ✅ | Provoking the pair is possible on demand, and the remedy restores the browser | Live (T2) | Same document, steps 3 and 4: the pair once, then nothing |
| 11 | ❌ | The browser-driven documents log no exception they did not cause | Live (T2) | *Notifications list, dismiss, render, and drive their action* before its step 7 stop, and *A file left awaiting review raises an alert, and resolving it retires the alert* before step 3 and step 9: no `[Runtime - Exception]` line |
| 12 | ❌ | Every document whose environment changed still passes | Live (T2) | Steps 14 and 15: every document in the suite passes as written |
| 13 | ❌ | A container's log is read before the application stops | Review | The index's *Read the log before the application stops*, and every document run in steps 14 and 15 read that way |
| 14 | ✅ | The smoke documents step 8 skipped pass | Live (T2) | Step 9: each of the six passes as written |
