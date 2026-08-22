# #305 — Database integrity check: verify all expected tables exist at startup, not just row counts

**Status:** Planning
**GitHub issue:** #305
**Tiers required:** T1, T2
**Depends on:** none

---

## Description

`QuotinatorDatabaseInitializer.HasPendingContentSeedAsync` decides whether content-seed has pending
work by checking `Quotes`/`Genres` row counts alone. It never verifies that every table the current
schema version implies should exist actually exists on disk. The app can therefore report itself
fully healthy — "schema is up to date", real quote counts, "Quotinator ready" — while a table is
silently missing, and the problem only surfaces downstream at whichever call site queries it first.

This is the class of problem `CLAUDE.md`'s migration policy already treats as a hard failure: *a
database whose recorded schema version doesn't match its actual on-disk schema is a hard failure, not
a self-heal.* Nothing at startup enforces that today.

**Observed twice.** First during #293's T1 verification (2026-08-12) by renaming
`System_Notification` to simulate the condition. Then again, unsimulated, on a developer's own local
database in the same session: startup logged `schema is up to date (data v3, app v5)` and `Quotinator
ready`, and the next operation touching `System_Notification` threw `SQLite Error 1: 'no such table'`
repeatedly. The only visible symptom was an unhandled exception logged by the runtime itself, plus a
`WRN` from `NotificationSeeding`'s non-fatal catch.

**This plan needs refining before it can be executed.** Step 2 is a design decision the issue itself
defers to planning, and the shape of the tests in step 4 depends on its answer.

---

## Steps

### 1. Derive the expected table set from the schema version

**Status:** ⬜ Not started

Every table the current schema version should have created, across both the Data-owned and
Consumer-owned sides of the existing `System_SchemaVersion`/`System_ConsumerSchemaVersion` split.

The obvious source is the migration and baseline SQL those versions correspond to. Whatever the
mechanism, it must not become a hand-maintained list that drifts from the migrations — that failure
mode is the same one the schema-drift parity tests already exist to prevent.

### 2. Decide where the check runs and what failure does

**Status:** ⬜ Not started — **design decision, blocks steps 3–4**

Two questions, both open in the issue:

- **Where:** extend `HasPendingContentSeedAsync`, or add a dedicated pre-flight step. The former is
  where the inadequate check lives today; the latter separates "is content seeded" from "is the schema
  intact", which are different questions that happen to share a call site.
- **On failure:** most likely the same backup/degrade/never-self-heal treatment `CLAUDE.md`'s existing
  schema-version-mismatch policy already mandates for `InitialiseAsync`. Confirm against that policy
  rather than inventing a third behaviour — and note it must not become exception-based recovery, which
  the same policy forbids.

Since #326, a startup failure degrades rather than terminating. Whatever this check does on failure has
to reach that path, not a new one.

### 3. Report which tables are missing, not that something is wrong

**Status:** ⬜ Not started

Name the specific table(s) missing or unexpected. The current downstream symptom —
`NotificationSeeding`'s non-fatal catch logging a raw exception with no guidance — is precisely the
diagnostic quality this issue exists to replace.

Per `docs/logging.md`, the message carries the `[Subsystem - Phase]` prefix. It ships without a
Knowledgebase code: #333 is v1.9.0 and retrofits codes onto every message predating it, the same
sequencing #326 followed.

### 4. Write the tests named in the verification table, red first

**Status:** ⬜ Not started

Their exact shape depends on step 2's answer. A real-SQLite integration test per
`docs/testing-policy.md`, not a fake — the whole point is what the on-disk schema actually contains.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Every table implied by the current schema version is verified to exist on disk, both Data-owned and Consumer-owned | Unit test | `DatabaseIntegrityCheckTests.AllExpectedTablesPresent_PassesCleanly` |
| 2 | ❌ | A missing expected table is detected as a schema inconsistency and treated as a hard failure | Unit test | `DatabaseIntegrityCheckTests.MissingExpectedTable_DetectedAsSchemaInconsistency_TreatedAsHardFailure` |
| 3 | ❌ | Failure reaches the existing degraded-startup path rather than terminating the process | Unit test | Extends `StartupResilienceTests`' contract — process alive, `/health` unhealthy, OpenAPI reachable |
| 4 | ❌ | The diagnostic names the specific missing table(s) | Unit test | Assert the message contains the table name, not a generic failure string |
| 5 | ❌ | The expected-table set cannot drift from the migrations without a test failing | Unit test | TBD — named once step 1's mechanism is chosen |
| 6 | ❌ | A live database with a missing table reports the fault instead of claiming ready | Live | T1 + T2: rename a table, restart, confirm the startup output names it |
