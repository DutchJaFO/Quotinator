# ADR 012 — External entity ids are canonicalized once, at the point of capture

**Status:** Accepted
**Date:** 2026-07-19
**GitHub issues:** #190, #207, #209, #210

---

## Context

This project already has a documented rule (CLAUDE.md's "GUID/enum/id comparisons are case-insensitive
by default") that every id-matching `WHERE`/`JOIN` clause must compare case-insensitively
(`UPPER(column) = UPPER(@param)`), because `.NET` serializes `Guid` lowercase by default while stored
values are consistently uppercase, and a curator-authored explicit id is under no obligation to match
that casing. That rule was found and fixed piecemeal across `status`/`entityType`/`batchId` (#154), a
conversation `{id}` route (#69), and Sources'/People's own id-first lookup (#180) before being generalised
— each time, the fix was applied at the point a query *reads* an id.

While investigating a live bug found during #190's own T2 pass (`GET /api/v1/masterdata/sources/{id}`
404ing for a Source created with a file-authored explicit id), a second, distinct failure mode was
found: **the read-side rule assumes the stored data is already canonical, but nothing enforces that on
write.** `ImportActionPlanner` threads a curator-authored explicit id (`SourceEntry.Id`,
`PersonEntry.Id`) through in-memory batch indexes (`sourceIndex`, `personIndex`) and into
`SystemImportAction.EntityId` in whatever raw casing the file used. Every downstream insert/update that
binds that id as a Dapper parameter does so with a plain `string`-typed parameter — never `Guid`-typed —
so `GuidHandler`'s uppercase normalization (`.ToString("D").ToUpperInvariant()`, applied automatically to
every `Guid`-typed parameter) never runs. The row is written to disk with whatever casing the file
happened to use.

This was masked rather than caught, because two separately-written columns that both derive from the
same uncanonicalized in-memory value (`Sources.Id` and `Quotes.SourceId`, both sourced from the same
`sourceIndex` entry) stay accidentally consistent with each other — a plain `JOIN Sources s ON s.Id =
q.SourceId` still matches, because both sides carry the same wrong casing. The inconsistency only becomes
visible when a *different* code path compares against the same column with a canonically-uppercased
value — exactly what `GuidHandler` produces for any `Guid`-typed parameter, and exactly what surfaced the
masterdata `GetById` 404.

**A read-side-only fix would have made this worse.** The first fix considered — wrapping the failing
`SELECT` in `UPPER()`, matching the existing read-side rule — was rejected after tracing the actual write
path: it would have fixed the one symptom while leaving the row non-canonical on disk, permanently
dependent on every *other* current and future query against that column remembering to do the same
wrapping. A second fix considered — uppercasing only the Source's own insert — was also rejected after
finding that `Quotes.SourceId` is written from the *same* raw value: canonicalizing one side without the
other would have broken the Quote→Source join outright, a more severe and more silent regression than the
bug being fixed.

---

## Decision

**An id originating outside this codebase's own generation (a JSON file's explicit `id` field, a URL path
segment, any other externally-supplied string) is canonicalized to this project's standard uppercase form
exactly once, at the single earliest point it is captured** — before it is used for anything: indexed
into an in-memory lookup, staged as a value another entity's write will reference, or written to any
table. Every value derived from that capture point (a same-batch FK reference, a staged
`SystemImportAction.EntityId`, an inserted primary key) inherits the canonical form for free, because
nothing downstream re-derives it from the original raw string.

This is stricter than, and does not replace, the existing read-side rule. Both are required, because they
guard against different failure modes:

- **Read-side** (`UPPER(column) = UPPER(@param)`): protects a query that compares an incoming value
  against data that is already correctly stored, when the incoming value's casing can't be controlled
  (a URL segment, a lookup keyed by an external system).
- **Write-side** (this ADR): protects the *stored data itself* from ever becoming non-canonical in the
  first place, so that every future read — whether or not its author remembered to wrap it in `UPPER()`
  — simply works.

