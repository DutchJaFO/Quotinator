# Open Issue Audit

A periodic check to ensure every closed or shipped fix has a proper closing comment and verification record. Run this at the start of a release cycle (before Step 3 of `release.md`) and at the start of any new milestone session.

The milestone plan docs in `docs/milestones/` and the post-deploy verification checklist in memory are the audit trail. An issue without a closing comment is an incomplete closure regardless of whether the code was merged.

---

## When to run

- At the start of every release session — before checking Dependabot PRs
- At the start of every milestone session — before picking up new work
- Any time a release is being prepared and open issues look suspicious

---

## Step 1 — Fetch all open issues

```bash
gh issue list --state open --limit 100 --json number,title,labels,milestone
```

Also check the post-deploy verification checklist in memory (`project_post_deploy_verification.md`) — deployment-verified issues are intentionally left open until confirmed in the live add-on.

---

## Step 2 — Classify each open issue

For each open issue, determine which state it is in:

| State | Description | Action |
|---|---|---|
| **Legitimately open** | Work not yet started or in-progress in a milestone | No action — confirm it is tracked in a milestone or backlog |
| **Deployment-pending** | Fix is shipped but awaiting live HA add-on confirmation | Confirm it is in the post-deploy checklist in memory; if not, add it |
| **Done, not closed** | Fix is merged and confirmed but the issue was never formally closed | Close it now — see Step 3 |
| **Stale / unclear** | Status is ambiguous — unclear if fixed, deferred, or abandoned | Read the issue spec, linked PRs, and milestone plan doc; decide and document |

---

## Step 3 — Close a done-but-not-closed issue

Before closing, verify the full closure criteria from `checklist.md` (Before closing an issue):

- [ ] All code is merged to `main` and in a tagged release
- [ ] The fix is confirmed working — unit test, local Docker smoke-test, or live HA add-on as appropriate
- [ ] A plan doc exists, or a logged reason explains why one was not needed
- [ ] No requirement from the spec was silently dropped
- [ ] The verification table covers every in-scope requirement using the 5-column format

Once all criteria are met, close with the verification table:

```bash
gh issue close <N> --comment "<verification table>"
```

The verification table format (from `process.md`):

```
| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅     | Description | Unit test / Live | TestClass.MethodName or exact command + expected output |
```

**Never close an issue without a comment.** The comment is the permanent audit record. A bare `gh issue close <N>` with no comment is not acceptable.

---

## Step 4 — Update the audit trail

After resolving each issue:

- Remove deployment-pending items from memory once confirmed in the live add-on
- Update `overview.md` in the relevant milestone folder if the issue belonged to a milestone
- If a stale issue was decided to be abandoned or deferred: post a comment on the GitHub issue explaining the decision before closing or re-labelling

---

## Reference

| Document | Purpose |
|---|---|
| `docs/workflow/checklist.md` | Full before-closing-an-issue checklist |
| `docs/workflow/process.md` | Verification table format and scope-change rules |
| `docs/workflow/release.md` | Step 4 (open bug issues check) runs in parallel with this audit |
| Memory: `project_post_deploy_verification.md` | Deployment-pending issues awaiting live HA add-on confirmation |
| `docs/milestones/` | Per-milestone plan docs — the audit trail for what was decided and why |
