# ADR 008 — Enum-backed database columns require a matching CHECK constraint

**Status:** Accepted
**Date:** 2026-07-07
**GitHub issues:** #154

---

## Context

While adding `ImportBatches.Status` (backed by the new `ImportBatchStatus` enum: `Staged`,
`Applied`, `Discarded`) during #154, the question of whether to add a `CHECK` constraint was
answered by comparing two existing columns rather than by checking documentation:

- `ImportBatches.Type` (backed by `ImportBatchType`) has
  `CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed'))`.
- `ImportBatches.ConflictPolicy` (backed by `DuplicateResolutionPolicy`) has **no** `CHECK` at all —
  just `TEXT NOT NULL DEFAULT 'skip'`.

Neither `docs/architecture-decisions/`, `docs/decisions/` (empty at the time of writing), nor
`CLAUDE.md` documented which of these two existing patterns was the intended rule versus an
oversight. Picking one by pattern-matching existing code — rather than checking whether a real
decision existed — is the exact failure mode ADR 002 already describes (`SystemAuditEntry` shipped
without `RecordBase` because nobody checked whether ADR 002 governed it; each subsequent entity
then copied that same deviation instead of checking independently). This ADR exists so the next
enum-backed column has an actual answer to check, not another inconsistent example to copy.

