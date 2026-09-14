#!/bin/sh
# =============================================================================
# check-layering.sh — the architectural invariants that no compiler can catch.
#
# Vardholm's layering rules are load-bearing but invisible: nothing in C# stops
# a UI screen from calling SessionService, and Unity will happily compile a Core
# file that imports UnityEngine the moment someone flips noEngineReferences.
# These five checks are the enforcement. They are cheap, they run on a clean
# checkout with no Unity installed, and they are the gate on every pull request.
#
# Usage:   sh ci/check-layering.sh [project-root]
# Exit:    0 = clean, 1 = at least one violation (each printed as file:line)
#          2 = the script could not run (missing project root, bad arguments)
#
# POSIX sh + grep/sed/awk/find only. No bash-isms, no jq, no python.
# See ci/README.md for what each check enforces and which ADR it comes from.
# =============================================================================

set -u

# --- locate the project root -------------------------------------------------
# Default: the parent of the directory holding this script, so the file works
# whether CI invokes it as `sh ci/check-layering.sh` from the root or as
# `sh /abs/path/ci/check-layering.sh` from anywhere.
if [ "$#" -gt 1 ]; then
    echo "usage: sh check-layering.sh [project-root]" >&2
    exit 2
fi

if [ "$#" -eq 1 ]; then
    ROOT=$1
else
    SCRIPT_DIR=$(dirname -- "$0")
    ROOT=$(cd -- "$SCRIPT_DIR/.." && pwd) || exit 2
fi

if [ ! -d "$ROOT/Assets" ]; then
    echo "check-layering: '$ROOT' does not look like the project root (no Assets/ folder)." >&2
    exit 2
fi

CORE_DIR="$ROOT/Assets/Scripts/Core"
UI_DIR="$ROOT/Assets/Scripts/UI"
CORE_ASMDEF="$CORE_DIR/ForgottenIsle.Core.asmdef"

VIOLATIONS=0
TMP=${TMPDIR:-/tmp}/check-layering.$$
trap 'rm -f "$TMP" "$TMP.a" "$TMP.b"' EXIT INT TERM

fail() {
    VIOLATIONS=$((VIOLATIONS + 1))
    echo "$1"
}

# -----------------------------------------------------------------------------
# strip_comments <file>
#
# Emits the file with every // line comment and /* */ block comment blanked out,
# LINE NUMBERING PRESERVED (a removed line becomes empty, it is not dropped).
#
# WHY this exists: a raw grep for "UnityEngine" under Core reports four hits in
# this very repository, and all four are doc comments that say Core may NOT
# reference UnityEngine. Failing the build on prose that states the rule would
# train everyone to stop writing the prose. What matters is the code, so the
# comments come out first. A string literal is deliberately NOT stripped —
# Type.GetType("UnityEngine.Vector3") is exactly the kind of back door this is
# here to catch.
# -----------------------------------------------------------------------------
strip_comments() {
    awk '
        BEGIN { inblock = 0 }
        {
            line = $0
            out = ""
            i = 1
            n = length(line)
            while (i <= n) {
                two = substr(line, i, 2)
                if (inblock) {
                    if (two == "*/") { inblock = 0; i += 2 } else { i += 1 }
                    continue
                }
                if (two == "/*") { inblock = 1; i += 2; continue }
                if (two == "//") { break }
                out = out substr(line, i, 1)
                i += 1
            }
            print out
        }
    ' "$1"
}

