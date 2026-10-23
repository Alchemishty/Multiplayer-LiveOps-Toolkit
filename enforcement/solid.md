# Enforcement: SOLID Principles

**Rule:** Classes must have a single responsibility. Dependencies must point downward.

## Violations

### Multiple responsibilities in one MonoBehaviour
```csharp
// VIOLATION — this class does UI rendering, API calls, AND business logic
public class ShopController : MonoBehaviour
{
    // UI: updates display text, manages buttons
    // API: calls GameAPI.FetchShopItems, GameAPI.PurchaseItem
    // Business logic: checks affordability, tracks ownership
    // That's 3 responsibilities.

// REQUIRED — split into focused classes
public class ShopPanel : MonoBehaviour       // UI only: displays items, forwards user intent
public class PurchaseValidator               // Pure C#: affordability checks, ownership rules
public class ShopService                     // API communication: fetch items, submit purchases
```

**When to split:** If you can describe what a class does and use the word "and", it has multiple responsibilities. "Displays shop items" is one. "Displays shop items and validates purchases and calls the API" is three.

### MonoBehaviour containing business logic
```csharp
// VIOLATION — business rule lives inside a MonoBehaviour
public class PlayerAccessory : NetworkBehaviour
{
    public bool CanEquip(string accessoryId)
    {
        // checks slot compatibility, ownership, currency...
        // This logic can't be tested without spawning a NetworkObject
    }
}

// REQUIRED — extract to pure C# class
public static class AccessoryEquipValidator
{
    public static bool CanEquip(string accessoryId, AccessoryType slotType,
        HashSet<string> ownedIds)
    {
        // Same logic, now testable in EditMode without NetworkObject
    }
}
```

### Upward dependency
```csharp
// VIOLATION — Data layer referencing Player layer
// File: Scripts/Data/AccessoryItemData.cs
using HarnessTestBed.Player; // Data depends on Player = upward

// REQUIRED — Player depends on Data, not the other way around
```

### Fat interface
```csharp
// VIOLATION — one interface forces implementers to handle everything
public interface IShopItem
{
    void Display();
    void Purchase();
    void Equip();
    void Preview();
    void Serialize();
}

// REQUIRED — split by consumer
public interface IDisplayable { void Display(); }
public interface IPurchasable { void Purchase(); }
public interface IEquippable { void Equip(); }
```

## When NOT to split

- A class has one responsibility but it's complex (a large state machine is fine as one class)
- Splitting would create classes with 1-2 methods that are always used together
- The class is a data container (DTOs, ScriptableObjects with no logic)

## Pre-Code Architecture Declaration

Before writing code for any implementation step, state the intended class structure:

```
This step creates/modifies:
- ShopPanel (UI only: displays items, forwards user intent)
- PurchaseValidator (pure C#: affordability checks, ownership rules)
- ShopService (API communication: fetch items, submit purchases)
```

**If any class description uses "and", split before coding.** This catches god classes at design time, not during review.

This declaration is not optional. Skipping it and going straight to code is a violation.

## Checking

For each new or modified class:
1. Was a pre-code architecture declaration written?
2. Can you describe its job without "and"?
3. Does it mix UI + logic, or API + logic, or networking + business rules?
4. Could the business logic be tested in EditMode if extracted?
5. Do its references only point to same-layer or lower-layer code?
