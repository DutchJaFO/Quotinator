# Milestone Checklist Template

Use this as a starting checklist when kicking off a milestone. The process detail is in `process.md`.

---

## Milestone start

- [ ] Fetch all issues: `gh issue list --milestone "<Name>" --state all --limit 50 --json number,title,state`
- [ ] Read each issue spec: `gh issue view <N>` for **every** issue — do not skip any
- [ ] Map dependencies between issues
- [ ] Decide on an order of operations
- [ ] Create `docs/milestones/{slug}/overview.md` with the full issue list, dependency graph, and ordered plan
- [ ] Create per-issue plan docs for all issues (defer only if the issue is far in the dependency chain)
- [ ] Commit the milestone folder to `main`
- [ ] Create the feature branch: `git checkout -b feature/{slug}`

---

## Session start

- [ ] Check for new issues: `gh issue list --milestone "<Name>" --state open --json number,title`
- [ ] Update `overview.md` and create plan docs for any issues added since last session
- [ ] Review plan docs for issues being worked on today

---

## Before closing an issue

- [ ] Verify all blocking/related issues in the dependency map are fully closed first
- [ ] Re-read the **full** issue spec: `gh issue view <N>`
- [ ] If any requirement from the spec was deferred to a later issue: confirm a comment exists on the GitHub issue documenting what was deferred, why, and which issue owns it — a silent drop is never acceptable
- [ ] Confirm the plan doc spec and the GitHub issue spec are in agreement — either the scope is unchanged, or the plan doc has a **Scope changes** section and the issue has a matching comment
- [ ] Confirm the plan doc has a verification checklist entry for every in-scope requirement, each naming either the exact unit test (class + method) or the exact live command and expected output — Status must be its own column between # and Requirement, never embedded in the Verification column
- [ ] For bug fixes: confirm a failing test or reproducible steps existed before the fix was written — the bug must have been demonstrably red before turning green
- [ ] All unit tests named in the checklist pass (green)
- [ ] All live verification commands have been run and produced the expected output (green)
- [ ] No requirement is still red — if anything is unverified, the issue stays open
- [ ] Confirm all changes are merged to `main` and included in a tagged release
- [ ] Update the plan doc status to `Complete`
- [ ] Update the status column in `overview.md`
- [ ] Re-verify the order of operations table — update if this issue's completion changes the correct sequence
- [ ] **User manual test** — user builds and runs the app in Visual Studio and confirms it starts and works; any findings are filed as new issues before proceeding
- [ ] **User confirms closure** — show the user the closing comment and verification table and wait for explicit approval before running `gh issue close`
- [ ] Close: `gh issue close <N> --comment "<verification table>"`

---

## Milestone close

- [ ] All issues verified: `gh issue list --milestone "<Name>" --state open` returns empty
- [ ] Build clean: `dotnet build --configuration Release` — 0 warnings, 0 errors
- [ ] Tests pass: `dotnet test --configuration Release` — all tests pass, 0 warnings
- [ ] Docker build succeeds: `docker build -f docker/Dockerfile -t quotinator:local .`
- [ ] Changelogs updated (`CHANGELOG.md` and `addon/CHANGELOG.md`)
- [ ] Version bumped (`src/Quotinator.Api/Quotinator.Api.csproj`, `addon/config.yaml`, both changelogs)
- [ ] Pushed to `main` and tagged: `git tag vX.Y.Z && git push origin vX.Y.Z`
- [ ] Milestone closed on GitHub: `gh api repos/DutchJaFO/Quotinator/milestones/<N> -X PATCH -f state=closed`
