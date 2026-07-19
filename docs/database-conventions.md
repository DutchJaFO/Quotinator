# Database Do's and Don'ts

A scannable reference for schema, migration, and query-safety rules. Each item links to the
authoritative source (ADR or doc) for the full reasoning — this page is the checklist, not the
argument. For *how* to use the repository/join-query infrastructure, see
[`data-access.md`](data-access.md) instead — this page covers what's allowed in the schema and SQL
itself.

---

## Every table

| | Rule |
|---|---|
| ✅ Do | Give every table `RecordBase`'s four columns without exception — `DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted` — including junction/link tables, using a synthetic `Guid Id` plus a `UNIQUE` constraint on the natural key. |
| ❌ Don't | Invent a bespoke primary key shape for a junction table "because the surrogate key is meaningless" — the aesthetic argument was considered and rejected. |

📖 [ADR 002](architecture-decisions/002-recordbase-on-all-tables.md)

---

## Entity id casing

| | Rule |
|---|---|
| ✅ Do | Canonicalize an externally-supplied id (a JSON file's explicit `id` field, a URL path segment) to this project's uppercase form exactly once, at the single earliest point it is captured — before indexing it, staging it for another write, or inserting it. Everything derived from that capture point inherits the canonical form for free. |
| ✅ Do | Compare an id read back from storage case-insensitively (`UPPER(column) = UPPER(@param)`) wherever the incoming comparison value's casing can't be controlled — this is a *separate*, still-required rule; it does not substitute for canonicalizing on write. |
| ❌ Don't | Assume a `Guid`-typed Dapper parameter is automatically canonicalized and therefore skip explicit canonicalization — `GuidHandler` only normalizes parameters that are actually `Guid`-typed; an id threaded through the system as a plain `string` (as staged-action ids necessarily are, since a not-yet-existing row's id must be computable before any `Guid` exists to type it as) bypasses that entirely. |
| ❌ Don't | Fix an id-casing bug by wrapping only the query that surfaced it in `UPPER()` without checking whether other writes derive from the same uncanonicalized in-memory value — two columns that are both wrong in the same way can still join correctly against each other, masking the underlying defect until a third, canonically-typed comparison exposes it. |

📖 [ADR 012](architecture-decisions/012-canonicalize-entity-ids-at-capture.md), CLAUDE.md's
"GUID/enum/id comparisons are case-insensitive by default"

---

## Enum-backed columns

| | Rule |
|---|---|
| ✅ Do | Add a SQL `CHECK` constraint enumerating the same values as any column backed by a real, closed C# `enum` — at column-creation time, inline on the `CREATE TABLE` or the `ALTER TABLE ... ADD COLUMN` that introduces it. Confirmed against [sqlite.org's ALTER TABLE docs](https://www.sqlite.org/lang_altertable.html) — `ADD COLUMN` explicitly supports a `CHECK` constraint (existing rows are tested against it), subject to the documented restrictions (no `PRIMARY KEY`/`UNIQUE`, `NOT NULL` needs a real default, etc. — see ADR 008) — so a new column never needs the full rebuild-migration dance just to carry one. |
| ✅ Do | Type the POCO property itself as `SafeValue<TEnum?>` and register a handler once via `RegisterEnumHandler<TEnum>()` (see `DatabaseConfiguration.Configure()`) — not a plain `string` property with `.ToString()` sprinkled at every call site. `SystemChangeLog.InitiatedByType`/`Action` and `SystemImportConflict`/`SystemImportAction`'s `Status`/`ActionType` all follow this. Compare with `.Parsed` (a real `TEnum?`), not string equality; `.Raw` is only for a diagnostic/message surface (e.g. an exception's `CurrentStatus`) or an API DTO field that's contractually a string. |
| ❌ Don't | Reach for a plain `string` property plus manual `.ToString()` calls "to avoid Dapper's enum-as-int default" — the generic `SafeValue<TEnum?>`/`SafeEnumHandler<TEnum>` mechanism already solves that, and skipping it means every comparison/assignment site has to remember the `.ToString()` by hand, which is exactly the kind of thing that gets missed (found the hard way while converting `ImportConflictStatus`/`ImportActionStatus`/`ImportActionKind` in #154 — see `SystemChangeLog.cs` for the pattern that should have been copied from the start). |
| ❌ Don't | Add a `CHECK` to a column whose value set is genuinely open — defined and extended by a *consuming* project, not by the table's own project — e.g. `SystemImportConflict.EntityType`, `SystemImportAction.BatchId`/`ExistingBatchId`. This is not the same as "lives on a domain-agnostic table": `System_ImportConflicts.Status` and `System_ImportActions.Status`/`ActionType` live on those same tables but are a closed set the owning project's own coordinator logic assigns — they *are* real `enum`s with a `CHECK`. The test is who defines the possible values, not which project the table belongs to. Document the exemption in the entity's own XML doc comment when a field is genuinely open. |
| ❌ Don't | Decide this by comparing two existing columns that disagree (`ImportBatches.Type` has a `CHECK`, `ImportBatches.ConflictPolicy` doesn't) — the latter is a known, tracked gap, not a pattern to copy. Check this page and the ADR first. |

