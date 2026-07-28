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

## Handling conflicting sources

When Tier 1 sources disagree — as happened verifying "The Godfather Part II": IMDb's release-info page
formats it "The Godfather: Part II" (with a colon) while Wikipedia's own article title and the film's
original theatrical poster both use "The Godfather Part II" (no colon) — prefer the work's own primary,
canonical presentation over a database's internal formatting convention:

- Wikipedia's **article title** (not an incidental colon in an infobox field or an aggregator's own
  metadata formatting)
- The original theatrical release title/poster, when identifiable, over a home-video or reissue title
- Document the conflict and which side won, in the same place the fact itself is recorded

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
