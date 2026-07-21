# ADR 012 — External entity ids are canonicalized once, at the point of capture

**Status:** Accepted
**Date:** 2026-07-19
**GitHub issues:** #190, #207, #209, #210

This ADR states the current, settled design. It does not narrate how the design arrived here — that
history lives in git commit messages and closed-issue discussion, not in this file.

---

## Context

An entity id can enter this system from more than one place: generated internally
(`EntityIdentity.StableId`, `QuoteIdentity.StableId`, `Guid.NewGuid()`), or supplied externally (a JSON
import file's explicit `id` field, a URL path segment). An externally-supplied id is under no obligation
to match this project's own casing convention, and two independently-written copies of "the same" id can
disagree in casing without either one being wrong on its own terms.

That disagreement causes two distinct failure classes if left unhandled:

1. **A comparison fails to match.** SQLite's default `TEXT` comparison is case-sensitive, so a
   `WHERE`/`JOIN` against an id column silently returns nothing (or joins nothing) when the two sides
   disagree in casing, even though both refer to the same row.
2. **A stored value renders inconsistently.** A `SELECT` with no filter or join on the column in
   question — list every pending action, list every audit entry — never runs any comparison at all, so a
   row written in one casing keeps rendering in that casing indefinitely, regardless of what casing new
   writes use.

These are separate problems needing separate mechanisms; a fix for one does not cover the other.

---

## Decision

### Canonical form

**The canonical form for every entity id, system-wide, is lowercase** — `Guid.ToString("D")`'s own
default output, and the conventional RFC 4122 UUID string representation most tooling expects. This is a
readability choice, not a technical requirement: the comparison mechanism below works identically
regardless of which casing is canonical. Lowercase was chosen because it requires no explicit casing call
when a fresh `Guid` is rendered (`.ToString("D")` alone is already canonical), whereas uppercase always
needed an extra step a future call site could forget.

**`Quotinator.Data.Helpers.GuidExtensions.ToCanonicalId(this Guid id)`** — `id.ToString("D")` — is the
single choke point for rendering a `Guid` as this project's canonical id string. Every site that needs a
`Guid` as a string (storage, comparison, API presentation) calls this rather than typing `.ToString("D")`
inline.

**`Quotinator.Data.Helpers.EntityIdCanonicalizer.CanonicalizeLowercase`/`TryCanonicalizeLowercase`**
parses a raw externally-supplied id as a `Guid` and re-renders it via `ToCanonicalId()` — canonicalizing
it, and rejecting a malformed id at the earliest possible point rather than silently storing garbage. The
non-throwing `Try*` form is for a capture site that must fall back gracefully rather than abort an entire
import batch.

**A `Guid`-typed Dapper parameter is not sufficient proof of canonicalization on its own.** Dapper's
built-in `typeMap` resolves a bare `Guid` parameter's `DbType` *before* it ever consults a registered
`ITypeHandler` — so `Quotinator.Data.Helpers.GuidHandler`, despite being registered, is silently skipped
for outbound `Guid`-typed parameters unless `SqlMapper.RemoveTypeMap(typeof(Guid))` runs first (it does,
in `DatabaseConfiguration.Configure()`, as defence-in-depth — but nothing should depend on that alone). An
id threaded through the system as a plain `string` — as a staged action's id necessarily is, since a
not-yet-existing row's id must be computable before any `Guid` exists to type it as — bypasses `GuidHandler`
entirely either way. `ToCanonicalId()` must be called explicitly at every capture point; it cannot be
relied on to happen automatically.

### Capture-time canonicalization

An id originating outside this codebase's own generation is canonicalized exactly once, at the single
earliest point it is captured — before it is indexed into an in-memory lookup, staged as a value another
entity's write will reference, or written to any table. Every value derived from that capture point (a
same-batch FK reference, a staged `SystemImportAction.EntityId`, an inserted primary key) inherits the
canonical form for free, because nothing downstream re-derives it from the original raw string.

