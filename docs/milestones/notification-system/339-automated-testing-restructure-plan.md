# #339 — Restructure the T2 suite into docs/automated-testing/, one document per test

**Status:** In progress — all 25 steps are done and every verification row except 28 is closed. The suite is PowerShell throughout, guarded by a test, and all 43 documents were executed on 2026-08-26 with no application defect found. Row 28 stays blocked independently: `16` and `17` cannot confirm their own behaviour until [#347](https://github.com/DutchJaFO/Quotinator/issues/347) makes the staleness evaluation observable
**GitHub issue:** #339
**Tiers required:** none — see *Tiers* below
**Depends on:** [#347](https://github.com/DutchJaFO/Quotinator/issues/347) for verification row 28

---

## Description

`docs/smoke-tests.md` holds 44 test sections in a single 2,205-line file that only ever grows. This
issue splits it into `docs/automated-testing/`, one document per test inside a category subfolder,
behind a common template — and defines the three run scopes the suite has never had.

It lands before #327 and #328, which both author into this structure once it exists.

**Assertions did change here, contrary to what this section said when it was written.** The original
boundary was "content moves verbatim, no test's assertions change". Three developer decisions widened
it in turn — readiness polls, environment profiles, and finally the requirement that every test add
value and actually test the defect it claims to. See the Scope change sections below; each records what
moved the line and why. What remains true is that #327 still owns rewriting
`startup-and-degradation/05`, whose premise is unreachable.

**Tiers.** None. The change is documentation, one revised ADR, a script relocation under `scripts/`,
and five guard tests. Nothing under `src/` is touched, so neither T1 nor T2 has anything to exercise
that the guard tests and the milestone-close full run do not already cover. The header previously said
`T1, T2` while this paragraph said N/A — a contradiction present from the first draft.

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

## Scope change — the suite gets environment profiles (2026-08-22)

**Added by developer decision after step 14's audit.** The audit found that 21 of 43 documents start
no container: the previous file's §1 was a *Baseline* section supplying the container, port, admin key
and first-boot seed once, for everything after it. Splitting one document per test removed it and put
nothing in its place.

**That has to be resolved here, not deferred.** This issue is the restructure; leaving half the suite
unrunnable would make the result worse than the 2,205-line file it replaced, which is the one outcome
the split cannot be allowed to produce.

Steps 15–17 are the addition: named environment profiles each document invokes, a snapshot/restore
procedure so a group of tests does not pay for a rebuild per test, and the mechanical fixes that stop a
document executing at all. Test-quality defects that predate the split — a test that cannot fail, a
missing unhappy flow, a document that should not be live at all — are recorded and filed, not absorbed.

---

## Scope change — every test owns its container, so any two can run together (2026-08-23)

**Added by developer decision.** The environment profiles as first built shared one container, `qt-env`,
across most documents. That was wrong, and the tell was already in the suite: sixteen documents ended
with "restore the Fresh profile before the next test" — a restore step between every pair, which is
coupling wearing a cleanup label.

**Each test creates and destroys its own container and volume.** A profile is a *recipe* a test
instantiates under its own name, not an instance it borrows. The only genuinely shared step is building
the image, because it is the same image for every test.

**The consequence is that any two tests can run at the same time.** No machine will have the resources
for all forty-three at once, but nothing in the design prevents it — and running several concurrently is
what makes an end-of-issue T2 pass quick rather than a serial slog. That only holds while no two tests
can reach each other's state, which is why per-test containers are a necessity rather than a preference.

**Host ports are derived from where the document lives** — `18` + category ordinal + test number — so
they cannot collide and no allocation table has to be maintained. A guard enforces both halves: a
document publishes every port it talks to, and no two documents publish the same one.

**What this deletes:** every "restore the profile" instruction, and every sentence describing what a
test leaves behind *for another test*. What survives is what a test must undo outside its own container
— a mutated bundled rule file, a rebuilt image, `Directory.Build.props` — because those are genuinely
shared.

**The recipe lives in `scripts/testing/test-env.csx`, not in forty-three copies of it** (developer
direction, 2026-08-23). Spelled out per document, the same eight-line `docker run` block and teardown
pair differed only by name and port — so adding one environment variable to how a test environment is
built would have meant editing every document that has one. A document now says only what makes it
different: an extra setting, a prior published image, a bind directory, which readiness condition it
wants. The script echoes every `docker` command before running it, so a reader still sees exactly what
executed.

Three constraints it must respect, each measured rather than assumed:

- **A bind path is passed through untouched.** Bash and `docker -v` both resolve `/tmp/x` to `%TEMP%\x`;
  .NET's `Path.GetFullPath` roots it at the current drive and yields `C:\tmp\x`, which does not exist.
  Resolving it in the script would bind that, and the test would read an empty database — indis-
  tinguishable from a pass. Two documents explained this as a Docker-VM path; that was wrong and is
  corrected.
- **`--port` is optional.** A container waited on by its own log line publishes none, and requiring a
  port forced two documents to invent numbers that then contradicted their own `Determinism`.
- **`create` always starts clean, so it cannot re-enter an environment.** Eight steps — upgrades against
  a seeded directory, second startups — stay raw `docker` and say why.

**That last constraint was accepted too readily and is lifted in the 2026-08-26 scope change below.**
Re-entry is not something `create` *cannot* express — it is something the script was never asked to
express. Five of the nine raw blocks re-enter a **bind** directory, which `create --bind` already
preserves because the script deliberately never touches a bind path, so those nine lines were duplicating
the recipe for no reason at all.

---

## Scope change — the suite is converted from bash to PowerShell (2026-08-26)

**Added by developer decision.** Verification row 27 records the finding and defers the work: the suite
is written in a shell this project does not use. 320 ```bash fences, none of which run in PowerShell —
`grep` 158, `MSYS_NO_PATHCONV=1` 51, `cut -d` 40, `until … done` 37, `wc -l` 29, heredocs 19.

**This is not a dialect preference.** Three separate reasons, each measured rather than assumed:

- **ADR 010 already forbids it.** *"No Python, Perl, Node.js, or Unix text-processing one-liners (`sed`,
  `awk`, etc.) anywhere in this repository or its tooling — not as committed scripts, not as ad hoc
  one-off commands"*, and *"PowerShell remains the primary shell"*. A committed
  `grep -o '"batchId":"[^"]*"' | cut -d'"' -f4` is exactly the shape that names, and 40 documents carry
  one. The suite has been out of compliance with an ADR this same issue revised (step 9) since it was
  written.
- **Bash actively produced two false defect reports during #339's own run.** Its path conversion mounted
  a VM-internal directory where `dotnet script` mounted the Windows one, and without
  `MSYS_NO_PATHCONV=1` it rewrote `-e Quotinator__DataDir=/data` into `C:/Program Files/Git/data`.
- **The instrument bugs row 28 kept repairing are a property of string-matching a response.**
  `grep -c` counting lines rather than matches, `Select-String` being case-insensitive where `grep` is
  not, a pattern missing a space against a pretty-printed spec — six of the twelve repairs after the
  full run were this class. Parsing the response into an object removes the class, rather than fixing
  its instances one at a time.

**The idiom, settled before conversion (developer decision, 2026-08-26).** Cmdlets where they work,
`.csx` helpers where Windows PowerShell 5.1 cannot. `pwsh` is not installed on the development machine
and installing it is not a prerequisite this suite may impose, so 5.1 is the target — which rules two
things out by measurement, not by taste:

| Written in PowerShell 5.1 | What a native exe actually receives |
|---|---|
| `'{"quoteText":{"choice":"keep"}}'` | `{quoteText:{choice:keep}}` — the JSON is destroyed |
| `'{\"quoteText\":\"keep\"}'` | `{"quoteText":"keep"}` — correct, and hand-escaped 52 times |

So `curl.exe` cannot carry a JSON body without hand-escaping every one of the 12 `-d` bodies and 40
`-F 'settings={…}'` fields — the same silent-corruption shape the suite already got burned by. And
`Invoke-RestMethod` has **no `-Form`** before PowerShell 7, so multipart upload has no cmdlet path at
all. The resolution is one helper rather than one workaround per call site.

**Four rules, each with the reason stated at the point of use:**

- **Reading JSON to assert on** — `Invoke-RestMethod`, then assert on properties. No argument layer to
  mangle the URL, and the result is an object rather than a line of text.
- **Sending a JSON body** — `Invoke-RestMethod -Method Post -ContentType application/json -Body`. A
  cmdlet parameter, so the JSON survives.
- **A status a test expects to be non-2xx** — `scripts/testing/http.csx`. 88 such expectations across 24
  documents (35 × 422, 24 × 503, 15 × 404), and `Invoke-RestMethod` throws on every one of them; reading
  the body back out of the exception in 5.1 takes a `StreamReader` over `$_.Exception.Response`.
- **Uploading a file** — the same helper. It also returns the `batchId` directly, which retires the
  `grep -o … | cut` capture idiom the index currently has to teach.

**Measured, so the helper is affordable:** a warm `dotnet script` invocation costs ~300 ms on this
machine, so the ~120 helper calls across the suite add well under a minute in total.

**Converting without running is the mistake row 27 already records** — *"the replacements written for
this row are themselves bash, so they moved the documents further from runnable"*. The conversion is
therefore not done when the fences are rewritten; it is done when they have been executed.

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

**Status:** ✅ Done — 2026-08-22

A survey of all 43 documents found seventeen prose cross-references. **Seven named a specific document
and are now links**; the rest are general statements about the suite — "the tests that follow", "an
earlier test's batch", "every other test here" — with no single target. Converting those would have
meant inventing one, so they stand.

**Adding the links exposed a gap and closed it.** The existing guard checks only that the *index's*
links resolve, so seven new inter-document links were created with nothing protecting them.
`EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument` now covers every relative Markdown
link in every document. Confirmed it can fail: one link was repointed at a nonexistent file, the test
failed, and the link was restored.

Today one section points at "section 37's opening paragraph" in prose, which survives neither a
renumber nor a split. Every cross-reference becomes an explicit link to a named document.

### 9. Move test-only scripts, and revise ADR 010 and CLAUDE.md

**Status:** ✅ Done — 2026-08-22

`execute-sql.csx` and `sqlite-storage-probe.csx` moved to `scripts/testing/` via `git mv`, so history
follows them. `changelog-import.csx`, `changelog-upgrade.csx` and `scripts/changelog-reference/`
removed per step 3's approved classification.

ADR 010 revised in place with two clauses: a test-support script lives in `scripts/testing/`, and a
script kept only for verification is kept only if something verifies with it. `CLAUDE.md`'s Developer
Context bullet and `scripts/README.md` match; the README's four obsolete sections went with the
scripts they documented, and its stale `src/Quotinator.Api/resources/changelog.json` path is gone.

Move the scripts step 3 classified as test-only into `scripts/testing/`. Add the subfolder rule to
[ADR 010](../../architecture-decisions/010-repository-is-csharp-only.md)'s Decision section, revised
in place — the ADR states the effective rule, not the history of arriving at it. Update `CLAUDE.md`'s
Developer Context bullet, which states the `scripts/` placement rule without the subfolder.

The repository stays C#-only and scripts stay `.csx` under `scripts/`. What changes is that a script
supporting a test is visibly separated from one the application or its workflows depend on.

### 10. Remove `docs/smoke-tests.md` and update every reference to it

**Status:** ✅ Done — 2026-08-22

A survey found more than a `docs/*.md` grep had: four references in code and scripts, and 38 in
historical plan docs. All now point somewhere real.

`release-verification.md`'s T2 tier needed a rewrite rather than a path swap — its "When required" and
"Gate" text described one all-or-nothing file, which the three run scopes replace. `CLAUDE.md`
(Pre-Push Checklist step 6, plus a new Key Files row), `ci-cd.md` and `workflow/release.md` follow.
Two source comments citing tests by number — `SqliteConnectionFactory.cs` and `ChangelogReaderTests.cs`
— now name the document instead.

**A `grep` for `smoke-tests.md` still returns hits, and that is correct.** The archival pointers wrap
onto a second line, so a line-scoped grep shows the old name without the adjacent replacement. Verified
per file by comparing reference counts instead.

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

**Status:** ✅ Done — 2026-08-22

A test creates a specific circumstance on purpose and shows what it produces, which is what a
Knowledgebase entry needs with the cause already established and the remedy already exercised. The
template's **Observed effect** field is what captures it.

#333 has been updated to sweep the test documents alongside its message sweep, and to state affected
versions explicitly on every entry. The remaining half is the index recording that these outcomes are
entry material.

No entry is written here — #333 is milestone v1.9.0.

### 13. Resolve the live-only Definition-of-done gap in `docs/workflow/issues.md`

**Status:** ✅ Done — 2026-08-22

`issues.md` gains a **live-only variant**: a second fixed pair of Definition-of-done boxes an issue
copies instead of the two that reference an Expected tests table. It preserves what red-to-green
protects — the expectation is committed to before the result is seen — without pointing at a table
that does not exist.

The placeholder row (`| — | Live tests only | ❌ |`) is explicitly ruled out: it satisfies the shape
and not the rule.

**Follow-on, not done here:** #327 and #328 both carry that placeholder and should adopt the variant.
That is an issue-body edit needing its own draft, best done once as a combined change after this issue
closes.

### 14. Audit every document against the rules the index now states

**Status:** ✅ Done — 2026-08-22. All 43 audited in three batches; findings below.

Run in three batches rather than one sweep, so a bad assessment is caught before it propagates.

**What the audit produces is a list, not a set of edits.** Only the cheap fixes land here: a missing
`Determinism` line, a count that should be a relationship, a wait that should be a poll. Everything
requiring new test content — most obviously a missing unhappy flow — is recorded against the issue
that owns the feature. Expect the "both flows" rule to fail widely: these documents were written to
describe what the previous suite did, and the previous suite was overwhelmingly happy-path.

#### Findings — batch 1 (`api-surface`, `identity-and-casing`, `database-lifecycle`; 12 documents)

| Rule | Fails |
|---|---|
| 1 — both flows | 8 of 12 |
| 2 — preconditions confirmed | 7 of 12 |
| 3 — determinism | 11 of 12 |
| 4 — no predicted counts | 1 of 12 |
| 5 — waits are conditions | **0 of 12** |
| 6 — could be in-process | 12 of 12 — **superseded, see batch 3** |

**The category-level finding: `identity-and-casing` should not be a live category at all.** All five
documents are in-process candidates end to end — foreign keys, repository queries and endpoint wiring
against real SQLite, none of which needs a container, volume or network. `02` justifies itself as
proving "route binding, 404-versus-200", which is precisely what `WebApplicationFactory` proves and
what `StartupResilienceTests` already does for a harder case. Moving them is new work; recorded, not
done here.

**That conclusion was re-checked against the original sections and holds — but the rule 6 count above
does not.** All five are in-process reachable *in principle*; they are also unrunnable *as written*,
because their container came from §1. Both are true and they are independent. The count of 12 of 12
treated the second as evidence for the first. See the consolidated finding under batch 3 for the
measured figure.

**Determinism fails for a reason the move could not have caught.** Four documents — `02-pagination-contract`
and `identity-and-casing/01`–`03` — contain **no container start at all** and assume something correct
is listening on `:8080`. They inherited that from a single-file suite read top to bottom, where an
earlier section had started the container. Split into standalone documents, the assumption is exposed.
This is the same class as the ordering dependencies already recorded, found from the other direction.

**Both flows fails across all of `identity-and-casing`**, which has a second consequence per the
index: a happy-path-only test has nothing to contribute to the Knowledgebase, so that whole category
is currently invisible to #333.

**Waits: clean.** No fixed `sleep` survives anywhere. The residual problem is inverted — four documents
have no wait where one is required, counted under rule 2.

**Fixed here:** the one predicted count, `api-surface/03`'s "returns `200` with one item", now asserts
the fixture's own item is present rather than a total.

#### Findings — batch 2 (`startup-and-degradation`, `notifications-and-changelog`; 12 documents)

| Rule | Fails |
|---|---|
| 1 — both flows | 6 of 12 |
| 2 — preconditions confirmed | 7 of 12 |
| 3 — determinism | 7 of 12 |
| 4 — no predicted counts | **0 of 12** |
| 5 — waits are conditions | 1 of 12 (disputed, see below) |
| 6 — could be in-process | 12 of 12 — **superseded, see batch 3** |

**A test that could not fail, found and fixed.** `notifications-and-changelog/04` counted duplicate
announcements with `grep -c`, which counts matching *lines* — and the API returns single-line JSON, so
a genuine duplicate still reported `1`. Its stated pass condition, "still `1`, not `2`", was
unreachable in the failing direction. This is the same class of defect as the §37/§38 contradiction
that prompted this issue, found by the audit rather than by a run. Now `grep -o … | wc -l`.

**Two apparent contradictions, neither of which was one** (resolved 2026-08-23, after the developer
asked why both sides would have to be wrong).

`notifications-and-changelog/02` and `05` disagree about whether the current build reports `1.8.3` —
because `05` temporarily edits `Directory.Build.props` and `02` does not. Two setups, both correct. What
`02` does carry is a real fragility: its "exactly one row" holds only while the current version equals
the last release, and turns into a false failure the moment `Directory.Build.props` moves.

`startup-and-degradation/01` and `02` disagree about whether a healthy restart takes a backup — and here
exactly one was wrong. #277 gated backups on each action's own real-work signal; `01` went on describing
the behaviour from before it, and argued that behaviour was "a deliberately chosen tradeoff, not a bug"
while #277's own background names it as the defect being fixed. `01`'s justification even described the
missing gate #277 supplied.

**The mount-type explanation recorded here earlier was invented, not found.** Bind mount versus named
volume has nothing to do with backup gating; it was a plausible-sounding difference offered as a
possible discriminator, and it propagated into both documents before anyone checked it.

**Rule 5's single failure is disputed and was not changed.** The audit argued `startup-and-degradation/03`'s
`sleep 1` should poll for `503 {"status":"starting"}`. That state is pollable but *transient*: if
seeding finishes before the first poll the loop hangs forever, where the sleep merely fails. A poll
waits for something to become true and stay true; it cannot catch a closed window. The document now
says so explicitly.

**Directory reset is inconsistent within one folder** — `/tmp/qprov` and `/tmp/qws` are `rm -rf`'d,
while `/tmp/q312`, `/tmp/qv4`, `/tmp/qdup` and `/tmp/qt-changelog` are reused. A leftover directory
silently changes what the run starts from.

#### Findings — batch 3 (`import-and-staged-actions`; 19 documents)

| Rule | Fails |
|---|---|
| 1 — both flows | 12 of 19 |
| 2 — preconditions confirmed | **15 of 19** |
| 3 — determinism | see below |
| 4 — no predicted counts | 2 of 19 |
| 5 — waits are conditions | **0 of 19** |
| 6 — could be in-process | 14 of 19 wholly, 3 more in part |

**Rule 2 is the finding.** Fifteen of nineteen start no container, and every one of them drives
`localhost:8080`; most send an `X-Api-Key` that nothing in the document sets. Only `14`–`17` provision
themselves.

**Three documents cannot fail at all.** `16` and `17` assert only that a list is empty, with no
positive control — a disabled staleness mechanism, a silently failed reseed, a regressed `status=`
filter and the intended pass produce the identical observation. `03` deliberately selects `skip`
policy "so the staged batch leaves nothing pending", which guarantees that a correct apply and a dead
handler are indistinguishable; its stated purpose is proving that path is not dead.

**Determinism, named rather than counted.** One unpinned variable —
`Quotinator__AutoPurgeBundledImportActions` — sits under `14`, `16` and `17`, each of which concludes
from an empty list that auto-purge produces independently of the behaviour under test. Only `15` names
it. Separately: `09` computes a before/after delta with no before-value, `12` is non-idempotent against
its own container (it marks a Character `Complete` permanently), and `19` runs `reset` mid-document and
then reads a startup log written before it.

**Cross-contamination through the image tag.** `15` edits a bundled rule file, rebuilds, and never
rebuilds after reverting — so `quotinator:local` stays mutated, and `14`, `16` and `17` then run it.

**Rule 4 is fully landed across all three batches** — 3 failures in 43. `10` asserts `Frozen` returns
`2013` with no query for `Frozen` anywhere, and `19` hardcodes "all nine entity types", which goes
stale exactly the way a migration number does.

#### The consolidated finding: the split removed the environment, and nothing replaced it

Across all 43 documents, **21 start no container**. The previous suite's §1 was a *Baseline* section
that supplied the container, the published port, the admin key and the first-boot seed once, for
everything that followed. One document per test removed it without replacing it.

That single line is the whole of it:

```bash
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
```

**And it does not work as written.** `--rm` without `-d` runs in the foreground, so the `curl` lines
after it in the same block can never execute — verbatim from the original, in both `api-surface/01` and
`03`. `api-surface/01` is the baseline the twenty-one were relying on.

**Live-necessity, measured rather than assumed.** Eighteen of 43 have a genuinely live-only component:
the published `1.8.2`/`1.8.3` images, a `--read-only` root, `docker exec ls /data/backups`, Kestrel
binding a real socket, a file diffed from inside the image, and one case where real Kestrel handles a
bodyless request differently than `TestServer` does. The other 25 have none. An earlier assessment in
this step put that at 12 of 12 and then 24 of 24; it was wrong, and wrong for a specific reason worth
recording — a document that starts no container *looks* container-free, and that appearance was counted
as evidence it needed none. It is evidence of a broken split instead. The two are independent facts and
were conflated.

**#339 verifies the tests, it does not only relocate them** (developer direction, 2026-08-22). The move
established the structure and the rules; this step checks each of the 43 documents against them rather
than assuming the move produced compliance.

Per document:

- **Both flows covered.** A document proving only the happy path is incomplete. Several already
  satisfy this — bodyless validation, `batchId` validation, the degraded-startup tests — and several
  do not.
- **`Preconditions` states what must be true and how it is confirmed**, not inferred from the recipe.
- **`Determinism` names every variable the outcome depends on.**
- **No predicted count** survives in an Expected output section.
- **Every wait is a condition**, or an elapsed-time exception that says what it measures.
- **`Observed effect` is honest** — recorded where it exists, stated as absent where it does not.
- **No dependency on execution order.** The test establishes everything it needs and leaves nothing
  another test did not ask for.

  **Needing content is a legitimate precondition; inheriting it from an earlier test is not.** Each
  affected document picks one of two honest resolutions: guarantee the precondition itself, or declare
  what blocks it and accept that it cannot run until that is fixed. The second is where a prepared
  resource earns its place — a test whose content comes from the application's own import path is lost
  exactly when an import defect makes it most worth running.

  Four documents breach this today and carry a note: the pagination contract assumes populated audit
  and import tables, the staged workflow deliberately leaves its applied batch, the read-time
  presentation test leaves staged actions, and the two-phase reversal expects a batch a sibling
  created. Resolving each means writing its own setup or its own fixture — new content, not a move.
- **Nothing live that could be proven in-process.** Ask of each document what part genuinely needs a
  container, a volume or a network; the rest belongs in a test that runs on every build. Where a
  document is wholly or partly reducible, record which part and against which issue — writing the
  replacement test is new work, not a move.

Retrofitting a missing unhappy flow means writing new test content, not moving existing content. Where
that is more than a small addition, record it as a finding for the issue that owns the feature rather
than expanding this one — the audit's job is to establish which documents are complete, not to make
every one complete regardless of cost.

### 15. Define the environment profiles and the snapshot/restore procedure

**Status:** ✅ Done — 2026-08-23, in the index

The audit's consolidated finding is that the split removed the environment. This step replaces it with
something better than §1 was: a small set of named profiles, each stated once in the index and
*invoked* by every document that needs it. A named setup a test runs is not a dependency on another
test — it is the index's own first honest resolution, "guarantee it".

Three profiles, grouped by what must be true before step 1:

| Profile | What it establishes |
|---|---|
| **Fresh** | New volume, first boot, bundled seed, nothing else |
| **Constrained** | Deliberately defective — read-only root, dropped table, missing writable path |
| **Upgraded** | A real prior published image ran first, then the current one |

**Content is a separate axis and stays the test's own responsibility.** The ~26 documents needing a
populated database are Fresh plus a step they already own, not a fourth profile. This is what keeps
the index's "anything a test needs, it establishes itself" intact rather than quietly reintroducing a
shared seeded state.

**Every profile pins `Quotinator__AutoPurgeBundledImportActions` explicitly** — Fresh to the
application's own default, `true`. It is the unpinned variable underneath three of the cannot-fail
documents, and pinning it makes which behaviour is in play readable rather than assumed.

**Pinned to the default, not to the value that would be convenient.** Setting Fresh to `false` would
have made those three documents' empty-list assertions meaningful, and was the first choice here — but
it would also have made the profile unrepresentative of what a user runs, which is a worse trade. A
test needing the rows retained declares `false` as its own delta; `database-lifecycle/02` already does
exactly that.

**Snapshot and restore, so a group runs without paying for a rebuild per test** (developer direction,
2026-08-22):

- **Image** — tag the milestone's base image (`quotinator:m<N>-base`) and `docker save` it once at
  milestone start. Tests run against the pinned tag; a test that must rebuild builds its own throwaway
  tag. This alone removes the `15`→`14`/`16`/`17` contamination, where a mutated bundled rule file
  stays baked into `quotinator:local`.
- **Database** — captured from a **stopped** container with the `-wal`/`-shm` sidecars, per the
  procedure `import-and-staged-actions/10` already documents with measured evidence, or via SQLite's
  own `VACUUM INTO`. A backup taken from a running container is the exact raciness `14` and `15`
  currently exhibit.
- **Restore is unconditional between tests**, never "if the test dirtied something" — the moment it is
  conditional, inherited state is back under a better name.
- Ordering within a group is allowed; ordering across groups is not, and every group must start cold.

**The milestone-start snapshot is also the migration fixture.** The four upgrade documents currently
hardcode `ghcr.io/dutchjafo/quotinator:1.8.3`, so they can only test an upgrade someone already
published and they go stale on the next release. A base image captured at milestone start *is* the
version this milestone upgrades from — provided the milestone branched from the released tag, which is
the condition that makes the snapshot legitimate and must be stated when it is taken. Backups are
deleted once the milestone is published.

**Not adopted yet: reset-and-reseed as a cheaper restore.** `POST /api/v1/admin/database/reset`
followed by `POST /api/v1/admin/database/reseed` would avoid a container restart, but since #156 Reset
is a full wipe that deliberately does not reseed, and whether the pair reproduces a fresh first boot is
unverified — quote content should reproduce from deterministic ids, but audit rows, notification rows,
the two schema-version counters and `Import_Action` rows plausibly differ. Prove that equivalence
before the suite rests on it.

### 16. Give every document its environment

**Status:** ✅ Done — 2026-08-23. All 43 declare a profile and establish it; guard green.

Every document names its profile and runs it. The 21 that start no container stop assuming one; the
rest are checked for the same class rather than trusted.

Guard, alongside step 5's three: **every document names a known profile.** Same shape as the existing
link guards, and it is what prevents the next document from being written the way these were.

### 17. Fix the defects that stop a document from being run at all

**Status:** ✅ Done — 2026-08-23

Scoped deliberately: **what the restructure broke, or what blocks a document from executing.** These
are cheap and mechanical.

- `docker run --rm` without `-d` in `api-surface/01` and `03` — the container holds the terminal, so
  nothing after it runs.
- No `--name` on any `docker run`, leaving `<container>` unresolvable in every `docker cp`/`docker logs`.
- Steps written as prose with no command, and SQL literals elided to `'f0000002-...'`.
- Inconsistent `/tmp` directory resets — `/tmp/qprov` and `/tmp/qws` are cleared, `/tmp/q312`,
  `/tmp/qv4`, `/tmp/qdup` and `/tmp/qt-changelog` are reused.

### Scope, settled (developer direction, 2026-08-23)

**Every test in this suite must add value and actually test the defect it claims to test.** That is
this issue's bar, not "runnable and no worse than the file it replaced" — an earlier line drawn here
and since overtaken. A document that cannot fail is not coverage, and moving it into a tidier folder
does not make it coverage. So the cannot-fail defects are fixed here rather than filed.

What that pulls in:

- **Every document that cannot fail**, by the three mechanisms step 14 found — an absence asserted with
  no positive control, a state change asserted with no read-back, and a comparison with only one side
  measured.
- **Both live contradictions** — `startup/01` vs `02` on backup-on-healthy-restart, `notif/02` vs `05`
  on the version pin. Two documents asserting opposite outcomes means at least one is wrong, and
  neither is coverage until that is settled.
- **The two surviving predicted counts** (verification row 18).

**A dedicated issue is only for coverage that does not exist at all** (developer direction,
2026-08-23) — not for repairing a test that exists but does not work. One thing meets that bar here:
**`User`-origin coverage and origin parity**, filed as
[#346](https://github.com/DutchJaFO/Quotinator/issues/346). Content reaches the database by three routes
with separately-configured behaviour, and this suite exercises two of them; the `{dataDir}/imports/`
folder has never been touched. That is a test nobody has written, so it is filed rather than fixed.

**The id-casing guarantees are the sharpest part of that gap** (developer direction, 2026-08-23). All
five `identity-and-casing/` documents establish capture-time canonicalization and either-casing lookup
by `POST /import`, which is `Upload`. Nothing asserts either for a `User` file, and `System` exercises
it only incidentally with whatever casing the bundled files happen to carry.

The class this leaves half-covered is the one `CLAUDE.md`'s case-insensitive-by-default rule exists
for: a value that can arrive in two casings — an id, an enum, a Name or Title natural key — compared
without both sides being folded, so a lookup silently matches nothing. That rule records finding it
piecemeal across a query filter, a route parameter, a file-authored explicit id, and several
natural-key lookups, each fixed on its own before it was recognised as one recurring class. That is
the reason to cover it at every origin rather than one: the failure mode is a silent no-match, which
looks the same as "no such row".

**Open, not decided: re-tiering the 25 documents with no live component.** By the rule above it is not
a candidate for its own issue — the behaviour *is* tested today, just live — but converting them means
writing new C# in another test project rather than repairing a document here. Raised for a decision
rather than settled in either direction.

### 18. Register every new document in `Quotinator.slnx` and confirm the guards green

**Status:** ✅ Done — 2026-08-23. 44 of 44 registered; all four guards green; full suite green.

One flat top-level `<Folder>` element per category path — `.slnx` does not support nested folders.
Then confirm both guard tests from step 5 are green, and the full suite shows no regression.

Step 5's second guard already had its real red-green, taken as soon as `api-surface/` gave it links to
resolve — see that step.

### 19. Run all 43 documents end to end, and repair what the run exposes

**Status:** ✅ Run done — 2026-08-25. Repairs done; two rows still open, see the verification checklist.

**28 passed, 15 failed, and not one failure was an application defect.** Every one was a defect in a
test document, apart from the two blocked on [#347](https://github.com/DutchJaFO/Quotinator/issues/347)
and the one [#327](https://github.com/DutchJaFO/Quotinator/issues/327) already owns. That is the
headline result: the restructure moved 43 documents and the application underneath them was sound.

**What the run found, by class:**

- **Six instruments that could not fail or could not pass.** Three greps in `api-surface/04` missed a
  space against the pretty-printed spec, so a *removal* assertion had been passing on a pattern that
  could never match. `16` and `17` counted `"operation":"Purge"` against an audit that records
  `"Purged"`. `import-and-staged-actions/05` counted `totalCount` on an endpoint returning
  `totalMatching`. `notifications-and-changelog/01` step 7 required `3` from a `grep -c` against
  single-line JSON. Two poll gates matched a `title` v1.8.3 never returns, so they hung instead of
  failing — one ran ten minutes before being stopped.
- **Six assertions that were vacuous as ordered**, each confirmed correct by other means and then given
  a form that can fail.
- **One misdiagnosis of mine, corrected.** `notifications-and-changelog/06` was reported as an
  application defect; it was not. Its rollback emptied the schema counter, the replay failed on an
  existing table, and the initializer restored its backup and degraded — which imitates a broken
  backfill exactly.
- **A version-scheme finding**, which became its own change: while development shares the released
  version number the running build is indistinguishable from the release it upgrades from. See
  `docs/workflow/checklist.md`'s *Version during development*.

**Three browser-driven checks were proven automatable** against a live browser — Scalar's DOM, the
degraded Blazor pages, and the full notification flow including Run → Confirm and the resulting reset.
That answers the question row 27 deferred to this run, without clearing the row: placeholders and
browser-worded steps remain elsewhere.

**Three index rules came out of it** — a count needs a working instrument, a removal needs a positive
control, and one prior version is not enough because users skip releases.

---

### 20. Write `scripts/testing/http.csx`, the one helper the conversion needs

**Status:** ✅ Done — 2026-08-26. Written and exercised against a live container on all seven paths:
body to stdout piped into `ConvertFrom-Json`, `--status`, an expected `422` whose problem body is
readable, an `--expect` mismatch exiting 1, a multipart import returning its `batchId` through the
parsed body, `--json-stdin`, and `--no-key` producing `401`.

**One thing the run found that would have been a silent corruption.** PowerShell 5.1 prefixes a **BOM**
when it pipes a string into a native process, so a JSON body arrives as `﻿{…}` and fails to parse
server-side — which would have read as an application defect. The helper strips it, and says why at the
line that does.

One helper, not two. It covers exactly what Windows PowerShell 5.1 cannot do cleanly and nothing more:
a multipart file upload, a status a test expects to be non-2xx, and a JSON body reaching a native
process intact. Everything a cmdlet already does well stays a cmdlet.

Its output contract matters as much as its inputs: the body goes to stdout so a step can pipe it
straight into `ConvertFrom-Json`, and the status goes somewhere that does not corrupt that pipe. An
`--expect <code>` that exits non-zero on a mismatch is what makes a failure stop at the step that
caused it, which the index already requires of every step.

Per ADR 010 it is a `.csx` under `scripts/testing/`, not a `.ps1` — that ADR names a multi-line
PowerShell script as having the same reviewability problem it rejects Python for.

### 21. Extend `test-env.csx` so no document hand-rolls `docker run`

**Status:** ✅ Done — 2026-08-26. `reenter` and `--read-only` added, both verified live: `reenter` left
an imported batch's 12 pending actions in place where `create` on the same name and port returned 0, and
`--read-only` reached `docker run` and produced the degraded `503` the read-only tests are about.

Nine raw `docker run -d` blocks survive, and the claim in verification row 25 that they *cannot* be
expressed is only half true:

- **Five need no script change at all.** `notifications-and-changelog/02`–`06` re-boot against a bind
  directory, and `create --bind` preserves it — the script never touches a bind path by design. These
  nine lines are pure duplication, and each one carries an `MSYS_NO_PATHCONV=1` that exists only because
  the block is bash.
- **Four need the script to grow.** `startup-and-degradation/02`, `04` and `05` re-enter a **named**
  volume, which `create` wipes on purpose, and two of them add `--read-only`.

The addition is a re-entry command that runs the identical recipe without removing the volume, and a
`--read-only` flag. Naming it as its own command rather than a flag on `create` is deliberate: a step
that re-enters an environment should say so, instead of a reader inferring it from which mount type
happens to be in use.

### 22. State the shell conventions in the index

**Status:** ✅ Done — 2026-08-26. `README.md` carries the five idioms with their reasons, both measured
5.1 traps, `reenter`, the bind-path guidance, and a table of the five scripts the suite runs on. The
id-capture section shrank as expected: the helper returns the `batchId` in a parsed response, so nothing
extracts it from text.

### 23. Convert all 43 documents

**Status:** ✅ Done — 2026-08-26, category by category. 320 ```bash fences became 0.

**The mechanical half was the smaller half.** `MSYS_NO_PATHCONV=1` was simply deleted — all 51 of them —
and every `docker cp`, `docker logs` and bind mount then worked with a plain Windows path. What took the
time was the `grep`-based assertions: each one became a property assertion on a parsed object, which is
the point of the exercise rather than a side effect of it.

**Four documents got a materially better mechanism, not just a translation:**

- `api-surface/04` reads the OpenAPI spec as an object graph instead of matching a pretty-printed
  document — the exact shape that let its removal half pass for weeks on patterns that could never match.
- `notifications-and-changelog/02`, `03` and `05` read the database with `DbInspector` from the host
  instead of a throwaway `alpine` container that installed `sqlite3` **over the network** and nested SQL
  three quoting levels deep. What made that possible is the absolute Windows bind path: the old
  POSIX-style path was the reason a host-side read was unsafe.
- `notifications-and-changelog/01` and `06` write their fixtures through `execute-sql.csx` rather than
  the same alpine container.
- `import-and-staged-actions/15` no longer asks a person to hand-edit a bundled rule file three times —
  `scripts/testing/conflict-rule.csx` does it, and `git checkout` reverts it.

### 24. Guard the conversion so it cannot drift back

**Status:** ✅ Done — 2026-08-26.
`RepositoryStructureTests.EveryAutomatedTestingCodeBlock_IsPowerShell`, red against 283 fences across
all 43 documents before the conversion and green after it.

It checks fenced code only — prose naming a past defect is load-bearing in the index and banning the
word would delete it — and exempts a fence containing `sh -c`, where the payload runs inside a Linux
container. Two of its own false positives were found by running it and fixed: a PowerShell `for (…)`
loop, and the word `isDismissed` containing `sed `.

It also guards the `@(…).Count` trap, which is not a bash construct at all but fails in exactly the
same silent way.

### 25. Run the converted suite

**Status:** ✅ Done — 2026-08-26. **All 43 documents executed against real containers**, every step of
each, in the order they are written. Two steps were not reachable by shell and two were blocked, all
four for reasons the documents themselves already state:

| Not executed | Why |
|---|---|
| `notifications-and-changelog/01` steps 5 and 8 | Browser-driver steps — DOM reads and a Run → Confirm click. Verified against a real browser during the 2026-08-25 run; a driver, not a shell, performs them |
| `startup-and-degradation/05` step 4 | The same browser-driver shape. Its steps 1–3 failed on 2026-08-26 and **pass since the 2026-08-27 rewrite** — see the scope change below |

**Nothing failed in the application.** Every failure was a defect in a test document, and each was
repaired and re-run:

- `notifications-and-changelog/02` step 6 **could not fail as written** — it inserted a legacy row while
  the real metadata-carrying announcement was still present, so structural dedupe correctly suppressed
  the re-announcement whether or not text matching was also alive. Fixed by deleting the structural row
  in the same edit; verified both ways, and the assertion is now by identity rather than by a total,
  because the totals are equal either side.
- `import-and-staged-actions/16` step 3 asserted `purgedTraces` equals the seed-batch count. That holds
  on a fresh boot — which is `17`'s step 2 — but this step runs *after* a reseed, where the total is two
  rounds: measured `4` then `8`. Now a before/after delta.
- `import-and-staged-actions/19` step 9 asserted the API's field names against a log line that writes
  human-readable plurals (`stage directions`, not `stageDirections`), reporting three types missing that
  were plainly there.
- `api-surface/03`'s import returns `200`, not the `202` the document expected — `newest-wins` resolves
  as it goes, and only `review` stages something.

**`16` and `17` still fail, and that is [#347](https://github.com/DutchJaFO/Quotinator/issues/347).**
Neither emits a `rule staleness evaluated` / `alias staleness evaluated` line (`evaluationLines=0`,
measured), so the mechanism's own execution stays unobservable. Row 28 records it.

---

## Scope change — `05`'s premise turned out to be reachable (2026-08-27)

**Added after the developer questioned whether `startup-and-degradation/05` failing meant the
assumptions behind it were wrong.** They were. Two things were wrong, and the first hid the second.

**First, the conversion changed a pinned variable.** `04` and `05` originally stopped the seeding
container with `docker stop -t 15` — a clean shutdown, after which SQLite has checkpointed and removed
the `-wal`/`-shm` sidecars. `reenter` used `docker rm -f`, a SIGKILL that leaves them behind. #326 had
measured that *sidecar state decides whether a read-only mount degrades*, so this silently changed what
the two read-only tests were testing. `reenter` now stops cleanly first, and says at the line why.

**Second, and the real finding: the premise was never unreachable.** This document had said since
2026-08-18 that #294 made the failure state impossible to provoke and that
[#327](https://github.com/DutchJaFO/Quotinator/issues/327) would have to replace the test. Measured
2026-08-27, across four combinations:

| `/data` | Migration pending | Sidecars | Result |
|---|---|---|---|
| Writable, root `--read-only` | yes | either | `200` healthy — #294's fix working |
| Read-only | no, already migrated | present | `200` healthy, `SQLite Error 8` logged |
| **Read-only** | **yes** | absent | **`503` unhealthy, `SQLite Error 14`** |
| **Read-only** | **yes** | present | **`503` unhealthy** |

The lever was wrong, not the feature. `--read-only` denies the *root filesystem* while leaving `/data`
writable — which is `04`'s subject and survivable by design. Denying **`/data` itself** with a genuinely
pending migration degrades every time, and **sidecar state does not decide it**, which narrows #326's
finding rather than contradicting it.

**It reproduces the original incident's own error code.** `SQLite Error 14: 'unable to open database
file'` is exactly what the live v1.8.2 → v1.8.3-beta upgrade reported. The old technique could not:
its own Observed effect recorded `SQLite Error 10: 'disk I/O error'` and conceded "an exact match is not
expected".

`test-env.csx` gained `--read-only-data`; `05` was rewritten around it and **now passes end to end** —
steps 1–3 verified live, step 4 being the browser-driver step. It was a failing test blocked on another
issue; it is neither now. **#327's scope should be revisited rather than assumed** — this implements the
first of its three proposed scenarios, and the corrupt-database and schema-ahead cases remain its own.

---

## Scope change — the missing OpenAPI tag description (2026-08-27)

**Added by developer direction**, on the finding the run surfaced: `Notifications` was used by two
operations but absent from the spec's top-level `tags` array, so the group rendered with no description
and no ordering while the other six had both. Consistency is the point, so it is fixed here rather than
filed.

One line in `Program.cs`'s document transformer, plus a guard —
`OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` — confirmed red against the
unfixed file and green after, and verified on a rebuilt image rather than only in the test: seven tags,
all described, nothing undeclared. **The guard derives the expected set from the operations' own tags**,
so a tag added to an endpoint and nowhere else fails on its own rather than waiting for someone to
remember a list; it also catches a tag declared with an empty description, which renders identically to
a missing one.

**Documented as [ADR 020](../../architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md)**
(developer direction: *document and guard it so we don't make the mistake again*). No existing ADR
reached the tag set — the nearest rules were `CLAUDE.md`'s *Endpoint naming convention* and its
*OpenAPI and Scalar documentation language* decision — so this is a new one rather than a revision. It
records why the two mechanisms are independent by construction, and why the guard reads the operations
rather than a maintained list: a list would reproduce the same manual step that failed, one layer down.
`CLAUDE.md` gained a matching *Endpoint groups* section beside the naming conventions it belongs with.

**One thing left as an observation, not fixed.** Nothing guards that an ADR is listed in
`docs/architecture-decisions/README.md`'s index and in `Quotinator.slnx` — both were updated by hand
here. That is the same class of two-independent-places drift this ADR is about, and worth its own issue
if the developer wants one.

**`import-and-staged-actions/15` leaves the shared image mutated by design**, and its own Cleanup says
so. The rule file was reverted with `git checkout`, `quotinator:local` rebuilt from it, and the result
confirmed by a fresh container reporting `1.9.0-alpha`, 799 quotes and 0 pending actions.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `docs/automated-testing/` exists with one kebab-case subfolder per category | Live | `ls -d docs/automated-testing/*/` lists the six categories agreed in step 1, all kebab-case |
| 2 | ✅ | One numbered document per existing section; no test dropped, merged, or rewritten | Live | 43 documents from 44 sections — old §16 was an explanatory note, not a test, and is folded into `identity-and-casing/02`. The index's mapping table resolves all 44 old numbers |
| 3 | ✅ | Every test document carries the full template, including Preconditions, Determinism and Observed effect | Live | All template headings present in all 43 documents; checked by heading, not by eye. `Expected output` is no longer among them — see row 24 |
| 4 | ✅ | An index carries the rules block, the living-checklist rule, and every test document | Live | `docs/automated-testing/README.md` lists all documents; the row 13 guard proves the list is complete |
| 5 | ✅ | Every test is marked in or out of the designated smoke set, and the index lists the set | Unit test | Every document has exactly one `Smoke:` field; row 23's guard proves the index's table and the documents agree |
| 6 | ✅ | The index states the three run scopes as the authoritative definition | Live | Index names per-issue T2, milestone close, and release; `release-verification.md`'s T2 tier points at it rather than restating it |
| 7 | ✅ | The numbering scheme is recorded; each filename carries a number and a stable slug | Live | Index states the scheme; every filename matches `NN-slug.md` with no exceptions |
| 8 | ✅ | Cross-references between tests are explicit links, not prose | Live | No document refers to another by section number or by "the section above/below" |
| 9 | ✅ | Test resources sit beside the document; test scripts sit in `scripts/testing/` | Live | `scripts/testing/` holds `execute-sql.csx` and `sqlite-storage-probe.csx`; no `.csx` anywhere under `docs/` |
| 10 | ✅ | ADR 010 revised in place; `CLAUDE.md`'s Developer Context bullet matches | Live | ADR 010's Decision section states the subfolder rule; `CLAUDE.md` states it identically |
| 11 | ✅ | The reliability rule is stated in the index and served by a Determinism field on every document | Live | Index carries the rule; row 3's field check covers the per-document half |
| 12 | ✅ | `docs/smoke-tests.md` is removed and every reference updated in the same commit | Live | The file is gone. Remaining `smoke-tests.md` mentions are historical records in closed issues' plan docs, each naming the old file *and* redirecting to `docs/automated-testing/` — no live link resolves to the deleted file. The original "grep returns no hit" wording was unachievable and is corrected here |
| 13 | ✅ | Guard tests: every document is linked from the index, and every index link resolves | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex`, `...EveryAutomatedTestingIndexLink_ResolvesToAnExistingDocument`, `...EveryAutomatedTestingCrossReference_ResolvesToAnExistingDocument` — all red before step 6 |
| 14 | ✅ | Test outcomes are recorded as Knowledgebase material, and #333 sweeps the test documents | Live | #333 requirement 6 states the sweep (done 2026-08-22); the index states the relationship |
| 15 | ✅ | A live-only issue has a Definition of done it can honestly tick | Live | `docs/workflow/issues.md` no longer requires a placeholder Expected-tests row for live-only verification |
| 16 | ✅ | Every fixed wait is a readiness poll, or a duration justified in `Determinism` | Live | One fixed `sleep` survives — `startup-and-degradation/03`, whose `Determinism` states why a poll cannot catch a transient state. Every other `sleep` is a poll interval inside an `until` loop |
| 17 | ✅ | Every document is audited against the index's rules, and each one's compliance is recorded | Live | Step 14's audit completed over all 43 in three batches; findings recorded per batch, with work needing new test content filed rather than silently left non-compliant |
| 18 | ✅ | No predicted count survives in a step's expected result | Live | All 3 fixed. `import-and-staged-actions/10` gained the `Frozen` query its expectation had always lacked; `19` now names the nine entity types instead of counting them, so a tenth reads as an absent name rather than a wrong number; `api-surface/03` asserts its fixture's own item rather than a total |
| 19 | ✅ | The index defines the environment profiles, each pinning `AutoPurgeBundledImportActions` | Live | `README.md` states Fresh, Constrained and Upgraded with runnable setup commands; Fresh pins the flag to the application default `true`, and a test needing `false` declares it as its own delta |
| 20 | ✅ | The index defines snapshot, restore, and the milestone-start base image | Live | Index states image tag + `docker save`, stopped-container DB capture with `-wal`/`-shm`, unconditional restore between tests, and deletion after the milestone publishes |
| 21 | ✅ | Every document names a known profile and establishes it rather than assuming one | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_NamesAKnownEnvironmentProfile` — red against all 43 before step 16 |
| 22 | ✅ | No document is blocked from executing by its own commands | Live | No `docker run` holds the terminal ahead of later steps; every `docker run` carries `--name`; no `<container>` placeholder survives; every `docker cp` of the database targets `/data`, the path the profile actually mounts |
| 23 | ✅ | The index's smoke set cannot drift from the documents' own `Smoke` field | Unit test | `RepositoryStructureTests.SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` — red while the table named tests by title only, which is why each row now links its document |
| 24 | ✅ | Every test creates and destroys its own container and volume, so any two can run concurrently | Unit test | `RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` — red while documents shared `qt-env` on `8080`. Checks three things: a document publishes every port it talks to, no two publish the same one, and every port is a real one. That last check exists because the first port scheme derived a second container's port by appending a digit, producing `181031` — above the 65535 maximum, so `docker run` would simply have failed, and a uniqueness check alone was perfectly happy with it. 43 documents, 51 distinct ports, no `qt-env` and no "restore the profile" anywhere |
| 25 | ✅ | The container recipe exists once, and every document invokes it rather than repeating it | Live | `scripts/testing/test-env.csx` creates and destroys a test's environment; all 43 documents call it. Verified live: named volume with a port (healthy, seeded), no port at all (publishes nothing), and a bind mount (database lands in the bound directory). Eight steps stay raw `docker` because they re-enter an environment an earlier step produced, which `create` cannot express |
| 26 | ✅ | Every step carries its own expected result, so a failure stops the run at the step that caused it | Unit test | `RepositoryStructureTests.EveryAutomatedTestingStep_CarriesItsOwnExpectedResult` — red against all 43 before the conversion. Checks per step, not by total: a document where one step carries three expectations and another none would balance out under a count |
| 27 | ✅ | Every test runs unattended | Live | **All 43 documents are PowerShell and all 43 were executed** — 2026-08-26, every step of each. `RepositoryStructureTests.EveryAutomatedTestingCodeBlock_IsPowerShell` holds the suite there. The last three human-required steps are gone: `conflict-rule.csx` makes `import-and-staged-actions/15`'s three rule-file edits, and the two remaining alpine-plus-`apk`-over-the-network database reads became `DbInspector` calls. 51 `MSYS_NO_PATHCONV=1` deleted; every `--bind` is an absolute Windows path, which is what made a host-side read safe in the first place. Four steps were not run and each says why in its own document: `notifications-and-changelog/01`'s two browser-driver steps, and `startup-and-degradation/05`'s two steps past the [#327](https://github.com/DutchJaFO/Quotinator/issues/327) stop. Two 5.1 traps were measured and are now index rules plus guard checks: a native process never receives a double quote (so no JSON reaches one — `execute-sql.csx` gained `--sql-file`), and `.Count` on a single `PSCustomObject` returns blank, failing precisely when a well-targeted assertion matches its one row. Earlier: every `<batchId>`/`<id>` placeholder is gone, two poll gates that hung rather than failed are fixed, and `api-surface/04` step 6 counts the published spec instead of opening a browser |
| 28 | ❌ | Every document can distinguish the feature working from the feature broken | Live | **Twelve more repaired after the full run**, on top of the 13 earlier. Six instruments could not fail or could not pass: `api-surface/04`'s three greps missed a space against the pretty-printed spec, so its *removal* half had been passing on a pattern that could never match; `16` and `17` counted `"operation":"Purge"` where the audit records `"Purged"`, reading 0 against 8 and 4 real traces; `import-and-staged-actions/05` counted `totalCount` on an endpoint that returns `totalMatching`; `notifications-and-changelog/01` step 7 required 3 from a `grep -c` against single-line JSON. Six assertions were vacuous as ordered: a scoped-clear check comparing 0 with 0, a schema-version check comparing 1 with 1 under a profile where multi-row history cannot exist, an already-canonical id fixture, a merge check against the one bundled rule file shipping zero rules, and two fixtures asserting outcomes they could not produce. `notifications-and-changelog/06` was misread as an application defect and was not one — its rollback emptied the counter and the replay failed, restored its backup and degraded, which imitates a broken backfill exactly. **`16` and `17` still cannot**, and remain failing tests that block release: the staleness *evaluation* is still unobservable, filed as [#347](https://github.com/DutchJaFO/Quotinator/issues/347). Their `Purged` grep is fixed, so that is now the only gap of the two |
| 29 | ✅ | The unverified changelog round-trip tooling is removed, and `changelog.csx`'s own lack of test coverage is filed rather than absorbed | Live | `scripts/changelog-import.csx`, `scripts/changelog-upgrade.csx` and `scripts/changelog-reference/` are gone, and [#340](https://github.com/DutchJaFO/Quotinator/issues/340) covers testing `changelog.csx`. `scripts/README.md` still names them — deliberately, in a removal note recording what went and why. This row originally asked for "no reference to them", which a removal record cannot satisfy and should not |
| 30 | ✅ | No reference to `docs/smoke-tests.md` resolves to nothing | Live | Every remaining mention names where the suite went. **Not** a bare `grep` for absence — archival plan docs keep the old name deliberately, with the pointer alongside. Nor a same-line grep: the pointer is often on the wrapped line below, so ten mentions look unresolved until each is read |
