# Achievements System Design

## Problem

The toolkit lacks an achievements system. Players have no goals beyond purchasing and equipping accessories. An event-driven achievement system would increase engagement, give players long-term progression, and demonstrate a reusable live-ops pattern for the toolkit.

## Requirements

- **Fully data-driven:** New achievements added via ScriptableObject + backend without code changes
- **Three condition types:** ThresholdCounter, CollectionCompletion, OneTimeTrigger
- **Event-driven:** Game systems fire events into the achievement service; no polling
- **Persistent:** Progress loaded from backend on login, unlocks synced immediately
- **Toolkit-grade authority abstraction:** `IAchievementAuthority` interface separates evaluation from trust model. Ships with `TrustClientAuthority` (client decides, backend records). Consuming projects can swap in server-validated authority.
- **Toast notification** on unlock + **Achievements tab** at index 4 in existing TabManager
- **API contract** defined here; no backend exists yet

## Data Layer

### Enums

```csharp
// AchievementConditionType.cs
public enum AchievementConditionType
{
    ThresholdCounter,       // "Purchase 5 items" — increment counter, unlock at target
    CollectionCompletion,   // "Own all headwear" — check ownership of a category
    OneTimeTrigger          // "Make your first purchase" — unlock on first matching event
}

// GameEventType.cs
public enum GameEventType
{
    Purchase,       // Single accessory purchased
    SetPurchase,    // Accessory set purchased
    Login,          // Player authenticated
    CurrencySpent   // Currency spent on any purchase
}
```

### GameEventContext

```csharp
// GameEventContext.cs — pure C# struct
public struct GameEventContext
{
    public int itemId;
    public int setId;
    public string currencyName;     // "gold", "gems", etc.
    public int amount;              // currency amount spent
}
```

### AchievementDefinition

```csharp
// AchievementDefinition.cs — pure C# class, [System.Serializable] for SO storage
public class AchievementDefinition
{
    public string achievementId;
    public string displayName;
    public string description;
    public AchievementConditionType conditionType;
    public GameEventType triggerEvent;
    public int targetCount;             // ThresholdCounter: unlock at this count
    public AccessoryType targetSlot;    // CollectionCompletion: which slot category
    public string targetCurrency;       // CurrencySpent: which currency (null = any)
    public string iconName;             // resolved by UI layer
}
```

Validation rules:
- `achievementId` must be non-null and non-empty — throw `ArgumentException`
- `targetCount` must be > 0 for `ThresholdCounter` — throw `ArgumentException`
- `targetSlot` must not be `None` for `CollectionCompletion` — throw `ArgumentException`

### AchievementProgress

```csharp
// AchievementProgress.cs — pure C# class
public class AchievementProgress
{
    public string achievementId;
    public int currentCount;
    public bool isUnlocked;
    public long unlockedAtTimestamp;
}
```

### AchievementProgressTracker

The core evaluation engine. Pure C#, no Unity dependencies.

```csharp
// AchievementProgressTracker.cs
public class AchievementProgressTracker
{
    // State
    Dictionary<string, AchievementDefinition> definitionsById;
    Dictionary<string, AchievementProgress> progressById;
    Func<AccessoryType, bool> collectionCompleteChecker;  // injected by service

    // Setup
    void SetDefinitions(List<AchievementDefinition> definitions);
    void LoadProgress(List<AchievementProgress> progressList);
    void SetCollectionCompleteChecker(Func<AccessoryType, bool> checker);

    // Core
    List<string> ProcessEvent(GameEventType eventType, GameEventContext context);
    AchievementProgress GetProgress(string achievementId);
    List<AchievementProgress> GetAllProgress();
    List<AchievementDefinition> GetAllDefinitions();
}
```

`ProcessEvent` logic per condition type:

**ThresholdCounter:**
1. Filter definitions where `triggerEvent == eventType` and `conditionType == ThresholdCounter`
2. For `CurrencySpent`: skip if `targetCurrency != null` and `targetCurrency != context.currencyName`
3. Increment `currentCount` by `context.amount` for CurrencySpent, by 1 for others
4. If `currentCount >= targetCount` and not already unlocked → mark unlocked, add to return list

**CollectionCompletion:**
1. Filter definitions where `triggerEvent == eventType` and `conditionType == CollectionCompletion`
2. Call `collectionCompleteChecker(definition.targetSlot)`
3. If returns true and not already unlocked → mark unlocked, add to return list

