# Enforcement: Layer Import Direction

**Rule:** Lower layers must not reference higher layers. Layers below are ordered from lowest to highest; code may depend only on the same layer or earlier layers.

Package assembly references enforce SDK boundaries; demo layers are checked by directory and type references. There is no single linear ordering across independent integrations: use the dependency table below.

SDK root: `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/`. Runtime paths below are relative to that package. Demo Scripts paths are under `Unity/Assets/`. No SDK assembly may reference the demo assembly `MultiplayerLiveOpsToolkit` or TMP/uGUI.

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
| UnityIntegration | `Runtime/UnityIntegration/` | Core, Identity and Unity; no Django/demo dependency |
| Backends.Django | `Runtime/Backends/Django/` | Core, mechanic assemblies and UnityIntegration; no demo dependency |
| Registry | `Scripts/Registry/` | Core, Shop, Inventory, Equipment, Achievements, DailyRewards, Data, UnityIntegration, Backends.Django |
| Networking | `Runtime/Netcode/` | Identity, Equipment, Unity Netcode and Collections; no demo/UI/prefab dependencies |
| Player | `Scripts/Player/` | Core, Identity, Equipment, Data, UnityIntegration, Backends.Django, Registry, Networking |
| UI | `Scripts/UI/`, `Scripts/Example/UI/` | Core, Shop, Inventory, Equipment, Achievements, DailyRewards, Data, UnityIntegration, Backends.Django, Registry, Networking, Player |
| Example | `Scripts/Example/` | All layers |

## Checking

Check directory paths, type references and .asmdef references, not just using directives. Demo types include global C# types; SDK assemblies use MultiplayerLiveOpsToolkit namespaces. For every changed type, identify its owning assembly and ensure dependencies follow the table. Lifecycle module dependencies are additional initialization constraints, not permission to import a higher layer.

Examples of forbidden dependencies:

- A demo ShopCatalog referencing DjangoShopBackend: data should receive snapshots/DTOs, not perform HTTP work.
- A Django adapter referencing ShopPanel: backend code must complete through contracts/callbacks, leaving UI to subscribe.
- NetcodeIdentityGate referencing PlayerNameplate or a demo prefab: the gate verifies peers independently of rendering/spawning.
- A package runtime type referencing AccessoryRegistry: the registry belongs to the demo.

Pass required state through existing contracts, expose scoped events for observers, and keep presentation in its consuming integration. Never move demo dependencies into the SDK to work around a forbidden reference.
