# ADR 011 — Series/Universe hierarchy and Character↔Source many-to-many identity

**Status:** Accepted
**Date:** 2026-07-15
**GitHub issues:** #169, #179, #174

---

## Context

Character is currently per-Source scoped: `Character.SourceId` is a required FK, and the natural key
is `(SourceId, Name)`. While planning #174 ("Character: from per-Source to global identity"), the
original approach was to copy Person's shape exactly — drop `SourceId` entirely and merge every row
sharing a `Name` into one global row.

#169's research (see its closing comment and `docs/milestones/data-import-sources/
169-universe-setting-research-plan.md`) found this concretely wrong, not merely risky, given data
already bundled with this project:

- The bundled dataset already contains real franchises (Lord of the Rings, The Hobbit, Star Wars,
  Terminator) where the same character legitimately appears across multiple Source rows (e.g. Gandalf
  across six films). A Source-less global row cannot represent this — Character needs a many-to-many
  relationship to Source, not zero relationship.
- The same character Name can validly refer to different, distinct portrayals across different
  media — a book adaptation's Gandalf and a film adaptation's Gandalf are different Characters
  despite sharing a Name and a fictional universe.

This ADR is deliberately scoped to the **structural shape only** — the hierarchy, the join table, and
the identity-anchor invariant. It does not decide the Character merge *algorithm* (which existing
per-source rows actually get consolidated into which global rows) — that is #174's own, separate ADR,
which operates within the boundary this ADR establishes.

---

## Decision

### 1. Universe → Series → Source hierarchy, one-to-many at both levels

A new `Universe` table (a fictional world or franchise, e.g. "Middle Earth") and a new `Series` table
(a direct continuity within a universe, e.g. "The Lord of the Rings" trilogy, "The Hobbit" trilogy)
are added. A `Series` belongs to at most one `Universe` (nullable FK); a `Source` belongs to at most
one `Series` (nullable FK). Not many-to-many at either level — no genuine one-Source-belongs-to-many-
Series case was identified during #169's research, and this project's Simplicity priority (ranked
above Extensibility in `CLAUDE.md`'s "Project Priorities") favours the narrower shape. A `Source`/
`Series` with no parent is implicitly standalone (e.g. Casablanca has no Series; a standalone Series
has no Universe).

### 2. Character ↔ Source becomes many-to-many via `CharacterSources`

`Characters.SourceId` (a required FK) and its `UNIQUE (SourceId, Name)` constraint are dropped. A new
`CharacterSources` join table replaces them, following this project's junction-table convention (ADR
002 — `RecordBase` on every table without exception, including junction tables, with a synthetic
`Id` surrogate key and a `UNIQUE` constraint on the natural key pair): `Id`, `CharacterId`,
`SourceId`, the standard `RecordBase` audit columns, `UNIQUE (CharacterId, SourceId)`. No
`CompletenessStatus`/`NoValueKnown` columns — mirrors `QuoteGenres`' own junction-table shape exactly
(a link row has no content field that could itself be incomplete).

### 3. `Universe`/`Series` get the full standard entity shape

Both tables receive the complete shape already used by `Source`/`Character`/`Person`: `RecordBase`
audit columns plus `ImportBatchId`, `CompletenessStatus`, `NoValueKnown` — not a lighter, RecordBase-
only shape. Reasoning: `Universe`/`Series` rows will be created and corrected through the same
staged-import machinery as every other entity (a curated overlay file per #180, and potentially
future bundled-source population), so they need the same `CompletenessGuard`/decide-time machinery
(#165/#168) uniformly available, rather than special-casing two tables out of an otherwise-consistent
pattern.

### 4. `Source.Type` is a hard identity anchor

Two `Character` rows must never be merged into one if their linked Sources disagree on `Type`. This
is stated here as an invariant for #174's own (separate) merge-algorithm ADR to operate within — this
ADR does not decide *how or when* #174 applies it, only that the boundary must never be crossed.

### 5. This ADR's own migration performs zero data merging

Every existing `Characters` row is reshaped 1:1: its current `SourceId` becomes exactly one
`CharacterSources` row. No two existing rows are combined by this migration. This is an explicit
design choice, not an oversight — it keeps this structural change's own risk profile at zero,
independent of the harder, still-undecided merge algorithm #174 will build on top.

---

## Consequences

- `EntityIdentity.CharacterId`, `Sql.Characters.SelectIdBySourceAndName`,
  `Sql.Characters.InsertIfNotExists`, `Sql.Characters.CountActiveReferences`, and
  `Sql.Sources.CountActiveReferences` all change mechanism (querying through `CharacterSources`
  instead of a `Characters.SourceId` column) as part of #179's own implementation. #179 preserves
  today's per-Source *meaning* for these — only #174 changes the *meaning* to reflect global,
  Type-anchored identity.
- `CharacterActionPayload` and `ResolveCharacterAsync` (`ImportActionPlanner.cs`) are left
  operating in terms of a single `SourceId` per Character by #179 — #174 is where these change to
  reflect the new many-to-many, Type-anchored reality.
- Populating `Series`/`Universe` values on existing Sources needs no new import mechanism — a
  hand-authored curated overlay file (#180), reusing #162's already-shipped Source Modify/
  decidability path.
- #174's merge algorithm may consolidate little to nothing beyond what's already explicitly curated
  until `Series`/`Universe` data is populated over time — an intentional, conservative starting
  point per #174's own plan doc, not a shortfall of this ADR's design.

---

## Follow-on

- #179 — implements this ADR's schema (Migration009, baseline update, call-site mechanism changes)
- #174 — Character merge algorithm, operating within this ADR's structural boundary (own, separate
  ADR)
- #180 — populates `Series`/`Universe` data via a curated overlay file
- #169 — the research that surfaced the need for this ADR (closed, see its closing comment)