📖 [ADR 008](architecture-decisions/008-enum-backed-columns-require-check-constraints.md)

---

## JSON-blob columns

| | Rule |
|---|---|
| ✅ Do | Type a column as a concrete shape (e.g. `IReadOnlyList<string>`) and register `RegisterJsonHandler<T>()` when the *owning* project can define `T` itself — a generic BCL shape with no dependency on a consumer's domain model. `FieldMergeResolver`'s `IReadOnlyList<string>` (registered in the base `DatabaseConfiguration.Configure()`) is the working example: Data owns both the column and the shape. |
| ❌ Don't | Type a Data-owned entity property as a consumer's concrete DTO (e.g. `QuoteConflictFieldsDto` from `Quotinator.Core.Models`, or `ConflictDecisionRequest` from `Quotinator.Engine.Models`) just to use `RegisterJsonHandler<T>()` — that requires `Quotinator.Data` to reference the consumer's type, which is exactly what ADR 004 forbids (Data has zero dependency on Core/Engine). `SystemImportConflict`/`SystemImportAction`'s `ExistingValue`/`IncomingValue`/`MergedFields` stay `string`/`string?` for this reason — documented in each entity's own `<remarks>`. |
| ❌ Don't | Treat every manual `JsonSerializer.Deserialize<T>(entity.SomeField)` call in a consumer project as a missed `JsonHandler<T>` opportunity. `JsonHandler<T>` only replaces a Dapper *materialization* step — it has nothing to fix here, because Dapper already round-trips these TEXT columns as plain `string` correctly (unlike the enum-as-int bug `SafeEnumHandler<TEnum>` exists for). The second deserialize happening in `SqliteConflictResolutionService.cs`/`QuoteSeedWriter.cs` is ordinary domain-layer JSON parsing of an intentionally opaque blob, not a workaround for a Dapper defect. |
| ❌ Don't | Assume a JSON-blob column has one consistent shape without checking every writer. `SystemImportConflict.MergedFields` holds a plain field→`"ours"`/`"theirs"` dictionary when auto-resolved by merge policy (`QuoteSeedWriter.cs`), but a serialized `ConflictDecisionRequest` when manually decided (#149, `SqliteConflictResolutionService.DecideAsync`) — two incompatible shapes in the same column, so no single `JsonHandler<T>` registration could type it correctly even if the layering problem above didn't already rule it out. |

---

## Migrations

