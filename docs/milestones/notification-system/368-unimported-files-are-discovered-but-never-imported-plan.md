# #368 — New import files are discovered but never imported, and nothing says so

**Status:** Planning
**GitHub issue:** #368
**Tiers required:** T1, T2
**Depends on:** #303 (its `/import-review` page is where a remedy would point), #304 (the reseed
action), #390 (a reseed must see the current files before it can be a remedy at all)

---

## Description

Files added to `{dataDir}/imports/` on a database that already holds quotes are found, recorded in an
auto-created manifest, and then never imported. Nothing in the UI or the log says they were skipped.

Found in #303's T1 pass (2026-09-01): four files were dropped into `data/imports/`, the startup log
reported `auto-created manifest.json listing 4 file(s) alphabetically`, and no `importing N quotes
from …` line followed for any of them.

**This plan needs refining before it can be executed.** Step 1 is an open design decision — what the
remedy actually is — and it decides whether this issue is a notification, an import path, or both.
Until it is answered, this is a plan to refine, not a plan to execute.

## Background

The two halves run in different places:

1. **Discovery** happens when `IDatabaseInitializer` is first resolved — `SeedBatchesBuilder.Build` →
   `ManifestSeedPlanner.PlanSeed` scans the directory and writes a manifest for what it finds. That is
   the `WRN` line above.
2. **Seeding** happens later in `OnInitialisedAsync`, and `SeedIfEmptyInternalAsync` returns
   immediately when the Quotes table is non-empty:

```csharp
int count = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
if (count > 0) return;
```

So the manifest is evidence the files were seen; nothing read them.

A reseed no longer truncates anything (#372), which would make it the natural route — but it plans from
the file list built when the application started, so a file added after startup is invisible to it until
a restart (#390). Until #390 lands, a reseed is not a route for these files at all.

---

## Steps

### 1. Decide what the remedy is

**Status:** ⬜ Not started — **blocks every step below**

Since #372 a reseed deletes nothing, and once #390 makes it plan from the current files it is itself a
candidate: it imports every designated file, reporting the ones already stored as unchanged. `POST
/api/v1/import` imports a single uploaded file the same way, so the mechanism exists either way; what is
missing is a path from "file found on disk" to it. The candidates are a notification whose action
reseeds, a notification whose action imports just the new files, a notification that only reports and
leaves the user to act, or importing them automatically and reporting what happened. #390 also raises a
notification when a directory's manifest is out of date; the two are designed together.

Whether the notification is `Informational` or `Action-required` follows from this answer, and so does
every test below.

### 2. Decide how "not yet imported" is determined

**Status:** ⬜ Not started

`Import_Batch` records what was imported and `FileResource` (#251) records file content, so the answer
may already be derivable from existing state rather than needing a new column. Establish that before
adding anything.

### 3. Produce the notification

**Status:** ⬜ Not started

The sibling of #304's "source content changed upstream, consider reseeding", with the same producer
shape and the same `NotificationSeeding` dedupe helper. Identity has to survive a restart that
re-discovers the same files.

### 4. Correct `TryWriteAutoManifest`

**Status:** ⬜ Not started

It writes a manifest with no `duplicateResolution` for files it then does not import. Since #303 the
absent-policy default is `review` rather than `newest-wins`, so nothing is silently overwritten any
more — but a manifest written for content that is never read is still misleading, and whether it should
be written at all at that point depends on step 1's answer.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | TBD — every row below is named once steps 1 and 2 are decided | | |

**The test list is completed before implementation starts, not during it** — see
[#302's plan](302-clean-reseed-confirmation-notification-plan.md)'s deviation section for why.

---

## Note for whoever picks this up

#303's own T2 document
([20-pending-review-alert.md](../../automated-testing/import-and-staged-actions/20-pending-review-alert.md))
starts from a fresh container specifically because of this defect: against a populated database its
fixture does nothing at all. When this issue lands, that precondition can be relaxed — and the
relaxation is itself a check that the fix works.
