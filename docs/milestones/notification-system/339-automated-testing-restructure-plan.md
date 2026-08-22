# #339 — Restructure the T2 suite into docs/automated-testing/, one document per test

**Status:** Planning
**GitHub issue:** [#339](https://github.com/DutchJaFO/Quotinator/issues/339)
**Tiers required:** N/A
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

## Steps

### 1. Settle the category split and the numbering scheme

**Status:** ⬜ Not started

The issue proposes six categories and maps all 44 current sections onto them. Confirm the split
against the actual section contents rather than the Contents list alone — a section's title does not
always match what it exercises.

Decide numbering in the same pass: per-category (`01`, `02`, … restarting in each folder) or global
(`01`–`44` across the whole tree). Either satisfies the issue; the choice has to be recorded because
it determines every filename. `smoke-tests.md`'s existing rule — refer to a test by what it verifies,
never by its number — carries over unchanged, and each filename carries a stable slug after the
number so a renumber never orphans a reference.

### 2. Propose the smoke-set designation for all 44 tests

**Status:** ⬜ Not started — **approval gate**

Mark every test in or out of the designated smoke set, and put the full proposal here for the
developer to approve before any document is written. Which tests constitute a smoke pass is a policy
call, not an implementation detail: the set is what every issue's T2 pass runs from then on.

Nothing in steps 6 or 7 can be finished before this is answered — the `Smoke` field is part of the
template, and the index lists the set.

### 3. Propose the functional/test-only classification of `scripts/`

**Status:** ⬜ Not started — **approval gate**

Classify every existing script under `scripts/` as functional or test-only, and put the full list
here for approval before anything moves. `scripts/sqlite-storage-probe.csx` is a known test-only
instance — written for #326's measurement, supporting a startup-and-degradation test. The rest are
not assumed either way; `changelog.csx` and its siblings are load-bearing for the release workflow
and a wrong move breaks it silently.

### 4. Define the test-document template

**Status:** ⬜ Not started

Write the template every test document follows, with the fields the issue names: what feature it
verifies, `Smoke`, traces-to, preconditions, determinism, observed effect, commands, expected output,
cleanup.

Two of those fields are the ones that would have prevented the defect this issue was filed from.
**Preconditions** states the exact state the setup must reach before the assertions mean anything,
and how that state is confirmed rather than assumed from the recipe — sections 37 and 38 share a
setup and assert opposite outcomes precisely because neither confirmed it reached its own premise.
**Determinism** names every variable that must be pinned for the result to repeat.

### 5. Write the two guard tests and confirm them red

**Status:** ⬜ Not started

`RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex` and
`...EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument`, added to the class that already
owns `DocsMarkdownFiles_OnDisk_AreAllInSlnx`. Both must be red before step 6 creates anything — with
no folder and no index, they fail for the right reason.

`.slnx` coverage is deliberately not rebuilt: `DocsMarkdownFiles_OnDisk_AreAllInSlnx` already covers
every Markdown file under `docs/` and picks the new folder up for free.

### 6. Create the folder and move all 44 sections into it

**Status:** ⬜ Not started

Content moves verbatim except where the template requires restructuring. No test is dropped, merged,
or rewritten — that boundary is what keeps this issue reviewable, and what leaves #327's and #328's
content changes visible as their own work rather than buried in a 44-file move.

Where a test needs fixture files, seed data or expected-output samples, they go in a subfolder beside
its document.

### 7. Write the index

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

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
