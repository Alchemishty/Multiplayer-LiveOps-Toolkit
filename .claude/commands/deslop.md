# Deslop

Code cleanup: remove unnecessary comments, improve naming, simplify logic, remove dead code.

## Scope Boundaries

- No feature changes
- No architecture moves (don't relocate files between layers)
- No API signature changes (don't rename public methods other classes depend on)
- No test assertion changes
- Err on the side of leaving code untouched if ambiguous

## Workflow

### Step 1 — Read Target Files

Read all specified files fully. If no files specified, read all recently modified files.

### Step 2 — Classify Comments

For each comment, classify:

**Remove:**
- What-comments that restate the code: `// get the player` above `GetComponent<Player>()`
- Section dividers that add no information: `// ---- Methods ----`
- Commented-out code (if it's needed, it's in version history)
- TODO/FIXME without actionable context

**Keep:**
- Why-comments explaining non-obvious constraints: `// OnValueChanged doesn't fire for initial value`
- Workaround explanations: `// Unity 2022.3 bug: NetworkVariable callback fires twice on host`
- Legal/license headers
- `[Tooltip("...")]` and `[Header("...")]` attributes (these are inspector documentation, not code comments)

### Step 3 — Identify Improvements

**Naming improvements** (enforce `enforcement/naming.md`):
- Expand abbreviations: `pa` → `playerAccessory`
- Add boolean prefixes: `equipped` → `isEquipped`
- Describe collections: `items` → `accessoryItems`
- Describe methods: `Refresh()` → `RefreshWalletDisplay()`

**Simplification:**
- Replace nested if/else with early returns
- Remove dead code (unreachable branches, unused private methods)
- Collapse single-use variables that obscure rather than clarify
- Replace verbose null checks with `??` throw pattern

**Unity-specific:**
- Do not remove `[SerializeField]`, `[Header]`, `[Tooltip]`, `[FormerlySerializedAs]` attributes
- Do not remove `#region` blocks without considering inspector organization intent
- Do not rename serialized fields without adding `[FormerlySerializedAs]` (breaks saved data)

### Step 4 — Apply Changes

Apply changes file by file, minimal diffs. Group related changes together.

### Step 5 — Verify

1. Run `refresh_unity`
2. Check `read_console` for zero errors
3. If errors: fix and re-verify

### Step 6 — Report

Summarize changes per file:
- Comments removed (count)
- Names improved (list old → new)
- Code simplified (brief description)
- Lines removed (count)
