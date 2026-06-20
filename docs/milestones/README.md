# milestones/

One subfolder per development milestone. Each subfolder contains the planning documents for that milestone's GitHub issues.

## Structure

```
milestones/
  {milestone-name}/     ← named after the Git feature branch
    overview.md         ← milestone summary, goals, issue list
    {NN}-{slug}-plan.md ← one plan doc per GitHub issue
```

## Conventions

- The folder name matches the feature branch name (e.g. `data-import-sources` → branch `feature/data-import-sources`).
- `overview.md` lists all issues in scope, their status, and any cross-issue dependencies.
- Individual plan docs follow the naming pattern `{issue-number}-{short-title}-plan.md`.
- Plan docs are living documents — update them as scope or approach changes during the milestone.
- When a milestone is complete and merged, its folder is kept for reference. Do not delete.
