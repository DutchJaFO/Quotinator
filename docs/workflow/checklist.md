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

- [ ] Re-read the **full** issue spec: `gh issue view <N>`
- [ ] Map every requirement bullet, table cell, and endpoint shape from the spec to the actual implementation in code
- [ ] Confirm every requirement is implemented and tested — if anything is missing, the issue stays open
- [ ] Update the plan doc status to `Complete`
- [ ] Update the status column in `overview.md`
- [ ] Close: `gh issue close <N> --comment "<brief note>"`

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
