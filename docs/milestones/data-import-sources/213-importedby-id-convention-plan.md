# #213 — Rename ImportBatch.ImportedBy to ImportedById for guard/convention compliance

**Status:** Waiting for release
**GitHub issue:** #213
**Tiers required:** T1, T2
**Depends on:** #212

---

## Spec requirements

1. Rename the `ImportBatches.ImportedBy` column to `ImportedById` via a new, single-statement,
   append-only migration (`Migration010`), applied after `QuotinatorMigrations.All`'s existing nine
   entries, per this project's migration rules (never reorder/edit an existing migration; one schema
   change per migration; idempotent where SQLite allows it).
2. Rename `Quotinator.Data.Entities.ImportBatch.ImportedBy` (`string?`) to `ImportedById`, matching the
   renamed column 1:1 — this codebase has no `[Column]`-style property/column remapping anywhere, so
   the C# property name and the SQL column name must stay identical.
3. Update `QuotinatorMigrations.BaselineSchema` to create the column as `ImportedById` directly, so a
   genuinely fresh database's baseline path and an upgraded database's incremental-replay path produce
   identical schemas (enforced by the existing `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`
   test — see Background).
4. **Owned by #212, automatic here:** #212 rewrites `Sql.ImportBatches.SelectAll`/`SelectByType` from
   `SELECT *` to a column list built by reflecting over `ImportBatch`'s own properties
   (`ReflectedColumnMetadata.For(typeof(ImportBatch))`), not a hand-typed list — so once #212 lands,
   this issue's rename is picked up automatically the moment the property is renamed (Spec requirement
   2), with **zero further code change** to `Sql.ImportBatches`. This is still required for the rename
   to actually achieve guard coverage for these two queries (a bare rename alone is not sufficient while
   they used `SELECT *`) — the dependency on #212 stays; only the "add one line" work it used to imply
   is gone. See Background's "`SELECT *` bypasses both guards entirely" finding and Notes for the full
   ownership split.
5. Update the one existing test that hard-codes the literal string `"ImportedBy"`
   (`ImportBatchesTests.Schema_ImportBatchesTable_HasAllRequiredColumns`) to `"ImportedById"`.
6. Update every existing test assertion that hard-codes the pre-#213 consumer schema version (`9`) to
   `10` — found live via grep, listed exhaustively in Background/Steps, not left to be discovered by a
   failing build.
7. Add the tests described in the Verification checklist, proving: the migration renames the column and
   preserves any pre-existing value; the rewritten `Sql.ImportBatches` queries and the generic
   `SqliteRepository<ImportBatch>` path both present a deliberately mixed-case `ImportedById` value in
   canonical lowercase; the existing reflection-driven `SqlQueryGuardTests` (`Quotinator.Data.Tests`)
   automatically exercise the rewritten queries and pass.

---

## Background — why this issue exists

### Current state (verified against code, not assumed from the issue text)

`Quotinator.Data.Entities.ImportBatch.ImportedBy` (`src/Quotinator.Data/Entities/ImportBatch.cs:24`):

```csharp
/// <summary>UUID of the user who triggered the import. Null for seeded batches.</summary>
public string? ImportedBy { get; init; }
```

Confirmed exactly as the issue states: `string?`, documented as holding a UUID, no `[Column]` remapping.

**Grep confirms the issue's "zero assignments, zero WHERE-clause references" claim, with one addition
the issue didn't mention:** searching all of `src/` for `ImportedBy` finds only:
- The property declaration itself (`ImportBatch.cs:24`).
- Three column-list appearances inside migration DDL/copy-forward SQL in
  `src/Quotinator.Core/Database/QuotinatorMigrations.cs` (lines 155, 168, 175, 195, 203, 204, 499) —
  `CREATE TABLE`/`INSERT INTO ... SELECT ...` column lists that carry the column through schema
  changes; none of these assign a real value (the two `INSERT` statements pre-seed rows with
  `ImportedBy = NULL` explicitly) or compare it.
- Four appearances in already-published changelog prose (`changelog.en.json`, `.nl.json`, `.de.json`,
  and the generated `changelog.json`) describing the `ImportBatches` table's column list as it existed
  at the time of a past release. These are historical release notes and must **not** be edited by this
  issue — see Approach.

