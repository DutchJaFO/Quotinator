# #55 — Record completeness flag

**Status:** Planning

**Tiers required:** T1, T2

**GitHub issue:** #55

**Depends on:** #64

**Connects to:** #56, #19, #48 (all different milestones or not yet started)

---

## Spec requirements (reconciled — see Scope changes)

The original issue predates #64 (conflict resolution) and #143 (migration ownership/baseline split); neither existed when it was written. Reconciled against the current codebase before planning:

1. New `IsComplete` column (`BIT NOT NULL DEFAULT 0`) on `Quotes`, `Sources`, `Characters`, `People`
2. New `NoValueKnown` column (`TEXT NOT NULL DEFAULT '[]'`, JSON array of field names) on all four tables
3. A brand-new row (first insert, whether via startup seeding or the `POST /api/v1/quotes/import` endpoint) always starts `IsComplete = false`, `NoValueKnown = []`, regardless of the source payload
4. **An existing row being rewritten by `newest-wins`/`merge-ours`/`merge-theirs`/`skip`/`review` (#64's conflict engine) must never reset `IsComplete`/`NoValueKnown`** — both columns are excluded entirely from the `UPDATE` path; only a genuinely new row gets the `false`/`[]` defaults
5. Enrichment providers skip records where `IsComplete = true` and skip individual fields listed in `NoValueKnown` (implementation deferred to #19)
6. Management UI actions ("Mark as complete", "Mark field as no value known") deferred to the Blazor import UI milestone (#11)
7. Stats/counts reporting deferred entirely to #48 (not yet built — see Scope changes)
8. **Database-only for now** — `IsComplete`/`NoValueKnown` are not added to `QuoteResponse` or any other public API response shape in this issue

---

## Steps

### 1. Schema migration
**Status:** ⬜ Not started

New `Migration006_RecordCompleteness` in `QuotinatorMigrations.cs` (next consumer migration number after #64's `Migration005_ImportBatchConflictPolicy`), following the established plain-`ALTER TABLE ADD COLUMN` pattern (no idempotency guard needed — the existing transaction+backup/restore safety net in `ApplyMigrationsAsync` covers a partial-failure case, per CLAUDE.md's migration policy):

```sql
ALTER TABLE Quotes     ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE Quotes     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
ALTER TABLE Sources    ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE Sources    ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
ALTER TABLE Characters ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE Characters ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
ALTER TABLE People     ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE People     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
```

Update `QuotinatorMigrations.Baseline` in the same commit (per CLAUDE.md's baseline-sync rule) — a fresh database must create these columns directly, not replay this migration. Confirm via `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues`, don't assume.

### 2. Update C# entity models
**Status:** ⬜ Not started

`QuoteEntity`, `Source`, `Character`, `Person` (all in `Quotinator.Engine.Entities`) each gain:
- `bool IsComplete { get; init; }`
- `IReadOnlyList<string> NoValueKnown { get; init; } = []`

`NoValueKnown` needs a Dapper type handler (`string[]`/`IReadOnlyList<string>` ↔ JSON TEXT column) — check whether the existing `SafeEnumHandler<TEnum>` infrastructure has a JSON-list equivalent already, or whether a new handler is needed, registered in `QuotinatorDapperConfiguration.RegisterDomainHandlers()`.

### 3. New-row defaults on every insert path
**Status:** ⬜ Not started

Both entity-creation paths need to write the defaults explicitly (or rely on the column `DEFAULT`, confirmed sufficient by a test — Dapper.Contrib's `InsertAsync` writes all mapped properties, so an explicit `false`/`[]` on the C# object achieves the same thing without depending on SQLite's own default):

- `QuoteSeedWriter.GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/`GetOrCreatePersonAsync` (startup seeding, shared with #45's live import per the #45 extraction)
- The main quote insert in both `QuotinatorDatabaseInitializer.SeedIfEmptyInternalAsync` and `SqliteQuoteImportService.ImportAsync`

### 4. Existing-row updates never touch these columns
**Status:** ⬜ Not started

`Sql.Quotes.UpdateOnNewestWins` and the equivalent Source/Character/Person `GetOrCreate*` "found existing" paths must not include `IsComplete`/`NoValueKnown` in their `SET`/write list — confirm by inspecting `Sql.cs`, not by assumption, since this is the one column-set every future modification to these tables must remember to keep excluding.

**New tests required** (both pipelines that can hit an existing row):
- `ConflictResolutionTests` — a quote already marked `IsComplete = true` survives a `newest-wins`/`merge-ours`/`merge-theirs` reseed unchanged
- `QuoteImportServiceTests` — same guarantee via the live import endpoint

### 5. Stats/counts
**Status:** N/A — deferred entirely to #48 (see Scope changes)

### 6. Enrichment hooks
**Status:** N/A — deferred to #19 (different milestone, not started)

### 7. Blazor UI hooks
**Status:** N/A — deferred to #11 (Blazor: Import UI milestone)

### 8. Tests
**Status:** ⬜ Not started — see step 4 for the two correctness-critical cases; also: schema migration + baseline drift, C# model round-trip (`NoValueKnown` JSON ↔ list), default values on a genuinely new insert.

---

## Scope changes

Reconciled 2026-07-05, before implementation — pending a comment on #55 recording the same:

- **Update-path preservation of `IsComplete`/`NoValueKnown` is a new, explicit requirement** not present in the original issue text — added because #64's conflict engine (which didn't exist when #55 was written) rewrites existing rows on every reseed/reimport, and silently resetting a human's completed review on every reseed would defeat the entire point of the feature. "Import always sets isComplete: false" is now understood to apply only to a row's first insert, never to an update of an existing row.
- **Stats/counts reporting is deferred to #48** (stats endpoint), which is still open and unstarted — #55 ships schema and model changes only, matching how enrichment (#19) and the Blazor UI (v3/#11) are already deferred in the original issue text.
- **No public API exposure in this issue** — `IsComplete`/`NoValueKnown` do not appear in `QuoteResponse` or any other response DTO; they are database-only until a management API/UI actually needs to read or write them.
- **`NoValueKnown` ships on all four tables**, including `Characters` (whose only field, `Name`, is required and has no candidate for "no value known" today) — kept for consistency and to avoid a later schema change if `Characters` ever gains a nullable field (e.g. a nickname/alias).

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `IsComplete`/`NoValueKnown` columns added to all four tables, baseline matches incremental replay | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` |
| 2 | ⬜ | A brand-new row defaults to `IsComplete = false`, `NoValueKnown = []` | Unit test | TBD at implementation time |
| 3 | ⬜ | An existing row's `IsComplete`/`NoValueKnown` survive `newest-wins`/`merge-ours`/`merge-theirs` via startup seeding | Unit test | New case in `ConflictResolutionTests` |
| 4 | ⬜ | Same guarantee via the live `POST /api/v1/quotes/import` endpoint | Unit test | New case in `QuoteImportServiceTests` |
| 5 | ⬜ | `NoValueKnown` round-trips correctly between `string[]`/`IReadOnlyList<string>` and its JSON TEXT column | Unit test | TBD at implementation time |
| 6 | N/A | Stats endpoint reports completeness counts | N/A | Deferred to #48 |
| 7 | N/A | Enrichment providers skip complete/no-value-known fields | N/A | Deferred to #19 |
| 8 | N/A | Management UI actions | N/A | Deferred to #11 |
| 9 | ⬜ | T1 — app starts in VS without error; migration applies cleanly | Live | Not yet run |
| 10 | ⬜ | T2 — Docker smoke test | Live | Not yet run |
