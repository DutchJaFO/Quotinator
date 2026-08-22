# #339 — Restructure the T2 suite into docs/automated-testing/, one document per test

**Status:** In progress (step 8)
**GitHub issue:** #339
**Tiers required:** T1, T2
**Depends on:** none

---

## Description

`docs/smoke-tests.md` holds 44 test sections in a single 2,205-line file that only ever grows. This
issue splits it into `docs/automated-testing/`, one document per test inside a category subfolder,
behind a common template — and defines the three run scopes the suite has never had.

No test's assertions change here. #327 fixes the two whose content is wrong and #328 adds new
coverage; both author into this structure once it exists, which is why this lands before them.

**Tiers.** N/A: the change is documentation, one revised ADR, a script relocation under `scripts/`,
and two guard tests. Nothing under `src/` is touched, so neither T1 nor T2 has anything to exercise
that the guard tests and the milestone-close full run do not already cover.

**Two decisions need approval before execution, not during it** — steps 2 and 3. Both are policy
calls that the rest of the work depends on, and both are recorded in this plan for sign-off rather
than settled while moving files.

---

## Scope change — fixed waits become readiness polls (2026-08-22)

**Added by developer decision during step 6.** The issue's boundary says content moves verbatim and no
test's assertions change. This extends it: a test's *wait* is not an assertion, and the suite carries
34 fixed `sleep` calls across 11 distinct values from 1 to 70 seconds, with no readiness-poll pattern
anywhere.

Shipping requirement 11's reliability rule alongside 34 violations of it would make the rule
decorative. Fixing them during the move is also far cheaper than a second pass over 44 documents
later.

**Not a blanket replacement — the waits are not all waiting for the same thing**, and conflating them
would break tests:

1. **Waiting for healthy** — the normal case, becomes a poll on `/health` returning 200.
2. **Waiting for listening** — degraded scenarios, where `/health` returning 503 *is* the expected
   outcome. A poll on 200 would loop forever here, so it polls for any answer at all.
3. **Genuinely waiting for elapsed time** — a TTL expiring, a refresh interval passing, confirming a
   container *stayed* dead rather than became ready. These keep their `sleep` and justify the duration
   in `Determinism`.

Case 3 means this is per-test judgement, not a mechanical sweep. Both poll forms are documented in the
suite index.

---

## Steps

### 1. Settle the category split and the numbering scheme

**Status:** ✅ Done — 2026-08-22

Six categories, confirmed against each section's actual content rather than its title:

| Category | Current sections | Count |
|---|---|---|
| `api-surface/` | 1, 13, 28, 34 | 4 |
| `import-and-staged-actions/` | 2–11, 19–27 | 19 |
| `identity-and-casing/` | 12, 14–18 | 6 |
| `startup-and-degradation/` | 29, 35, 36, 37, 38 | 5 |
| `database-lifecycle/` | 30, 31, 32 | 3 |
| `notifications-and-changelog/` | 33, 39–44 | 7 |

**One correction against the issue's proposal.** It placed section 12 in `import-and-staged-actions/`
with sections 2–11. Reading it, "Canonicalize explicit ids at capture" is an id-canonicalization test
that happens to be exercised through import — the same subject as 14–18. It moves to
`identity-and-casing/`. Nothing else changed.

`import-and-staged-actions/` holds 19 documents, which is large. Left as one folder deliberately:
the candidates for splitting it (staging workflow, entity Modify/decidability, conflict rules,
reporting) are stages of one workflow, and a split on size alone would invent a boundary the content
does not have.

**Numbering is per-category**, two digits plus a stable slug — `api-surface/02-pagination-contract.md`.
Global 01–44 numbering was rejected: inserting a test would renumber every document after it across
the whole tree, which is the fragility the refer-to-a-test-by-name rule already exists to avoid. Per
category, a new test appends to its own folder and disturbs nothing.

### 2. Propose the smoke-set designation for all 44 tests

**Status:** ✅ Done — approved 2026-08-22

The question the set answers: *does this container fundamentally work?* A test earns `Smoke: yes` by
covering a path whose failure would invalidate most other results, not by being important in its own
right. Everything else is regression coverage for a specific issue and runs at milestone close.

**Proposed `Smoke: yes` — 9 of 44:**

