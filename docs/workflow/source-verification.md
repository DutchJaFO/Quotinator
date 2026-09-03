# Source verification procedure

Authoritative procedure for verifying a factual claim about a real film, TV show, or book — a
canonical title, a release date, or which real-world work two differently-spelled entries both refer
to. Referenced from CLAUDE.md's Data Sources section. Used for both manual data corrections today and
the planned Data Enrichment milestone's automated matching/verification work.

## When this applies

Any time a change asserts a fact about a real-world work, including:

- A `data/sources/*-conflict-rules.json` entry (`ConflictResolutionRule`)
- A `data/sources/*-source-aliases.json` entry (`SourceAliasRule`)
- A manual edit to `quotinator-curated.json` or `quotinator-series-universe.json` that sets or changes
  a title, `date`, `seriesName`, or `universeName`
- A conflict rule attributing a quote to a specific episode, speaker, or season (#375) — see
  "Verifying a quote's text, speaker, and episode" below
- Any future Data Enrichment milestone work that resolves a Source against an external dataset

It does **not** apply to purely mechanical/structural changes (e.g. reordering a series's own
`sources[]` entries, renaming a JSON property) — only to claims that could be right or wrong about the
real world.

## Procedure

1. **Formulate a specific, narrow query** naming the work and the exact fact being checked (e.g.
   `"Movie Title" 1999 official release date`) — not a vague query that could return anything.
2. **Run the first search restricted to Tier 1 sources only**, using the search tool's domain-scoping
   option (e.g. `allowed_domains: ["en.wikipedia.org", "imdb.com"]`).
3. **If Tier 1 sources agree and clearly answer the question, stop there** — that's the verified fact.
   Cite the specific page URL.
4. **If Tier 1 sources conflict with each other, or the fact isn't found**, escalate to Tier 2, in
   order, stopping at the first source that resolves it.
5. **Tier 3 is corroboration only, never sole evidence.** A fan wiki, forum, or blog may support a Tier
   1/2 finding but must never be the only citation for a fact.
6. **Record the source** (specific URL, not just "web search") in the commit/PR description or the
   governing plan doc — the same convention this project already uses for citing a CVE advisory.

## Source tiers

**Tier 1 — always checked first, in this order:**
1. Wikipedia (`en.wikipedia.org`) — the work's own article, infobox fields (title, release date)
2. IMDb (`imdb.com`) — main title page + release-info subpage

**Tier 2 — only if Tier 1 doesn't resolve it, or Tier 1 sources conflict:**
3. Wikidata (`wikidata.org`) — structured data, often disambiguates an exact title string when
   Wikipedia and IMDb differ in formatting (e.g. IMDb inserting a colon Wikipedia's own article title
   doesn't use)
4. The work's own studio/franchise/publisher site, when one exists and is reachable (e.g.
   `starwars.com`, `marvel.com`)

**Tier 3 — last resort, corroboration only:**
5. Fan wikis (Fandom and similar), review aggregators, or general unscoped search results

## Fix what you find, when you find it

**A data error noticed during ordinary use is corrected then — not held back for whichever systematic
pass owns that class of error** (developer decision, 2026-08-29). Batching known-wrong data behind a
future issue leaves it served in the meantime and buys nothing.

Two things keep that from becoming untracked drift:

- **The verification procedure above applies in full**, however small the fix. An intermediate
  correction is not a licence to skip the Tier 1 lookup or the citation.
- **The commit carries the umbrella issue's number**, so the trail stays in one place and the
  systematic pass can see what has already been done. A wrong quote→Source attribution carries
  [#355](https://github.com/DutchJaFO/Quotinator/issues/355); a missing field carries
  [#5](https://github.com/DutchJaFO/Quotinator/issues/5); a quote that cannot be verified to exist at
  all carries [#219](https://github.com/DutchJaFO/Quotinator/issues/219). That satisfies
  `process.md`'s `type [#N]: short summary` convention without waiting for a dedicated issue per fix.

## House style: hyphens, never en dashes or em dashes

**A title stored by this project uses a plain hyphen (`-`) wherever a source renders an en dash (`–`)
or em dash (`—`)** (developer decision, 2026-08-29). This is a presentation convention, applied *after*
the verification above has established what the title is — it never changes which work a title refers
to, only how the separator is written.

It matters because a dash character is part of the title string, and the title string is what a Source
is matched and identified by. Wikipedia renders `Star Wars: Episode II – Attack of the Clones` with an
en dash; storing that verbatim gave us four en-dash titles sitting beside six hyphen ones for films in
the same franchise, which look identical to a reader and do not match each other.

**A bundled source file that emits an en dash is bridged with a `SourceAliasRule`, not edited.** Those
files are regenerated from upstream by their converter, so an edit is overwritten on the next refresh.
`nikhilnamal17-source-aliases.json` carries the two Star Wars entries this applies to today.

**This does not resolve the underlying problem**, which is that a title like
`Star Wars: Episode II - Attack of the Clones` carries a franchise, an episode number and a subtitle in
one opaque string, and upstream data refers to the same film by any combination of them. A per-variant
alias for each is a list that only grows — two of its rows exist solely because of the dash convention
above. [#354](https://github.com/DutchJaFO/Quotinator/issues/354) is the mechanism that would make both
those rows and this convention's manual application unnecessary.

## Handling conflicting sources

When Tier 1 sources disagree — as happened verifying "The Godfather Part II": IMDb's release-info page
formats it "The Godfather: Part II" (with a colon) while Wikipedia's own article title and the film's
original theatrical poster both use "The Godfather Part II" (no colon) — prefer the work's own primary,
canonical presentation over a database's internal formatting convention:

- Wikipedia's **article title** (not an incidental colon in an infobox field or an aggregator's own
  metadata formatting)
- The original theatrical release title/poster, when identifiable, over a home-video or reissue title
- Document the conflict and which side won, in the same place the fact itself is recorded

## Verifying a quote's text, speaker, and episode

This project's Tier 1/Tier 2 order above governs *which work* a title or date refers to. It does not by
itself say how to verify a narrower claim: that a specific quote was actually said, by whom, and in
which episode. Found live in #375, verifying which episode of a multi-season show four bundled quotes
belong to.

**IMDb is already Tier 1, and its per-title, per-episode, and per-character quotes pages are the source
for this class of claim** — no new tier, no escalation beyond what the procedure above already permits.
A series' own quotes page (`/title/<id>/quotes/`) names the character alongside each line; a specific
episode's own page narrows it to that episode; a character's own page lists every quote IMDb has
attributed to them. Cross-checking an episode's own air date against the season it is claimed to belong
to is cheap corroboration — it caught nothing wrong in #375's four cases, but would have caught an
episode attributed to the wrong season.

**A quote absent from IMDb is not an unverified quote, and must not be treated as one.** IMDb's quote
pages are user-contributed and incomplete by construction — absence there is evidence of nothing, in
either direction. A quote demonstrably said on screen (a screen capture of the broadcast, a transcript,
the work itself) is verified regardless of whether IMDb's own crowd-sourced page happens to carry it.
Do not read a failed IMDb search as grounds to flag a quote under
[#219](https://github.com/DutchJaFO/Quotinator/issues/219) (a quote that cannot be verified to exist at
all) — that issue is for quotes nothing confirms, not quotes IMDb's own contributors haven't gotten to
yet.

**Attribution is expected to be partial, and that is not a failure to resolve before moving on.** Not
every quote's episode can be found from Tier 1 sources — a line may appear only on a series-level quotes
page with no episode named, or nowhere on IMDb at all despite being real. When that happens: attach the
quote to the nearest Source that *is* identifiable — the whole work, if no instalment can be pinned down
(#375's own plan doc records the reasoning: seeding has no pending results when done, but never
guaranteed the data is complete, and a quote's Source may be as coarse as what is currently known) —
record the row as a candidate for the data enhancement milestone, and continue. Holding the rest of a
batch open because one quote's episode couldn't be pinned down treats an ordinary, expected gap as a
blocker it was never meant to be.

## Practical use with the search tool

- First query: scope `allowed_domains` to the Tier 1 domains (`en.wikipedia.org`, `imdb.com`).
- If that returns nothing relevant, or the two disagree, issue a second query with `allowed_domains`
  widened to include `wikidata.org` and the specific official domain for the work in question (if
  known).
- Only after Tier 1 and Tier 2 both fail to resolve the fact, run an unrestricted query — and treat any
  single Tier 3 result as corroboration, never as the sole basis for the fact.

## Background

Written 2026-07-25 after #181/#217's title-consistency review revealed two gaps: most of that
session's title/date corrections had been made from recalled model knowledge rather than a live,
citable lookup (later retroactively verified, all found correct — see
`docs/milestones/data-import-sources/181-minimal-conflict-resolution-rule-file-plan.md`, Step 10's own
addendum), and even the retroactive verification pass itself used inconsistent, unscoped searches with
no defined source priority, which is what let the Godfather Part II colon/no-colon conflict surface
without a rule to resolve it cleanly. This document is the fix for both: a defined, reproducible source
order to check *before* any correction is made, not just a "verify it somehow" instruction.
