# #169 — Research: "universe/setting" concept linking Source and Character

**Status:** Released
**GitHub issue:** #169 (closed)
**Depends on:** none

---

## Question (from the GitHub issue)

Should Quotinator introduce a "universe/setting" concept (e.g. a franchise or shared fictional world) linking multiple Source rows and the Characters within them, to support cross-source character identity and future "query by setting" endpoints?

---

## Investigation steps

### 1. Survey structural shape options

**Status:** Done.

**Finding (revised 2026-07-14 — see Conclusion):** `Source.cs` confirms the column set is exactly
`Title`/`Type`/`Date`/`ImportBatchId`/`CompletenessStatus`/`NoValueKnown` — no franchise/grouping
concept exists today, in either shape. The shape is a **two-level hierarchy**, not a single flat tag
as this step originally proposed: `Universe` → `Series` → `Source`, one-to-many at both levels (a
`Series` belongs to at most one `Universe`; a `Source` belongs to at most one `Series`) — matching the
domain examples that surfaced during review (the Hobbit trilogy and the Lord of the Rings trilogy are
two distinct Series, both under one Universe, "Middle Earth"). No genuine one-Source-to-many-Series
case was identified, so one-to-many rather than many-to-many is still the right choice per this
project's Simplicity priority (CLAUDE.md "Project Priorities," ranked above Extensibility). A
`Source`/`Series` with no parent is implicitly standalone (e.g. Casablanca has no Series; a
standalone Series would have no Universe).

Compare the two shapes the issue names in its "What to investigate" item 1: a new `Universes`/`Settings`
table with a many-to-many link to `Source` (a franchise can span multiple films/books, and a single
Source arguably could belong to more than one grouping in edge cases — e.g. a crossover film), versus a
simpler single-valued tag/grouping field directly on `Source` (e.g. `Source.UniverseName TEXT NULL`).
Weigh this project's stated priorities in order (Correctness, Simplicity, Maintainability, Portability,
Extensibility — CLAUDE.md "Project Priorities") — a homelab project favours the simplest shape that
still answers the question truthfully; a many-to-many join table is only justified if a genuine
one-Source-to-many-universes case is expected to matter, not merely theoretically possible. Check
`src/Quotinator.Engine/Entities/Source.cs` for the current column set (`Title`, `Type`, `Date`,
`ImportBatchId`, `CompletenessStatus`, `NoValueKnown`) — there is no existing franchise/grouping
concept on Source today, so this is a genuinely new field or table either way, not an extension of
something half-built.

### 2. Assess interaction with #174's Character merge algorithm

**Status:** Done.

**Finding (revised 2026-07-14 — this step's original conclusion was wrong, corrected directly by the
developer):** the original finding here understated this badly. It is **not** merely "a partial
mitigation" for #174's collision risk — #174's originally-planned approach (copy Person's shape,
merge every same-`Name` Character globally with no safeguard) is **concretely incorrect**, not just
risky, given data already bundled with this project: the dataset already contains real franchises
(Lord of the Rings, The Hobbit, Star Wars, Terminator) where the same character legitimately spans
multiple Source rows (Gandalf across six films) — Character cannot simply drop its Source link to
nothing, it needs a many-to-many relationship (`CharacterSources`), not a Source-less global row like
Person. Separately, `Source.Type` must act as a **hard identity anchor**: a book adaptation's Gandalf
and a film adaptation's Gandalf are different Characters despite sharing a Name and a Universe — a
Name-only global merge would wrongly conflate them. This is a structural correction to #174's data
model, not a probabilistic risk-reduction question. Filed as new issue #179 ("Series/Universe schema:
link related Sources, and Character↔Source many-to-many identity"), which #174 now depends on — see
the Conclusion section below for the full outcome.

