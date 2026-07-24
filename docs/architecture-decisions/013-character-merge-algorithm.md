# ADR 013 — Character merge algorithm: Type-anchored, Series-scoped global identity

**Status:** Accepted
**Date:** 2026-07-24
**GitHub issue:** #174

---

## Context

ADR 011 (#179) established the structural shape Character identity now operates within: a
`Universe` → `Series` → `Source` hierarchy, `CharacterSources` as a many-to-many join between
Character and Source, and `Source.Type` as a hard identity anchor (two Characters must never merge
if their linked Sources disagree on `Type`). ADR 011 deliberately performed zero data merging — every
pre-existing `Characters` row was reshaped 1:1 into one `CharacterSources` link, and it explicitly
left the merge *algorithm* to this ADR.

This ADR decides: which per-Source `Character` rows actually consolidate into one global row, what
happens to divergent `CompletenessStatus`/`NoValueKnown` values when they do, what `Characters`' new
uniqueness constraint (if any) looks like, and how `EntityIdentity.CharacterId` derives a stable id
under the new model. The same algorithm applies twice — once retroactively, in a one-time migration
consolidating already-imported rows, and forever afterward, prospectively, every time a new quote's
Character is resolved during import.

#174's own plan doc floated a *speculative* design during planning ("likely `Name` plus a
`Source.Type`-derived component" for both the merge key and `EntityIdentity.CharacterId`'s new
signature) but explicitly left the actual algorithm undecided. Working through it here surfaced a
real correctness problem with that speculation — see Decision 5.

---

## Decision

### 1. The merge-candidate test

Two Characters — or an existing Character and an incoming (Source, Name) pair being resolved during
import — are the **same identity** if and only if all of:

- **(a) Name matches case-insensitively.** Unlike `Sources.Title` (deliberately case-sensitive —
  `Sql.Sources.SelectIdByTitleAndType`'s remark), Character storage always preserves the exact casing
  a Name was originally written with (display is never normalised), but the merge-candidate
  *comparison* itself folds case — confirmed directly by the developer during this ADR's authoring,
  correcting an initial draft that (wrongly) extended `Sources.Title`'s case-sensitive precedent to
  Character as well. `EntityIdentity.CharacterId`'s stable-id hash was already case-insensitive by
  construction (`QuoteIdentity.Normalise` lowercases every hashed part), so no change was needed there
  — only the SQL lookup (Decision 7) and the migration's own grouping (Decision 3) needed an explicit
  `LOWER()` wrap, since SQLite's default `TEXT` comparison is case-sensitive.
- **(b) `Source.Type` anchor matches.** Both are linked (directly or, for the Character side,
  via any of its existing `CharacterSources` links) to a Source of the identical `Type`. Hard
  invariant, per ADR 011 — never relaxed.
- **(c) A Series relationship is known.** At least one Source already linked to the candidate
  Character shares a non-null `SeriesId` with the Source being resolved.

Condition (c) is the conservative-by-default gate. A Source with no `SeriesId`, or a candidate
Character none of whose linked Sources share the new Source's `SeriesId`, is **never** treated as the
same identity — a new, separate Character row is created (or, for an already-linked-to-this-exact-
Source case, condition (c) is trivially satisfied since the shared Source itself counts as the
signal — see Decision 7).

**Universe-level relationships are deliberately not used as a merge signal**, only Series. A shared
`Universe` is a much broader, weaker signal than a shared `Series` (e.g. an animated spin-off and a
mainline film trilogy can share a Universe while portraying a character quite differently) — using it
would risk exactly the kind of over-merging #169's research already found concretely wrong for
Name-alone matching, one level down. This project's Simplicity priority (ranked above Extensibility in
`CLAUDE.md`) favours the narrower, safer signal. Revisit only if real bundled data shows a concrete
need — not preemptively.

### 2. One algorithm, two applications

The identical test in Decision 1 is applied:

