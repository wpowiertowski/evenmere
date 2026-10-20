#!/usr/bin/env bash
# Godot headless smoke test (M0-09). It builds the game assembly, imports the Godot project, then runs the simulation
# inside Godot through SimHost for a fixed number of ticks and expects one line: SMOKE OK hash=<16 lowercase hex>.
# That hash is the sim's state hash, so it must equal a plain .NET run with the same content, seed and tick count.
#
# Skips with exit 0 when Godot 4.7 .NET is not installed, unless REQUIRE_GODOT=1 (then it fails).
#
# Environment:
#   GODOT                 path to (or name of) the Godot .NET binary; otherwise `godot` on PATH, then the macOS app
#   REQUIRE_GODOT=1       fail instead of skipping when Godot is missing
#   EVENMERE_CONTENT      content root; otherwise <repo>/content if it has eras/, else the minimal fixture pack
#   GODOT_SMOKE_TICKS     ticks to run (default 1000)
#   GODOT_SMOKE_SEED      world seed (default 42)
#   GODOT_SMOKE_TIMEOUT   seconds before a Godot run is killed (default 300); dotnet build gets twice as long
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Shells started before the SDK was installed (and non-login shells) miss this directory.
if ! command -v dotnet >/dev/null 2>&1 && [ -x /usr/local/share/dotnet/dotnet ]; then
    export PATH="/usr/local/share/dotnet:$PATH"
fi

SMOKE_TICKS="${GODOT_SMOKE_TICKS:-1000}"
SMOKE_SEED="${GODOT_SMOKE_SEED:-42}"
RUN_TIMEOUT="${GODOT_SMOKE_TIMEOUT:-300}"
BUILD_TIMEOUT=$((RUN_TIMEOUT * 2))

say() { echo "godot smoke: $*"; }
fail() { echo "godot smoke: FAILED: $*" >&2; exit 1; }

# GNU `timeout` does not exist on macOS. A Perl alarm survives exec, so the process is killed (SIGALRM, status 142) on a hang.
guarded() {
    local seconds="$1"
    shift
    perl -e 'alarm shift; exec @ARGV' "$seconds" "$@"
}

# Prints the first usable Godot .NET binary: $GODOT, then `godot` on PATH, then the macOS app bundle.
find_godot() {
    local candidate resolved version
    for candidate in "${GODOT:-}" godot /Applications/Godot_mono.app/Contents/MacOS/Godot; do
        [ -n "$candidate" ] || continue
        resolved="$(command -v "$candidate" 2>/dev/null || true)"
        [ -n "$resolved" ] || continue
        # The standard (non-.NET) editor cannot load the C# game assembly; the .NET edition reports "4.7.2.stable.mono...".
        version="$("$resolved" --version 2>/dev/null || true)"
        case "$version" in
            *mono*) ;;
            *)
                echo "godot smoke: ignoring $resolved (not the Godot .NET edition)" >&2
                continue
                ;;
        esac
        printf '%s\n' "$resolved"
        return 0
    done
    return 1
}

if ! GODOT_BIN="$(find_godot)"; then
    if [ "${REQUIRE_GODOT:-0}" = "1" ]; then
        fail "Godot .NET not found and REQUIRE_GODOT=1"
    fi
    say "skipped (Godot not found)"
    exit 0
fi

# Until content/eras exists (M1-02), everything runs against the minimal fixture pack. This is the rule the game's
# ContentPaths uses; exporting it keeps the smoke run independent of where Godot thinks the project is.
if [ -z "${EVENMERE_CONTENT:-}" ]; then
    if [ -d "$REPO_ROOT/content/eras" ]; then
        EVENMERE_CONTENT="$REPO_ROOT/content"
    else
        EVENMERE_CONTENT="$REPO_ROOT/tests/fixtures/content/minimal"
    fi
fi
export EVENMERE_CONTENT

say "using $GODOT_BIN"
say "content root $EVENMERE_CONTENT"

import_log="$(mktemp)"
smoke_log="$(mktemp)"
trap 'rm -f "$import_log" "$smoke_log"' EXIT

say "building game/Evenmere.Game.csproj (Debug)"
status=0
guarded "$BUILD_TIMEOUT" dotnet build game/Evenmere.Game.csproj -c Debug --nologo -v quiet || status=$?
[ "$status" -eq 0 ] || fail "dotnet build exited with status $status"

# The import pass registers scripts and resources in game/.godot (untracked) before the first real run.
say "importing the project"
status=0
guarded "$RUN_TIMEOUT" "$GODOT_BIN" --headless --path "$REPO_ROOT/game" --import >"$import_log" 2>&1 || status=$?
if [ "$status" -ne 0 ]; then
    cat "$import_log" >&2
    fail "godot --import exited with status $status$([ "$status" -eq 142 ] && echo ' (timed out)')"
fi

say "running $SMOKE_TICKS ticks, seed $SMOKE_SEED"
status=0
guarded "$RUN_TIMEOUT" "$GODOT_BIN" --headless --path "$REPO_ROOT/game" -- "--smoke-ticks=$SMOKE_TICKS" "--seed=$SMOKE_SEED" >"$smoke_log" 2>&1 || status=$?
if [ "$status" -ne 0 ]; then
    cat "$smoke_log" >&2
    fail "godot exited with status $status$([ "$status" -eq 142 ] && echo ' (timed out)')"
fi

smoke_line="$(grep -E '^SMOKE OK hash=[0-9a-f]{16}$' "$smoke_log" | tail -n 1 || true)"
if [ -z "$smoke_line" ]; then
    cat "$smoke_log" >&2
    fail "no 'SMOKE OK hash=<16 hex>' line in the Godot output"
fi

echo "$smoke_line"
