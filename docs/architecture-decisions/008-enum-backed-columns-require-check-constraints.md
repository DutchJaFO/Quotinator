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

A related question also needed separating out: `System_ImportConflicts.Status` (#149) and the new
`System_ImportActions.Status`/`ActionType` (#154) are deliberately **not** backed by a real C#
`enum` — they're open string constants (`ImportConflictStatus`, `ImportActionStatus`,
`ImportActionKind`), specifically so `Quotinator.Data` (a domain-agnostic, reusable library per
ADR 004) never forces a closed vocabulary onto a future consumer with different needs (e.g. a
consumer that wants a `"Remove"` action type `Quotinator.Engine` doesn't need today). This decision
needs to state clearly that those columns are an intentional, documented exception — not another
unexplained inconsistency for someone to "fix" later by copying `Type`'s CHECK onto them.

---

## Decision

**Every database column whose value is backed by a genuine, closed C# `enum` type must have a SQL
`CHECK` constraint enumerating the same member names**, at the point the column is first created
(in the `CREATE TABLE`, or inline on the `ALTER TABLE ADD COLUMN` that introduces it — verified
empirically against the actual bundled SQLite runtime that `ALTER TABLE ... ADD COLUMN col TEXT ...
CHECK (col IN (...))` is valid syntax, so a new column never needs the expensive rebuild-migration
dance just to carry a `CHECK`).

**Columns intentionally backed by an open string-constant set — not a closed C# `enum` — are
exempt**, and must say so in their own XML doc comment, mirroring the existing precedent in
`SystemImportConflict.cs`/`SystemImportAction.cs`: *"this project has no dependency on any specific
domain schema."* The tell: is there a real `enum SomeName { A, B, C }` backing the property, or a
`static class SomeName { public const string A = "A"; ... }`? Only the former gets a `CHECK`.

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
  any future one) keep their open string-constant columns unconstrained by design — this ADR does
  not apply to them, and their entity doc comments already say why.
- `ImportBatches.ConflictPolicy` remains a known, tracked gap under this rule — not fixed by this
  ADR, and not to be treated as precedent for a future column's design.
- The existing schema-drift tests (`Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues`
  et al.) already verify a `CHECK`, once added, behaves identically on both the incremental-replay
  and fresh-baseline paths — this ADR doesn't change that test's job, it just governs *when* a
  `CHECK` must exist in the first place.
