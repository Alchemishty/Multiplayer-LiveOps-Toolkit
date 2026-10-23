# Enforcement: Layer Import Direction

**Rule:** Lower layers must not reference higher layers. Dependencies flow left-to-right only.

```
Data → API → Registry → Player → UI → Example
```

## Layer-to-Directory Mapping

| Layer | Directory | Allowed Dependencies |
|---|---|---|
| Data | `Scripts/Data/`, `Scripts/ScriptableObjects/` | None (leaf layer) |
| API | `Scripts/API/` | Data |
| Registry | `Scripts/Registry/` | Data, API |
| Player | `Scripts/Player/` | Data, API, Registry |
| UI | `Scripts/UI/`, `Scripts/Example/UI/` | Data, API, Registry, Player |
| Example | `Scripts/Example/` | All layers |

## Checking

This project does not use C# namespaces — layer enforcement is based on **directory paths and type references**, not `using` directives.

For each `.cs` file:
1. Determine its layer from its directory path
2. Scan for references to types defined in other layers (class names, method calls, field types)
3. Verify all referenced types belong to the same layer or a lower layer
4. Flag any reference to a type in a higher layer

**How to identify cross-layer references without namespaces:**
- A file in `Scripts/Data/` that references `ShopAPI` (defined in `Scripts/API/`) → Data referencing API → violation
- A file in `Scripts/API/` that references `AccessoryManager` (defined in `Scripts/UI/`) → API referencing UI → violation
- A file in `Scripts/Player/` that references `AccessoryRegistry` (defined in `Scripts/Registry/`) → Player referencing Registry → allowed

## Violations

```csharp
// VIOLATION — Data layer file referencing an API type
// File: Scripts/Data/ShopCatalog.cs
private ShopAPI shopApi; // ShopAPI lives in Scripts/API/ — upward reference

// VIOLATION — API layer file referencing a UI type
// File: Scripts/API/ShopAPI.cs
private ShopPanel panel; // ShopPanel lives in Scripts/UI/ — upward reference

// VIOLATION — Registry referencing Player
// File: Scripts/Registry/AccessoryRegistry.cs
private PlayerAccessory player; // PlayerAccessory lives in Scripts/Player/ — upward reference
```

## How to Fix Common Violations

**Data needs API response types:** Move the shared DTOs into Data layer. API layer uses Data DTOs, not the other way around.

**API needs to notify UI:** Use C# events or callbacks. API fires an event, UI subscribes. No direct reference needed.

**Registry needs Player state:** Pass the state as a parameter from the calling layer. Registry provides data; the caller (Player or UI) provides context.

**Player needs UI feedback:** Fire a C# event. UI subscribes in its own layer.
