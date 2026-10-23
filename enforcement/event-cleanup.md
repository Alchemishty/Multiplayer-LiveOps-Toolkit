# Enforcement: Event Subscription Cleanup

**Rule:** Every event subscription (`+=`) must have a corresponding unsubscription (`-=`) in the teardown counterpart.

## Lifecycle Pairs

| Subscribe in | Unsubscribe in |
|---|---|
| `OnEnable()` | `OnDisable()` |
| `OnNetworkSpawn()` | `OnNetworkDespawn()` |
| `Awake()` or `Start()` | `OnDestroy()` |

Choose the pair that matches the subscription's intended lifetime:
- **Scene lifetime:** `OnEnable` / `OnDisable`
- **Network lifetime:** `OnNetworkSpawn` / `OnNetworkDespawn`
- **Object lifetime:** `Awake` / `OnDestroy` (only for non-networked objects)

## Violations

### Missing unsubscription
```csharp
// VIOLATION — subscribes but never unsubscribes
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    accessoryState.OnValueChanged += OnAccessoryStateChanged;
}
// No OnNetworkDespawn override — leak

// REQUIRED
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    accessoryState.OnValueChanged += OnAccessoryStateChanged;
}

public override void OnNetworkDespawn()
{
    accessoryState.OnValueChanged -= OnAccessoryStateChanged;
    base.OnNetworkDespawn();
}
```

### Mismatched lifecycle pair
```csharp
// VIOLATION — subscribed in OnNetworkSpawn but unsubscribed in OnDestroy
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    accessoryState.OnValueChanged += OnAccessoryStateChanged;
}

private void OnDestroy()
{
    accessoryState.OnValueChanged -= OnAccessoryStateChanged; // too late for network events
}

// REQUIRED — match the lifecycle pair
public override void OnNetworkDespawn()
{
    accessoryState.OnValueChanged -= OnAccessoryStateChanged;
    base.OnNetworkDespawn();
}
```

### Anonymous lambda subscriptions
```csharp
// VIOLATION — cannot unsubscribe from anonymous lambda
button.onClick.AddListener(() => OnBuyClicked(itemId));
// No way to RemoveListener for this specific lambda

// REQUIRED — use a named method or store the delegate
private void OnBuyButtonClicked() => OnBuyClicked(currentItemId);

private void OnEnable()
{
    buyButton.onClick.AddListener(OnBuyButtonClicked);
}

private void OnDisable()
{
    buyButton.onClick.RemoveListener(OnBuyButtonClicked);
}
```

## Checking

For each `+=` or `AddListener` in a file, verify a corresponding `-=` or `RemoveListener` exists in the lifecycle counterpart. Flag any subscription without a matching teardown.
