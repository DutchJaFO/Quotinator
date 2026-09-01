# #369 — A review row whose batch is gone offers decisions that cannot be carried out

**Status:** Planning
**GitHub issue:** #369
**Tiers required:** T1, T2
**Depends on:** #303 (the page this corrects, and the notification metadata this reads the file name from)

---

## Description

A reseed deletes `Import_Batch` and keeps `Import_Action`, so any batch staged before that reseed is
left with `Pending` actions whose parent row is gone. `/import-review` and `GET /import/actions` both
list them as work awaiting a decision.

Found in T1 on 2026-09-01 — four conflicting files staged, then two reseeds. Confirmed against the
database:

```
055c95c5-…  1 action  ORPHAN                     5916365c-…  1 action  conflicting.json
46b6aab9-…  1 action  ORPHAN                     a0b1209c-…  1 action  conflicting-1.json
bcd9ba25-…  1 action  ORPHAN                     aaed69d1-…  1 action  conflicting-2.json
c5449117-…  1 action  ORPHAN                     e0ae51fd-…  1 action  conflicting - Copy.json
```

Eight rows, four actionable. The orphans render their raw batch id and still offer **Keep existing** /
**Take incoming**, neither of which can succeed.

The notification side is already correct — `DismissAlertsForRemovedBatchesAsync` retired exactly the
four superseded alerts as `Obsolete`. The alerts know a batch was removed; the actions do not.

**This predates #303.** `Sql.SystemImportActions` carries no join to `Import_Batch`, so the REST
endpoint returns the same eight rows; the page made it legible, it did not cause it.

## Design (developer decision, 2026-09-01)

Taken over both alternatives originally proposed — deleting `Import_Action` during reseed, and adding
a status meaning "superseded". Neither is needed, and neither costs a migration:

1. The file name comes from the **notification**, not `Import_Batch`. `ImportReviewPendingMetadataDto`
   already stores `FileName` next to `BatchId`, and the notification outlives the batch.
2. An unresolvable batch id **is** the signal that the action is no longer possible. No new status —
   the fact is already known at render time.
3. Such a row offers **only dismiss**. `IImportActionService.DiscardBatchAsync` already discards a
   whole batch without touching a domain table, and every action in an orphaned batch is equally
   impossible, so they go together.

---

## Steps

### 1. Resolve the file name from notification metadata when the batch is gone

**Status:** ⬜ Not started

`FileNameFor` currently reads `Import_Batch.Name` only. Add the notification-metadata lookup as the
second source, keyed on the metadata's own `batchId`. One read for the page, not one per row — the
same N+1 rule the existing batch lookup already follows.

### 2. Make "batch no longer exists" a first-class state on the row

**Status:** ⬜ Not started

Rendered visibly, with a localised label in all three `UI.*.json` files. Decided here rather than
inferred at each call site, so the page and its tests agree on one predicate.

### 3. Offer only dismiss on such a row

**Status:** ⬜ Not started

Keep/take removed, not disabled-and-still-shown — they are impossible, not unavailable. Dismiss calls
`DiscardBatchAsync`.

### 4. Replace #303's id-fallback requirement

**Status:** ⬜ Not started

`ImportReviewPageTests.FileNameFor_UnknownBatch_FallsBackToTheId` asserts the behaviour this issue
removes. It is replaced, not deleted quietly — the replacement asserts the name resolves from the
notification instead.

### 5. Decide what a pre-#303 batch does

**Status:** ⬜ Not started

A batch staged before #303 shipped has no `ImportReviewPending` notification, so its name cannot be
recovered by this route. Either it renders as a plain unresolved row or it is out of scope; say which
rather than leaving it to fall out of the implementation.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | TBD — every row named once steps 2 and 5 are settled | | |

**The test list is completed before implementation starts, not during it.** One row is already known
to be needed and easy to miss: `DiscardBatchAsync` is written against actions rather than against the
batch row, and has never been exercised with the parent missing.