| | Rule |
|---|---|
| ✅ Do | Write migrations as numbered, append-only entries — never reorder or edit one that has already applied to a real database. Add new ones at the end and increment the version. |
| ✅ Do | Make every DDL statement idempotent where SQLite allows it (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`). SQLite has no idempotent form for `ALTER TABLE ... RENAME` or `ADD COLUMN` — a non-idempotent migration that fails partway through leaves the version unrecorded but the schema partially changed, causing a crash loop on every restart. |
| ✅ Do | Keep one schema change per migration where possible — easier to reason about when partially applied. |
| ✅ Do | Update the fresh-database baseline (`DataBaselineSql` / `BaselineSchema`) in the *same commit* as any migration that changes the final schema — the schema-drift tests (`*BaselineAndIncrementalReplay*`) fail on any drift, including `CHECK`-constraint behaviour that `PRAGMA table_info` can't see structurally. |
| ✅ Do | Before closing a milestone, verify the full incremental migration path against a database matching the **last published release's** schema — not the accumulated local dev database, which may have passed through edited-in-place or abandoned intermediate states. The from-empty schema-drift tests are necessary but not sufficient; they don't prove the path works starting from what a real installation actually has. |
| ❌ Don't | Rely on catching a migration's own exception to detect "already applied" — a genuinely different failure with the same error message would be silently misclassified. Fix the root cause instead (e.g. `Reset` never replays `Quotinator.Data`'s own migration history at all, so its rename-collision case simply can't occur — no exception-catching needed). |
| ❌ Don't | Widen or otherwise change an existing `CHECK` constraint in place — SQLite has no `ALTER TABLE ... MODIFY CHECK`. Rebuild under a temporary name, copy data, drop, rename (see `Migration004_ImportBatchTypeUserSeed` for the worked example). |

📖 CLAUDE.md → "Schema migration policy" · "No exception-based migration recovery" ·
[ADR 009](architecture-decisions/009-verify-migrations-against-last-released-schema.md)

### Data vs. consumer migration ownership

| | Rule |
|---|---|
| ✅ Do | Keep `Quotinator.Data`'s own migrations (`DataOwnedMigrations`, for `System_`-prefixed tables it defines itself) entirely separate from a consuming project's domain migrations (`QuotinatorMigrations.All`) — tracked in independent version tables (`System_SchemaVersion` vs `System_ConsumerSchemaVersion`) so neither side's version count is affected by the other's. |
| ✅ Do | Apply Data's own migrations first, always, before any consumer migration. |

📖 CLAUDE.md → "Migration ownership split"

---

## SQL injection and centralisation

| | Rule |
|---|---|
| ✅ Do | Use parameterised queries or a query builder that parameterises automatically, for every parameter that originates from an HTTP request. |
| ✅ Do | Put every SQL string in a project's own `Sql.cs` — never inline. Generic infrastructure SQL (no Quotinator-domain table name) goes in `src/Quotinator.Data/Queries/Sql.cs`; SQL naming a Quotinator-domain table (Quotes, Sources, Characters, Conversations, etc.) goes in `src/Quotinator.Engine/Queries/Sql.cs` instead (ADR 004 — Quotinator.Data must stay domain-agnostic, see below). Fixed queries as `const` fields, dynamic queries (e.g. optional WHERE clauses) as `static` factory methods. Add a `SqlQueryGuardTests.AssembledQueryCases` entry (in the guard test class for whichever project's `Sql.cs` the query lives in) for every clause combination a factory method can produce. |
| ❌ Don't | Build a SQL string by concatenating user input, anywhere, for any reason. |
| ❌ Don't | Write a SQL string literal anywhere else in `src/` — including inline in a service, repository, or endpoint handler. If a query needs a table name interpolated, it must come from a `[Table]` attribute (developer-controlled metadata) or a compile-time literal, never user input. |

📖 [`data-access.md`](data-access.md) → `Sql.Queries` / `Sql.Joins`

## Aggregate queries (CVE-2025-6965)

| | Rule |
|---|---|
| ✅ Do | Add any new SQL constant containing `COUNT`/`SUM`/`AVG`/`MIN`/`MAX`/`GROUP_CONCAT`/`TOTAL` to `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory`'s whitelist in the same commit, with a one-line comment explaining why it's safe (no `GROUP BY`/`HAVING`, or reviewed aggregate-term count ≤ output-column count). |
| ❌ Don't | Combine an aggregate function with `GROUP BY`/`HAVING` without manual review — that's the exact CVE-2025-6965 pattern (memory corruption in SQLite ≤ 2.1.11 when aggregate terms exceed available columns). The guard is a heuristic that flags candidates; it doesn't count terms for you. |

📖 [`sql-safety.md`](sql-safety.md)

---

## Quotinator.Data must stay domain-agnostic

| | Rule |
|---|---|
| ✅ Do | Keep `Quotinator.Data` reusable by any future consumer with its own schema — generic infrastructure (repositories, Unit of Work, migrations base, type handlers, conflict-resolution strategies) only. |
| ✅ Do | Put Quotinator-specific entities, migrations, and Dapper handler registrations in `Quotinator.Engine` instead — the one project allowed to reference both `Core` and `Data`. |
| ✅ Do | Put Quotinator-domain SQL query strings in `Quotinator.Engine/Queries/Sql.cs`, not `Quotinator.Data`'s — a query naming a domain table is domain coupling even though it's "just a string," not a C# type reference (found via #157: `Quotes`/`Characters`/`Sources`/etc. had lived in `Quotinator.Data`'s `Sql.cs` since before `Quotinator.Engine` existed, and every table added since had copied that placement without checking). |
| ❌ Don't | Let `Quotinator.Data` reference `Quotinator.Core`, or carry any Quotinator-domain type (`Genre`, `QuoteType`, `Source`, etc.) — the dependency only flows one way. |
| ❌ Don't | Expose `IDbConnection`/`IDbTransaction`/any Dapper type on a public `Quotinator.Data` interface — `IUnitOfWork` exists specifically so callers never see the underlying connection type. |

📖 [ADR 003](architecture-decisions/003-unit-of-work-and-data-project-design-goals.md) ·
[ADR 004](architecture-decisions/004-quotinator-data-project-boundaries.md)

## Connections and transactions

| | Rule |
|---|---|
| ✅ Do | Use `IUnitOfWork` (or the `TransactionScope.ExecuteAsync` helper) for any multi-step write that must be atomic — repository methods accept an optional `IUnitOfWork?` parameter. |
| ❌ Don't | Open, close, or dispose a connection yourself in calling code — the repository or Unit of Work owns the connection lifecycle for its scope. |

📖 [`data-access.md`](data-access.md) → "Transaction coordination"

## JSON stored in a column

| | Rule |
|---|---|
| ✅ Do | Keep JSON blob columns (e.g. `ExistingValue`/`IncomingValue`/`MergedFields` on the conflict/action log tables) as opaque strings inside `Quotinator.Data` — never deserialize them there. The consuming project (`Quotinator.Engine`) is the one that knows the shape and deserializes via a POCO + `JsonSerializer`. |
| ❌ Don't | Walk a DB-stored JSON blob by hand (`JsonNode`/`JsonDocument` indexers) anywhere, in either project — same rule as the project's general JSON parsing policy. |

📖 CLAUDE.md → "JSON parsing policy"

---

## Testing database code

| | Rule |
|---|---|
| ✅ Do | Use a real SQLite database for `Quotinator.Data.Tests`/`Quotinator.Engine.Tests` — no mocks or `NoOp*` stubs for the repository/migration/coordinator actually under test. Each test creates its own temp directory and `.db` file in `[TestInitialize]` and deletes it in `[TestCleanup]`. |
| ✅ Do | Leave test classes sequential by default — opt into `[Parallelize]` only when no global state (Dapper type handlers, static caches) is touched, no filesystem is shared, and `SqliteConnection.ClearAllPools()` isn't called in cleanup. |
| ✅ Do | Register any global Dapper type handler exactly once, in `[AssemblyInitialize]` — never in `[ClassInitialize]`/`[TestInitialize]`. |
| ❌ Don't | Share a database file or connection between tests, or assume a test can safely run in parallel without explicitly checking the three conditions above. |

📖 [`testing-policy.md`](testing-policy.md) · [ADR 006](architecture-decisions/006-sequential-test-execution-by-default.md)
