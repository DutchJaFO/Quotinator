# #328 — Smoke tests: verify bundled imports and endpoint behaviour against a real database

**Status:** Planning
**GitHub issue:** #328
**Tiers required:** T1, T2
**Depends on:** #339

---

## Description

Two guarantees are only meaningful against a live database, and neither is covered as a feature in its
own right.

**Bundled imports should result in a clean import.** A test exists covering per-source
conflict-resolution rule files, but it is framed around one issue's rule files rather than the general
guarantee that a fresh install seeds bundled content with nothing left needing review.

**Endpoints need exercising against an actual database.** The endpoint tests replace `IQuoteService`
with `FakeQuoteService` and `IDatabaseInitializer` with `NoOpDatabaseInitializer` by design, so they
never touch SQL. That is right for those tests, but it means nothing proves the endpoints behave
correctly against real data — the `pageSize = 0` → `LIMIT 0` defect passed every endpoint test because
the stubs echoed their input back.

Both documents are authored into #339's structure.

---

## Steps

### 1. Establish what the live tier proves that existing tests do not

**Status:** ⬜ Not started — **do this before writing any assertion**

The `pageSize = 0` class is already covered at repository level:
`SqliteRepositoryTests.GetPageAsync_PageSizeZero_ReturnsEveryRowAsOnePage` covers the generic
repository every masterdata endpoint uses, and `AuditEntryReaderTests`,
`ImportActionWriterReaderTests`, `NotificationReaderTests`, `SqliteFileResourceRepositoryTests` and
`SqliteQuoteServiceTests` cover the hand-written readers.

Re-derive that list rather than trusting it — it is what one pass found on 2026-08-22.

What the live tier adds is the whole path end to end against the **real bundled dataset**: real row
counts, real reference resolution, real translations, which no test with a constructed fixture reaches.
Each case states which of those it proves. A case that proves nothing beyond an existing test is
removed, not kept for symmetry.

### 2. Write the clean-import document

**Status:** ⬜ Not started

A fresh container seeds every bundled source and finishes with zero pending import actions — the
guarantee stated on its own, independent of any one issue's rule files. Assert bundled entity counts
are all non-zero and that no import action remains in a state needing review.

`Pending`, `Blocked` and `Stale` are three distinct states; "needing review" covers all three, not just
`Pending`.

### 3. Write the live-endpoint document

**Status:** ⬜ Not started

The paginated list endpoints, their `GET /{id}` counterparts, and the masterdata endpoints, asserting
real returned data rather than status codes. Cover what step 1 established as genuinely additive.

Enumerate the endpoints from the route registrations at implementation time, not from
`docs/api-endpoints.md` alone — a divergence between the two is itself worth reporting.

### 4. Make both documents independent

**Status:** ⬜ Not started

Of every other test and of each other: each creates, seeds, and destroys its own container and volume.

### 5. Assert no migration number or schema version

**Status:** ⬜ Not started

The suite's existing rule.

### 6. Fill in #339's template fields and propose the smoke designation

**Status:** ⬜ Not started

The clean-import check is a strong smoke candidate — a fresh container failing to seed cleanly
invalidates most other tests. Proposed here for approval rather than assumed.

Fixture files and expected-output samples go in a subfolder beside the document; any script goes to
`scripts/testing/`, per #339.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A fresh container seeds every bundled source and finishes with zero pending import actions | Live | T2: fresh container, no action in `Pending`/`Blocked`/`Stale` |
| 2 | ❌ | Bundled entity counts are all non-zero | Live | T2: every bundled entity type reports a non-zero count |
| 3 | ❌ | Read endpoints are exercised against the real seeded database, asserting returned data | Live | T2: list endpoints, their `GET /{id}` counterparts, and the masterdata endpoints |
| 4 | ❌ | Each endpoint case states what it proves beyond existing stub and repository tests | Live | Every case in the document carries that statement; cases that prove nothing additive are absent |
| 5 | ❌ | Both documents are independent of each other and of every other test | Live | Running either alone, in any order, produces the same result |
| 6 | ❌ | Neither asserts a migration number or schema version | Live | `grep` for version literals in both documents returns nothing |
| 7 | ❌ | Both carry #339's template fields including Determinism and a smoke designation | Live | Field check against the template |
| 8 | ❌ | Resources sit beside the document; any script sits in `scripts/testing/` | Live | No fixture or script outside those two locations |
