#!/usr/bin/env bash
# The definition-of-done gate (AGENTS.md): restore, build with warnings as errors,
# all tests, content lint, and the Godot smoke test. Sections are marked so later
# tasks can wire their own section without touching the others.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Shells started before the SDK was installed (and non-login shells) miss this directory.
if ! command -v dotnet >/dev/null 2>&1 && [ -x /usr/local/share/dotnet/dotnet ]; then
    export PATH="/usr/local/share/dotnet:$PATH"
fi

# --- section: build ---
dotnet restore
dotnet build -c Release --no-restore

# --- section: test ---
dotnet test -c Release --no-build

# --- section: lint ---
echo "lint: not yet wired"

# --- section: godot ---
echo "godot smoke: not yet wired"

echo "verify: OK"
