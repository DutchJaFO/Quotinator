# Issue Closure Criteria

An issue may only be closed when **both** gates below are satisfied. Either gate alone is not enough — an issue with a complete verification table but no confirmed release stays open, and an issue in a tagged release but with missing verification rows stays open.

---

## Gate 1 — Verification completeness

Every in-scope requirement from the GitHub issue spec must appear as a row in the 5-column verification table.

```
| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅     | Description | Unit test / Live | TestClass.MethodName or exact command + expected output |
```

Rules:

- **Status is its own column** — never embed ✅ or ❌ inside the Verification column.
- **Method must be one of:** Unit test, Integration test, Build, Live (Docker), Live (HA add-on), or Manual (for content/documentation).
- **Verification must be exact:** test class + method name, or the literal command and the expected output. A description of what the test does is not acceptable.
- **All rows must be ✅** — a ❌ row means the issue stays open regardless of release status.
- **No requirement may be silently dropped.** If a requirement was deferred to a later issue, that deferral must be documented as a comment on the GitHub issue and reflected in the plan doc's Scope changes section. The verification table covers only the in-scope requirements — deferred ones are listed separately with a pointer to the owning issue.
- **The table lives in the closing comment** posted on the GitHub issue via `gh issue close <N> --comment "..."`. It must also exist in the plan doc. The closing comment is the permanent public audit record.

---

## Gate 2 — Release artefact confirmation

The fix must be confirmed working in the appropriate artefact. Merging the PR, watching CI pass, and pushing the tag are preconditions — they are not the confirmation itself.

| Issue type | Confirmation required |
|---|---|
| API / logic bug | Smoke-test against the local Docker image — confirm the specific failure no longer occurs |
| Docker / container behaviour | Local Docker build and smoke-test (all three health/version/random curls in `release.md`) |
| HA add-on behaviour (ingress, supervisor config, add-on panel, container restart) | Install the new release in the live HA add-on and verify the behaviour there — a local Docker run is not sufficient |
| Documentation / content only | User reads the updated content at the exact location stated in the verification table and confirms it is correct |
| Code refactor / internal change | Build clean + all tests green on `main` after the PR is merged |

**Timing rule:** do not run `gh issue close` at any of these points:

| Point in the cycle | Gate 1 | Gate 2 | Close? |
|---|---|---|---|
| Tests green on feature branch | May be ✅ | ❌ — not yet merged | No |
| PR merged to `main`, no tag yet | May be ✅ | ❌ — not in a release | No |
| Tag pushed, CI green, artefact not yet confirmed | May be ✅ | ❌ — not confirmed | No |
| Tag pushed, artefact confirmed | ✅ | ✅ | Yes — close now |

**HA add-on issues** are a special case: the confirmation can only happen after the user installs the new release. These are tracked in the post-deploy verification checklist in memory (`project_post_deploy_verification.md`) and closed after the developer confirms them in the live add-on.

---

## Evaluating a done-but-not-closed issue

When an issue was merged but never formally closed, run through the steps below before deciding whether a retroactive close is possible or the issue must be reopened.

### Step 1 — Map every requirement to a verification method

Read the GitHub issue spec in full (`gh issue view <N>`). For each DoD item, determine what kind of verification it needs:

| Verification method | When it applies |
|---|---|
| **Unit test** | Logic, service behaviour, model methods, schema structure — anything the test harness can assert |
| **Integration test** | Database queries, file I/O, startup behaviour |
| **Build** | Compilation correctness, 0 warnings |
| **Manual (browser)** | Blazor component rendering, UI interaction (language switching, layout, disclaimer visibility) |
| **Manual (script)** | Generator output, seed script behaviour |
| **Live (Docker)** | API behaviour, container startup, health endpoints |
| **Live (HA add-on)** | Ingress, supervisor config, add-on panel — cannot substitute a Docker run |

For each requirement: find the specific unit test (class + method) that covers it, or declare it needs manual verification and describe exactly what to check and what the expected outcome is.

### Step 2 — Identify gaps

A gap is any of:

- A requirement for which a unit test **should** exist but does not — for example, a structural invariant about a file or model that the test suite could assert but currently does not
- A requirement that needs manual verification where no procedure has been written describing what to check and what to expect
- A test that exists but does not actually cover the requirement (e.g. checks for null but not for completeness)

### Step 3 — Retroactive close or reopen?

| Situation | Decision |
|---|---|
| All requirements have tests; tests just need to be re-run | Retroactive close — run the tests, do the manual checks, write the table, close |
| Some requirements lack tests that should exist | **Reopen** — implement the missing tests first, then return to this evaluation |
| Some requirements need manual verification but no procedure was written | **Reopen** — write the procedure in the plan doc, run it, then close |
| A requirement was silently dropped with no deferral comment | **Reopen** — either implement it or document the deferral, then close |

### Step 4 — Always test against the latest version on `main`

Do not test against the version the issue was originally shipped in. Always verify against the current `main` branch and the latest tagged release. If behaviour has regressed since the original ship, that is a new bug — file a separate issue rather than blocking this closure.

### Step 5 — Confirm which tagged version contains the fix

```bash
git tag --contains <merge-commit-sha> --sort=version:refname | head -1
```

The output is the earliest tag that includes the fix. Record this as the release version in the verification table.

### Step 6 — Verify the issue is listed in `changelog.en.json`

Check that the correct release entry in `src/Quotinator.Api/resources/changelog.en.json` includes this issue number in its `issues` array. If it is absent, add it:

```json
"issues": [82]
```

After editing, regenerate both markdown changelogs:

```bash
dotnet-script scripts/changelog.csx -- --format keepachangelog --input src/Quotinator.Api/resources/changelog.en.json --output CHANGELOG.md
dotnet-script scripts/changelog.csx -- --format ha-addon        --input src/Quotinator.Api/resources/changelog.en.json --output addon/CHANGELOG.md
```

Run `dotnet test --filter ChangelogSchema` to confirm structure is valid after the edit.

### Step 7 — Bring NL/DE changelog files up to date

`changelog.nl.json` and `changelog.de.json` must be kept in lockstep with `changelog.en.json`. After any edit to the EN file, check:

- The same release entry exists in each language file
- The same `issues` and `cves` arrays are present (these are language-neutral — identical across all files)
- All prose arrays (`highlights`, `added`, `changed`, `fixed`, `removed`, `audienceHighlights` values) are translated in NL and DE

Commit EN, NL, and DE changes together. Regenerate the markdown changelogs after all three are updated.

---

## Closing command

```bash
gh issue close <N> --comment "<5-column verification table>"
```

Never use a bare `gh issue close <N>` with no comment — the comment is the permanent audit record. The closing comment must reproduce the full verification table, not summarise it.

---

## Quick-reference: what disqualifies closure

- Any ❌ row in the verification table
- A requirement from the spec that has no row in the table (silent drop)
- A deferral that was not documented as a comment on the issue
- Changes not yet merged to `main`
- No tagged release containing the changes
- The fix not yet confirmed in the appropriate artefact (Docker smoke-test, HA add-on, or user content review)
- No closing comment on the GitHub issue

---

## Related documents

| Document | What it covers |
|---|---|
| `docs/workflow/process.md` | Verification table format; scope change and deferral protocol |
| `docs/workflow/checklist.md` | Full step-by-step before-closing-an-issue checklist |
| `docs/workflow/release.md` | Step 11 — confirm in release artefact, then close |
| `docs/workflow/issue-audit.md` | Periodic audit for done-but-not-closed issues |
