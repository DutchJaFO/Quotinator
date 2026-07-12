# #169 — Research: "universe/setting" concept linking Source and Character

**Status:** Planning
**GitHub issue:** #169
**Depends on:** none

---

## Question (from the GitHub issue)

Should Quotinator introduce a "universe/setting" concept (e.g. a franchise or shared fictional world) linking multiple Source rows and the Characters within them, to support cross-source character identity and future "query by setting" endpoints?

---

## Investigation steps

### 1. Survey structural shape options

**Status:** Not started.

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

**Status:** Not started.

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

**Status:** Not started.

List concrete endpoint shapes a universe/setting concept would enable (the issue's own examples: "all
of Luke's quotes across the trilogy," "all quotes from the Star Wars franchise") and classify each
against this project's milestone structure — does it belong in the current Data Import & Sources
milestone (`docs/milestones/data-import-sources/overview.md`), or is it read/query-surface work that
belongs with a later Blazor UI or public API milestone. This classification directly feeds the
"Outcome tracking" table below (specifically "New issues in the current milestone" vs. "New milestone").

### 4. Determine whether the existing Genre mechanism already covers this need

**Status:** Not started.

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

**Status:** Not started.

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
| New issues in the current milestone | Not yet assessed | |
| New milestone | Not yet assessed | |
| Not feasible / rejected | Not yet assessed | |
| Architecture decision required | Not yet assessed | |

(This table mirrors the issue's own "Possible outcomes" list — filled in once the investigation
concludes, not now.)

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

Step 3's confirmation (during this planning pass) found no existing franchise/series/universe concept
anywhere in the schema (`Source` has only `Title`/`Type`/`Date`/`ImportBatchId`/`CompletenessStatus`/
`NoValueKnown`; `Genre` is a fixed 13-value content-classification enum joined per-quote via
`QuoteGenres`, structurally unrelated to franchise grouping) — this investigation is not being routed
around an existing half-built mechanism, and step 4 exists to confirm this conclusion formally rather
than take this planning pass's grep as the final word.