An id that is generated internally (`EntityIdentity.StableId`'s SHA-256-derived ids, `Guid.NewGuid()`)
is already canonical by construction and needs no additional handling.

**A `Guid`-typed parameter is not sufficient proof of canonicalization on its own.** `GuidHandler`
normalizes any `Guid`-typed parameter at bind time, but an id that is threaded through the system as a
plain `string` (as `SystemImportAction.EntityId` and the `*ActionPayload` records necessarily are, since
they must also carry not-yet-existing stable ids computed before any row exists) bypasses that entirely.
The canonicalization in this ADR must happen in application code, at capture, independent of whichever
Dapper parameter type a downstream query happens to use.

### Mechanism: a single reusable helper, not a repeated `.ToUpperInvariant()` idiom

This project already has a working precedent for turning a discipline ("always do X") into something a
test suite can enforce, rather than trusting every future author to remember it: `Sql.cs`'s SQL
centralization plus `SqlQueryGuardTests`, which reflects over every query-producing method and drives it
through a full input matrix. Id canonicalization gets the same treatment, at the same layer (domain-
agnostic infrastructure, `Quotinator.Data` — matching `Optional<T>`/`FieldMergeResolver`'s own placement
reasoning), rather than being left as an idiom each Core call site is expected to repeat correctly:

- **`Quotinator.Data.Helpers.EntityIdCanonicalizer.Canonicalize(string rawId)`** — parses `rawId` as a
  `Guid` and returns `.ToString("D").ToUpperInvariant()`, the exact form `GuidHandler` produces for a
  `Guid`-typed parameter. Parsing (not a bare `.ToUpperInvariant()` on the raw string) also rejects a
  malformed id at the earliest possible point, rather than silently storing garbage. This is the single
  place this logic is allowed to exist — a capture site calls this, it does not reimplement it.
- **`EntityIdCanonicalizerTests`** (`Quotinator.Data.Tests`) — direct correctness coverage: casing
  normalization, idempotence (canonicalizing an already-canonical id is a no-op), and that a malformed
  id throws rather than silently producing a wrong value.
- **A cross-entity regression guard**, data-driven across every entity type that accepts a file-authored
  explicit id (Source, Person, StageDirection, SoundCue, Conversation — `[DynamicData]`-driven, one test
  method covering all five, matching `SqlQueryGuardTests.AssembledQueryCases`'s shape rather than five
  independently-written near-duplicate tests) — asserting the full, real invariant end to end: importing
  a lowercase-authored explicit id results in a canonically-uppercase stored row, reachable via a
  `Guid`-typed lookup, *and* still correctly joined to by whatever else references it in the same batch
  (the Quote→Source join specifically, per the incident that found this bug). A helper-level unit test
  proves the helper is correct; this guard proves the helper is actually being used everywhere it needs
  to be — the same gap `SqlQueryGuardTests` closes for `AddOpenApi`/transformer registration, not just
  the transformer class in isolation.

A stronger, purely-structural guarantee — changing `SystemImportAction.EntityId`/`*ActionPayload`'s id
fields from `string` to `Guid` throughout, so `GuidHandler` canonicalizes every binding automatically
with no possibility of a capture site forgetting to call `EntityIdCanonicalizer` — was considered and
is not ruled out, but is a substantially larger refactor across the whole staged-action pipeline. It is
noted here as a future option, not decided or required by this ADR.

### Mechanism: a read-side systemic guard, `SqlIdCaseGuard` (#210)

The write-side guard above (`EntityIdCanonicalizer` + its cross-entity regression test) proves ids are
canonicalized *at capture*. It does not, by itself, prove every *read* against an id column is safe
against a value that — for whatever reason — did not go through that capture point (a value from before
this ADR existed, a future call site that reads an id from somewhere other than the documented capture
points, a defensive lookup against externally-supplied input). #210 found this gap directly: while
implementing Quotes.Id's own capture-point fix, a broader audit (prompted by the developer's explicit
instruction — "never assume a comparison that works won't break in the future simply because the known
references have the same logic") found several more case-sensitive id comparisons across
`Quotinator.Core`'s and `Quotinator.Data`'s `Sql.cs` files, `RepositorySql.cs` (the generic repository
layer shared by every entity), and a dynamically-assembled filter clause in `SqliteQuoteService` — none
of which the write-side guard alone could have caught, because they are read-side defects independent of
whether the underlying data is actually canonical.

**`Quotinator.Data.Diagnostics.SqlIdCaseGuard`** closes this the same way `SqlAggregateGuard` (CVE-2025-6965)
already closes its own class of SQL defect — a regex-based static analyzer, not a runtime check, applied
to every SQL string the codebase produces:

- Flags any comparison between an id-named column (`\w*Id`, alias-qualified or bracket-quoted) and a bound
  parameter (`= @param`, `IN @param`, or `NOT IN @param`) that is not wrapped `UPPER(column) = UPPER(@param)`
  on both sides (or, for an `IN`/`NOT IN` clause, at least the column side — the parameter side of an
  expanded list is the caller's responsibility to pre-canonicalize, not expressible as a single SQL-side
  wrap).
- Flags any comparison between **two id-named columns** — a JOIN `ON` condition or a correlated-subquery
  predicate (e.g. `s.Id = q.SourceId`, `cl2.ConversationId = cl.ConversationId`) — that is not wrapped
  `UPPER(...) = UPPER(...)` on both sides. This is defense-in-depth, not a correction of an active bug:
  both sides are already canonical by construction once write-side canonicalization is in place. It was
  added after the developer's explicit direction to wrap joins too, reversing this ADR's original position
  (an earlier draft of this section reasoned joins didn't need wrapping — that reasoning is superseded).
- A **half-protected** comparison (only one side wrapped) is still flagged in every case above — it is
  exactly as unsafe as an unwrapped one.
- `UPDATE ... SET` assignments are stripped before scanning (a write-side, capture-time concern this guard
  does not cover — that is `EntityIdCanonicalizer`'s job) so a raw `SET SourceId = @sid` assignment isn't
  misflagged as a read-side comparison.

**A gap in the guard's own reflection-based test enumeration was found and closed during the same pass**:
`EnumerateSqlConstants()` (in both `SqlQueryGuardTests.cs` files) originally called only `Type.GetFields`,
so a query declared as an arrow-bodied `static string` *property* rather than a field silently evaded
scanning entirely — `Sql.SystemImportActions.SelectById` was exactly this case, with a real, live,
unwrapped `WHERE Id = @id` that no test had ever exercised. `EnumerateSqlConstants()` now also calls
`Type.GetProperties`, and the fixed query has a dedicated regression test
(`SystemImportActionWriterReaderTests.GetByIdAsync_LowercaseStoredId_StillResolves`) that inserts a
lowercase-stored row via raw SQL to prove the read side resolves it independent of how it was written.

It is wired into the existing guard-test infrastructure via the same `DynamicData`-driven enumeration
methods `SqlAggregateGuard` already uses — `AllNamedSqlConstants`/`AssembledQueryCases` in both
`Quotinator.Core.Tests.Security.SqlQueryGuardTests` and `Quotinator.Data.Tests.Security.SqlQueryGuardTests`,
and `RepositorySqlCases` in `Quotinator.Data.Tests.Repositories.RepositorySqlGuardTests` — so every SQL
constant, every dynamically-assembled query, and every generic-repository factory method is scanned on
every test run, with no duplicated enumeration logic. `SqlIdCaseGuardTests`
(`Quotinator.Data.Tests.Diagnostics`) covers the guard's own regex logic directly (bare/prefixed/aliased/
bracket-quoted columns, half-protected wraps, `SET`-clause stripping, `IN`/`NOT IN` clauses, and both
unwrapped and half-wrapped JOIN/correlated-subquery conditions).

**This guard does not replace the write-side mechanism above — it is the read-side counterpart the
"Read-side" bullet under Decision already called for**, now made structural instead of relying on each
future query author remembering CLAUDE.md's case-insensitivity rule unaided.

### Mechanism: `IdClauses` — construct the comparison correctly, don't just catch it wrong

`SqlIdCaseGuard` catches a mistake after it's written. The developer's follow-on direction was to prevent
the mistake from being typed in the first place: **`Quotinator.Data.Queries.IdClauses`** is a small
public static class — `Equals(column, paramName)`, `In(column, paramName)`, `NotIn(column, paramName)`,
`Join(leftColumn, rightColumn)` — each returning the correctly `UPPER()`-wrapped SQL fragment for that
shape of comparison. Every fixed query and factory method across `Quotinator.Core.Queries.Sql`,
`Quotinator.Data.Queries.Sql`, and `RepositorySql.cs` was rewritten to call it instead of hand-typing
`UPPER(...)` text.

A fixed query that calls `IdClauses` cannot remain a compile-time `const` — C# does not allow a method
call in a constant expression — so every converted query became `static readonly` instead (evaluated
once at type-init time; functionally identical to a `const` for every consumer). This is what made the
reflection gap above ((`GetFields` only) surface: it was already a latent gap before this ADR's own
mechanism started producing `static readonly` fields, but converting queries to call `IdClauses` is what
made the gap load-bearing enough to matter, and specifically motivated widening the enumeration to also
cover the pre-existing property case found along the way.

`IdClauses` is deliberately minimal — four methods matching the four SQL shapes this codebase actually
uses (parameter equality, `IN`, `NOT IN`, column-to-column). It does not attempt to generate whole
queries or clauses; it only owns the comparison fragment itself, the same scoping discipline
`PaginationParsing`/`EntityFilterParsing` already established for other shared query-building concerns.

---

## Reasoning

### Fixing the read side alone is treating a symptom, not the disease

CLAUDE.md's own "No exception-based migration recovery" section already establishes this project's
general preference: "fix the root cause instead of adding a check." A `UPPER()`-wrapped query is a check
— it tolerates bad data instead of preventing it. Every un-audited query against the same column remains
a landmine. Canonicalizing at capture removes the landmine entirely rather than requiring it be
rediscovered and individually defused, query by query, indefinitely.

### Two write paths deriving from one shared value must be fixed together, or not at all

The Quotes.SourceId/Sources.Id incident is the concrete proof that partial fixes in this area are not
just incomplete — they are actively dangerous. Before touching any write path that consumes an
externally-supplied id, every other write path that consumes the *same in-memory value* must be
identified and included in the same change. This is why the follow-on issue (#207)
requires the fix to be verified against the Quote→Source join specifically, not only the endpoint that
first surfaced the bug.

### Verify before implementing, every time this bug class recurs

This is the third time this specific bug class (id casing) has been found and generalised in this
project's history (#154, #69/#180, and now this ADR). Each prior instance was fixed locally, at the
query that exhibited the symptom, and each time a later instance was found in a sibling query the
original fix hadn't covered. This ADR exists specifically so the next instance is checked against a
written rule instead of being independently rediscovered and independently reasoned through again.

---

## Consequences

- The follow-on issue (#207, spawned from #190's T2 pass) must build `EntityIdCanonicalizer` and its
  guard tests as described above, then canonicalize
  `ImportActionPlanner`'s capture points for `SourceEntry.Id`/`PersonEntry.Id` through it (both the Add
  path and, separately, the correction-match path — which has its own related risk of seeding a
  same-batch index from the file's casing rather than the matched row's actual stored id) — and must
  audit every `Ensure*ExistsAsync` helper and every `Sql.*.Insert`/`Update` binding an entity id as a raw
  `string` for the same gap.
- `docs/database-conventions.md`'s new "Entity id casing" section references this ADR alongside the
  existing case-insensitive-query rule, so a future entity with a file-authored explicit id
  (`StageDirection`/`SoundCue`/`Conversation` already have one; any future entity that adds one) checks
  this ADR before assuming read-side tolerance is sufficient.
- This does not require a schema or migration change — it is an application-code discipline, enforced at
  the point external ids enter the system, not a database constraint.
- #210 delivered the read-side counterpart described above (`SqlIdCaseGuard`) and used it to find and fix
  every case-sensitive id comparison across `Quotinator.Core`/`Quotinator.Data`'s `Sql.cs` files,
  `RepositorySql.cs`, and `SqliteQuoteService`'s dynamic filter clauses — not just Quotes.Id, the issue's
  original scope. Any future SQL query that compares an id column to a bound parameter, another id
  column, or an `IN`/`NOT IN` list without wrapping both sides in `UPPER(...)` now fails the build's test
  suite automatically; this is no longer a discipline that depends on the author remembering CLAUDE.md's
  rule.
- #210 also delivered `IdClauses` (`Quotinator.Data.Queries`), now the required way to build any id
  comparison in new or edited SQL — call `IdClauses.Equals`/`In`/`NotIn`/`Join`, don't hand-type
  `UPPER(...)`. `SqlIdCaseGuard` remains the backstop for the rare case where calling it isn't practical
  (an id embedded in a larger hand-assembled fragment), not the primary mechanism.
- `docs/database-conventions.md`'s "Entity id casing" table was updated to reference both `IdClauses` and
  `SqlIdCaseGuard`, and to reverse its prior "joins don't need wrapping" framing.
