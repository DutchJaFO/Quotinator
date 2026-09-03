# ADR 011 — Universe/Series/Season hierarchy and Character↔Source many-to-many identity

**Status:** Accepted
**Date:** 2026-07-15
**GitHub issues:** #169, #179, #174, #375

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

A quote carries no date or title of its own — both are read from the Source row it points at — so for a
serialised work whose Source is the whole series, a quote from one instalment has nowhere to record
which instalment it came from. Four bundled television titles already carry such quotes. Grouping
instalments is a structural gap in the hierarchy below, not a property of any one medium.

This ADR is deliberately scoped to the **structural shape only** — the hierarchy, the join table, and
the identity-anchor invariant. It does not decide the Character merge *algorithm* (which existing
per-source rows actually get consolidated into which global rows) — that is #174's own, separate ADR,
which operates within the boundary this ADR establishes.

---

## Decision

### 1. Universe → Series → Season → Source hierarchy, one-to-many at every level

A `Universe` table (a fictional world or franchise, e.g. "Middle Earth"), a `Series` table (a direct
continuity within a universe, e.g. "The Lord of the Rings" trilogy, "The Hobbit" trilogy), and a
`Season` table (an ordered grouping of Sources within a Series). A `Series` belongs to at most one
`Universe`; a `Season` belongs to at most one `Series`; a `Source` belongs to at most one `Series` and
at most one `Season`. Every parent FK is nullable, and a row with no parent is implicitly standalone
(e.g. Casablanca has no Series; a standalone Series has no Universe). Not many-to-many at any level —
no genuine one-Source-belongs-to-many-Series case was identified during #169's research, and this
project's Simplicity priority (ranked above Extensibility in `CLAUDE.md`'s "Project Priorities")
favours the narrower shape.

**`Season` is not television-specific.** It is an ordered grouping of Sources within a Series, and
applies equally to a magazine's volumes or a podcast's seasons. Nothing about it keys off
`Source.Type`, and no behaviour is conditioned on the medium.

**A Source's granularity follows what can be established, not a fixed rule.** Where an instalment is
identified, the Source is that instalment and also carries a `Season`; where it is not, the Source is
the whole work and carries none. Both are ordinary Sources, so a quote always has one to point at and
`Quote.SourceId` stays non-nullable. Refining a Source from the whole work to one instalment later is
an improvement to that row, not the repair of a broken one — attribution is expected to be partial and
to improve over time.

**`Season` is keyed on its parent and an ordinal**, unlike `Universe` and `Series` whose `Name` is
globally unique. It carries `Number` (required) plus optional `Title` and `Subtitle` — Avatar: The Last
Airbender's first season is `Number` 1, "Book One", "Water", rendering "Book One: Water" — and its
natural key is `UNIQUE (SeriesId, Number)`, because an ordinal only means anything within its parent
and "Season 1" recurs for every series. `EntityIdentity.SeasonId` therefore takes the parent id
alongside the number, as `CharacterId` takes `sourceId`.

### 2. Character ↔ Source becomes many-to-many via `CharacterSources`

`Characters.SourceId` (a required FK) and its `UNIQUE (SourceId, Name)` constraint are dropped. A new
`CharacterSources` join table replaces them, following this project's junction-table convention (ADR
002 — `RecordBase` on every table without exception, including junction tables, with a synthetic
`Id` surrogate key and a `UNIQUE` constraint on the natural key pair): `Id`, `CharacterId`,
`SourceId`, the standard `RecordBase` audit columns, `UNIQUE (CharacterId, SourceId)`. No
`CompletenessStatus`/`NoValueKnown` columns — mirrors `QuoteGenres`' own junction-table shape exactly
(a link row has no content field that could itself be incomplete).

### 3. `Universe`/`Series`/`Season` get the full standard entity shape

All three tables receive the complete shape already used by `Source`/`Character`/`Person`: `RecordBase`
audit columns plus `ImportBatchId`, `CompletenessStatus`, `NoValueKnown` — not a lighter, RecordBase-
only shape. Reasoning: their rows are created and corrected through the same staged-import machinery as
every other entity (a curated overlay file per #180, and potentially future bundled-source population),
so they need the same `CompletenessGuard`/decide-time machinery (#165/#168) uniformly available, rather
than special-casing tables out of an otherwise-consistent pattern.

They are also read through the same generic repository (`IListableRepository<T>` against
`SqliteRepository<T>`) and exposed under the same `/api/v1/masterdata/` prefix as every other masterdata
entity. Neither a bespoke repository nor a separate route convention is introduced for any of them.

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
- A `Season` between `Series` and `Source` means two Sources sharing a title can be told apart by the
  instalment they belong to, which is what lets a serialised work hold quotes from more than one of its
  parts without either collapsing them or splitting the work in two.
- Because a Source may be the whole work or one instalment, the same query returns rows of differing
  granularity, and a consumer must not infer the medium or the level of detail from a Source's presence
  alone. This is the accepted cost of never leaving a quote without a Source to point at.

---

## Follow-on

- #179 — implements this ADR's schema (Migration009, baseline update, call-site mechanism changes)
- #174 — Character merge algorithm, operating within this ADR's structural boundary (own, separate
  ADR)
- #180 — populates `Series`/`Universe` data via a curated overlay file
- #169 — the research that surfaced the need for this ADR (closed, see its closing comment)
- #375 — implements `Season`, its `Source` link, and its masterdata endpoints