**OneTimeTrigger:**
1. Filter definitions where `triggerEvent == eventType` and `conditionType == OneTimeTrigger`
2. If not already unlocked → mark unlocked, add to return list

### IAchievementAuthority

```csharp
// IAchievementAuthority.cs — Data layer interface
public interface IAchievementAuthority
{
    void RequestUnlock(string achievementId, int currentCount,
        System.Action onConfirmed, System.Action<string> onRejected);
    void ReportProgress(string achievementId, int currentCount);
}
```

### AchievementApiDTOs

```csharp
// AchievementApiDTOs.cs
public class AchievementDefinitionResponse
{
    public List<AchievementDefinitionDTO> achievements;
}

public class AchievementDefinitionDTO
{
    [JsonProperty("id")] public string achievementId;
    public string name;
    public string description;
    [JsonProperty("condition_type")] public string conditionType;
    [JsonProperty("trigger_event")] public string triggerEvent;
    [JsonProperty("target_count")] public int targetCount;
    [JsonProperty("target_slot")] public string targetSlot;
    [JsonProperty("target_currency")] public string targetCurrency;
}

public class AchievementProgressResponse
{
    public List<AchievementProgressDTO> progress;
}

public class AchievementProgressDTO
{
    [JsonProperty("achievement_id")] public string achievementId;
    [JsonProperty("current_count")] public int currentCount;
    [JsonProperty("is_unlocked")] public bool isUnlocked;
    [JsonProperty("unlocked_at")] public long unlockedAt;
}

public class AchievementUnlockRequest
{
    [JsonProperty("achievement_id")] public string achievementId;
    [JsonProperty("current_count")] public int currentCount;
}

public class AchievementProgressUpdateRequest
{
    [JsonProperty("achievement_id")] public string achievementId;
    [JsonProperty("current_count")] public int currentCount;
}
```

## API Layer

### AchievementAPI

```csharp
// AchievementAPI.cs — singleton MonoBehaviour, same pattern as ShopAPI
public class AchievementAPI : MonoBehaviour
{
    public static AchievementAPI Instance { get; }

    IEnumerator GetDefinitions(Action<AchievementDefinitionResponse> onSuccess, Action<string> onError);
    IEnumerator GetProgress(Action<AchievementProgressResponse> onSuccess, Action<string> onError);
    IEnumerator ReportUnlock(string achievementId, int currentCount, Action onSuccess, Action<string> onError);
    IEnumerator ReportProgressUpdate(string achievementId, int currentCount, Action onSuccess, Action<string> onError);
}
```

Endpoints:

| Endpoint | Method | Body | Response |
|---|---|---|---|
| `/api/achievements/definitions/` | GET | — | `AchievementDefinitionResponse` |
| `/api/achievements/progress/` | GET | — | `AchievementProgressResponse` |
| `/api/achievements/unlock/` | POST | `AchievementUnlockRequest` | 200 / 400 |
| `/api/achievements/progress/update/` | POST | `AchievementProgressUpdateRequest` | 200 / 400 |

All use `SendWithTokenRefresh`.

### TrustClientAuthority

```csharp
// TrustClientAuthority.cs — MonoBehaviour, API layer
public class TrustClientAuthority : MonoBehaviour, IAchievementAuthority
{
    void RequestUnlock(string achievementId, int currentCount, Action onConfirmed, Action<string> onRejected)
        → StartCoroutine(AchievementAPI.Instance.ReportUnlock(...))
        → on 200: onConfirmed()
        → on error: onRejected(message)

    void ReportProgress(string achievementId, int currentCount)
        → StartCoroutine(AchievementAPI.Instance.ReportProgressUpdate(...))
}
```

## ScriptableObjects Layer

### AchievementCatalogSO

```csharp
// AchievementCatalogSO.cs — same pattern as AccessorySO
[CreateAssetMenu(fileName = "AchievementCatalog", menuName = "LiveOps/Achievement Catalog")]
public class AchievementCatalogSO : ScriptableObject
{
    [SerializeField] AchievementDefinition[] achievements;

    // Lazy-cached dictionary
    Dictionary<string, AchievementDefinition> definitionsById;

    public AchievementDefinition GetDefinitionById(string id);
    public AchievementDefinition[] GetAllDefinitions();
}
```