#174 ("Character: from per-Source to global identity") explicitly names #169 as a potential input to
its own merge-algorithm ADR: a "same universe" signal could scope character-row merging more safely
than "same name globally," reducing the risk of wrongly conflating two unrelated characters that
happen to share a name (e.g. two different "Sarah"s, two different "The Doctor"s in unrelated
properties). #174 is not blocked on #169 and may land first — if it does, this step also needs to read
#174's resulting ADR (once written) to confirm whether its chosen algorithm already closed this gap
some other way (e.g. manual-confirmation safeguard) or is still open to a universe-scoped merge boundary
being added later. Read `docs/milestones/data-import-sources/174-character-global-identity-plan.md`
(step 1's "safeguard" bullet) for the exact framing #174 is working from, and evaluate concretely
whether a universe/setting link would have been sufficient on its own to resolve that safeguard
question, or only a partial mitigation (e.g. it narrows collisions within one franchise but does
nothing for two unrelated single-Source characters that happen to share a name).

### 3. Scope candidate query endpoints against milestone boundaries

**Status:** Done.

**Finding:** both example endpoint shapes ("all of Luke's quotes across the trilogy," "all quotes
from the Star Wars franchise") are new read/query surfaces, not import or identity work — the current
Data Import & Sources milestone's scope is getting data in and correctly identified (see
`overview.md`'s own framing), not adding new query endpoints beyond what #67-#69 already built for
conversations. Any such endpoint belongs with a later Blazor UI or public API milestone, if and when
the underlying universe/setting data exists to query.

List concrete endpoint shapes a universe/setting concept would enable (the issue's own examples: "all
of Luke's quotes across the trilogy," "all quotes from the Star Wars franchise") and classify each
against this project's milestone structure — does it belong in the current Data Import & Sources
milestone (`docs/milestones/data-import-sources/overview.md`), or is it read/query-surface work that
belongs with a later Blazor UI or public API milestone. This classification directly feeds the
"Outcome tracking" table below (specifically "New issues in the current milestone" vs. "New milestone").

### 4. Determine whether the existing Genre mechanism already covers this need

**Status:** Done.

**Finding:** confirmed structurally distinct, not just "on its face." `Genre.cs` is a fixed 13-value
content-classification enum (Action…Thriller) with no franchise dimension whatsoever. A grep of
`src/` for `Franchise|Universe|Setting|Series` (excluding the word "series" appearing inside prose/
quote text) turned up no matches anywhere in entities, `Sql.cs`, or schemas — no half-built or
informally-repurposed mechanism exists to route around. Genre does not cover this need in any form.

`Genre` (`src/Quotinator.Core/Models/Genre.cs`) is a fixed enum of content-classification tags (Action,
Adventure, Animation, Comedy, Drama, Fantasy, Fiction, Horror, Mystery, NonFiction, Romance, SciFi,
Thriller) applied per-quote via the `QuoteGenres` join — it classifies subject matter/tone, not
franchise membership. On its face it is a different axis entirely (a `SciFi` quote from *Star Wars* and
a `SciFi` quote from *Star Trek* share a genre tag but not a universe), but this step must confirm that
conclusion explicitly rather than assume it — check whether any bundled or curated source data already
conflates the two (e.g. a genre-like tag informally being used as a franchise marker anywhere in
`data/sources/`), and confirm there is no existing free-text or lookup-table mechanism elsewhere in the
schema (grep `src/Quotinator.Engine/Entities/` and `Sql.cs` for anything resembling `Franchise`,
`Series`, `Universe`, or `Setting`) that would make this investigation moot before a new concept is
designed. Note also the parallel, not-yet-implemented idea already logged in memory
(`project_genre_extensible_table.md`) that Genre itself may move from a fixed enum to a DB-backed
lookup table for extensibility reasons unrelated to #169 — record whether that change, if and when it
happens, would have any bearing on how a universe/setting concept should be modelled (e.g. reusing the
same lookup-table pattern for consistency), without taking a dependency on it actually happening.

### 5. Determine data availability and curation cost

**Status:** Done.

**Finding (corrected 2026-07-14 — the original conclusion drawn from this finding was wrong):** no
*automated* population path exists — this half of the finding stands. The two originally-bundled
converter plugins this step planned to inspect (`Quotinator.Converters.Vilaboim`, `Quotinator.
Converters.NikhilNamal17`) were themselves removed by #144 in favour of generic, manifest-configured
converters (`BasicJsonArray`/`Csv`/`RegexArray`); none of the current source/schema files
(`vilaboim_movie-quotes.json`, `NikhilNamal17_popular-movie-quotes.json`, `quotinator-curated.json`,
`source-flat.schema.json`, `source-extended.schema.json`, `manifest.schema.json`) carry a
franchise/series/universe field — `source` objects are `{ id, title, type, date }` only.

**But the conclusion drawn from this — "a universe/setting concept would always require fully manual
curation... a much narrower payoff" — was a real error, corrected directly by the developer**: "no
field is *tagged* in the schema today" was wrongly written up as "no franchise/universe *exists* in
the data." The bundled dataset plainly already contains quotes from real franchises (Lord of the
Rings, Star Wars, Terminator) — manual curation cost is real, but it is manual curation of data that
demonstrably exists and matters, not a speculative feature with nothing to curate. Manual population
is entirely tractable via a small, hand-authored curated overlay file (same pattern as
`quotinator-curated.json`), reusing #162's already-shipped Source Modify/decidability path with zero
new import mechanism — see #179's issue body for the concrete mechanism, and the Conclusion section
below.

Check whether "which franchise/universe a Source belongs to" is derivable from any bundled or
importable source file today. Read the raw upstream schemas the two bundled converters consume
(`Quotinator.Converters.Vilaboim`'s `{ quote, movie }` and `Quotinator.Converters.NikhilNamal17`'s
`{ quote, movie, type, year }` — see the Data Sources table in CLAUDE.md) and confirm neither carries
any franchise/series field that a converter could map. If the answer is "no upstream source provides
this," state plainly in the findings that a universe/setting concept would always require manual
curation (same category as `data/sources/quotinator-curated.json`'s existing manually-verified
entries), which is a direct cost input to whether the concept is worth adding at all — a feature that
can only ever be populated by hand for a subset of Sources has a much narrower payoff than one a
converter could populate automatically.

---

## Outcome tracking

| Possible outcome | Applies? | Notes |
|---|---|---|
| New issues in the current milestone | **Yes** | Filed #179 ("Series/Universe schema: link related Sources, and Character↔Source many-to-many identity") — the structural schema (step 1's hierarchy) plus the `CharacterSources` many-to-many join and `Source.Type`-as-identity-anchor invariant that #174 needs to migrate correctly (step 2). Added to `overview.md`. |
| New milestone | No | The query-endpoint value (step 3) still belongs to a future Blazor UI/public API milestone, but the underlying schema itself is squarely import/identity-correctness work for the current milestone (#174 cannot proceed correctly without it), not deferred work. |
| Not feasible / rejected | No (superseded — see steps 2 and 5) | The original draft of this research concluded "rejected" based on two errors: overstating manual curation's cost by conflating "not tagged in the schema" with "doesn't exist in the data" (step 5), and understating the correction's necessity as "only a partial mitigation" when it is in fact a structural correctness fix (step 2). Both corrected directly by the developer 2026-07-14. |
| Architecture decision required | **Yes** | The structural decision (Universe→Series→Source hierarchy, `CharacterSources` join, `Source.Type` anchor) is documented as part of #179's own ADR, not a separate ADR filed by this research issue. #174's own (separate) ADR covers the merge *algorithm* that operates within that structure. |

(This table mirrors the issue's own "Possible outcomes" list.)

---

## Notes

No Tiers required — this is a research issue producing findings/recommendations, not shippable code.
Findings must be posted as a comment on GitHub issue #169 before closing, per its own Definition of
done. If the outcome is "new issues," they get filed and added to `overview.md` at that time — not part
of this plan doc. If the outcome is "architecture decision required," the ADR follows the standard
format in `docs/architecture-decisions/README.md` (Status/Date/Context/Decision/Consequences, file
`NNN-short-title.md`, numbered sequentially after the current highest — 010 as of this writing, same
caveat #174's own plan doc already notes about confirming the actual next-free number at write time
since sibling in-flight issues may claim it first) and must be added to that README's index in the same
commit.

**Conclusion (revised 2026-07-14):** the first pass through all five steps reached the wrong outcome
("Not feasible / rejected") by conflating "no franchise/universe field is tagged in the schema today"
with "no franchise/universe exists in the data" — the developer corrected this directly, pointing out
the bundled dataset already contains real, multi-Source franchises (Lord of the Rings, The Hobbit,
Star Wars, Terminator) and laying out the concrete domain model this plan doc's findings now reflect:
a `Universe`→`Series`→`Source` hierarchy (step 1), `Character`↔`Source` as many-to-many rather than
Source-less-global (step 2), and `Source.Type` as a hard identity anchor across media adaptations of
the same material (step 2). This is not a probabilistic risk reduction for #174 — it is a structural
correction to #174's data model, since its originally-planned "merge every same-Name Character
globally" approach would concretely conflate book and film portrayals of the same character.

**Outcome: New issues in the current milestone + Architecture decision required, both Yes** (see
Outcome tracking table). Filed **#179** ("Series/Universe schema: link related Sources, and
Character↔Source many-to-many identity"), which lands the structural schema with zero data-merging
risk of its own (every existing `Characters` row keeps its own Source, 1:1, via a new join table).
**#174 was rewritten** to depend on #179 and now owns the actual merge algorithm, operating within
#179's structural boundary rather than copying Person's shape. Populating `Series`/`Universe` values
on existing Sources needs no new mechanism — a hand-authored curated overlay file, reusing #162's
already-shipped Modify/decidability path, persists across `Quotinator__AutoUpdateSources` refreshes
without any new "enrichment rule" concept (a first pass at this session also considered generalizing
#153's declarative rule-file mechanism for this; the developer correctly flagged that as
overcomplicating a problem the existing curated-overlay pattern already solves, so #153 was left
untouched).

No new milestone proposed — the query-endpoint value (step 3) still belongs to a future Blazor UI/API
milestone. Findings posted as a GitHub comment on #169 per its Definition of done, then closed.
