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
# The build section above already built Evenmere.Tools in Release, so --no-build is safe here. `lint` exits 1 on any content
# error, which fails the script (set -e). The minimal fixture pack is always linted; the real content only once content/eras
# exists (M1-02), so an empty content/ does not fail verify before then.
dotnet run --project src/Evenmere.Tools -c Release --no-build -- lint --content tests/fixtures/content/minimal
if [ -d content/eras ]; then
    dotnet run --project src/Evenmere.Tools -c Release --no-build -- lint --content content
fi

# --- section: godot ---
scripts/godot-smoke.sh

echo "verify: OK"
