# #383 — Notification detail table has no totals line

**Status:** Waiting for release
**GitHub issue:** #383
**Tiers required:** T1
**Depends on:** [#377](https://github.com/DutchJaFO/Quotinator/issues/377)

**Next action: ship it** — every step is complete and every verification row is green.

---

## Description

The notification detail table renders one row per entity type — `Entity | Incoming | Added | Updated |
Skipped | Unchanged | Resolved` — and no totals line, so a reader with several entity types adds the
columns up by eye.

`NotificationTable.PayloadDetail` builds it as a `PayloadTable(Headers, Rows)` record
(`src/Quotinator.Api/Components/Controls/NotificationTable.razor.cs:124`), and `NotificationTable.razor`
renders it in **two** places: a `<details>` block in place for the startup popup, and the dialog on the
Notifications page. Both need the footer; only one of them being changed is the obvious failure mode.

**#377 established an invariant this table is the only place to see** —
`Incoming == Added + Modified + Unchanged + Skipped + ResolvedToExisting`. A totals line makes it
checkable at a glance, per column, across every entity type at once.

**The notification body already states overall totals in prose.** This is therefore the same
information aligned under the columns it belongs to, which prose cannot do — a deliberate duplication,
recorded here so a later reader does not "simplify" it away.

### Why a `Totals` member rather than an appended row

A totals line appended to `Rows` is indistinguishable from a real entity row: it renders inside
`<tbody>` and reads as an entity named "Total". Making it a separate member lets the markup put it in
`<tfoot>`, which is what makes it a footer semantically rather than by styling alone, and keeps the two
render sites from each needing to know that the last row is special.

### Two decisions, both settled

**A single-entity-type payload renders its totals line too**, where it necessarily repeats the only
data row. A footer that comes and goes reads worse than one whose shape is fixed, and suppressing it
would need a rule about when — a second decision this issue does not need to make. Accepted at T1
(developer, 2026-09-09: *"looks good enough"*) and pinned by verification row 2, so changing it later
is a visible change rather than a silent one.

Drafted first as an open question to settle after seeing the rendered result, with a verification row
to carry it. Both were wrong and were removed: a row whose method is "the developer decides" is the
shape `process.md` refuses — a promise rather than a step anyone can run, the one that held #307 open
for weeks — and **an open question does not belong in a plan that has started** (developer,
2026-09-09). A question raised during planning is decided before the plan runs, or it becomes its own
tracked issue; it is never carried alongside the work as a pending item.

**`ImportReviewPending` gains no totals line.** The other payload using `PayloadTable` has two columns
(`Status | Count`) whose sum its own body already states, so a footer there would restate the sentence
directly above it rather than align anything under a column.

---

## Steps

### 1. Write the four tests and confirm them red

**Status:** ✅ Done

All in `tests/Quotinator.Api.Tests/Components/NotificationTableTests.cs`, against `PayloadDetail`
directly — the computation is what these assert; the markup is T1's job.

| Test | Asserts |
|---|---|
| `PayloadDetail_ReseedFileApplied_TotalsRowSumsEveryColumn` | every numeric column's total equals the sum of that column across the rows, on a payload with more than one entity type |
| `PayloadDetail_ReseedFileApplied_SingleEntityType_StillCarriesTotals` | the line is present and equals the single row, pinning the settled decision so a later change to it is visible |
| `PayloadDetail_ImportReviewPending_HasNoTotals` | the sibling payload is untouched |
| `PayloadDetail_ReseedFileApplied_TotalsLabelComesFromTranslations` | the leading cell is the localised label, not a hardcoded `"Total"` |

The first must fail on a signature that does not yet exist, so it starts red by construction; that is
weaker evidence than a behavioural red, and the second and third are what carry the real contract.

### 2. Add the translation key

**Status:** ✅ Done

`NotificationsDetailTotalLabel` in `UI.en-GB.json`, `UI.de.json` and `UI.nl.json`, alongside the
existing `NotificationsDetail*Column` keys. `TranslationCompletenessTests` fails on any locale missing
it, which is the mechanical guard — English first would leave two locales red, so all three land
together.

### 3. Give `PayloadTable` a `Totals` member and populate it

**Status:** ✅ Done

`PayloadTable(Headers, Rows, Totals)` with `Totals` empty for every payload that has none, so the two
existing branches and the `_ => ` default keep working unchanged. The `ReseedFileApplied` branch sums
its own rows column-wise; the leading cell is the localised label.

Summing is over the same filtered collection the rows are built from, not a second independent pass
over `applied.Counts` — so the footer cannot disagree with the table above it the moment the row filter
changes and a separate sum is not updated to match. The branch became its own `ReseedTable` helper to
hold that shared collection; the switch arm now just calls it.

### 4. Render it in `<tfoot>`, at both sites

**Status:** ✅ Done

`NotificationTable.razor` renders the table twice — the in-place `<details>` block and the dialog. Both
get the same `<tfoot>`, guarded on `Totals.Count > 0` so a payload without totals renders no empty
footer.

**Razor caveat (CLAUDE.md):** a `.razor` change can build clean and still be wrong at runtime. T1 is
what covers this step, and it is why this issue requires T1 at all.

### 5. Build and full test run

**Status:** ✅ Done

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both `0 Warning(s)  0 Error(s)`. Every file touched goes into `.editorconfig`'s scoped `IDE0008`
section at the moment it is first touched, per the boyscout rules.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every numeric column's total equals the sum of that column across the rows | Unit test | `NotificationTableTests.PayloadDetail_ReseedFileApplied_TotalsRowSumsEveryColumn` |
| 2 | ✅ | A single-entity-type payload still carries a totals line | Unit test | `NotificationTableTests.PayloadDetail_ReseedFileApplied_SingleEntityType_StillCarriesTotals` |
| 3 | ✅ | `ImportReviewPending` gains no totals line | Unit test | `NotificationTableTests.PayloadDetail_ImportReviewPending_HasNoTotals` |
| 4 | ✅ | The leading cell is the localised label, never a hardcoded string | Unit test | `NotificationTableTests.PayloadDetail_ReseedFileApplied_TotalsLabelComesFromTranslations` |
| 5 | ✅ | The new key exists and is non-empty in all three locales | Unit test | `TranslationCompletenessTests` — existing, fails on any locale missing `NotificationsDetailTotalLabel` |
| 6 | ✅ | The totals line renders in `<tfoot>`, not `<tbody>` | Live | `grep -c "tfoot" src/Quotinator.Api/Components/Controls/NotificationTable.razor` → `4` (open and close, at both render sites) |
| 7 | ✅ | Build and full suite clean | Live | `dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1` — both `0 Warning(s)  0 Error(s)` |
| 8 | ✅ | T1: the totals line appears, correct and legible, in **both** the startup popup and the Notifications dialog | Live | Developer, 2026-09-09 — in-place popup (Quote/Source: 181, 0, 0, 0, 160, 21) and dialog (Character/Quote/Source: 1167, 1078, 56, 0, 32, 1); every column totals correctly and the footer reads as one. *"looks good enough"* |
