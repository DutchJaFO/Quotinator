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