## Registry Layer

### AchievementService

```csharp
// AchievementService.cs — singleton MonoBehaviour (DontDestroyOnLoad)
public class AchievementService : MonoBehaviour
{
    public static AchievementService Instance { get; }

    [SerializeField] AchievementCatalogSO masterCatalog;

    AchievementProgressTracker tracker;
    IAchievementAuthority authority;
    bool isLoaded;
    float progressSyncTimer;
    HashSet<string> dirtyProgressIds;   // progress changed since last sync

    const float PROGRESS_SYNC_INTERVAL = 30f;

    // Lifecycle
    void Awake()        → validate masterCatalog != null, set Instance
    IEnumerator Start() → wait for NetworkManager.IsClient → LoadAchievements()

    IEnumerator LoadAchievements()
        → fetch AchievementAPI.GetDefinitions()
        → merge with masterCatalog (backend controls availability, SO has display data)
        → fetch AchievementAPI.GetProgress()
        → populate tracker
        → wire collectionCompleteChecker from AccessoryRegistry
        → fire AchievementsLoaded

    // Event intake
    void ReportEvent(GameEventType type, GameEventContext context)
        → if not loaded, return
        → tracker.ProcessEvent(type, context) → newlyUnlocked
        → for each: authority.RequestUnlock(..., onConfirmed: fire AchievementUnlocked)
        → mark dirty progress for batch sync

    // Progress sync
    void Update() → debounce timer, batch-sync dirty progress every 30s
    void OnApplicationPause(bool paused) → if paused, flush dirty progress

    // Events
    event Action<string> AchievementUnlocked;   // achievementId
    event Action AchievementsLoaded;

    // Queries
    AchievementProgress GetProgress(string id);
    List<AchievementDefinition> GetAllDefinitions();
    bool IsLoaded();
}
```

**Merge strategy:** Backend definitions list controls which achievements are active. For each backend definition, look up the SO entry by `achievementId` for display data (name, description, icon). If the SO has no entry for a backend achievement, skip it (client needs update). If the SO has an entry not in backend, hide it (not yet released).

**Collection completion checker:** `AchievementService` injects:
```csharp
tracker.SetCollectionCompleteChecker(slot =>
{
    var items = AccessoryRegistry.Instance.GetShopItemsForType(slot);
    return items.Count > 0 && items.TrueForAll(item => item.isOwned);
});
```

## Example Layer — UI

### AchievementToast

```csharp
// AchievementToast.cs — singleton MonoBehaviour
public class AchievementToast : MonoBehaviour
{
    public static AchievementToast Instance { get; }

    Queue<AchievementDefinition> pendingToasts;
    bool isShowing;

    void OnEnable()  → subscribe AchievementService.AchievementUnlocked
    void OnDisable() → unsubscribe

    void OnAchievementUnlocked(string achievementId)
        → look up definition from AchievementService
        → enqueue
        → if not showing, show next

    void ShowNext()
        → build overlay UI (icon placeholder + name + "Achievement Unlocked!")
        → auto-destroy after 3 seconds
        → on destroy: if queue not empty, show next
```

### AchievementsPanel

```csharp
// AchievementsPanel.cs — tab index 4, same lifecycle pattern as InventoryPanel
public class AchievementsPanel : MonoBehaviour
{
    [SerializeField] Transform contentContainer;
    [SerializeField] TMP_Text statusText;

    void OnEnable()  → if not populated, WaitAndPopulate; else refresh
    void OnDisable() → unsubscribe

    IEnumerator WaitAndPopulate()
        → wait for AchievementService.IsLoaded()
        → PopulateAchievements()

    void PopulateAchievements()
        → "Unlocked" section: completed achievements, sorted by unlock time
        → "In Progress" section: locked achievements with "3/5" progress text
        → empty state: "No achievements yet"

    // Subscribes to AchievementUnlocked for live refresh
    // Row layout: icon placeholder | name + description | progress/checkmark
    // Uses ShopUIFactory.CreateSectionHeader, CreateEmptyStateMessage
    // Private methods for achievement-specific rows (progress bar, no buy button)
```

## Integration Points

Existing files modified with a single `ReportEvent` call each:

