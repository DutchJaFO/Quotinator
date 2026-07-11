# #157 — Sql.cs mixes domain-specific SQL into domain-agnostic Quotinator.Data

**Status:** Waiting for release
**GitHub issue:** #157
**Tiers required:** T2
**Depends on:** #69

---

## Spec requirements (from the GitHub issue)

1. `Schema`, `Joins`, `Queries` (the `Widgets`/`Owners` example), `SystemAudit`, `SystemImportActions`, `SystemChangeLog` stay in `Quotinator.Data/Queries/Sql.cs`.
2. `Quotes`, `SearchField`, `QuoteGenres`, `QuoteTranslations`, `SourceTranslations`, `CharacterTranslations`, `Characters`, `People`, `Sources`, `Conversations`, `ConversationLines`, `StageDirections`, `StageDirectionTranslations`, `SoundCues`, `SoundCueTranslations`, `ImportBatches` move to a new `Quotinator.Engine/Queries/Sql.cs`.
3. All call sites updated across `Quotinator.Engine`, `Quotinator.Api`, and every test project.
4. `InternalsVisibleTo` on both projects' `.csproj` updated to only what each new `Sql` class actually needs.
5. `SqlQueryGuardTests` split so both `Sql` classes remain covered by the CVE-2025-6965 aggregate guard, with no loss of coverage.
6. ADR 004 amended to close the gap — explicitly state where `Sql.cs` lives post-Engine-split.
7. `docs/database-conventions.md`'s contradictory "put every SQL string in Data's Sql.cs" line corrected.

---

## Steps

### 1. Write the red test

**Status:** ✅ Done — `Quotinator.Data.Tests.Queries.SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries` confirmed red (22 actual vs. 6 expected nested types) before any file was moved.

New `Quotinator.Data.Tests` test `Sql_ContainsOnlyGenericInfrastructureQueries` — reflects over `Quotinator.Data.Queries.Sql`'s nested types via the same `BindingFlags.NonPublic | BindingFlags.Static` pattern `SqlQueryGuardTests.EnumerateSqlConstants` already uses, and asserts the nested-type-name set is exactly `{ Schema, Joins, Queries, SystemAudit, SystemImportActions, SystemChangeLog }`. Must be red before any file is moved.

### 2. Move domain-specific nested classes to `Quotinator.Engine/Queries/Sql.cs`

**Status:** ✅ Done. Also found and fixed a related violation while moving: `DatabaseInitializer.TruncateDataAsync` (in `Quotinator.Data`, the supposedly domain-agnostic base class) hardcoded calls to `Sql.Quotes.DeleteAll`, `Sql.Conversations.DeleteAll`, etc. directly — domain leakage baked into the base class's own code, not just into the `Sql` constants file. Moved `TruncateDataAsync` itself into `QuotinatorDatabaseInitializer` (`Quotinator.Engine`, its only caller) as a `private static` method; `DatabaseInitializer` no longer references any Quotinator-domain table by name.

Create `src/Quotinator.Engine/Queries/Sql.cs`, namespace `Quotinator.Engine.Queries`, `internal static class Sql`. Move (not duplicate) `Quotes`, `SearchField`, `QuoteGenres`, `QuoteTranslations`, `SourceTranslations`, `CharacterTranslations`, `Characters`, `People`, `Sources`, `Conversations`, `ConversationLines`, `StageDirections`, `StageDirectionTranslations`, `SoundCues`, `SoundCueTranslations`, `ImportBatches` verbatim, including all XML doc comments — the reasoning captured in those comments (e.g. the `ConversationLines`/`Conversations` join rationale, the `#59` reversal-reference-count remarks) is implementation history, not something to rewrite. `Quotinator.Data/Queries/Sql.cs` keeps only `Schema`, `Joins`, `Queries`, `SystemAudit`, `SystemImportActions`, `SystemChangeLog`.

### 3. Update call sites

**Status:** ✅ Done. Every production-code reference to a moved nested class (`Sql.Quotes.*`, `Sql.Characters.*`, `Sql.Conversations.*`, etc.) across `Quotinator.Engine` (`SqliteQuoteService.cs`, `QuotinatorDatabaseInitializer.cs`, `ImportActionPlanner.cs`, `SqliteImportActionService.cs`, `QuoteSeedWriter.cs`, `SqliteImportBatchRepository.cs`) swapped `using Quotinator.Data.Queries;` for `using Quotinator.Engine.Queries;` — none of these files had a remaining need for a Data-owned `Sql` member, so no alias was needed. One doc-comment `<see cref="Sql.SystemImportActions.SelectAllForBatch"/>` in `ImportActionPlanner.cs` was fully qualified to `Quotinator.Data.Queries.Sql.SystemImportActions.SelectAllForBatch` since it now points across the namespace split. `DatabaseInitializerTests.cs` (Engine.Tests) kept `using Quotinator.Data.Queries;` unchanged — its only `Sql.*` usage is `Sql.Schema.GetUserTables`, which stayed in Data.

### 4. Split `SqlQueryGuardTests`

