# Enforcement: Null Safety

**Rule:** Never silently consume null when the value is required for the mechanism to work.

## Violations

Each of these patterns is a violation when the value's absence means the system is broken:

### Silent return on null
```csharp
// VIOLATION
var registry = AccessoryRegistry.Instance;
if (registry == null) return;

// REQUIRED
var registry = AccessoryRegistry.Instance
    ?? throw new InvalidOperationException(
        "AccessoryRegistry singleton not initialized. Check bootstrap ordering.");
```

### Null-conditional on required value
```csharp
// VIOLATION — hides the bug, downstream code silently gets null
var catalog = AccessoryRegistry.Instance?.GetCatalog();

// REQUIRED — fail at the point of absence, not downstream
var registry = AccessoryRegistry.Instance
    ?? throw new InvalidOperationException("AccessoryRegistry not initialized");
var catalog = registry.GetCatalog();
```

### Unvalidated GetComponent
```csharp
// VIOLATION — silently null if component is missing
var networkObject = GetComponent<NetworkObject>();

// REQUIRED when the component must exist
var networkObject = GetComponent<NetworkObject>()
    ?? throw new InvalidOperationException(
        $"{gameObject.name} requires a NetworkObject component");
```

### Unvalidated deserialization
```csharp
// VIOLATION — proceeds with null if JSON is malformed
var data = JsonConvert.DeserializeObject<AccessoryData>(json);

// REQUIRED
var data = JsonConvert.DeserializeObject<AccessoryData>(json)
    ?? throw new InvalidOperationException(
        $"Failed to deserialize AccessoryData from JSON: {json[..Math.Min(json.Length, 100)]}");
```

## When null IS a valid state

Use guard patterns (early return, TryGet) only when absence is expected and handled:

```csharp
// ACCEPTABLE — optional component, absence is a valid state
if (TryGetComponent<ParticleSystem>(out var particles))
{
    particles.Play();
}

// ACCEPTABLE — collection may legitimately be empty
var accessories = catalog.GetByType(AccessoryType.Head);
if (accessories.Count == 0) return;
```

## Decision rule

Ask: "If this value is null, is the system broken or is this just one valid path?"
- System broken → throw
- Valid path → guard
- Unsure → throw
