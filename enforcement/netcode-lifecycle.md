# Enforcement: Netcode Lifecycle Ordering

**Rule:** Never access NetworkVariables, invoke RPCs, or check ownership before `OnNetworkSpawn`.

## Violations

### NetworkVariable access in Awake
```csharp
// VIOLATION — NetworkVariable not initialized in Awake
private void Awake()
{
    accessoryState.OnValueChanged += OnAccessoryStateChanged; // will fail
    Debug.Log(accessoryState.Value); // undefined behavior
}

// REQUIRED — access in OnNetworkSpawn
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    accessoryState.OnValueChanged += OnAccessoryStateChanged;
    OnAccessoryStateChanged(default, accessoryState.Value);
}
```

### RPC call before spawn
```csharp
// VIOLATION — RPC cannot be sent before spawn
private void Start()
{
    RequestAccessoriesServerRpc(); // silently fails
}

// REQUIRED
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    if (IsOwner)
    {
        RequestAccessoriesServerRpc();
    }
}
```

### IsOwner check in Awake/Start
```csharp
// VIOLATION — ownership not assigned yet
private void Awake()
{
    if (IsOwner) { /* ... */ } // always false or undefined
}

// REQUIRED
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    if (IsOwner) { /* ... */ }
}
```

### Missing base.OnNetworkSpawn call
```csharp
// VIOLATION — skips internal Netcode initialization
public override void OnNetworkSpawn()
{
    accessoryState.OnValueChanged += OnAccessoryStateChanged;
}

// REQUIRED
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    accessoryState.OnValueChanged += OnAccessoryStateChanged;
}
```

### Unsubscribe in wrong lifecycle method
```csharp
// VIOLATION — OnDestroy fires after OnNetworkDespawn, may cause callbacks on despawned objects
private void OnDestroy()
{
    accessoryState.OnValueChanged -= OnAccessoryStateChanged;
}

// REQUIRED — unsubscribe in the network lifecycle counterpart
public override void OnNetworkDespawn()
{
    accessoryState.OnValueChanged -= OnAccessoryStateChanged;
    base.OnNetworkDespawn();
}
```

## Lifecycle Summary

Safe operations by lifecycle method:

| Method | NetworkVariable | RPC | IsOwner/IsServer | Local refs |
|---|---|---|---|---|
| `Awake()` | No | No | No | Yes |
| `OnNetworkSpawn()` | Yes | Yes | Yes | Yes |
| `Start()` | Yes (if spawned) | Yes (if spawned) | Yes (if spawned) | Yes |
| `Update()` | Yes | Yes | Yes | Yes |
| `OnNetworkDespawn()` | Unsubscribe only | No | Yes | Yes |
| `OnDestroy()` | No | No | No | Cleanup only |
