# #421: A quote's id depends on whether the install was upgraded or fresh, after Migration009's dedupe

**Status:** Planning
**GitHub issue:** #421
**Tiers required:** T1, T2
**Depends on:** none. Parent: [#182](https://github.com/DutchJaFO/Quotinator/issues/182)

---

## Next action

**Execute this plan**, starting at step 1. Nothing is outstanding: the approach is decided (developer,
2026-10-09, recorded in *Scope changes*), and step 1 is a verification the fix depends on rather than
an open question.

---

## What this fixes

Migration009 deduplicates `(QuoteText, SourceId)` by keeping the lowest `rowid`. On an upgraded
database that keeps whichever copy was seeded first, which for the Princess Bride quote is
NikhilNamal17's derived `f3557c41`, deleting the curated row whose id `quotinator-curated.json`
declares explicitly, `da53310a`. A fresh install keeps `da53310a`. The same quote is therefore served
under two ids depending on how the install began, and a consumer that stored one breaks.

`rowid` is insertion order, which is not a statement about which row is correct. The curated file's
explicit id is, and that is the only one a curated conversation also references.

It has a second, worse symptom: every later reseed restages the curated quote as an `Add`, which the
unique index refuses, so the file reports `blocked=1` permanently. Fixing which row survives fixes
both, because the curated row is then already present.

**Exactly one pair is affected in the current corpus**, measured 2026-10-09 by comparing the curated
file against the other two bundled files on normalised quote plus source: `da53310a`, The Princess
Bride. That matches Migration009's own comment. The fix is still written against the general case,
since the corpus changes and the count is a property of today's data.

---

## Steps

### 1. Confirm what identifies the curated row

**Status:** ⬜ Not started

The fix turns on the dedupe being able to tell a curated row from a derived one. Each bundled file
gets its own `Import_Batch` row, reachable from a quote through `ImportBatchId`, but
`SeedBatch.Label` is a group label (`"bundled sources"`), not a file name, so it is not yet
established what `Import_Batch.Name` actually holds.

Confirm it against a real seeded database rather than by reading the write path. If `Name` carries
the file name, the dedupe orders on it. If it carries the group label, find the column that does
distinguish the batches and use that; if nothing does, the discriminator has to be added before the
dedupe can be corrected, and that becomes this issue's first change.

### 2. Write the failing tests and confirm them red

**Status:** ⬜ Not started

Against a fixture database holding both rows in the order a 1.8.2 install produced, derived first and
curated second, so the current migration demonstrably keeps the wrong one. Red before any change, per
`testing-policy.md`.

Both outcomes and the extremes belong here, per *A feature is covered when both outcomes, both
extremes, and a canary exist*: the curated row survives when it is second; it still survives when it
is first, so the test cannot pass by accident of ordering; a pair with no curated row at all still
dedupes to the lowest `rowid`; and the child-table rows follow the survivor.

### 3. Correct which row Migration009 keeps

**Status:** ⬜ Not started

Order the survivor by whether its batch is the curated file, then by `rowid`, replacing the bare
`rowid` ordering in all three of the migration's statements. No id is hardcoded.

**`Quotinator_ConversationLine` is currently missed and must be included.** Migration009 cleans
`Quotinator_QuoteGenre` and `Quotinator_QuoteTranslation` only, while three tables reference a quote.
A line pointing at the deleted row is left dangling today; once the survivor changes, it must be
repointed rather than deleted, since the conversation still needs its line.

### 4. Verify the upgrade path from a real released schema

**Status:** ⬜ Not started

ADR 009's gate, and the "very careful" this edit was made conditional on. Apply the migrations in
order against a database matching **v1.8.3**, the last published release, not against an accumulated
development database, and confirm the curated id survives and the fresh path is unchanged.

### 5. Update the knowledgebase entry

**Status:** ⬜ Not started

`docs/knowledgebase/a-quote-id-stops-working-after-an-upgrade.md`. The entry outlives the fix: a
database that already deduped wrongly keeps the id it kept, because repairing one belongs to the
parent's mechanism, not here. Say which versions are affected, that a Reset resolves it, and drop the
status code per the issue's own instruction.

### 6. Changelog

**Status:** ⬜ Not started

`changelog.en.json`'s `unreleased`, with `421` in `unreleased.issues`, and matching translated entries
in `nl` and `de` in the same commit. Regenerate `CHANGELOG.md` only.

### 7. T1

**Status:** ⬜ Not started

The application still starts. Nothing beyond startup.

### 8. T2

**Status:** ⬜ Not started

The smoke set, plus the documents whose subject this changes: *A fresh seed resolves every bundled
file with nothing left pending*, and any document asserting the curated quote's id.

---

## Scope changes

**This issue fixes which row a dedupe keeps. It does not build the re-keying mechanism**, which is
[#182](https://github.com/DutchJaFO/Quotinator/issues/182), this issue's parent. Ids are derived from
content, so a rule that changes a governing column moves the id, and every foreign key referring to it
would have to move too. Nothing in the codebase does that: `Sql.Quotes.UpdateOnNewestWins` rewrites
`QuoteText` and `SourceId` in place while matching on `Id`, so identity and content diverge silently,
and no code anywhere assigns a new `Id` to an existing row or repoints a foreign key to follow one.
Repairing links already broken that way wants a *repair broken links* routine and is the parent's
concern (developer, 2026-10-09).

**Migration009 is corrected in place rather than repaired by a new migration, deliberately and
against the usual rule.** The standard procedure is a new migration, and the milestone will squash its
migrations into a discrete set in any case. It does not work here: on an upgrade the migrations run in
sequence, so by the time a Migration010 ran, Migration009 would already have deleted the curated row,
and the id it carried exists nowhere else. It is not derivable, since `StableId` reproduces only the
other id and SQLite has no SHA-256, and it is not in the audit trail, since migrations execute raw SQL
and the blocked `Import_Action` that carries it is written later, by seeding. The only point at which
both rows still exist is Migration009 itself.

Editing it is safe for installations and risky only for development databases: **Migration009 has
never shipped.** It is absent from `v1.8.3` and arrived with #374, still unreleased, so no user
database has run it, which is the condition `CLAUDE.md`'s freeze rule exists to protect. A development
database that already ran it keeps the wrong id until a Reset. Step 4 is the care that was made the
condition of taking this route.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A curated row and a derived row with identical text and source dedupe to the curated one | Unit test | `QuotinatorMigrationsTests.Migration009_DuplicateWithAnExplicitCuratedId_KeepsTheCuratedId` |
| 2 | ❌ | The outcome does not depend on insertion order | Unit test | `QuotinatorMigrationsTests.Migration009_CuratedRowInsertedFirst_IsStillTheSurvivor` |
| 3 | ❌ | A duplicate pair with no curated row still keeps the lowest `rowid` | Unit test | `QuotinatorMigrationsTests.Migration009_DuplicatesWithNoCuratedRow_KeepTheLowestRowid` |
| 4 | ❌ | A conversation line pointing at the losing row is repointed to the survivor, not left dangling or deleted | Unit test | `QuotinatorMigrationsTests.Migration009_ConversationLineOnTheLosingRow_FollowsTheSurvivor` |
| 5 | ❌ | Genre and translation rows follow the survivor | Unit test | `QuotinatorMigrationsTests.Migration009_ChildRowsOnTheLosingRow_FollowTheSurvivor` |
| 6 | ❌ | Replaying every migration from empty still matches the fresh-database baseline | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` |
| 7 | ❌ | Build and full test run are clean | Live | `dotnet build --configuration Release` then `dotnet test --configuration Release --verbosity normal -m:1`, both `0 Warning(s)  0 Error(s)` |
| 8 | ❌ | Upgrading a real v1.8.3 database serves the curated quote under the curated id | Live | ADR 009's gate, step 4: `GET /api/v1/quotes/da53310a-21c1-4742-a0b6-bcd981f926ca` returns `200` after the upgrade |
| 9 | ❌ | A fresh install serves it under the same id, so the two paths agree | Live | The same `GET` on a fresh container returns `200`, and the derived id returns `404` on both |
| 10 | ❌ | The curated file no longer reports a blocked quote on reseed | Live | *A fresh seed resolves every bundled file with nothing left pending*, and the upgraded container's reseed reports `blocked=0` for `quotinator-curated.json` |
| 11 | ❌ | The application still starts | Live | T1, Visual Studio, developer's own action |
| 12 | ❌ | T2 scope passes | Live | The smoke set plus the documents named in step 8 |
