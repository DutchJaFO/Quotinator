# #381 — A cross-file duplicate quote's CharacterId/PersonId silently reverts to null on every reseed

**Status:** Planning
**GitHub issue:** #381
**Tiers required:** T1, T2
**Depends on:** none

---

## Description

`ImportActionPlanner.ResolveCharacterAsync`/`ResolvePersonAsync` resolve a Quote's `CharacterId`/
`PersonId` from the *current source file's own raw incoming record* (`q.Character`/`q.Author`),
called before the field-merge decision for that quote's `character`/`author` field is known. When the
same quote id appears in two bundled files and one of them carries no character/author data at all,
that file's own pass resolves the FK to `null` and overwrites whatever the other file already
established — every single reseed, since nothing about this ever converges.

Found live while verifying #374/#378: `quotinator-curated.json`'s "Hello. My name is Inigo Montoya..."
quote (`da53310a-21c1-4742-a0b6-bcd981f926ca`) declares `"character": "Inigo Montoya"`;
`NikhilNamal17_popular-movie-quotes.json`'s own entry for the same id (that converter's raw upstream
schema has no character field) carries `"character": null`. `quotinator-curated.json` runs before
`NikhilNamal17_popular-movie-quotes.json` in every reseed, so within one reseed: curated's pass fills
`CharacterId` in (existing was empty) → NikhilNamal17's pass resolves `CharacterId` from its own null
character field and overwrites it back to `null`. The field-level text comparison correctly decides to
*keep* "Inigo Montoya" (existing non-empty, incoming empty → keep existing) — the bug is that the FK
column written to the database is resolved independently of that decision.

This never surfaces as Pending/Blocked/Stale, so nothing flags it for review — it is silent, permanent
data loss on the character link, currently live in the shipped corpus.

**This plan needs refining before it can be executed.** The exact fix mechanism is not yet decided:
`ResolveCharacterAsync`/`ResolvePersonAsync` currently run at the top of the quote loop, before
`existing`/the field-merge decision are computed (needed for `ResolveSourceAsync`'s Source resolution,
per #378's own ordering fix) — the FK resolution needs to consult the *resolved* `character`/`author`
value instead of the raw one, without breaking that existing ordering constraint or the Add-path case
(no `existing` to merge against, no reordering to do).

---

## Steps

### 1. Design the fix's ordering

**Status:** ⬜ Not started — **design decision, blocks steps 2-4**

Decide how `ResolveCharacterAsync`/`ResolvePersonAsync` get access to the resolved `character`/`author`
value rather than the raw one, for the Modify path specifically (the Add path has no existing value to
diverge from and is not itself broken). Candidates: re-resolve the FK a second time, after the merge
decision is known, mirroring `ReresolveSourceIdForDecidedDate`'s existing pattern for Source; or defer
the initial resolution's *effect* (not its Character/Person Add-action side effects, which must still
run once per distinct value) until the merged value is known.

### 2. Write the failing test, red first

**Status:** ⬜ Not started

A synthetic two-file fixture: file A declares `character` for a quote id, file B (processed after A)
carries the same id with no `character` field at all. Assert `Quotinator_Quote.CharacterId` is
non-null and correct after both files apply, and stays correct after a second full reseed of both
files. Cover `PersonId`/`author` the same way if the fix is generic to both (expected, since both go
through the identical pattern).

### 3. Implement the fix

**Status:** ⬜ Not started

### 4. Confirm no regression against the real bundled corpus

**Status:** ⬜ Not started

Re-run `NikhilNamal17RealCorpusWithCurrentRuleFile_ResolvesCompletelyAndStaysStable` and query the
Inigo Montoya quote's `CharacterId` directly (T1/T2 live) to confirm it survives a reseed of the real
files, not just the synthetic fixture.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A quote's CharacterId survives a later file's own pass when that file carries no character data for the same id | Unit test | TBD, named once step 1 is decided |
| 2 | ❌ | Same guarantee for PersonId/author | Unit test | TBD, named once step 1 is decided |
| 3 | ❌ | The guarantee holds across a second reseed, not just the first | Unit test | Extends the fixture above with a repeat reseed |
| 4 | ❌ | The real bundled corpus's Inigo Montoya quote keeps its CharacterId after a live reseed | Live | T1 + T2: query `Quotinator_Quote` for `da53310a-21c1-4742-a0b6-bcd981f926ca` after two reseeds |
