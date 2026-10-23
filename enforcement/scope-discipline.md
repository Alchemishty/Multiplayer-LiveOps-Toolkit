# Enforcement: Scope Discipline

**Rule:** Bug-fix tasks must limit changes to the minimum needed to fix the root cause. Do not extract new classes, create new files, rename files, or restructure architecture during a bug fix.

## Why

Over-scoped bug fixes introduce unnecessary risk, make the fix harder to review, and blur the line between "fixing what's broken" and "improving what's inelegant." Architecture improvements belong in dedicated refactor tasks where they get their own tests and review.

## Violations

### Extracting a new class during a bug fix
```csharp
// VIOLATION — the bug was "items don't sync on late join"
// but the fix extracted a new EquipmentManager class and moved logic into it
// This is a refactor, not a bug fix

// REQUIRED — fix the subscription timing in the existing class
// If EquipmentManager is a good idea, propose it as a separate refactor task
```

### Renaming or moving files during a bug fix
```csharp
// VIOLATION — renamed PlayerInventory.cs to InventoryController.cs
// as part of a null-reference fix

// REQUIRED — fix the null reference in PlayerInventory.cs as-is
```

### Adding abstractions during a bug fix
```csharp
// VIOLATION — introduced an IItemProvider interface
// to "properly" fix a direct-reference bug

// REQUIRED — fix the reference. If the interface is warranted, propose it separately.
```

## When restructuring IS acceptable

- The fix genuinely requires a new type (e.g., fixing a serialization bug requires a DTO that didn't exist)
- Fixing adjacent enforcement violations (AGENTS.md rule 7) — but these go in separate commits from the bug fix

## Scope by task type

| Task Type | Scope Constraint |
|---|---|
| Bug fix | Minimum change to fix root cause. No new abstractions. |
| Feature | Full implementation scope per plan. New classes allowed. |
| Refactor | Restructuring is the point. Preservation tests required. |

## Checking

After completing a bug fix, verify:
1. No new `.cs` files were created (unless the fix genuinely required a new type — document why)
2. No files were renamed or moved
3. No new interfaces or abstract classes were introduced
4. The diff is focused on the root cause, not on "improving" surrounding code
5. Any adjacent enforcement fixes are in separate commits