No construction site ever sets `ImportedBy` — `SqliteQuoteImportService.cs:65` and
`QuotinatorDatabaseInitializer.cs:400` both build `new ImportBatch { ... }` object initializers that
omit it, leaving it at its default `null`. Confirms the issue's "currently inert" claim exactly:
zero writes, zero comparisons, anywhere in `src/`.

### How the two guards actually infer "is this an id column" (read in full before this issue's fix)

Both `SqlIdCaseGuard` (`src/Quotinator.Data/Diagnostics/SqlIdCaseGuard.cs`) and
`SqlSelectPresentationGuard` (`src/Quotinator.Data/Diagnostics/SqlSelectPresentationGuard.cs`) are
**regex-based, suffix-driven scanners with no maintained "these are the id columns" registry.** Every
pattern in both classes hard-codes `\w*Id` (case-insensitive) as the shape of an id-column reference —
e.g. `SqlSelectPresentationGuard`'s `UnwrappedIdColumnPattern`:
`(?:(?:\[\w+\]|\w+)\.)?(?:\[(?<col>\w*Id)\]|(?<col>\w*Id))`. A column whose name does not end in the
literal two characters `Id` is structurally invisible to either guard, regardless of what it actually
holds. `ImportedBy` ends in `By`, not `Id`, so today it can never be flagged by either guard — not
because of an oversight in a maintained list, but because it doesn't match the shape both guards look
for at all.

`SqlSelectPresentationGuard.ExemptColumnNames` (`SqlSelectPresentationGuard.cs:48`) is the **only**
list-based mechanism either guard has, and it works in the opposite direction from what this issue
needs: it **excludes** a column that *does* end in `Id` (`InitiatedById`) from being treated as an id,
because it isn't always one. This issue needs the reverse — force a column that does *not* end in `Id`
to be treated as one. No such inclusion mechanism exists in either guard today; building one would mean
modifying the regex patterns themselves (adding a literal alternation for one specific column name) in
both `SqlIdCaseGuard.cs` and `SqlSelectPresentationGuard.cs`, not adding one entry to an existing list —
see Approach for why this makes Option 2 markedly more invasive than it first appears.

`Quotinator.Data.Repositories.ReflectedColumnMetadata` (`src/Quotinator.Data/Repositories/EntityColumnMetadata.cs:58-60`)
uses the identical suffix rule for the generic-repository path:
`ValidColumnNames.Where(name => name.EndsWith("Id", StringComparison.Ordinal))`. `IEntityColumnMetadata`
is an interface specifically so a non-conforming entity could supply a custom implementation instead
(`EntityColumnMetadata.cs:14-20`), but no such override exists anywhere in the codebase today — adding
one just for `ImportedBy` would be the first, introducing a bespoke exception into infrastructure that
is otherwise uniform across every entity.

### Migration mechanics (so Option 1's cost is measured, not assumed)

