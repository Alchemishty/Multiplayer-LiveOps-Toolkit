#!/usr/bin/env bash
set -uo pipefail

# Two-agent review loop for unity-gamedev-bench.
# Replaces "claude -p --dangerously-skip-permissions" as the --agent command.
#
# Usage:
#   ./scripts/run-all.sh --agent "/path/to/review-loop.sh" ...

PROMPT="$1"
MODEL="${REVIEW_LOOP_MODEL:-opus}"

run_claude() {
    claude -p --dangerously-skip-permissions --model "$MODEL" -- "$@" 2>&1
}

# ---- Pass 1: Worker implements the task ----
run_claude "$PROMPT" 2>&1

# ---- Capture diff for review ----
git add -A
DIFF=$(git diff --cached -- . ':!Library/' ':!Temp/' ':!Logs/' ':!obj/')

if [[ -z "$DIFF" ]]; then
    echo "[review-loop] Worker produced no changes. Exiting."
    exit 0
fi

# ---- Collect enforcement rules ----
RULES=""
if [[ -d enforcement ]]; then
    for f in enforcement/*.md; do
        RULES+="
--- $(basename "$f") ---
$(cat "$f")
"
    done
fi

# ---- Pass 2: Reviewer checks the diff ----
REVIEW_PROMPT="You are a code reviewer for a Unity/C# project. Review the following diff against the enforcement rules below.

List every violation as a numbered item: file, line, what's wrong, what the rule requires.
If the diff also fails to test the actual bug scenario (tests extracted code instead of the reported behavior), flag that.
If there are no violations, respond with exactly: CLEAN

## Enforcement Rules
${RULES}

## Diff to Review
\`\`\`diff
${DIFF}
\`\`\`"

REVIEW=$(run_claude "$REVIEW_PROMPT")

# ---- Check if clean ----
if echo "$REVIEW" | grep -q "^CLEAN$"; then
    echo "[review-loop] Reviewer found no violations. Done."
    exit 0
fi

echo "[review-loop] Reviewer found violations. Running fix pass..."

# ---- Pass 3: Worker fixes the violations ----
FIX_PROMPT="A code reviewer found these violations in your implementation. Fix each one in the existing code. Do not rewrite from scratch — make targeted fixes.

## Review Findings
${REVIEW}

## Original Task (for context)
${PROMPT}"

run_claude "$FIX_PROMPT"

echo "[review-loop] Fix pass complete."