- **Retroactively**, once, by this issue's migration — consolidating rows that predate Series/Universe
  data entirely, using whatever Series data happens to be populated (via #180's curated overlay file)
  at migration time.
- **Prospectively**, forever after, by `ResolveCharacterAsync` — every time a quote introduces a
  Character, checking the same conditions against already-committed database state.

Keeping these textually identical (not two independently-maintained implementations) is itself a
design goal — see Decision 8 for the one place they intentionally still diverge in practical coverage.

### 3. Canonical survivor selection (migration only)

Within a merge group — grouped by `LOWER(Name)` (Decision 1(a)), `SourceType`, and the Series-relatedness
test — the surviving row is the one with the earliest `DateCreated`, tie-broken by the lexicographically
smallest `Id`. The survivor's own `Name` (whatever casing it happens to carry) is kept as-is; grouping by
`LOWER(Name)` never rewrites any row's stored casing, it only decides which rows count as the same group.
Every other row's `CharacterSources` links and every `Quotes.CharacterId` referencing it are re-pointed to
the survivor; the merged-away rows are then soft-deleted (never hard-deleted — consistent with this
project's system-wide "soft-deleted rows are invisible by default, everywhere" invariant, `CLAUDE.md`).

### 4. Divergent `CompletenessStatus`/`NoValueKnown` resolution

`CompletenessStatus` resolves to the **most-reviewed** value across the merged group:
`Complete` > `NeedsReview` > `Incomplete` (i.e. if any merged row is `Complete`, the survivor becomes
`Complete`).

`NoValueKnown` is always `[]` for Character today — `Character`'s only field is `Name` (required,
never itself markable as "no value known"), so there is currently nothing for this array to contain
for any pre-existing row. The migration therefore sets the survivor's `NoValueKnown` to `[]` directly
rather than implementing a general JSON-array-union mechanism this codebase has no other precedent for
(no existing SQL anywhere in this project relies on SQLite's JSON1 functions). If Character ever gains
an optional field with real `NoValueKnown` entries, a proper union rule becomes the responsibility of
whichever future issue adds that field — not preemptively built here for a case that cannot occur yet.

### 5. `EntityIdentity.CharacterId`'s signature: `(sourceId, name, sourceType)` — not `(name, sourceType)`

The plan doc's own speculative framing suggested dropping `sourceId` entirely, deriving the stable id
from `(name, sourceType)` alone. **This would be a real correctness bug, not just a naming choice.**

Because two independent Characters can legitimately share the same `(Name, SourceType)` when no Series
relationship connects them (Decision 1's condition (c) — e.g. two wholly unrelated movies each having a
character literally named "Sam"), a hash keyed purely on `(name, sourceType)` would deterministically
compute the **same id** for both the moment each is introduced for the first time. `Characters.
InsertIfNotExists`'s `INSERT OR IGNORE` would then silently no-op the second insert against the first
one's row, and the caller's subsequent `CharacterSources.InsertIfNotExists` would attach the wrong
Source to the *first* "Sam" — silently merging two genuinely distinct Characters, exactly the failure
mode this whole ADR exists to prevent, just one level down from where #169 originally found it.

Retaining `sourceId` in the hash preserves today's actual guarantee: two batches concurrently
introducing the same never-before-seen `(sourceId, name)` pair still compute the identical id and
safely coalesce via `INSERT OR IGNORE` (`Sql.Characters.InsertIfNotExists`'s existing remark on
concurrent-batch safety, unchanged in substance). `sourceType` is included for defense-in-depth and to
make the anchor invariant explicit in the id derivation itself, even though it is technically derivable
from `sourceId` via a `Sources` lookup.

This EntityIdentity-computed id is used **only** as the fallback when Decision 7's lookup finds no
existing match at all. An actual match always reuses the *found* row's real, already-existing id —
never a freshly computed hash.

### 6. `Characters` gains a denormalized `SourceType` column — no new `UNIQUE` constraint

`Characters.SourceType` (`TEXT NOT NULL`, `CHECK` matching `Sources.Type`'s value list —
`'Unknown','Movie','Tv','Anime','Book','Person'` — per ADR 008's enum-backed-column rule) makes the
Type anchor a first-class, directly-queryable property of the Character row itself, instead of
requiring a join through `CharacterSources`/`Sources` on every lookup. Since every Source linked to a
given Character is already guaranteed (by the anchor invariant) to share one `Type`, this column is
never ambiguous about "which linked Source's Type."

**Deliberately no `UNIQUE (Name, SourceType)` constraint** — unlike `Sources`' own `UNIQUE (Title,
Type)`, two independent Characters *can* legitimately share `(Name, SourceType)` (Decision 1/5).
A database-level uniqueness constraint on that pair would actively reject legitimate data. Deduplication
is enforced entirely at the application/staging layer, via Decision 1's merge-candidate test — mirroring
existing precedent already in this codebase: `Quotes` itself has no natural-key `UNIQUE` constraint
either, for the analogous reason (its own multi-attribute dedup logic lives entirely in
`ImportActionPlanner`/the conflict-resolution policies, not the schema).

### 7. A single unified lookup query replaces the old per-Source-only one

`Sql.Characters.SelectIdBySourceAndName` (already rewritten once by #179, preserving old per-Source
*meaning*) is replaced by one query implementing Decision 1's full test:

```
SELECT c.Id FROM Characters c
JOIN CharacterSources cs ON cs.CharacterId = c.Id AND cs.IsDeleted = 0
JOIN Sources s2          ON s2.Id = cs.SourceId
WHERE LOWER(c.Name) = LOWER(@name) AND c.SourceType = @sourceType AND c.IsDeleted = 0
  AND (cs.SourceId = @sourceId OR (s2.SeriesId IS NOT NULL AND s2.SeriesId = @seriesId))
ORDER BY (cs.SourceId = @sourceId) DESC, c.DateCreated ASC
LIMIT 1;
```

`LOWER(c.Name) = LOWER(@name)` — not a bare `Name = @name` — per Decision 1(a). The stored row's
`Name` itself is left exactly as originally written; only this comparison folds case.

The "already linked to this exact Source" case (today's #179 behaviour) is subsumed as a trivial case
of the same `OR` condition — no separate fast path is needed as distinct code. `@seriesId` is the
resolving quote's own Source's `SeriesId` (nullable); when it is `NULL`, the second `OR` branch can
never be true (SQL `NULL = NULL` is unknown, not true), which naturally implements the
conservative-by-default fallback with no extra special-casing.

### 8. Known, accepted scope limitation: a quote's own resolving Source must already be committed, with its SeriesId already set, for Series-scoped matching to apply

`ResolveCharacterAsync`'s Series-relatedness signal (Decision 7's `@seriesId` parameter) comes from a
plain `SELECT SeriesId FROM Sources WHERE Id = @sourceId` (`Sql.Sources.SelectSeriesIdById`) —
`ImportActionPlanner` is read-only against the database during planning (its own doc-comment:
"Read-only against the database — never writes"), so this always returns `NULL` for a Source that is
itself only *staged* (not yet applied) within the current batch, even when that Source's own
`sources[]` entry declares a real `seriesName` in the very same file. Concretely: **the current quote's
own resolving Source must already exist as a committed row, with its `SeriesId` already set, for
Series-scoped matching to have anything to match against.** This is broader than an earlier draft of
this ADR assumed (which described only the *candidate* Character's linked Sources as needing to be
committed) — verified live during this ADR's own T2 pass, where two quotes each introducing their own
brand-new Source (with a `sources[]`/`seriesName` declaration in the *same* file as the quote) did not
merge, because neither Source existed in the database yet at the moment its own quote's Character was
resolved.

**The correct, working workflow** (also verified live): commit the Sources and their `Series` links
*first* — either via a `sources[]`-only import with no quotes (as demonstrated live), or via #180's
curated-overlay pattern — so that by the time a Character-bearing quote references that Source,
`ResolveSourceAsync` finds it as a genuinely pre-existing row via natural-key lookup and its real,
committed `SeriesId` is available. Two quotes, each in its own separate batch, referencing two
different but already-Series-linked Sources under the same Character name, correctly merge into one
Character — confirmed live via Docker T2: importing "The Fellowship of the Ring"/"The Two Towers" (pre-
committed, same Series) each with an "Aragorn174" quote in separate batches produced exactly one
`Aragorn174` Character row linked to both Sources; the identical setup with one Source `Movie`-typed
and the other `Book`-typed correctly produced two separate rows despite sharing a Series (Type anchor,
Decision 1(b), holds even here).

This mirrors an existing, already-accepted limitation of this exact shape (Source/Person natural-key
resolution has the identical "won't dedupe against other not-yet-committed rows" property), and keeps
the migration and the ongoing algorithm textually identical rather than requiring a second,
same-batch-aware merge engine that would need to mirror `PlanSourcesAsync`'s own resolution logic
(existing-by-id, existing-by-natural-key changed/unchanged/blocked/pending branches, and the Add
fallback) to predict a not-yet-committed Source's eventual `SeriesId` correctly. Revisit only if this
proves to matter in practice for a real bundled dataset — not preemptively.

### 9. `CharacterActionPayload` and apply-time code need no changes

`CharacterActionPayload`'s existing 4 fields (`SourceId, Name, SourceTitle, SourceType`) already
represent "the specific Source this particular Add action is linking" — that remains exactly correct
and sufficient under the many-to-many model; no redesign needed.

`EnsureCharacterExistsAsync`, the apply-time `Character` action branch, and the Quote apply branch's
own defensive re-ensure (`if (payload.CharacterId is not null) await EnsureCharacterExistsAsync(...)`)
already generically implement "ensure this `(CharacterId, SourceId)` link exists," regardless of
whether `CharacterId` came from a freshly staged Add or from reusing an existing match found during
planning — #179 already built this robustly enough to support #174's cross-Source-reuse case for free.
A Series-scoped match found during planning (Decision 7) needs no new Add action and no new apply-time
plumbing beyond one small, mechanical addition: `EnsureCharacterExistsAsync` now also threads
`sourceType` through to `Characters.InsertIfNotExists` (both call sites already had a `Type` string in
scope — `payload.SourceType` on the Character apply branch, `resolved.Type.ToString()` on the Quote
apply branch's own defensive re-ensure — so this is parameter-threading, not new logic). `SourceType`
is written only on first insert; `INSERT OR IGNORE` never touches it again on an already-existing row,
consistent with the Type anchor never changing after a Character is created.

---

## Consequences

- `EntityIdentity.CharacterId(string sourceId, string name)` gains a third parameter, `sourceType`
  (Decision 5). Every call site (currently only `ImportActionPlanner.ResolveCharacterAsync`) updates
  accordingly.
- `Sql.Characters.SelectIdBySourceAndName` is replaced by the query in Decision 7, taking `sourceId`,
  `name`, `sourceType`, and `seriesId` as parameters instead of `sourceId`/`name` alone.
- `ResolveCharacterAsync`'s in-memory intra-batch cache stays keyed by exact `(sourceId, name)` — it is
  an optimization for the literal-repeat case only (the same quote-shape resolved twice within one
  batch), not a substitute for Decision 7's database lookup; Decision 8 already documents why a
  same-batch-aware fuzzy cache is out of scope.
- `Characters` gains `SourceType` (Decision 6); the migration backfills it from each pre-existing row's
  single `CharacterSources` link (guaranteed 1:1 at this point in migration history, per ADR 011
  Decision 5) before any merging happens.
- No new `UNIQUE` constraint is added to `Characters` (Decision 6) — `#174`'s plan doc anticipated one
  might be needed; this ADR concludes it would be actively wrong given Decision 1's semantics.
- `CharacterActionPayload`, `ToFieldMap(CharacterActionPayload)`, `EnsureCharacterExistsAsync`, and the
  apply-time `Character`/`Quote` branches are audited against this ADR and found to need no functional
  changes (Decision 9) — only stale doc-comments referencing the pre-#174 per-Source meaning are
  updated.
- The migration implementing Decisions 3–4 is `Migration011_CharacterGlobalIdentity` (Migration010 was
  already claimed by #213 this same milestone).

---

## Follow-on

- #175 — Character Modify/decidability, building on this ADR's global model (not in this ADR's scope —
  see #174's plan doc Background).
- #179 — the structural ADR (011) this one builds on top of.
- #169 — the research that first found Name-alone matching concretely wrong, motivating both ADR 011's
  Type anchor and this ADR's Series-scoped conservatism.