`ImportActionPlanner`'s capture points (the quote loop, and all five `Plan*Async` methods) call
`EntityIdCanonicalizer.TryCanonicalizeLowercase`.

### Comparison-time case-insensitivity

**`Quotinator.Data.Queries.IdClauses`** builds the standard case-insensitive SQL fragment for comparing
an id column against a bound parameter, a parameter list, or another id column:

- `Equals(column, paramName)` → `LOWER(column) = LOWER(@paramName)`
- `In(column, paramName)` / `NotIn(column, paramName)` → `LOWER(column) IN/NOT IN @paramName` (only the
  column side can be wrapped in SQL; the bound list's own values must already be lowercase — see
  "IN-list binding" below)
- `Join(leftColumn, rightColumn)` → `LOWER(leftColumn) = LOWER(rightColumn)`

Every fixed query and factory method across `Quotinator.Core.Queries.Sql`, `Quotinator.Data.Queries.Sql`,
and `RepositorySql.cs` builds its id comparisons through `IdClauses` rather than hand-typing `LOWER(...)`
— a helper cannot forget the wrap or apply it to only one side; a hand-typed comparison can, and has. A
fixed query that calls `IdClauses` cannot be a compile-time `const` (a method call isn't a constant
expression) and is `static readonly` instead.

**Joins are wrapped unconditionally, not only where a live bug was found.** Both sides of a join between
two id columns are already canonical by construction once capture-time canonicalization is in place, but
`IdClauses.Join` wraps anyway — deliberate defense-in-depth, matching this project's standing rule to
never assume a comparison stays safe just because its only known callers currently behave.

**`IdClauses` wraps in `LOWER(...)`, matching `ToCanonicalId()`'s own lowercase output**, so a value
produced by that method binds directly into an `IN`/`NOT IN` list with no further transformation. This
matters because SQL can only wrap the *column* side of an `IN`-list (SQLite has no syntax to transform
every element of a bound list) — the list's own values must already agree with the wrapper's casing.

