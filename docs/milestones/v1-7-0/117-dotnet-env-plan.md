# Issue #117 — Add .NET SDK to Claude Code remote execution environment via session-start hook

**Milestone:** v1.7.0  
**Status:** Open  

---

## Problem

The Claude Code cloud/remote execution environment (Ubuntu 24.04) does not have the .NET SDK installed. Claude cannot:

- Run `dotnet build` or `dotnet test` to verify code changes before pushing
- Run `dotnet-script scripts/changelog.csx` to regenerate `CHANGELOG.md` and `addon/CHANGELOG.md`
- Catch compiler errors, warnings, or test failures locally — they only surface in CI

Discovered during v1.7.0 milestone work (#109). Three consecutive CI failures on PR #116 were caused by compiler errors (CS0738, CS0718, CS8619) that a local `dotnet build` would have caught immediately.

---

## Impact

- Every push requires a full CI round-trip to surface build errors that are trivially detectable locally
- Changelog markdown files cannot be regenerated in cloud sessions — they fall out of sync
- The pre-push checklist (build + test + changelog regen) cannot be fully executed in a cloud session

---

## Solution

Configure a **session-start hook** for the Claude Code remote environment. The hook installs the .NET 10 SDK and `dotnet-script` once per session at container start.

### Hook script

```bash
#!/usr/bin/env bash
set -euo pipefail

# Install .NET 10 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh \
  | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

# Install dotnet-script for running .csx scripts
dotnet tool install -g dotnet-script --version "*" 2>/dev/null \
  || dotnet tool update -g dotnet-script
```

### Environment variables to persist

```
DOTNET_ROOT=$HOME/.dotnet
PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH
```

### Notes

- The CCR proxy CA bundle (`/root/.ccr/ca-bundle.crt`) must be trusted for outbound HTTPS — set `SSL_CERT_FILE` if the install script fails TLS verification
- Install takes ~30–60 seconds; acceptable as a one-time session cost
- See [Claude Code on the web docs](https://code.claude.com/docs/en/claude-code-on-the-web) for hook configuration

---

## Acceptance Criteria

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `dotnet build --configuration Release` succeeds in a cloud session | Live | Run in cloud session; expect `0 Warning(s)  0 Error(s)` |
| 2 | ⬜ | `dotnet test --configuration Release` succeeds in a cloud session | Live | Run in cloud session; expect all tests passed |
| 3 | ⬜ | `dotnet-script scripts/changelog.csx` produces updated output | Live | Run changelog regen command; expect no error, file updated |
| 4 | ⬜ | Full pre-push checklist can be executed end-to-end in a cloud session | Live | Execute each step in `CLAUDE.md` Pre-Push Checklist without needing a local terminal |
