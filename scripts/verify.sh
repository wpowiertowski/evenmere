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
# When EVENMERE_TEST_RESULTS names a directory, each test project also writes its TRX
# report (named per assembly, so all five land side by side) and the reports are moved into
# that directory, even when the test run fails, so CI can upload them with `if: always()`.
# CI sets this to $RUNNER_TEMP/test-results. Local runs without the variable behave exactly
# as before.
if [ -n "${EVENMERE_TEST_RESULTS:-}" ]; then
    mkdir -p "$EVENMERE_TEST_RESULTS"
    move_trx() {
        for trx in TestResults/*.trx; do
            [ -f "$trx" ] && mv "$trx" "$EVENMERE_TEST_RESULTS/"
        done
        rmdir TestResults 2>/dev/null || true
    }
    trap move_trx EXIT
    dotnet test -c Release --no-build -- --report-trx
    trap - EXIT
    move_trx
else
    dotnet test -c Release --no-build
fi

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
