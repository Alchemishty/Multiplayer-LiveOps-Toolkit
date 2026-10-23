# Garden

Entropy scan and maintenance. Identifies codebase health issues and fixes what's safe to fix automatically.

## Skip Patterns

Always skip:
- `Library/`, `Temp/`, `Logs/`, `obj/`
- `TextMesh Pro/` (third-party, don't touch)
- `.meta` files (Unity-managed)
- `.unity` scene files (binary, don't scan for code patterns)
- `.asset` files (serialized Unity assets)

Only scan: `.cs` files in `Assets/Scripts/` and `Assets/Tests/`

## Checks

### 1. Docs Freshness

Scan all `.md` files in `docs/` and `enforcement/`:
- Verify file path references still exist on disk
- Verify class/type names mentioned still exist in the codebase
- Flag stale references

**Auto-fix:** Remove references to deleted files. Flag renamed types for manual review.

### 2. Test Coverage Gaps

For each `.cs` file in `Assets/Scripts/` (excluding `Example/`):
- Check if a corresponding test file exists in `Assets/Tests/EditMode/` or `Assets/Tests/PlayMode/`
- Flag files without any test coverage

**Report only.** Do not auto-generate tests — that's `/create-tests`.

### 3. Unused Code

Find `.cs` files that are never referenced by any other `.cs` file.

**Unity-aware exceptions — do NOT flag:**
- MonoBehaviour subclasses (referenced by scenes/prefabs via GUID, not `using` statements)
- ScriptableObject subclasses (referenced by `.asset` files)
- Classes with `[CreateAssetMenu]` attribute
- Editor scripts in `Editor/` folders
- Test files in `Tests/` folders
- Classes referenced in `[AddComponentMenu]`, `[RequireComponent]`, or assembly definitions

**Only flag:** Pure C# classes, static helpers, and DTOs that no other code references via `using` or direct type reference.

**Report only.** Do not auto-delete — user must confirm.

### 4. Memory Freshness

Scan all `.md` files in `memory/`:
- Verify file paths mentioned still exist
- Verify patterns reference types/methods that still exist
- Flag entries that reference deleted or renamed code

**Auto-fix:** Mark stale entries with `[SUPERSEDED - YYYY-MM-DD: referenced code no longer exists]`. Do not delete — the historical context may still be valuable.

### 5. Scratch Cleanup

Delete all files in `scratch/` except `session-state.md` (if a feature implementation is in progress).

**Auto-fix:** Delete without confirmation.

## Auto-Fix Policy

| Check | Auto-fix? | What it does |
|---|---|---|
| Docs freshness | Yes | Remove dead file references |
| Test coverage gaps | No | Report only |
| Unused code | No | Report only |
| Memory freshness | Yes | Mark stale entries as SUPERSEDED |
| Scratch cleanup | Yes | Delete temp files |

## Verification

After auto-fixes:
1. Run `refresh_unity`
2. Check `read_console` for zero errors

## Report

Summarize:
- Docs: stale references found and fixed
- Tests: files without coverage (list)
- Unused: potentially dead code (list with caveat about Unity references)
- Memory: stale entries marked
- Scratch: files cleaned