`QuotinatorMigrations.All` (`src/Quotinator.Core/Database/QuotinatorMigrations.cs:18-29`) currently has
nine entries (`Migration001`…`Migration009`), each a `private const string`, applied in order and
tracked in `System_ConsumerSchemaVersion` independently of `Quotinator.Data`'s own migration counter
(`DataSchemaVersion`, currently at 10 — an unrelated counter, already confirmed unaffected by this
issue's change). `docs/database-conventions.md:80` confirms: *"SQLite has no idempotent form for `ALTER
TABLE ... RENAME` or `ADD COLUMN`"* — this project already accepts non-idempotent, single-shot `ALTER
TABLE ... ADD COLUMN` statements as normal (migrations 005, 006, 007 all do this with no `IF NOT
EXISTS` equivalent), so a non-idempotent `ALTER TABLE ... RENAME COLUMN` is consistent with existing,
already-accepted precedent, not a new risk category.

`ALTER TABLE ... RENAME COLUMN ... TO ...` is a single, atomic SQLite statement (supported since SQLite
3.25.0, 2018-09-15) — it is **not** the same operation as `Migration004_ImportBatchTypeUserSeed`'s
create-copy-drop-rename dance (`QuotinatorMigrations.cs:188-210`), which was only necessary because
SQLite cannot `ALTER` a `CHECK` constraint. `ImportedBy`/`ImportedById` carries no `CHECK` constraint, no
index, and no `UNIQUE`/`REFERENCES` clause, so the full table-rebuild pattern is unnecessary here — a
plain `RENAME COLUMN` preserves the column's type, nullability, and any existing (currently always
`NULL`) data automatically. No prior migration in this codebase has used `RENAME COLUMN` — this would be
the first — but it is a strictly simpler operation than the `RENAME TO` (table rename) already used in
Migration004's rebuild, which this project's own migration rules already sanction.

### `SELECT *` bypasses both guards entirely — a finding beyond the issue's literal text

`Sql.ImportBatches.SelectAll`/`SelectByType` (`src/Quotinator.Data/Queries/Sql.cs:122-126`) are:

```csharp
internal const string SelectAll =
    "SELECT * FROM ImportBatches WHERE IsDeleted = 0 ORDER BY ImportedAt DESC, ROWID DESC;";

internal const string SelectByType =
    "SELECT * FROM ImportBatches WHERE IsDeleted = 0 AND Type = @type ORDER BY ImportedAt DESC, ROWID DESC;";
```

Both guards work by scanning the literal text of a query's `SELECT ... FROM` clause for column-name-
shaped tokens. `SELECT *` contains no column names at all — there is nothing for `\w*Id` to match
against. This means that **even after renaming to `ImportedById`, these two specific queries would
still silently pass both guards** — not because the column is protected, but because the guard cannot
see it. `ImportBatch.Id` (the primary key) is `Guid`-typed and gets free, correct lowercase presentation
from `System.Text.Json`'s own default `Guid` formatting regardless of this gap (per ADR 012's own
documented reasoning for why that safety net exists) — but `ImportedById` is `string?`-typed and has no
such safety net; a row written with a non-canonically-cased value would render exactly as stored,
forever, through these two queries specifically.

`SqliteImportBatchRepository.GetAllAsync`/`GetByTypeAsync` (`src/Quotinator.Data/Repositories/SqliteImportBatchRepository.cs:18-46`)
are the only call sites for these two queries, and both are live (`SqliteImportActionService.cs:352`
calls `GetAllAsync` in the reversal LIFO-stack check). By contrast, the **generic**
`SqliteRepository<ImportBatch>.GetByIdAsync` (used live at `SqliteImportActionService.cs:342`,
`SqliteQuoteImportService.cs:143`) goes through `RepositorySql`'s explicit-column-list builder and
`ReflectedColumnMetadata`, and would automatically wrap `ImportedById` the moment the rename lands, with
zero further code change — this is the "covered the moment it's ever used" benefit the issue's own title
describes, and it is real for that one path. It is not real for `Sql.ImportBatches.SelectAll`/
`SelectByType` unless those two queries are also rewritten away from `SELECT *`.

**This exact rewrite is #212's entire scope, filed independently from the same #207 audit before this
issue's own investigation reached the same query.** Rather than duplicating #212's work here, this issue
depends on #212 landing first (see header). #212 was itself revised during review (2026-07-23) to build
`SelectColumns` by reflecting over `ImportBatch`'s own properties (`ReflectedColumnMetadata.For(typeof(ImportBatch))`)
instead of listing them by hand — the same mechanism `SqliteRepository<ImportBatch>.GetByIdAsync`
already uses (previous paragraph). Once #212 lands on that basis, this issue's rename requires **zero**
further code change to `Sql.ImportBatches` — no one-line addition, no manual sync at all. See Notes for
the full ownership split.

### Existing tests that will break or need updating (found via grep, not left implicit)

- `tests/Quotinator.Core.Tests/Repositories/ImportBatchesTests.cs:111` — `Schema_ImportBatchesTable_HasAllRequiredColumns`'s
  `expected` array contains the literal string `"ImportedBy"`.
- `tests/Quotinator.Core.Tests/Repositories/ImportBatchesTests.cs:159` — `Schema_MigrationVersion_IsBumped`
  asserts `Assert.AreEqual(9, db.SchemaVersion, "SchemaVersion should be 9 after Migration009");`.
- `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs:616` — `Assert.AreEqual(9, db3.SchemaVersion, ...)`.
- `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs:916` — `Assert.AreEqual(9, db.SchemaVersion);`.
- `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs:943` — `Assert.AreEqual(9, db2.SchemaVersion, "All six remaining App migrations (4, 5, 6, 7, 8, and 9) should have replayed");`
  — both the number and the parenthetical migration list need updating (becomes 4–10, seven migrations).
- `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs:988` — `Assert.AreEqual(9, db3.SchemaVersion, "An explicit Reset must fully resolve the mismatch");`.

`tests/Quotinator.Data.Tests/Database/DatabaseInitializerOwnershipTests.cs:300,335` reference version
`9`/`10` too, but those are `Quotinator.Data`'s **own** `DataSchemaVersion` counter (a `System_`-prefixed
migration history entirely separate from `QuotinatorMigrations`) — confirmed unrelated and left
untouched.

`Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`/`...AcceptSameCheckConstraintValues`
(`DatabaseInitializerTests.cs:684`, `:714`) contain no hardcoded version numbers or column lists — they
diff the two paths' actual schemas at runtime, so they need no manual update and will fail automatically
if `BaselineSchema` and `Migration010` ever disagree. These are cited as existing regression coverage in
the Verification checklist rather than duplicated with new test code.

---

## Approach

**Decision: Option 1 — rename the column and property to `ImportedById`.** Option 2 (an inclusion-list
override) is rejected for this issue. Reasoning:

1. **Option 2 is not "add one entry to an existing list."** `SqlSelectPresentationGuard.ExemptColumnNames`
   is the only list either guard has, and it excludes — the opposite of what's needed. Neither guard
   has any inclusion mechanism today; building one means editing the regex patterns in both
   `SqlIdCaseGuard.cs` and `SqlSelectPresentationGuard.cs` to special-case one literal, non-conforming
   column name, plus a matching override in `ReflectedColumnMetadata`/`IEntityColumnMetadata` for the
   generic-repository path (a third mechanism, since that path doesn't use either guard's regexes at
   all). That is new, permanent, three-place exception machinery for a column that has no actual reason
   to be named differently from every other id column in this codebase — unlike `InitiatedById`, which
   is genuinely polymorphic and has no other option, `ImportedBy` always and only ever holds a UUID (or
   `null`) per its own doc comment. There is no legitimate non-id value this column could hold that a
   rename would break.
2. **The rename's actual cost is low and this is the cheapest time to pay it.** The column is 100%
   unused today (Background, confirmed via grep) — no production data to migrate meaningfully (existing
   rows are `NULL`), no API surface exposes it (no `GET /import-batches` listing endpoint exists), and
   the fix is a single atomic `ALTER TABLE ... RENAME COLUMN` statement, not a full table rebuild.
   Deferring the rename until after v1's "no auth" restriction lifts (CLAUDE.md's "What NOT to do") and
   a real write path starts populating this column would only make the same rename more expensive later
   (real data to preserve, more call sites to touch, more risk).
3. **A rename produces permanent, structural coverage with zero exception machinery**, matching ADR
   012's own stated design principle — "every mechanism above is applied going forward... never as a
   data migration or retroactive re-casing pass" — and CLAUDE.md's Simplicity priority. Every future
   query or repository method that ever touches `ImportedById` is automatically covered by both guards
   and by `ReflectedColumnMetadata`, the same as every other id column in this codebase, with nothing
   to remember and nothing to keep in sync.

### Concrete changes

**1. New migration**, appended to `QuotinatorMigrations.All` as entry 10:

```csharp
new SchemaMigration { Version = 10, Sql = Migration010_RenameImportBatchImportedById },
```

```csharp
// Renames ImportBatches.ImportedBy to ImportedById (#213) — the column always held a UUID (or NULL)
// but its name didn't carry the *Id suffix every id-casing guard (SqlIdCaseGuard,
// SqlSelectPresentationGuard, ReflectedColumnMetadata) relies on to find id/FK columns by name. Single
// atomic RENAME COLUMN — no CHECK/UNIQUE/REFERENCES on this column, so the full create-copy-drop-rename
// table rebuild Migration004 needed for a CHECK-constraint change is unnecessary here. Column is
// unused in v1 (no auth/user management — CLAUDE.md's "What NOT to do"), so every pre-existing row's
// value is NULL and survives the rename unchanged.
private const string Migration010_RenameImportBatchImportedById = """
    ALTER TABLE ImportBatches RENAME COLUMN ImportedBy TO ImportedById;
    """;
```

**2. `QuotinatorMigrations.BaselineSchema`** (`QuotinatorMigrations.cs:492` onward): change the
`ImportBatches` `CREATE TABLE` block's `ImportedBy   TEXT,` (line 499) to `ImportedById TEXT,`, and
extend the baseline's own explanatory comment (lines 470-491) to note Migration010 is folded in
directly, the same way it already documents migrations 004-009's inclusion.

**3. Entity rename** — `src/Quotinator.Data/Entities/ImportBatch.cs:24`:

```csharp
/// <summary>UUID of the user who triggered the import. Null for seeded batches.</summary>
public string? ImportedById { get; init; }
```

**4. `Sql.ImportBatches` column list — owned by #212, nothing further needed here.** #212 rewrites
`SelectAll`/`SelectByType` (`src/Quotinator.Data/Queries/Sql.cs:115-133`) to build their column list via
`RepositorySql.BuildSelectColumns(ReflectedColumnMetadata.For(typeof(ImportBatch)))` — reflection over
`ImportBatch`'s actual properties, not a hand-typed string. Once #212 lands, this issue's Step 4 rename
(`ImportedBy` → `ImportedById`) is picked up automatically the next time that reflection runs (it's
evaluated once per process, at static-field initialization) — no edit to `Sql.ImportBatches` at all.

If this issue is implemented before #212 for any reason, it must instead perform #212's full
reflection-based rewrite itself (see #212's own Approach for the exact code) rather than hand-typing a
column list that would immediately become exactly the kind of manual-sync burden #212 exists to avoid —
in that case #212, if still open, becomes "already done, confirm and close" rather than net-new work.
Sequencing #212 first (this issue's header `Depends on`) avoids that duplication in the common case.

`DeleteAll` and `UpdateRecordCount` are untouched — neither selects columns.

**5. `ReflectedColumnMetadata`/`SqliteRepository<ImportBatch>` generic path** — no code change needed;
`ImportedById` is automatically included in `IdColumnNames` the moment the property is renamed, since
the inference is pure `EndsWith("Id")` reflection (`EntityColumnMetadata.cs:58-60`). This is the same
mechanism point 4 above now also relies on for `Sql.ImportBatches` — both read paths share one source of
truth for "which columns exist and which are ids."

**Changelog note:** past changelog entries mentioning `ImportedBy` (`changelog.en.json`/`.nl.json`/`.de.json`
around line 400-407) describe the table's column list as it existed at a past release and are **not**
edited by this issue — they are historical fact for their release, not living documentation (this
project's own rule: doc/changelog history is never retroactively rewritten). This issue's own eventual
`unreleased` changelog entry (added at the "Waiting for release" phase, per CLAUDE.md's Pre-Push
Checklist) should use `highlights: ["Internal improvements — no user-facing changes."]` — the column is
unused, unexposed via any endpoint, and this is pure internal schema/guard hygiene.

---

## Steps

### 1. Write the failing tests first (red)

**Status:** ✅ Done — all six expected-red tests confirmed red for the expected reason. In
`Quotinator.Data.Tests`: `SqliteImportBatchRepositoryTests.cs` fails to *compile* (`ImportBatch` has no
`ImportedById` property yet) — a genuinely red compile-time failure, since the rename hasn't landed. In
`Quotinator.Core.Tests`: `Schema_ImportBatchesTable_HasAllRequiredColumns` (missing `ImportedById`),
`Schema_MigrationVersion_IsBumped` (expected 10, actual 9), and the new
`Migration_RenameImportedByToImportedById_ColumnRenamedAndDataPreserved` (column doesn't exist) all fail
at runtime for the expected reason; `DatabaseInitializerTests`' four updated version-10 assertions
(`InitialiseAsync_PartialMigrationState_FailsSafelyAndRequiresExplicitReset`,
`InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental`,
`InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally`,
`InitialiseAsync_PreSplitCombinedCounterDatabase_FailsSafelyAndRequiresExplicitReset`) also fail for the
expected reason (expect 10, actual 9).

Write every test in the Verification checklist below against the *current* (pre-fix) code and confirm
each fails for the expected reason (missing `ImportedById` column/property; `Sql.ImportBatches` still
`SELECT *`; schema version still 9) before touching implementation code.

### 2. Add Migration010 and wire it into `QuotinatorMigrations.All`

**Status:** ✅ Done — added as the 10th entry in `QuotinatorMigrations.All`, exactly per Approach.

Exact SQL and wiring per Approach's "1. New migration" above.

### 3. Update `QuotinatorMigrations.BaselineSchema`

**Status:** ✅ Done — `ImportBatches` CREATE TABLE block now creates `ImportedById` directly; baseline
comment updated to note Migration010 is folded in. `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`
and `...AcceptSameCheckConstraintValues` both pass (confirmed in the full suite run, Step 7).

### 4. Rename the entity property

**Status:** ✅ Done — `src/Quotinator.Data/Entities/ImportBatch.cs:24` renamed to `ImportedById`. Build
clean after the rename (0 warnings/errors).

### 5. Confirm `Sql.ImportBatches`'s reflection-based column list already covers `ImportedById`

**Status:** ✅ Done — confirmed via the passing guard tests (`SqlConstant_PassesIdCaseGuard`/
`SqlConstant_PassesSelectPresentationGuard`, and #212's own `ImportBatches_SelectColumns_ReflectsEveryImportBatchProperty`)
with zero edits to `Sql.cs` — exactly the "picked up automatically" behaviour #212's reflection-based
rewrite was built to provide.

### 6. Update pre-existing tests broken by the rename/version bump

**Status:** ✅ Done — every location in Background's list updated: `"ImportedBy"` → `"ImportedById"` in
`ImportBatchesTests.cs`; all six hardcoded `9` → `10` assertions across `ImportBatchesTests.cs` and
`DatabaseInitializerTests.cs` (including the migration-list comment). All confirmed genuinely red before
the fix (Step 1) and green after.

**Bug found and fixed while writing Step 1's new `Migration_RenameImportedByToImportedById_ColumnRenamedAndDataPreserved`
test:** the new `CreateInitializer(batches, migrations, useBaseline)` overload added to `ImportBatchesTests.cs`
(mirroring `DatabaseInitializerTests.cs`'s existing 3-arg pattern) had its constructor call hardcode
`QuotinatorMigrations.All` instead of using the new `migrations` parameter — a copy-paste artifact from
the original 2-arg method's body. This silently made every `.Take(N)`-based partial-migration test run
the *full* migration list regardless of what was passed, defeating the test's entire purpose without any
compile error. Caught because the new migration test failed with "table ImportBatches has no column named
ImportedBy" immediately after only 9 migrations were meant to have applied — diagnosed via temporary debug
output (dumping the actual `pragma_table_info` result and applying the same 9 migrations' raw SQL text
directly, bypassing the initializer, to isolate the divergence to the constructor call itself) before
finding the one-line fix. Fixed by passing `migrations` instead of `QuotinatorMigrations.All`; full suite
confirmed green afterward.

**Second bug found by the developer, from a screenshot review:** the mixed-case round-trip tests added to
`SqliteImportBatchRepositoryTests.cs` (Verification checklist rows 4–5) originally used
`"11111111-1111-4111-8111-111111111111"` as the "uppercase" `ImportedById` fixture — an all-digit string,
so `UPPER(...)` on it is a no-op and the test proved nothing about case-insensitive rendering despite
passing. Fixed to `"aabbccdd-1234-4abc-8def-1234567890ab"` (contains real hex letters). A follow-up
codebase-wide audit (general-purpose agent, spot-checked directly) found no other instance of this bug
pattern anywhere in `tests/` — every other `UPPER()`/`.ToUpper*()` call site already used a literal with
genuine hex letters.

### 7. Full regression pass

**Status:** ✅ Done — `dotnet build --configuration Release`: 0 warnings, 0 errors.
`dotnet test --configuration Release --verbosity normal`: every project green
(`Quotinator.Core.Tests` 970/970, `Quotinator.Data.Tests` 610/610, `Quotinator.Api.Tests` 496/496, all
others unaffected), 0 failures — confirmed only after both bugs above were found and fixed; the first
full run (before the `CreateInitializer` fix) genuinely caught the new migration test failing for the
right reason.

### 8. T1 — Visual Studio verification (developer)

**Status:** ✅ Done — developer started the app in Visual Studio against their real pre-existing
development database (previously at App v9). Startup log shows: automatic pre-migration backup taken
(`quotinatordata_v10_20260723T181317Z.db`), "applying 1 pending App migration(s) (version 9 → 10)...",
"schema updated (data v10, app v10)", clean startup with "799 quotes 482 sources 7 characters 3 people"
unchanged, and several subsequent requests (`/import/actions`, `/admin/audit`,
`/admin/database/seed/preview`) all returning 200 with no errors. This is the stronger of the two
scenarios in the Step 8 description — a genuine incremental replay through Migration010 against a real,
previously-migrated database, not just a fresh empty one — confirmed live, exercising exactly the
upgrade path this migration exists to support.

### 9. T2 — Docker smoke verification

**Status:** ✅ Done — `docker build -f docker/Dockerfile -t quotinator:local .` succeeded. Fresh
container startup: baseline path logged "app v10", clean, no errors. `Quotinator.Tools.DbInspector`
`pragma_table_info('ImportBatches')` against the seeded database confirmed `ImportedById` present,
`ImportedBy` absent. Re-imported the curated file (200) and ran the full Reverse (undo) scenario
(`preview=true` then real — both 200) — this exercises `SqliteImportBatchRepository.GetAllAsync`
(`Sql.ImportBatches.SelectAll`, the exact query #212 rewrote and this issue's rename flows through) live
in a real container via `ReverseBatchAsync`'s LIFO-stack check. A follow-up `DbInspector` query confirmed
`ImportedById` reads back correctly (`NULL`, as expected — no write path sets this column, per
Background) on all four remaining seed batches after the reversal.

**The originally-planned mixed-case-insert scenario was dropped, not skipped silently:**
`Quotinator.Tools.DbInspector` is deliberately read-only by design (`Mode=ReadOnly`, see its own
README/`ConnectionStrings.BuildReadOnly`) and cannot write a mutation; no write path anywhere in the app
sets `ImportedById` either (confirmed in Background — the column is 100% unused). This is the exact same
situation CLAUDE.md's own T2 checklist already documents for `ExistingBatchId`: *"a live T2 run cannot
easily manufacture pre-existing non-canonical data through the API alone, since every write path now
canonicalizes at capture time"* — the mixed-case round-trip proof is instead fully covered by
`SqliteImportBatchRepositoryTests.GetAllAsync_MixedCaseImportedById_RendersLowercase`/
`GetByIdAsync_MixedCaseImportedById_RendersLowercase` (real, hermetic SQLite integration tests, not
mocks — already passing, see Step 6). No new scenario was added to CLAUDE.md's living T2 checklist, since
`ImportBatches` has no dedicated HTTP listing endpoint and the Reverse (undo) section already covers the
only externally-observable effect of this rename.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Migration010 renames `ImportBatches.ImportedBy` to `ImportedById` and preserves any pre-existing value | Unit test | `Quotinator.Core.Tests.Repositories.ImportBatchesTests.Migration_RenameImportedByToImportedById_ColumnRenamedAndDataPreserved` |
| 2 | ✅ | `QuotinatorMigrations.BaselineSchema` and incremental replay through Migration010 produce identical `ImportBatches` schema | Unit test | `Quotinator.Core.Tests.Database.DatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` |
| 3 | ✅ | `Sql.ImportBatches.SelectAll`/`SelectByType` (via #212's reflection-based `SelectColumns`) already wrap the renamed `ImportedById` with no code change here, and pass both id guards | Unit test | `Quotinator.Data.Tests.Security.SqlQueryGuardTests.SqlConstant_PassesIdCaseGuard`/`SqlConstant_PassesSelectPresentationGuard` plus #212's own `ImportBatches_SelectColumns_ReflectsEveryImportBatchProperty` |
| 4 | ✅ | A deliberately mixed-case `ImportedById` value round-trips to lowercase through `SqliteImportBatchRepository.GetAllAsync`/`GetByTypeAsync` (the rewritten `Sql.ImportBatches` queries) | Unit test | `Quotinator.Data.Tests.Repositories.SqliteImportBatchRepositoryTests.GetAllAsync_MixedCaseImportedById_RendersLowercase` (fixture corrected to a literal with genuine hex letters — see Step 6 Notes) |
| 5 | ✅ | The same mixed-case value round-trips to lowercase through the generic `SqliteRepository<ImportBatch>.GetByIdAsync` path | Unit test | `Quotinator.Data.Tests.Repositories.SqliteImportBatchRepositoryTests.GetByIdAsync_MixedCaseImportedById_RendersLowercase` |
| 6 | ✅ | `Schema_ImportBatchesTable_HasAllRequiredColumns` reflects the renamed column | Unit test | `Quotinator.Core.Tests.Repositories.ImportBatchesTests.Schema_ImportBatchesTable_HasAllRequiredColumns` |
| 7 | ✅ | Consumer schema version is 10 after all migrations apply | Unit test | `Quotinator.Core.Tests.Repositories.ImportBatchesTests.Schema_MigrationVersion_IsBumped` |
| 8 | ✅ | No regression across the five other hardcoded-version-9 assertions found in Background | Unit test | `Quotinator.Core.Tests.Database.DatabaseInitializerTests` (4 locations, all updated to assert `10`) plus full-suite run |
| 9 | ✅ | No regression anywhere else | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green (`Quotinator.Core.Tests` 970/970, `Quotinator.Data.Tests` 610/610, `Quotinator.Api.Tests` 496/496), 0 warnings, 0 errors |
| 10 | ✅ | T1 — app starts cleanly on a fresh database and on an existing pre-#213 database (migration replay) | Live (T1) | Developer confirmed in Visual Studio against a real pre-existing v9 database — startup log shows "applying 1 pending App migration(s) (version 9 → 10)...", "schema updated (data v10, app v10)", clean, no errors |
| 11 | ✅ | T2 — Docker smoke test: fresh seed shows `ImportedById` (not `ImportedBy`) via schema inspection; rewritten `Sql.ImportBatches.SelectAll` continues to work live (Reverse scenario) | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; `Quotinator.Tools.DbInspector` `pragma_table_info('ImportBatches')` confirmed `ImportedById`; curated import (200) + Reverse preview/real (both 200); mixed-case round-trip covered by unit tests instead (DbInspector is read-only by design, no write path exists for this column — see Step 9's Notes) |

---

## Notes

**Ownership split with #212:** this plan doc's own investigation independently rediscovered #212's exact
finding — `Sql.ImportBatches.SelectAll`/`SelectByType`'s `SELECT *` bypasses both guards entirely,
regardless of this issue's rename. #212 is filed separately (same #207 audit) and owns that rewrite in
full. To avoid two issues independently rewriting the same two queries, this issue now depends on #212
(header `Depends on: #212`). #212 was itself revised during review (2026-07-23) — the developer pointed
out `SELECT *`'s real advantage is that it never needs updating when an entity's properties change, and
any replacement must keep that flexibility rather than trading a guard gap for a manual-sync burden —
to build its column list by reflecting over `ImportBatch`'s properties instead of listing them by hand.
Because of that, this issue's Spec requirement 4/Approach/Step 5 no longer need even the one-line
`ImportedById`-wrapping addition originally planned: the rename is picked up automatically once #212
lands, with zero code change to `Sql.ImportBatches`. `overview.md`'s dependency table and
order-of-operations should sequence #212 before #213 accordingly.

Siblings #214, #215 (also filed from #207's final coverage audit) touch guard-adjacent code in the same
`src/Quotinator.Data/Diagnostics/` and `src/Quotinator.Data/Queries/` files this issue's Background
investigated, but on different columns/mechanisms — no functional dependency either direction with this
issue. Worth a plain merge-order/rebase awareness if implemented close together, not a blocking sequence.