**ShopPanel.cs** — in `PurchaseItem` success callback, after `MarkAccessoryOwned`:
```csharp
if (AchievementService.Instance != null)
{
    int goldCost = 0;
    registry.TryGetGoldCost(itemId, out goldCost);
    AchievementService.Instance.ReportEvent(GameEventType.Purchase,
        new GameEventContext { itemId = itemId });
    if (goldCost > 0)
        AchievementService.Instance.ReportEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = goldCost });
}
```

**SetsPanel.cs** — in `PurchaseSet` success callback, after `MarkSetOwned`:
```csharp
if (AchievementService.Instance != null)
{
    AchievementService.Instance.ReportEvent(GameEventType.SetPurchase,
        new GameEventContext { setId = setId });
    // CurrencySpent for sets — look up set gold cost
}
```

**DevAuthBootstrap.cs** — in auth success callback:
```csharp
if (AchievementService.Instance != null)
    AchievementService.Instance.ReportEvent(GameEventType.Login, default);
```

## Testing

### EditMode Tests

| File | Tests |
|---|---|
| `AchievementProgressTrackerTests.cs` | ~20 tests covering: |
| | ThresholdCounter: increment by 1, reach threshold, unlock, no double-unlock |
| | ThresholdCounter + CurrencySpent: increment by amount, currency filtering |
| | ThresholdCounter + CurrencySpent: null targetCurrency counts all currencies |
| | CollectionCompletion: checker returns false → no unlock |
| | CollectionCompletion: checker returns true → unlock |
| | OneTimeTrigger: first event unlocks |
| | OneTimeTrigger: already unlocked → no duplicate |
| | ProcessEvent with non-matching event type → empty list |
| | ProcessEvent returns multiple unlocks from one event |
| | LoadProgress restores state, re-processing doesn't double-unlock |
| | GetProgress for unknown id returns null |
| | SetDefinitions with invalid definition throws |
| `AchievementDefinitionTests.cs` | Validation: null id throws, empty id throws, targetCount <= 0 throws for ThresholdCounter, targetSlot == None throws for CollectionCompletion |

### Not EditMode-testable

Verified via Unity compilation + manual testing:
- `AchievementCatalogSO`, `AchievementAPI`, `TrustClientAuthority`, `AchievementService`
- `AchievementToast`, `AchievementsPanel`

## Files

| Layer | File | Action |
|---|---|---|
| Data | `Assets/Scripts/Data/AchievementConditionType.cs` | New |
| Data | `Assets/Scripts/Data/GameEventType.cs` | New |
| Data | `Assets/Scripts/Data/GameEventContext.cs` | New |
| Data | `Assets/Scripts/Data/AchievementDefinition.cs` | New |
| Data | `Assets/Scripts/Data/AchievementProgress.cs` | New |
| Data | `Assets/Scripts/Data/AchievementProgressTracker.cs` | New |
| Data | `Assets/Scripts/Data/AchievementApiDTOs.cs` | New |
| Data | `Assets/Scripts/Data/IAchievementAuthority.cs` | New |
| ScriptableObjects | `Assets/Scripts/ScriptableObjects/AchievementCatalogSO.cs` | New |
| API | `Assets/Scripts/API/AchievementAPI.cs` | New |
| API | `Assets/Scripts/API/TrustClientAuthority.cs` | New |
| Registry | `Assets/Scripts/Registry/AchievementService.cs` | New |
| Example | `Assets/Scripts/Example/AchievementToast.cs` | New |
| Example/UI | `Assets/Scripts/Example/UI/AchievementsPanel.cs` | New |
| Example/UI | `Assets/Scripts/Example/UI/ShopPanel.cs` | Modify |
| Example/UI | `Assets/Scripts/Example/UI/SetsPanel.cs` | Modify |
| Example | `Assets/Scripts/Example/DevAuthBootstrap.cs` | Modify |
| Tests | `Assets/Tests/EditMode/AchievementProgressTrackerTests.cs` | New |
| Tests | `Assets/Tests/EditMode/AchievementDefinitionTests.cs` | New |

## Behavioral Guarantees

- Achievement system is optional — all `ReportEvent` calls use null-conditional (`?.`), existing behavior unchanged if `AchievementService` is absent from scene
- No existing tests break
- No visible UI changes to existing panels
- Toast appears over all UI, does not block interaction
- Progress sync is debounced (30s batches), unlocks sync immediately
