# Enforcement: State Safety

**Rule:** Mutable state must be protected against exceptions. Flags must use try/finally, dirty state must be cleared before throwing, and batch operations must isolate per-item errors.

## Violations

### Flag set/clear without try/finally
```csharp
// VIOLATION — if ProcessAll() throws, deferUpdates stays true forever
deferUpdates = true;
ProcessAll();
deferUpdates = false;

// REQUIRED
deferUpdates = true;
try
{
    ProcessAll();
}
finally
{
    deferUpdates = false;
}
```

### Throwing with dirty state
```csharp
// VIOLATION — caller who catches this exception inherits a corrupted buffer
if (batch.IsCorrupted())
    throw new InvalidDataException("Malformed batch data");
// batch still queued, next read processes garbage

// REQUIRED — clean the state, then throw
if (batch.IsCorrupted())
{
    batch.Clear();
    throw new InvalidDataException("Malformed batch data — buffer cleared");
}
```

### Batch operation without per-item isolation
```csharp
// VIOLATION — one bad callback kills every callback after it
foreach (var callback in pendingCallbacks)
    callback.Invoke();

// REQUIRED — isolate each item so one failure doesn't block the rest
foreach (var callback in pendingCallbacks)
{
    try
    {
        callback.Invoke();
    }
    catch (Exception e)
    {
        Debug.LogException(e);
    }
}
```

## When per-item isolation is NOT needed

- Items are dependent on each other (pipeline where item N needs item N-1's output)
- The collection is a transaction that must all-or-nothing succeed
- The loop body is trivial and cannot throw (e.g., incrementing a counter)

## Checking

After writing code that modifies mutable state, verify:
1. Every flag set/clear pair uses try/finally
2. Any `throw` inside a method that mutates instance state has cleanup before the throw
3. Any loop processing independent items has per-item try/catch (unless an exception above applies)
4. After a batch operation completes (even partially), the data structure is in a consistent state
