# #207 — Canonicalize file-authored explicit ids at capture (parent)

**Status:** Planning
**GitHub issue:** #207
**Depends on:** none

---

## Description

A curator-authored explicit id (Source/Person/StageDirection/SoundCue/Conversation/Quote) is threaded
through `ImportActionPlanner` and staged in whatever raw casing the file used, never canonicalized —
every downstream write binds that same raw string as a plain `string` Dapper parameter, bypassing
`GuidHandler`'s uppercase normalization entirely. This body of work delivers the fix, formalised as
`docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md`: canonicalize an externally
supplied id exactly once, at the single earliest point of capture, through one reusable
`Quotinator.Data.Helpers.EntityIdCanonicalizer` helper. It is one body of work split into two sub-issues
because the entities involved canonicalize to *different* target casings (uppercase vs. lowercase) and
the quote-id half carries its own distinct query-audit surface that the other entities' fixes never
touch — see each sub-issue's own plan doc for the full investigation and Steps.

---

## Sub-issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#209](https://github.com/DutchJaFO/Quotinator/issues/209) | Canonicalize explicit ids at capture — Source, Person, StageDirection, SoundCue, Conversation | Planning | T1 ⬜ T2 ⬜ | [209-canonicalize-entity-ids-part-a-plan.md](209-canonicalize-entity-ids-part-a-plan.md) |
| [#210](https://github.com/DutchJaFO/Quotinator/issues/210) | Canonicalize Quotes.Id at capture, case-insensitive lookup | Planning | T1 ⬜ T2 ⬜ | [210-canonicalize-quote-id-plan.md](210-canonicalize-quote-id-plan.md) |

---

## Dependency map

#209 and #210 both extend the same `Quotinator.Data.Helpers.EntityIdCanonicalizer` class (#209 adds its
uppercase forms, #210 its lowercase forms) but neither blocks the other — whichever lands first creates
the class with its own half; the second sub-issue extends it. Both are independent of every other open
issue in this milestone.

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #209 | Canonicalize explicit ids at capture — Source, Person, StageDirection, SoundCue, Conversation | Planning |
| 2 | #210 | Canonicalize Quotes.Id at capture, case-insensitive lookup | Planning |
