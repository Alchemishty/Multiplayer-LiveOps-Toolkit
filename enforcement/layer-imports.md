# Enforcement: Layer Import Direction

**Rule:** Lower layers must not reference higher layers. Layers below are ordered from lowest to highest; code may depend only on the same layer or earlier layers.

```
Core → Identity/Wallet/Inventory → Shop/Equipment/Achievements/DailyRewards → Data → API → Registry → Networking → Player → UI → Example
```

SDK root: `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/`. `Runtime/` paths below are relative to that package. Remaining `Scripts/` paths are under the repository's demo `Assets/`. Runtime assembly references enforce the package boundary; no SDK assembly may reference the demo assembly `MultiplayerLiveOpsToolkit` or TMP/uGUI.

## Layer-to-Directory Mapping

| Layer | Directory | Allowed Dependencies |
|---|---|---|
| Core | `Runtime/Core/` | System libraries only; no Unity/legacy project dependencies |
| Identity | `Runtime/Identity/` | Core, System libraries and Newtonsoft.Json for strict proof JSON parsing; no Unity/legacy dependencies |
| Wallet | `Runtime/Wallet/` | Core and System libraries only; no Unity, HTTP, scene or Netcode dependencies |
| Inventory | `Runtime/Inventory/` | Core and System libraries only; no Unity, Shop, Wallet, HTTP or Netcode dependencies |
| Equipment | `Runtime/Equipment/` | Core, Inventory, Identity and Newtonsoft.Json for signed/public state parsing; no Unity/HTTP/scene/Netcode dependencies |
| DailyRewards | `Runtime/DailyRewards/` | Core, Wallet and System libraries; no Unity/HTTP/Netcode/sample dependencies |
| Achievements | `Runtime/Achievements/` | Core, Wallet, Inventory and System libraries; no Unity/HTTP/Netcode/sample dependencies |
| Shop | `Runtime/Shop/` | Core, Wallet, Inventory and System libraries; no Unity/HTTP/scene/Netcode dependencies |
| EquipmentVisuals | `Runtime/EquipmentVisuals/` | Equipment and Unity; no Django, Netcode, UI, demo or humanoid slot enum |
| Netcode.Avatars | `Runtime/Netcode.Avatars/` | Netcode, Identity, Equipment, EquipmentVisuals and Unity transport; no demo or UI |
| CharacterControls | `Runtime/CharacterControls/` | Netcode.Avatars, Unity and Netcode components; no demo/UI/backend |
| Data | `Scripts/Data/`, `Scripts/ScriptableObjects/` | Core |
| API | `Runtime/UnityIntegration/`, `Runtime/Backends/Django/` | Core, Identity, Wallet, Inventory, Shop, Equipment, Achievements, DailyRewards, Data |
| Registry | `Scripts/Registry/` | Core, Shop, Inventory, Equipment, Achievements, DailyRewards, Data, API |
| Networking | `Runtime/Netcode/` | Core, Identity, Equipment, Data, API and Netcode; no player/UI/prefab dependencies |
| Player | `Scripts/Player/` | Core, Identity, Equipment, Data, API, Registry, Networking |
| UI | `Scripts/UI/`, `Scripts/Example/UI/` | Core, Shop, Inventory, Equipment, Achievements, DailyRewards, Data, API, Registry, Networking, Player |
| Example | `Scripts/Example/` | All layers |

## Checking

Legacy code uses global C# types. Core, Identity, Wallet and Inventory use their matching `MultiplayerLiveOpsToolkit.*` namespaces in assemblies with no engine references. Identity depends on Core; legacy composition/adapters may reference both. Check **directory paths, type references and assembly references**, not just `using` directives.

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