A related question also needed separating out, and was initially answered wrong in an earlier draft
of this ADR: `System_ImportConflicts.Status` (#149) and `System_ImportActions.Status`/`ActionType`
(#154) were first written as open string constants (`ImportConflictStatus`, `ImportActionStatus`,
`ImportActionKind` as `static class`es), reasoning that `Quotinator.Data` (a domain-agnostic,
reusable library per ADR 004) should never force a closed vocabulary onto a future consumer. That
reasoning conflated two different kinds of column. `Status`/`ActionType` are **not** consumer
vocabulary — they are states `Quotinator.Data`'s own coordinator logic (`ConflictResolutionCoordinator`,
`ImportActionResolutionCoordinator`) exclusively assigns and transitions between; no consuming
project ever writes or invents a new one. That makes them exactly the kind of closed, Data-owned set
this ADR's `CHECK` rule is about, and they were converted to real `enum`s (`ImportConflictStatus`,
`ImportActionStatus`, `ImportActionKind`) with matching `CHECK` constraints. The genuinely
consumer-defined, open-vocabulary fields on the same rows — `EntityType` (a consumer's own entity
type name) and the loose `BatchId`/`ExistingBatchId` string references (to a consumer's own batch
table, which `Quotinator.Data` doesn't know the schema of) — correctly remain plain strings; the
distinguishing question is *who defines the set of possible values*, not *which project the table
lives in*.

---

## Decision

**Every database column whose value is backed by a genuine, closed C# `enum` type must have a SQL
`CHECK` constraint enumerating the same member names**, at the point the column is first created
(in the `CREATE TABLE`, or inline on the `ALTER TABLE ADD COLUMN` that introduces it).

Confirmed against the official SQLite documentation
([`lang_altertable.html`](https://www.sqlite.org/lang_altertable.html), not just empirical testing)
— `ADD COLUMN` explicitly supports a `CHECK` constraint on the new column: *"When adding a column
with a CHECK constraint... the added constraints are tested against all preexisting rows in the
table and the ADD COLUMN fails if any constraint fails."* The complete, verbatim restriction list
for a column added via `ADD COLUMN` is:

- The column may not have a `PRIMARY KEY` or `UNIQUE` constraint.
- The column may not have a default value of `CURRENT_TIME`, `CURRENT_DATE`, `CURRENT_TIMESTAMP`,
  or an expression in parentheses.
- If a `NOT NULL` constraint is specified, then the column must have a default value other than
  `NULL`.
- If foreign key constraints are enabled and a column with a `REFERENCES` clause is added, the
  column must have a default value of `NULL`.
- The column may not be `GENERATED ALWAYS ... STORED`, though `VIRTUAL` columns are allowed.

None of these forbid a `CHECK` constraint, so a new enum-backed column never needs the expensive
rebuild-migration dance just to carry one — as long as it also satisfies the restrictions above
(most commonly relevant here: `NOT NULL` requires a real default, which a `CHECK (col IN (...))`
naturally needs anyway so pre-existing rows backfill to a valid value).

**Columns whose value set is genuinely open — defined and extended by a consuming project, not by
the project that owns the table — are exempt**, and must say so in their own XML doc comment. In
`Quotinator.Data`, this means fields like `SystemImportConflict.EntityType` or
`SystemImportAction.BatchId`/`ExistingBatchId`: free-text values a consumer invents and
`Quotinator.Data` never branches on. It does **not** mean every column on a domain-agnostic table —
`Status`/`ActionType` on those same tables are a closed set `Quotinator.Data` itself defines and
transitions between, so they get a real `enum` and a `CHECK` like any other. The tell is *who
defines the set of possible values*: if only this project's own code ever assigns one, it's a
closed `enum SomeName { A, B, C }` with a `CHECK`; if a future consumer could legitimately introduce
a value this project has never seen, it's an open `string`, exempt from this rule and documented as
such.

**Widening or otherwise changing an existing enum-backed column's `CHECK`** still requires the
create-rebuild-rename dance already established (`Migration004_ImportBatchTypeUserSeed` is the
worked example) — SQLite has no `ALTER TABLE ... MODIFY CHECK`. This ADR only changes when a
`CHECK` is added for the *first* time (at column-creation time), not how to change one afterward.

---

## Reasoning

### Two independent layers of protection are cheaper than reasoning about which one to trust

The C# `enum` type system stops invalid values from entering through application code. It does
**not** stop an invalid value entering through raw SQL (a one-off admin script, a future migration
bug, a manual `sqlite3` edit, a different consuming application against the same database file).
Without the `CHECK`, the column's real contract is "whatever the C# code happens to write today" —
an assumption that erodes the moment any other code path touches the table. The `CHECK` makes the
column's actual contract enforceable at the one layer every writer must pass through, regardless of
which application or script is doing the writing.

### The existing inconsistency was cost-free to introduce and easy to miss

`ConflictPolicy` most likely never got a `CHECK` simply because nobody was asked the question at
the time — not because of a considered reason to omit it. That is itself the risk this ADR closes:
a schema convention that exists only as "whatever the last similar column happened to do" drifts
further from consistent with every new column, and by the time it's noticed, retrofitting it means
a migration instead of a comment.

### Retrofitting `ConflictPolicy` is out of scope for this ADR

This ADR governs new columns going forward. `ConflictPolicy` is a known, pre-existing gap under
this rule — fixing it means a rebuild migration (an existing, currently-unconstrained column), not
a one-line addition. Whether to do that now or track it separately is a scope decision for whoever
picks it up, not settled by this ADR.

---

## Consequences

- Every future enum-backed column addition (in either `Quotinator.Data` or `Quotinator.Engine`)
  must include a `CHECK` in the same commit that introduces the column — reviewers can check this
  ADR instead of guessing from nearby code.
- `Quotinator.Data`'s domain-agnostic tables (`System_ImportConflicts`, `System_ImportActions`, and
  any future one) still get `CHECK`-constrained `enum` columns for their own Data-owned state
  (`Status`, `ActionType`) — only their genuinely consumer-defined fields (`EntityType`, `BatchId`,
  `ExistingBatchId`) stay open strings by design, and their entity doc comments say why per field.
- `ImportBatches.ConflictPolicy` remains a known, tracked gap under this rule — not fixed by this
  ADR, and not to be treated as precedent for a future column's design.
- The existing schema-drift tests (`Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues`
  et al.) already verify a `CHECK`, once added, behaves identically on both the incremental-replay
  and fresh-baseline paths — this ADR doesn't change that test's job, it just governs *when* a
  `CHECK` must exist in the first place.
