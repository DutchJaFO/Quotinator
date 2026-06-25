#!/bin/bash
set -euo pipefail

# Only run in Claude Code remote/cloud environments
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Install .NET 10 SDK via apt if not already present
if ! command -v dotnet &>/dev/null; then
  echo "[session-start] Installing .NET 10 SDK via apt..."
  apt-get update -qq
  apt-get install -y --no-install-recommends dotnet-sdk-10.0
else
  echo "[session-start] .NET SDK already available: $(dotnet --version)"
fi

# Install dotnet-script for running .csx scripts (idempotent)
if ! dotnet tool list -g 2>/dev/null | grep -q dotnet-script; then
  echo "[session-start] Installing dotnet-script..."
  dotnet tool install -g dotnet-script
else
  echo "[session-start] dotnet-script already installed"
fi

# Ensure global tools are on PATH for this session
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo "export PATH=\"\$HOME/.dotnet/tools:\$PATH\"" >> "$CLAUDE_ENV_FILE"
fi

echo "[session-start] .NET environment ready"
dotnet --version