**Status:** ✅ Done. `tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs` deleted. Replaced by:
- `tests/Quotinator.Data.Tests/Security/SqlQueryGuardTests.cs` — covers `Quotinator.Data.Queries.Sql` (`Schema`, `Joins`, `Queries`, `SystemAudit`, `SystemImportActions`, `SystemChangeLog`), including `SystemAudit`/`SystemImportActions`'s dynamic factory methods (previously untested by the guard — a coverage gain, not just a relocation) and the `IJoinStrategy<TResult>` reflection-based discovery test.
- `tests/Quotinator.Engine.Tests/Security/SqlQueryGuardTests.cs` — covers `Quotinator.Engine.Queries.Sql` (the domain set), with the `AssembledQueryCases`/`AggregateQueries_MatchDocumentedInventory` logic unchanged from the original, using `SqliteQuoteService.BuildFilterWhere` (internal, already visible to `Quotinator.Engine.Tests` via `Quotinator.Engine.csproj`'s own `InternalsVisibleTo`).

Every `AggregateQueries_MatchDocumentedInventory` documented-name entry moved to whichever new test class now owns that constant — no entry dropped or added. `ConflictResolutionTests.cs` (Engine.Tests) also needed its `using` swapped (`Sql.Quotes.UpdateOnNewestWins`).

### 5. Update `InternalsVisibleTo`

**Status:** ✅ Done. `Quotinator.Data.csproj`: removed the `Quotinator.Core.Tests` `InternalsVisibleTo` entry — confirmed via full rebuild that nothing in `Quotinator.Core.Tests` needs a `Quotinator.Data` internal member anymore (`SqlSourceScanTests.cs` only uses the public `SqlAggregateGuard`). `Quotinator.Engine.csproj` already granted `InternalsVisibleTo` to `Quotinator.Engine.Tests` (pre-existing) — sufficient for the new `Quotinator.Engine.Queries.Sql` to be reflectable; no change needed there. `Quotinator.Data.csproj`'s `Quotinator.Engine.Tests` entry was kept — `DatabaseInitializerTests.cs` still needs it for `Sql.Schema.GetUserTables`.

### 6. Amend ADR 004 and `database-conventions.md`

**Status:** ✅ Done. Added a "Revision — issue #157" subsection to ADR 004, matching the existing issue-#121 revision style, stating the split explicitly and covering the `TruncateDataAsync` finding from step 2. `docs/database-conventions.md`'s SQL-centralisation row and its "Quotinator.Data must stay domain-agnostic" section both updated to state the split and cross-reference each other.

### 7. Add new file to `Quotinator.slnx`

**Status:** ✅ Done — no action needed. `src/Quotinator.Engine/Queries/Sql.cs` and the three new/moved test files are all inside their respective project directories, so they're visible in Solution Explorer automatically through the project node (per CLAUDE.md's slnx rule: only files outside any project need explicit `<File>` entries). Confirmed no stale `<File>` reference to the deleted `tests/Quotinator.Core.Tests/Security/SqlQueryGuardTests.cs` exists in `Quotinator.slnx` (it was never listed there either, for the same reason). The plan doc itself (`157-sql-domain-engine-split-plan.md`) was already added to `Quotinator.slnx` at issue-filing time.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `Sql_ContainsOnlyGenericInfrastructureQueries` is red before the move, green after | Unit test | `Quotinator.Data.Tests.Queries.SqlBoundaryTests.Sql_ContainsOnlyGenericInfrastructureQueries` — red (22 vs. 6) before, green after |
| 2 | ✅ | `Quotinator.Data/Queries/Sql.cs` contains only `Schema`, `Joins`, `Queries`, `SystemAudit`, `SystemImportActions`, `SystemChangeLog` | Live | Manual file review — 6 nested classes confirmed via `grep -c "internal static class"` |
| 3 | ✅ | `Quotinator.Engine/Queries/Sql.cs` contains all 16 moved domain nested classes with their original XML docs intact | Live | Manual file review — 16 nested classes confirmed via `grep -c "internal static class"` |
| 4 | ✅ | No call site left referencing a moved class through the old `Quotinator.Data.Queries` namespace | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 5 | ✅ | CVE-2025-6965 aggregate guard coverage is unchanged — same constants and assembled-query cases covered, just relocated | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests` (54 tests) + `Quotinator.Engine.Tests.Security.SqlQueryGuardTests`, both full pass |
| 6 | ✅ | Full test suite green | Unit test | `dotnet test --configuration Release --verbosity normal` — 1,118/1,118 passed across all 9 test projects, 0 warnings, 0 errors |
| 7 | ✅ | ADR 004 and `database-conventions.md` no longer contradict each other on `Sql.cs` placement | Live | Manual doc review — ADR 004 "Revision — issue #157" section added; `database-conventions.md`'s SQL-centralisation and domain-agnostic sections both updated |
| 8 | ✅ | Docker build succeeds | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; container smoke test confirmed `/api/v1/version` healthy, `POST /api/v1/admin/database/reseed` (exercises the relocated `TruncateDataAsync`) returned `200` with correct counts (796 quotes, 479 sources, 7 characters, 45 duplicates), and a conversation-bearing quote's `conversations` field was intact post-reseed with no errors in container logs |

---

## Notes

T1 not required — this is a pure code-organisation change with no schema, DI registration, or Razor-visible surface affected; the existing T1 confirmation already covers everything this touches functionally.
