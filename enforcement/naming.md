# Enforcement: Naming Standards

**Rule:** Variable and member names must immediately tell the reader what they represent. No abbreviations.

## Violations

### Abbreviated names
```csharp
// VIOLATION
var pa = GetComponent<PlayerAccessory>();
var accType = AccessoryType.Head;
var eqMap = new Dictionary<string, AccessoryData>();
var btn = transform.Find("BuyButton");
var mgr = AccessoryManager.Instance;
var cb = OnPurchaseComplete;
var nv = accessoryState;

// REQUIRED
var playerAccessory = GetComponent<PlayerAccessory>();
var accessoryType = AccessoryType.Head;
var equippedAccessoriesBySlot = new Dictionary<string, AccessoryData>();
var buyButton = transform.Find("BuyButton");
var accessoryManager = AccessoryManager.Instance;
var onPurchaseComplete = OnPurchaseComplete;
var accessoryNetworkState = accessoryState;
```

### Allowed abbreviations

Only these universally understood abbreviations are permitted:
- `id` (identifier)
- `url` (uniform resource locator)
- `api` (application programming interface)
- `ui` (user interface)
- `rpc` (remote procedure call)
- `dto` (data transfer object)
- `jwt` (JSON web token)
- `i`, `j`, `k` (loop counters in `for` loops only)

### Boolean naming
Booleans must read as questions:
```csharp
// VIOLATION
bool equipped;
bool afford;
bool active;
bool done;

// REQUIRED
bool isEquipped;
bool canAffordItem;
bool isActive;
bool hasCompletedLoading;
```

### Collection naming
Collections must indicate what they contain and optionally their structure:
```csharp
// VIOLATION
var items = new List<AccessoryData>();
var lookup = new Dictionary<string, AccessoryData>();
var set = new HashSet<string>();

// REQUIRED
var accessoryItems = new List<AccessoryData>();
var accessoryDataById = new Dictionary<string, AccessoryData>();
var ownedAccessoryIds = new HashSet<string>();
```

### Method naming
Methods must describe their action:
```csharp
// VIOLATION
void Refresh();
void DoSync();
void Handle();
void Process();

// REQUIRED
void RefreshWalletDisplay();
void SyncEquippedAccessoriesToNetwork();
void HandlePurchaseResponse(PurchaseResult result);
void ProcessIncomingAccessoryState(AccessoryState state);
```

### Minimum length
- Local variables: 3+ characters (except `i`/`j`/`k` loop counters)
- Fields and properties: 5+ characters
- Methods: 5+ characters
- Classes: descriptive, no minimum but must be unambiguous

## Checking

For each new or modified identifier, verify:
1. Name uses full words (not abbreviations beyond the allowed list)
2. Booleans use `is`/`has`/`can`/`should`/`was` prefix
3. Collections describe contents
4. Methods describe action
5. No single-letter names outside `for` loop counters