**`Quotinator.Data.Diagnostics.SqlIdCaseGuard`** is the automated backstop: a regex-based static analyzer
(mirroring `SqlAggregateGuard`'s CVE-2025-6965 approach) that scans every SQL string this codebase
produces and flags any id-column comparison — parameter equality, `IN`/`NOT IN`, or a JOIN/correlated-
subquery condition between two id columns — that isn't `LOWER()`-wrapped on both sides. A half-protected
comparison (only one side wrapped) is flagged too. `UPDATE ... SET` assignments are stripped before
scanning (a capture-time concern, not a comparison-time one). Wired into `SqlQueryGuardTests` (both
`Quotinator.Core.Tests` and `Quotinator.Data.Tests`) and `RepositorySqlGuardTests` via `DynamicData`
enumeration over every SQL constant, factory method, and dynamically-assembled query — including
arrow-bodied `static string` properties, not only fields.

**IN-list binding**: never bind a raw `IReadOnlyList<Guid>` directly as an anonymous-object property.
Dapper's list-parameter expansion does not reliably invoke a registered `ITypeHandler` per element the
way scalar parameter binding does — pre-canonicalize every value via `.Select(id => id.ToCanonicalId())`
before binding.

### Read-time presentation normalization

Capture-time canonicalization and comparison-time case-insensitivity are not sufficient on their own: a
`SELECT` with no filter or join on a column never runs the comparison mechanism, so a row stored under a
prior or non-canonical casing keeps rendering that way indefinitely. **Case-insensitive matching and
canonical presentation are two different guarantees.**

A `Guid`-typed property gets canonical presentation for free — `System.Text.Json` always serializes a
`Guid` struct using its own default lowercase formatting, regardless of what casing the underlying column
held. A `string`-typed id-reference property (`SystemImportAction.BatchId`/`EntityId`/`ExistingBatchId`,
`SystemAuditEntry.RecordId`, `SystemChangeLog.EntityId` — `string`-typed for the same reason a
not-yet-existing row's id must be representable before any `Guid` can be typed as one) has no such safety
net and renders exactly the casing already on disk.

**`IdClauses.SelectColumn(column, alias)`** → `LOWER(column) AS alias` — the standard SELECT-list
fragment for returning *any* id column, primary key or foreign key, regardless of what C# type the
caller ultimately deserializes the value into. This applies uniformly, the same way `IdClauses.Join`
wraps unconditionally: a column that is `Guid`-typed today and renders correctly by accident of the
serializer is not evidence it will stay that way — a downstream type can change without the query ever
being touched (exactly what happened for `Quotinator.Core.Models.MasterDataReference.Id`, `string`-typed
specifically because a `Guid` wasn't enough for a not-yet-existing row's id). Every `*Id`-suffixed column
selected anywhere in this codebase goes through this method.

**`Quotinator.Data.Diagnostics.SqlSelectPresentationGuard`** is the automated backstop, mirroring
`SqlIdCaseGuard`'s own strip-then-scan technique rather than a registry of "columns known to need it":
strip every already-`LOWER(...)`-wrapped column reference (including its restated alias) from a query's
`SELECT ... FROM` span, then flag any `*Id`-suffixed column reference — bare, alias-qualified, or
bracket-quoted — that remains. Wired into the same `SqlQueryGuardTests`/`RepositorySqlGuardTests`
`DynamicData` enumeration `SqlIdCaseGuard` uses, so every SQL constant, factory method, and
dynamically-assembled query is scanned on every test run.

**The one exemption**: `SystemChangeLog.InitiatedById` is excluded from
`SqlSelectPresentationGuard.ExemptColumnNames`. Unlike every other column this guard protects, it is not
always an id — it holds an import batch UUID, an HTTP route, or an enrichment provider name (see its own
doc comment) — so forcing it lowercase would corrupt legitimate mixed-case content in the non-id cases.
This is the only column name excluded; every other `*Id`-suffixed column, PK or FK, must be wrapped.

**Structural boundary: `RepositorySql`'s generic queries.** `RepositorySql.cs` (`SelectById`,
`SelectByIds`, `SelectPage`, etc.) is entity-agnostic by design (ADR 004) — it receives only a table
name, never a column list, so it always queries `SELECT *`. There is no explicit column list for a
text-based guard to rewrap. Correctness here depends on every entity's id/FK properties staying
`Guid`-typed (which they all currently are — no domain entity in `Quotinator.Core.Entities` has a
`string`-typed id-suffixed property; only the `Quotinator.Data`-owned `System_`-prefixed tables do, and
those are read through explicit, non-generic queries that `SqlSelectPresentationGuard` does cover). If a
future domain entity ever needs a `string`-typed id/FK property, it cannot go through the generic
`SELECT *` path safely and needs its own explicit, guarded query instead.

---

## Consequences

- **No migration.** Every mechanism above is applied going forward, at the point of capture, comparison,
  or read — never as a data migration or retroactive re-casing pass. `IdClauses`/`SqlIdCaseGuard` make
  comparisons resolve correctly regardless of a row's actual stored casing; `IdClauses.SelectColumn`/
  `SqlSelectPresentationGuard` make presentation correct regardless of stored casing too. A row written
  under any prior convention needs no data-level fix.
- Any new SQL query that compares an id column, joins on one, or selects one, is caught automatically by
  the build's test suite if it doesn't go through `IdClauses`/`IdClauses.SelectColumn` — this is no
  longer a discipline that depends on the author remembering a documented rule unaided.
- `docs/database-conventions.md`'s "Entity id casing" section documents the ✅/❌ patterns this ADR
  establishes, for a developer who wants the short version without reading this file.
- A brand-new `string`-typed, `Id`-suffixed entity property must either be read through a query that
  calls `IdClauses.SelectColumn`, or be added to `SqlSelectPresentationGuard.ExemptColumnNames` with a
  documented reason (matching `InitiatedById`'s).