| Section | Why it is in the set |
|---|---|
| 1. Baseline | Health, version, random, search. If this fails nothing else is worth running |
| 2. Import and staged-action review workflow | The core import path end to end |
| 13. Pagination contract | One contract shared by three endpoints; a break here is broad |
| 22. Fresh seed produces zero pending actions | The clean-import guarantee — a container that seeds dirty invalidates most import results |
| 27. Per-file import/seed report | The reporting surface every seed operation returns |
| 32. Reset is a full wipe with no reseed | Reset is destructive and the documented recovery route |
| 33. Startup notification system | The mechanism this whole milestone builds on |
| 36. Startup wait page | Proves Kestrel serves during initialisation rather than appearing dead |
| 44. Changelog served from its own on-disk database | #309's regression was silent and permanent once triggered |

**Proposed `Smoke: no` — the other 35.** Chiefly: the entity-specific Modify/decidability tests
(8–11, 20), the identity-and-casing set (12, 14–18), conflict-rule staleness and overrides (23–26),
feature-specific lifecycle tests (30, 31), config wiring (28), spec checks (34), the expensive
failure-path container gymnastics (29, 35, 37, 38), and the version-specific notification migration
paths (39–43).

**The judgement calls worth challenging:** 29 (degraded startup and Reset recovery) is arguably core
never-crash behaviour, but it is an expensive multi-container scenario and #327 is about to rewrite
that area — it can be promoted afterwards. 34 (operationId renames) is cheap and would catch a
breaking API change, but it is narrow.

### 3. Propose the functional/test-only classification of `scripts/`

**Status:** ✅ Done — approved 2026-08-22

Classified by reading each script's own header, not by name.

**Test-only — move to `scripts/testing/`:**

| Script | Evidence |
|---|---|
| `sqlite-storage-probe.csx` | Its header: written for #326, "kept because the degraded and read-only scenarios in `docs/smoke-tests.md` need a way to establish what the storage does" |
| `execute-sql.csx` | Its header: "Exists specifically to break/repair a database file on the host side during manual verification (e.g. `docs/smoke-tests.md`'s #29 …)" |

**Functional — stays put:**

| Path | Why |
|---|---|
| `changelog.csx` | The release workflow and Pre-Push Checklist both invoke it |
| `hooks/` | `commit-msg` and `post-commit` enforce the draft-then-commit rule |

**Removed — `changelog-import.csx`, `changelog-upgrade.csx`, `scripts/changelog-reference/`**
(developer decision, 2026-08-22). The standard applied: a verification tool is kept only if tests
actually exercise it, otherwise it is removed — an unverified verifier proves nothing.

These three exist solely for a round-trip fidelity check documented in `scripts/README.md`. That
check has been broken since #309 moved the changelog source to `data/changelog/` while its step 2
still diffs against `src/Quotinator.Api/resources/changelog.json`, and the README additionally
attributes the output to `changelog-build.csx`, which does not exist. Nothing automated invokes any of
them, and CI installs no `dotnet-script`, so wiring them up would mean adding a build dependency to
test tooling whose only purpose is testing other tooling. Git history keeps them.

`scripts/README.md`'s changelog-import, changelog-upgrade and "Integration test" sections go with
them, and its stale `src/Quotinator.Api/resources/changelog.json` references are corrected in the same
commit.

**`scripts/cache/`** holds the two bundled source JSONs and is documented nowhere. Left in place,
unclassified — it is not a script, and nothing established what writes or reads it.

**Finding, raised separately: `changelog.csx` has no test either.** It produces `CHANGELOG.md` and
both add-on changelogs, and the only thing that ever stood behind it was the manual procedure being
deleted here. `ChangelogSchemaTests` validates the JSON source, not the generator's output. Filed as
#340 (milestone v1.9.0) rather than absorbed here — see the Verification checklist's row 16.

### 4. Define the test-document template

**Status:** ✅ Done — 2026-08-22

Write the template every test document follows, with the fields the issue names: what feature it
verifies, `Smoke`, traces-to, preconditions, determinism, observed effect, commands, expected output,
cleanup.

Two of those fields are the ones that would have prevented the defect this issue was filed from.
**Preconditions** states the exact state the setup must reach before the assertions mean anything,
and how that state is confirmed rather than assumed from the recipe — sections 37 and 38 share a
setup and assert opposite outcomes precisely because neither confirmed it reached its own premise.
**Determinism** names every variable that must be pinned for the result to repeat.

### 5. Write the two guard tests and confirm them red

**Status:** ✅ Done — 2026-08-22

Both added to `RepositoryStructureTests`, the class that already owns
`DocsMarkdownFiles_OnDisk_AreAllInSlnx`.

**`EveryAutomatedTestingDocument_IsLinkedFromTheIndex` — red**, failing on `Expected collection to
not be empty`. Its emptiness assertion is load-bearing rather than defensive: *every document is
linked* is vacuously true over zero documents, so without it the test would pass green on a missing
or empty folder — precisely the state it exists to catch.

