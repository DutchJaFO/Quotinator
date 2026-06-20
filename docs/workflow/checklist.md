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
- [ ] For any new issue without a plan doc: confirm the no-plan-doc decision is logged in the GitHub issue and in `overview.md`
- [ ] Review plan docs for issues being worked on today

---

## Before closing an issue

- [ ] Verify all blocking/related issues in the dependency map are fully closed first
- [ ] Re-read the **full** issue spec: `gh issue view <N>`
- [ ] **Plan doc check** — either a plan doc exists, OR the GitHub issue and `overview.md` both contain an explicit note explaining why one was not needed (e.g. "pure content fix, no implementation decisions required"). A missing plan doc with no logged reason is never acceptable.
- [ ] If any requirement from the spec was deferred to a later issue: confirm a comment exists on the GitHub issue documenting what was deferred, why, and which issue owns it — a silent drop is never acceptable
- [ ] Confirm the plan doc spec and the GitHub issue spec are in agreement — either the scope is unchanged, or the plan doc has a **Scope changes** section and the issue has a matching comment
- [ ] Confirm the verification table covers every in-scope requirement. Each row must name either: the exact unit test (class + method), an exact live command and expected output, or — for documentation/content issues — the exact document/UI location the user must confirm. Status must be its own column between # and Requirement, never embedded in the Verification column.
- [ ] For bug fixes: confirm a failing test or reproducible steps existed before the fix was written — the bug must have been demonstrably red before turning green
- [ ] All unit tests named in the table pass (green)
- [ ] All live commands have been run and produced the expected output (green)
- [ ] **User manual test** — user starts the app in Visual Studio and confirms it starts without error. For documentation/content issues: user reads or views every item listed in the verification table and confirms each one explicitly.
- [ ] No requirement is still unconfirmed — if anything is unverified, the issue stays open
- [ ] **PR merged to `main`** — do not run `gh issue close` while still on the feature branch; the issue stays open until the merge lands
- [ ] Confirm all changes are merged to `main` and included in a tagged release
- [ ] Update the plan doc status to `Complete` (or note "no plan doc — by decision" if none exists)
- [ ] Update the status column in `overview.md`
- [ ] Re-verify the order of operations table — update if this issue's completion changes the correct sequence
- [ ] **User confirms closure** — show the user the closing comment and verification table and wait for explicit approval before running `gh issue close`
- [ ] Close: `gh issue close <N> --comment "<verification table>"`

---

## Before any merge to main

- [ ] Build clean: `dotnet build --configuration Release` — 0 warnings, 0 errors
- [ ] Tests pass: `dotnet test --configuration Release` — all tests pass, 0 warnings
- [ ] No in-progress issue leaves broken code in the request path (half-wired services, failing endpoints, broken migrations)
- [ ] After merge: close only the fully verified issues; leave in-progress issues open

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