# -----------------------------------------------------------------------------
# CHECK 1 — Core is engine-free. (ADR-0002)
# -----------------------------------------------------------------------------
echo "[1/5] Core must not name UnityEngine or UnityEditor ..."
if [ -d "$CORE_DIR" ]; then
    find "$CORE_DIR" -type f -name '*.cs' -print > "$TMP" 2>/dev/null
    while IFS= read -r file; do
        [ -n "$file" ] || continue
        strip_comments "$file" \
            | grep -n 'UnityEngine\|UnityEditor' \
            | while IFS= read -r hit; do
                  lineno=${hit%%:*}
                  text=${hit#*:}
                  # Trim leading whitespace so the report lines up.
                  text=$(printf '%s' "$text" | sed 's/^[[:space:]]*//')
                  echo "VIOLATION $file:$lineno: Core references the engine -> $text"
              done
    done < "$TMP" > "$TMP.a"
    if [ -s "$TMP.a" ]; then
        cat "$TMP.a"
        # One violation per offending line, counted properly.
        COUNT=$(wc -l < "$TMP.a" | tr -d ' ')
        VIOLATIONS=$((VIOLATIONS + COUNT))
    fi
else
    fail "VIOLATION $CORE_DIR:0: Core source folder is missing."
fi

# -----------------------------------------------------------------------------
# CHECK 2 — Core's asmdef declares noEngineReferences. (ADR-0001, ADR-0002)
#
# Check 1 catches a file that uses the engine. This catches the setting that
# would let one compile. Both are needed: the flag is what makes the violation
# a red compiler error in the Editor rather than something only CI notices.
# -----------------------------------------------------------------------------
echo "[2/5] ForgottenIsle.Core.asmdef must set noEngineReferences: true ..."
if [ ! -f "$CORE_ASMDEF" ]; then
    fail "VIOLATION $CORE_ASMDEF:0: Core assembly definition is missing."
else
    # tr the file to one token per line so the grep does not depend on the
    # author's whitespace or key order.
    if tr -d ' \t\r\n' < "$CORE_ASMDEF" | grep -q '"noEngineReferences":true'; then
        :
    else
        LINE=$(grep -n 'noEngineReferences' "$CORE_ASMDEF" | head -n 1 | cut -d: -f1)
        [ -n "$LINE" ] || LINE=1
        fail "VIOLATION $CORE_ASMDEF:$LINE: \"noEngineReferences\" is not true. Core must compile without UnityEngine."
    fi
fi

# -----------------------------------------------------------------------------
# CHECK 3 — Core references nothing. (ADR-0001)
#
# Core sits at the bottom of the graph. The moment its references array gains an
# entry, something below it exists, and the "Core is portable and testable with
# no Unity" property is gone.
# -----------------------------------------------------------------------------
echo "[3/5] ForgottenIsle.Core.asmdef \"references\" must be empty ..."
if [ -f "$CORE_ASMDEF" ]; then
    # Collapse to one line, cut out the references array, look for any quoted entry.
    REFS=$(tr -d ' \t\r\n' < "$CORE_ASMDEF" \
        | sed -n 's/.*"references":\[\([^]]*\)\].*/\1/p')
    if [ -n "$REFS" ]; then
        LINE=$(grep -n '"references"' "$CORE_ASMDEF" | head -n 1 | cut -d: -f1)
        [ -n "$LINE" ] || LINE=1
        fail "VIOLATION $CORE_ASMDEF:$LINE: Core declares references [$REFS]. Core must depend on nothing."
    fi
fi

# -----------------------------------------------------------------------------
# CHECK 4 — UI never touches SessionService. (ADR-0012, ADR-0014)
#
# SessionService is the only mutator of GameState. A screen that reaches it
# directly bypasses CommandDispatcher validation, so illegal transitions stop
# being rejected and start being performed. UI goes through a controller, which
# dispatches a command.
# -----------------------------------------------------------------------------
echo "[4/5] UI must not reference SessionService directly ..."
if [ -d "$UI_DIR" ]; then
    find "$UI_DIR" -type f -name '*.cs' -print > "$TMP" 2>/dev/null
    while IFS= read -r file; do
        [ -n "$file" ] || continue
        strip_comments "$file" \
            | grep -n 'SessionService' \
            | while IFS= read -r hit; do
                  lineno=${hit%%:*}
                  text=${hit#*:}
                  text=$(printf '%s' "$text" | sed 's/^[[:space:]]*//')
                  echo "VIOLATION $file:$lineno: UI reaches SessionService directly; go through a controller + command -> $text"
              done
    done < "$TMP" > "$TMP.b"
    if [ -s "$TMP.b" ]; then
        cat "$TMP.b"
        COUNT=$(wc -l < "$TMP.b" | tr -d ' ')
        VIOLATIONS=$((VIOLATIONS + COUNT))
    fi
else
    echo "      (skipped: $UI_DIR does not exist yet)"
fi

# -----------------------------------------------------------------------------
# CHECK 5 — no user-facing string literals in UI. (ADR-0014 + the localization rule)
#
# Heuristic, and knowingly so: it flags Label(" and Button(" followed by a
# string literal. Real text always arrives as ILocalizedText.Get(LocKey), which
# puts an identifier — not a quote — after the paren, so correct code does not
# trip it. A line that genuinely needs a literal (a USS class fed to a helper
# that happens to be named Label) can opt out with a trailing
#     // ci:allow-literal
# marker, which is read from the RAW line before comments are stripped.
# -----------------------------------------------------------------------------
echo "[5/5] UI screens must not contain user-facing string literals ..."
if [ -d "$UI_DIR" ]; then
    find "$UI_DIR" -type f -name '*.cs' -print > "$TMP" 2>/dev/null
    FOUND=0
    while IFS= read -r file; do
        [ -n "$file" ] || continue
        strip_comments "$file" | grep -n '\(Label\|Button\)[[:space:]]*([[:space:]]*"' \
            | while IFS= read -r hit; do
                  lineno=${hit%%:*}
                  raw=$(sed -n "${lineno}p" "$file")
                  case "$raw" in
                      *ci:allow-literal*) continue ;;
                  esac
                  text=$(printf '%s' "$raw" | sed 's/^[[:space:]]*//')
                  echo "VIOLATION $file:$lineno: user-facing literal in UI; use LocKey + ILocalizedText -> $text"
              done
    done < "$TMP" > "$TMP.a"
    if [ -s "$TMP.a" ]; then
        cat "$TMP.a"
        COUNT=$(wc -l < "$TMP.a" | tr -d ' ')
        VIOLATIONS=$((VIOLATIONS + COUNT))
        FOUND=1
    fi
    [ "$FOUND" -eq 0 ] || true
else
    echo "      (skipped: $UI_DIR does not exist yet)"
fi

# -----------------------------------------------------------------------------
echo ""
if [ "$VIOLATIONS" -gt 0 ]; then
    echo "check-layering: FAILED with $VIOLATIONS violation(s)."
    echo "check-layering: see ci/README.md for what each check protects and why."
    exit 1
fi

echo "check-layering: OK — all five layering invariants hold."
exit 0
