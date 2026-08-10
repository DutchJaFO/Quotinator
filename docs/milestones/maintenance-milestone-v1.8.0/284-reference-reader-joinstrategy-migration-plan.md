# #284 — Migrate masterdata reference readers to JoinQueryRepository/IJoinStrategy, add missing integration tests

**Status:** In progress
**GitHub issue:** #284
**Tiers required:** T1, T2
**Depends on:** none (isolated to the 3 reference readers' internal implementation)

---

## Spec requirements

1. `SourceSeriesReferenceReader`, `CharacterSourceLinkReader`, `SeriesUniverseReferenceReader` execute
   their SQL via `JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>` instead of opening their own
   `IDbConnectionFactory` connection and calling Dapper directly — per
   [ADR 017](../architecture-decisions/017-join-capable-reads-use-joinqueryrepository.md).
2. Public interfaces (`ISourceSeriesReferenceReader`, `ICharacterSourceLinkReader`,
   `ISeriesUniverseReferenceReader`) and their return shapes (single tuple, batched
   `IReadOnlyDictionary`) are unchanged — this is an internal implementation swap only.
3. No SQL changes — reuse the existing 6 `Sql.cs` query strings as-is.
4. Add real-SQLite integration tests for all 3 readers (currently absent — only fakes exist for the
   API layer), covering single lookup found/not found and batched lookup with 0/1/many ids including
   a soft-deleted referenced row.

---

## Background — why this issue exists

Per ADR 017 (from #282's research), adopting `JoinQueryRepository`/`IJoinStrategy` for a join-capable
read is worth it even without an immediate capability gain — consistency and discoverability is the
justification. These 3 readers are the concrete case ADR 017's decision applies to.

**Verified before starting:**

- Confirmed via `git log --follow --diff-filter=A`: `JoinQueryRepository`/`IJoinStrategy` (#74,
  2026-06-28) predates all 3 readers (#184/#185/#187, 2026-07-18 and after) by at least 20 days —
  the pattern existed and was available when these readers were written.
- Confirmed via `grep` across `tests/Quotinator.Core.Tests/`: none of the 3 readers has a real-SQLite
  integration test today — only fakes (`FakeSourceSeriesReferenceReader`, etc.) used by
  `Quotinator.Api.Tests`' endpoint-layer tests. `ConversationLineCountReaderTests.cs` (the one sibling
  reader that does have a real-SQLite test) found two genuine bugs this way — a Dapper type-mapping
  bug and a case-sensitivity bug on an `IN` clause — that no fake-backed test could have caught.
- Confirmed the 6 existing `Sql.cs` queries need no changes: `CharacterSources.
  SelectSourceReferencesForCharacter`/`SelectSourceReferencesForCharacters`, `Sources.
  SelectSeriesReferenceForSource`/`SelectSeriesReferencesForSources`, `Series.
  SelectUniverseReferenceForSeries`/`SelectUniverseReferencesForSeries`.

---

## Approach

**Read-model POCOs and strategy classes live in `Quotinator.Core/Queries/`** (namespace
`Quotinator.Core.Queries`, matching `Sql.cs`'s own namespace already in that folder) — the Core-specific
mirror of how `Quotinator.Data`'s own canonical example (`WidgetWithOwner`/`WidgetWithOwnerStrategy`)
is laid out, since `IJoinStrategy<TResult>`/`JoinQueryRepository<TResult>` themselves are generic,
domain-agnostic infrastructure in `Quotinator.Data`, but these 6 implementations are Core-specific
(reference `Character`/`Source`/`Series`/`Universe`).

**POCOs are promoted from each reader's existing private nested records, keeping their exact existing
names** (made `public`, one type per file, per this project's established one-class-per-file
convention) — no new naming invented:

| Reader | Existing private name → new public file | Shape |
|---|---|---|
| `CharacterSourceLinkReader` | `SourceRow` → `Quotinator.Core/Queries/SourceRow.cs` | `(Guid Id, string Title)` |
| `CharacterSourceLinkReader` | `LinkRow` → `Quotinator.Core/Queries/LinkRow.cs` | `(Guid CharacterId, Guid SourceId, string SourceTitle)` |
| `SourceSeriesReferenceReader` | `SeriesReferenceRow` → `Quotinator.Core/Queries/SeriesReferenceRow.cs` | `(Guid Id, string Name)` |
| `SourceSeriesReferenceReader` | `SourceSeriesReferenceRow` → `Quotinator.Core/Queries/SourceSeriesReferenceRow.cs` | `(Guid SourceId, Guid SeriesId, string SeriesName)` |
| `SeriesUniverseReferenceReader` | `UniverseReferenceRow` → `Quotinator.Core/Queries/UniverseReferenceRow.cs` | `(Guid Id, string Name)` |
| `SeriesUniverseReferenceReader` | `SeriesUniverseReferenceRow` → `Quotinator.Core/Queries/SeriesUniverseReferenceRow.cs` | `(Guid SeriesId, Guid UniverseId, string UniverseName)` |

**One `IJoinStrategy<TResult>` implementation per POCO** (6 total, one file each, per
`docs/data-access.md`'s pattern — a strategy's `BuildSql()` takes no parameters, so a single-lookup
query and its batched counterpart are necessarily two different strategies, not one):
`CharacterSourceReferenceStrategy` (wraps `SelectSourceReferencesForCharacter` → `SourceRow`),
`CharacterSourceReferencesBatchStrategy` (wraps `SelectSourceReferencesForCharacters` → `LinkRow`),
`SourceSeriesReferenceStrategy` (→ `SeriesReferenceRow`), `SourceSeriesReferencesBatchStrategy` (→
`SourceSeriesReferenceRow`), `SeriesUniverseReferenceStrategy` (→ `UniverseReferenceRow`),
`SeriesUniverseReferencesBatchStrategy` (→ `SeriesUniverseReferenceRow`).

**DI registration** (`Program.cs`, alongside the existing reader registrations): one
`services.AddSingleton<IJoinStrategy<TResult>, TStrategy>()` + one
`services.AddSingleton<JoinQueryRepository<TResult>>()` pair per POCO, per `docs/data-access.md` step
5. The existing per-reader comments ("the generic `IListableRepository<T>`/`IRepository<T>` above
cannot express this join") are updated to point at ADR 017 instead, since that reasoning is no longer
the operative one (adopting `JoinQueryRepository` even without a capability gain is now the stated
rule).

**Reader classes** inject the matching `JoinQueryRepository<TResult>` instances (constructor
parameters, replacing `IDbConnectionFactory`) and call `.QueryAsync(parameters)` instead of opening
their own connection. Batched methods keep their own `.ToDictionary()`/`.GroupBy()` shaping — that
logic doesn't move, only the query-execution mechanism underneath it.

**Tests**: new `Quotinator.Core.Tests/Repositories/{Reader}Tests.cs` per reader, following
`ConversationLineCountReaderTests.cs`'s fixture pattern (`SqliteConnectionFactory` against a real
temp-file SQLite database via a hand-rolled `CREATE TABLE`, not `TempDatabase`'s full schema, to keep
each test file scoped to only the tables its reader touches).

---

## Files touched

- `src/Quotinator.Core/Queries/` — 6 new POCO files, 6 new strategy files.
- `src/Quotinator.Core/Repositories/SourceSeriesReferenceReader.cs`,
  `CharacterSourceLinkReader.cs`, `SeriesUniverseReferenceReader.cs` — internal implementation swap.
- `src/Quotinator.Api/Program.cs` — 6 new DI registration pairs, 3 updated comments.
- `tests/Quotinator.Core.Tests/Repositories/` — 3 new test files.
- `Quotinator.slnx` — new source files added.

---

## Steps

### 1. Add the 6 POCOs and 6 strategy classes
**Status:** ✅ Done — `Quotinator.Core/Queries/`: `SeriesReferenceRow`, `SourceSeriesReferenceRow`,
`SourceRow`, `LinkRow`, `UniverseReferenceRow`, `SeriesUniverseReferenceRow` (promoted from each
reader's existing private nested records, exact names preserved) and their 6 matching
`IJoinStrategy<TResult>` implementations, each wrapping the existing `Sql.cs` query string unchanged.

### 2. Update the 3 readers and Program.cs DI registrations
**Status:** ✅ Done — each reader now takes two `JoinQueryRepository<TResult>` constructor
parameters instead of `IDbConnectionFactory`, calling `.QueryAsync(parameters)` instead of opening
its own connection. `Program.cs` registers 6 new `IJoinStrategy`/`JoinQueryRepository` pairs and
updates each reader's own registration comment to point at ADR 017. `ConversationLineCountReader`'s
registration comment updated too (drive-by, same region) to state its ADR 017 exemption explicitly,
since the comment it previously cross-referenced ("same reasoning as ISourceSeriesReferenceReader
above") no longer applies once that reasoning changed.

### 3. Write the integration tests
**Status:** ✅ Done — one real-SQLite test file per reader (`SourceSeriesReferenceReaderTests`,
`CharacterSourceLinkReaderTests`, `SeriesUniverseReferenceReaderTests`), 15 tests total, following
`ConversationLineCountReaderTests.cs`'s fixture pattern. **Not a strict red-before-implementation
sequence**: the new tests construct each reader via its new `JoinQueryRepository<TResult>`-based
constructor, which didn't exist before Step 2 — so they could not compile, let alone run, against the
pre-migration code. Implementation and tests were developed together for this reason, not
red-then-green in the traditional sense. Once compiled, all 15 passed on first run — unlike
`ConversationLineCountReaderTests`' own precedent, no bug was found in the pre-existing SQL for any
of the 3 readers.

### 4. Verify
**Status:** ✅ Done — full solution build (0 warnings/0 errors) and test suite green: 1460/1460 in
`Quotinator.Core.Tests` (up from 1445 — exactly the 15 new tests), 3299 total across the solution
(up from 3284 pre-#284), 0 failures.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `SourceSeriesReferenceReader` executes via `JoinQueryRepository`, same public behaviour | Unit test | `SourceSeriesReferenceReaderTests` (5 tests) |
| 2 | ✅ | `CharacterSourceLinkReader` executes via `JoinQueryRepository`, same public behaviour | Unit test | `CharacterSourceLinkReaderTests` (5 tests) |
| 3 | ✅ | `SeriesUniverseReferenceReader` executes via `JoinQueryRepository`, same public behaviour | Unit test | `SeriesUniverseReferenceReaderTests` (5 tests) |
| 4 | ✅ | No regression | Build + test | `dotnet build --configuration Release` — 0/0; `dotnet test --configuration Release` — 3299/3299 passed |
| 5 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 6 | ✅ | T2 — live container's masterdata endpoints still resolve Series/Source/Universe references correctly | Live (T2) | `docker build` clean; fresh-seeded container's `/masterdata/sources`, `/masterdata/characters`, `/masterdata/series` all correctly resolve their reference (e.g. "The Empire Strikes Back" → Series "Original Trilogy"; "Darth Vader" → Source "The Empire Strikes Back"; "Sean Connery Era" → Universe "James Bond") |

---

## Notes

**No true red-before-implementation cycle for the new tests, and that's expected, not a shortcut.**
Each new test file constructs its reader via the new `JoinQueryRepository<TResult>`-based
constructor, which didn't exist until Step 2 landed — so the tests could not compile against the
pre-migration code, let alone fail meaningfully. Implementation and tests were built together for
this reason. Once compiled, all 15 passed immediately — unlike `ConversationLineCountReaderTests`'
own precedent (which found two real bugs), no bug was found in any of the 3 readers' pre-existing
SQL. This is a genuine, honest outcome for a coverage-adding refactor issue, not a process shortcut.