**`EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument` — green, vacuously when written.**
With no documents linked there were no links to resolve, so its failure condition could not be
constructed and claiming it started red would have been false.

**Closed 2026-08-22, once `api-surface/` was linked**: one index link was repointed at
`api-surface/03-does-not-exist.md`, the test was confirmed to fail, and the link was restored. It can
fail, and it fails for the right reason.

`.slnx` coverage is deliberately not rebuilt: `DocsMarkdownFiles_OnDisk_AreAllInSlnx` already covers
every Markdown file under `docs/` and picks the new folder up for free.

### 6. Create the folder and move all 44 sections into it

**Status:** ✅ Done — 2026-08-22

43 documents across six categories. 44 minus one: "Systemic id-case guard" had no commands and no
assertions — its own text said it was unit-tier coverage listed only for explanation — so it is folded
into the quote-id document it already named as its own coverage, rather than becoming a test that can
never be run.

Nine are marked `Smoke: yes`, matching step 2's approved set exactly.

**The move was not mechanical, and that was the point.** Filling `Preconditions` and `Determinism`
turned prose into findings repeatedly: a hidden ordering dependency in the pagination test, an id-case
claim that was true of the SQL and false of the endpoints (#341), a "known open gap" that had been
fixed weeks earlier and was still telling readers to expect the failure, and eleven predicted counts
the suite could not legitimately know.

Content moves verbatim except where the template requires restructuring. No test is dropped, merged,
or rewritten — that boundary is what keeps this issue reviewable, and what leaves #327's and #328's
content changes visible as their own work rather than buried in a 44-file move.

Where a test needs fixture files, seed data or expected-output samples, they go in a subfolder beside
its document.

### 7. Write the index

**Status:** ✅ Done — 2026-08-22

Beyond the sections the issue named, three rules were added during the move because the move surfaced
the need for them (developer direction):

- **Wait for a condition, never a duration** — three poll forms, plus the elapsed-time exception.
- **We only observe facts** — zero import failures as the invariant, non-zero where content must
  exist, relationships that survive a dataset change, and stable fixtures as a narrow exception.
- **Cover both the happy and unhappy flow** — a failure must be reproducible or its consequences
  cannot be recorded, and the minimum expected consequence is a stable application.

`docs/automated-testing/README.md` carries: the "Rules every section here follows" block from the
current file, the living-checklist rule, the complete list of every test document, the designated
smoke set from step 2, the three run scopes, and the reliability rule from step 11.

### 8. Convert cross-references into links

**Status:** ⬜ Not started

Today one section points at "section 37's opening paragraph" in prose, which survives neither a
renumber nor a split. Every cross-reference becomes an explicit link to a named document.

### 9. Move test-only scripts, and revise ADR 010 and CLAUDE.md

**Status:** ⬜ Not started

Move the scripts step 3 classified as test-only into `scripts/testing/`. Add the subfolder rule to
[ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md)'s Decision section, revised
in place — the ADR states the effective rule, not the history of arriving at it. Update `CLAUDE.md`'s
Developer Context bullet, which states the `scripts/` placement rule without the subfolder.

The repository stays C#-only and scripts stay `.csx` under `scripts/`. What changes is that a script
supporting a test is visibly separated from one the application or its workflows depend on.

### 10. Remove `docs/smoke-tests.md` and update every reference to it

**Status:** ⬜ Not started

Five live reference sites, all updated in the same commit as the removal:

- `CLAUDE.md` — Pre-Push Checklist step 6, and the Key Files table
- `docs/release-verification.md` — the T2 tier's "When required" and "Gate" text
- `docs/workflow/release.md`
- `docs/ci-cd.md`
- `docs/workflow/checklist.md` — the milestone-close section

Plus any milestone plan doc that links it. `release-verification.md` needs the most care: its current
text records that scoping T2 down was tried once (#196) and was wrong. The run scopes from step 7 are
a different rule — a designated set that always runs plus issue-relevant tests, rather than skipping
T2 because nothing relevant changed — and that text is rewritten to say so, not left standing beside
it.

### 11. State the reliability rule

**Status:** ✅ Done — 2026-08-22, in the index

A test must reproduce its condition reliably; an intermittent result is not a result. Goes in the
index as a rule every test is held to, and is what the template's Determinism field serves.

Two measured cases motivate it. #326 found that WAL sidecar state — not a pending migration — decides
whether a read-only mount degrades, so a test that does not pin how the seeding container was stopped
passes or fails by luck. The source-download path has produced both outright failures and ~300 ms
successes minutes apart. A test whose condition cannot be pinned is reported as such, not left in the
suite as a coin flip.

### 12. Record the Knowledgebase relationship

**Status:** 🟡 Half done — #333 updated 2026-08-22; the index half is not written

A test creates a specific circumstance on purpose and shows what it produces, which is what a
Knowledgebase entry needs with the cause already established and the remedy already exercised. The
template's **Observed effect** field is what captures it.

#333 has been updated to sweep the test documents alongside its message sweep, and to state affected
versions explicitly on every entry. The remaining half is the index recording that these outcomes are
entry material.

No entry is written here — #333 is milestone v1.9.0.

### 13. Resolve the live-only Definition-of-done gap in `docs/workflow/issues.md`

**Status:** ⬜ Not started

The Enhancement template says to omit the Expected tests section when all verification is live, but
the Definition of done is copied verbatim and its first box reads "All expected tests listed above
start red before implementation" — which then has no referent. The current workaround is a
placeholder table row existing only to give the box something to point at (see #328). Fix it in the
templates so a live-only issue has a Definition of done it can honestly tick.

### 14. Register every new document in `Quotinator.slnx` and confirm the guards green

**Status:** ⬜ Not started

One flat top-level `<Folder>` element per category path — `.slnx` does not support nested folders.
Then confirm both guard tests from step 5 are green, and the full suite shows no regression.

Step 5's second guard already had its real red-green, taken as soon as `api-surface/` gave it links to
resolve — see that step.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `docs/automated-testing/` exists with one kebab-case subfolder per category | Live | `ls docs/automated-testing/` lists exactly the categories agreed in step 1, all kebab-case |
| 2 | ❌ | One numbered document per existing section; no test dropped, merged, or rewritten | Live | Document count equals the section count of the removed file; each section's commands appear in exactly one document |
| 3 | ❌ | Every test document carries the full template, including Preconditions, Determinism and Observed effect | Live | Every file under `docs/automated-testing/*/` contains all template field headings |
| 4 | ❌ | An index carries the rules block, the living-checklist rule, and every test document | Live | `docs/automated-testing/README.md` lists all documents; guard test in row 13 proves the list is complete |
| 5 | ❌ | Every test is marked in or out of the designated smoke set, and the index lists the set | Live | Every document has a `Smoke:` field; the index's smoke list matches the documents marked `yes` |
| 6 | ❌ | The index states the three run scopes as the authoritative definition | Live | Index names per-issue T2, milestone close, and release, and `release-verification.md` points at it rather than restating it |
| 7 | ❌ | The numbering scheme is recorded; each filename carries a number and a stable slug | Live | Index states the scheme; every filename matches `NN-slug.md` |
| 8 | ❌ | Cross-references between tests are explicit links, not prose | Live | No document refers to another by section number or by "the section above/below" |
| 9 | ❌ | Test resources sit beside the document; test scripts sit in `scripts/testing/` | Live | `ls scripts/testing/` holds the scripts classified in step 3; no `.csx` under `docs/` |
| 10 | ❌ | ADR 010 revised in place; `CLAUDE.md`'s Developer Context bullet matches | Live | ADR 010's Decision section states the subfolder rule; `CLAUDE.md` states it identically |
| 11 | ❌ | The reliability rule is stated in the index and served by a Determinism field on every document | Live | Index carries the rule; row 3's field check covers the per-document half |
| 12 | ❌ | `docs/smoke-tests.md` is removed and every reference updated in the same commit | Live | `grep -rn "smoke-tests" --include=*.md .` returns no hit outside `.claude/` |
| 13 | ❌ | Guard tests: every document is linked from the index, and every index link resolves | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex`, `...EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument` — both red before step 6 |
| 14 | ❌ | Test outcomes are recorded as Knowledgebase material, and #333 sweeps the test documents | Live | #333 requirement 6 states the sweep (done 2026-08-22); the index states the relationship |
| 15 | ❌ | A live-only issue has a Definition of done it can honestly tick | Live | `docs/workflow/issues.md` no longer requires a placeholder Expected-tests row for live-only verification |
| 16 | ❌ | Every fixed wait is a readiness poll, or a duration justified in `Determinism` | Live | `grep -rn "sleep " docs/automated-testing/` returns only waits whose own document explains what the duration measures |
| 17 | ❌ | The unverified changelog round-trip tooling is removed, and `changelog.csx`'s own lack of test coverage is filed rather than absorbed | Live | `scripts/changelog-import.csx`, `scripts/changelog-upgrade.csx` and `scripts/changelog-reference/` gone; `scripts/README.md` carries no reference to them and no stale `resources/changelog.json` path; #340 covers testing `changelog.csx` |
