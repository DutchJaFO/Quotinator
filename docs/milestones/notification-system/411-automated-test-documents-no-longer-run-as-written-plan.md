# #411 — Automated-test documents no longer run as written

**Status:** In progress (step 7)
**GitHub issue:** #411
**Tiers required:** T1, T2
**Depends on:** —

---

## Next action

Step 7: fix the reset document's cleanup.

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
a value the code has checked is returned as an outcome, not thrown. That is code, not a document, so it
is outside this issue — raised with the developer.

A container with `Quotinator__IncludeDefaultSources=false`; a base file and a conflicting file of two
quotes each, the second imported under `review` for each of the three batches. The JSON and CSV round
trips set `keep` on each `quoteText` row, then expect `actionsDecided=2` and `errors=0`. The
malformed-row step sets `keep` on one action's `quoteText` row and `not-a-choice` on the other's, then
expects one error naming the rejected value, one action `Decided` and one still `Pending`. The CSV
edits use `corrupt-csv-cell.csx --row`, the row found by its `Field` value.

### 6. Fix the changelog document's step 5

**Status:** ✅ Done — run as written against the step 1 image; every step passes. At health the log held
one `refreshed 126 entries` line; the second appeared after about a second of polling, also 126.

The container's log holds two `[Runtime - Exception]` lines, both `SocketException (125): Operation
canceled`, logged the moment the restart stops the server — none before it, measured by counting after
start, after the page request and after the restart. They do not affect the app's function; they are
outside this issue and raised with the developer.

After the restart, poll the log until a second `[Changelog - Import]` line appears, for at most 60 s,
then assert it reports the same entry count as the first.

### 7. Fix the reset document's cleanup

**Status:** ⬜ Not started

The cleanup removes the `-wal`/`-shm` files beside both database copies.

### 8. T2 pass

**Status:** ⬜ Not started

Each changed document, in full, against a build of the branch; each container's log read for
`[Runtime - Exception]` lines before it is removed.

### 9. T1 pass

**Status:** ⬜ Not started

The developer starts the application in Visual Studio.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The pending-review alert document asserts what a reseed now produces and runs in its written order | Live (T2) | *A file left awaiting review raises an alert, and resolving it retires the alert* passes every step, in order |
| 2 | ❌ | An obsolete alert reads *No longer applicable* | Live (T2) | *Notifications list, dismiss, render, and drive their action*, steps 7 and 8, with the fourth row |
| 3 | ❌ | The already-reported conflict document prints its counts | Live (T2) | *An already-reported conflict does not stage a duplicate on every reseed*, steps 4 and 5 print `1 -> 1 -> 1` |
| 4 | ❌ | The bulk-decide document stages batches of its own and runs its round trips | Live (T2) | *Bulk-deciding a staged batch via file export and re-import, in both wire formats* passes every step |
| 5 | ❌ | The changelog document reads the import line once it is written | Live (T2) | *The changelog is served from its own on-disk database, not the JSON fallback*, step 5 |
| 6 | ❌ | The reset document leaves nothing in `.claude/temp` | Live (T2) | *Reset wipes the entire database and does not reseed*, then `Get-ChildItem .claude/temp -Filter 'smoke156*'` lists nothing |
