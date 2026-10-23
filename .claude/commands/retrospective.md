# Retrospective

Post-implementation learning capture. Run after completing a feature or after a significant debugging session.

## When to Run

- After `/implement-feature` completes
- After a complex debugging session
- After receiving and addressing code review feedback
- Periodically when significant domain knowledge has accumulated

## Workflow

### Step 1 — Reflect

Answer these guided questions about the just-completed work:

**Patterns:**
- What coding patterns worked well? (data structures, architecture decisions, testing approaches)
- Were there patterns from `memory/patterns.md` that proved useful?
- Did you discover a new pattern that should be documented?

**Errors and Fixes:**
- What errors did you encounter? (compilation, runtime, test failures)
- Were any of these errors recurring from previous sessions?
- What was the root cause and fix for each?

**Unity-Specific Issues:**
- Were there serialization problems? (JSON, NetworkVariable, ScriptableObject)
- MonoBehaviour lifecycle ordering issues? (Awake vs Start vs OnNetworkSpawn)
- Netcode sync problems? (NetworkVariable timing, RPC delivery, ownership)
- Performance issues caught? (allocations in Update, Find in hot paths)

**Documentation Gaps:**
- Was any information missing from `docs/` that would have helped?
- Are there conventions that should be added or clarified?
- Are there enforcement rules that should be added?

**Process:**
- Did the plan accurately predict the implementation?
- Were there steps that were harder than expected?
- Were there steps that should have been broken down further?

### Step 2 — Write to Memory

Append new learnings to the appropriate memory files:

| File | What goes here |
|---|---|
| `memory/patterns.md` | Coding patterns confirmed to work well in this project |
| `memory/fixes.md` | Error → root cause → fix recipes for recurring problems |
| `memory/preferences.md` | User workflow preferences and decisions |
| `memory/domain.md` | Domain knowledge accumulated (business rules, API quirks, Netcode behaviors) |
| `memory/review-lessons.md` | Standards learned from code review feedback |

**Format for each entry:**
```markdown
## [Date] — Brief Title

**Context:** What was being built/fixed
**Learning:** The specific insight
**Apply when:** When this knowledge is relevant in future sessions
```

**Temporal memory:** When updating an existing entry that's no longer accurate, don't delete the old entry. Mark it:
```markdown
[SUPERSEDED - 2026-10-07: replaced by new approach below]
```
This preserves WHY something changed, preventing re-learning forgotten lessons.

### Step 3 — Propose Doc Improvements

If reflection revealed documentation gaps:
- Draft specific edits to `docs/conventions.md`, `docs/architecture.md`, `docs/domain.md`, or `docs/testing.md`
- Present each proposed edit to the user for approval
- **Never auto-edit docs** — always get explicit user approval

If reflection revealed missing enforcement rules:
- Draft the rule with violation examples
- Present to user for approval before adding to `enforcement/`

### Step 4 — Report

Print a concise summary:
- Patterns captured (count and brief list)
- Fixes recorded (count)
- Domain knowledge added (brief list)
- Doc improvements proposed (list, pending user approval)
- Memory files updated (list)

## Memory File Initialization

If memory files don't exist yet, create them with this header:

```markdown
# [Category] — Cross-Session Memory

Entries are added by `/retrospective` after completing features and debugging sessions.
Entries marked [SUPERSEDED] are kept for historical context.

---
```
