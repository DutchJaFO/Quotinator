# scripts/hooks/

Git hooks for this repository. They are not installed automatically — run the commands below once after cloning.

## Install

```bash
cp scripts/hooks/commit-msg .git/hooks/commit-msg
chmod +x .git/hooks/commit-msg
```

## Hooks

| Hook | Purpose |
|---|---|
| `commit-msg` | Blocks commit messages containing GitHub issue-closing keywords (`fixes #N`, `closes #N`, `resolves #N`). Warns on `(#N)` at end of title. See `docs/workflow/process.md § GitHub auto-close behavior`. |
