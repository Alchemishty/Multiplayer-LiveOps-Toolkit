# Player Nameplates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display each player's username as a world-space label floating above their avatar; host shown in red with `[HOST]` prefix; fades linearly from 15 m to invisible at 20 m.

**Architecture:** A single `PlayerNameplate : NetworkBehaviour` on the player prefab owns a `NetworkVariable<FixedString64Bytes>` for the username. The owner writes their name from `TokenAPI` in `OnNetworkSpawn`; all clients read it via `OnValueChanged` and an explicit initial read. A child GameObject with a world-space `TextMeshPro` component is the visual; `PlayerNameplate.LateUpdate` billboards it and fades its alpha by distance. Display-name formatting (host prefix, color selection) is extracted into a pure `NameplateDisplayFormatter` class for testability.

**Tech Stack:** Unity Netcode for GameObjects 1.9.1, `Unity.Collections.FixedString64Bytes`, TextMeshPro (world-space `TextMeshPro` component, not `TextMeshProUGUI`)

**Spec:** Brainstormed in conversation: world-space billboard, username from `TokenAPI.GetUsername()`, `[HOST]` prefix + red for host, fade 15–20 m, all players see their own name.

## Global Constraints

- `PlayerNameplate` lives in `Assets/Scripts/Player/` (Player layer — no Example imports)
- `NameplateDisplayFormatter` lives in `Assets/Scripts/Player/` (Player layer)
- `NetworkVariable` permission: `ReadPermission.Everyone`, `WritePermission.Owner` — username is a display-only value set once by the owner; no server validation needed
- `OnValueChanged` subscribed in `OnNetworkSpawn`, unsubscribed in `OnNetworkDespawn` — never in `Awake`
- `base.OnNetworkSpawn()` called first; `base.OnNetworkDespawn()` called last
- Initial value must be explicitly read in `OnNetworkSpawn` for non-owners (`OnValueChanged` does not fire for the initial value)
- No `Debug.Log` in builds — wrap in `#if UNITY_EDITOR`
- `SetupExampleScene.cs` must be updated to add `PlayerNameplate` to the player prefab and wire the `TextMeshPro` child
- All variable names fully descriptive — no abbreviations

## Review Focus

- **Late-joining client sees stale empty nameplate**: `OnValueChanged` doesn't fire for values set before the client joined. Covered by the explicit initial-value read in Task 2 Step 3.
- **Host leaves and reconnects as client**: `IsHost` is rechecked on every `UpdateNameplateDisplay` call, not cached — covered by Task 2 Step 5.
- **Username longer than 63 bytes**: `FixedString64Bytes` silently truncates at 63 UTF-8 bytes. Covered by Task 1 Step 1 (test that truncated input doesn't throw).
- **Camera.main is null before spawn**: `LateUpdate` must null-guard `Camera.main`. Covered by Task 2 Step 7.
- **Player at exactly the fade boundary (15 m and 20 m)**: alpha should be 1.0 at 15 m and 0.0 at 20 m, not inverted. Covered by Task 1 Step 1.

---

### Task 1: NameplateDisplayFormatter

**Files:**
- Create: `Assets/Scripts/Player/NameplateDisplayFormatter.cs`
- Test: `Assets/Tests/EditMode/NameplateDisplayFormatterTests.cs`

**Interfaces:**
- Produces:
  - `NameplateDisplayFormatter.FormatDisplayName(string username, bool isHost) → string`
    Returns `"[HOST] " + username` when `isHost`, else `username` unchanged.
  - `NameplateDisplayFormatter.GetDisplayColor(bool isHost) → UnityEngine.Color`
    Returns `Color.red` when `isHost`, else `Color.white`.
  - `NameplateDisplayFormatter.ComputeAlpha(float distance, float fadeStartDistance, float fadeEndDistance) → float`
    Returns `1f` when `distance <= fadeStartDistance`, `0f` when `distance >= fadeEndDistance`, linearly interpolated between.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class NameplateDisplayFormatterTests
{
    // FormatDisplayName
    [Test]
    public void FormatDisplayName_NotHost_ReturnsUsernameUnchanged()
    {
        Assert.AreEqual("Alice", NameplateDisplayFormatter.FormatDisplayName("Alice", false));
    }

    [Test]
    public void FormatDisplayName_IsHost_PrependsHostPrefix()
    {
        Assert.AreEqual("[HOST] Alice", NameplateDisplayFormatter.FormatDisplayName("Alice", true));
    }

    [Test]
    public void FormatDisplayName_EmptyUsername_ReturnsEmptyForNonHost()
    {
        Assert.AreEqual("", NameplateDisplayFormatter.FormatDisplayName("", false));
    }

    [Test]
    public void FormatDisplayName_EmptyUsername_ReturnsHostPrefixOnly()
    {
        Assert.AreEqual("[HOST] ", NameplateDisplayFormatter.FormatDisplayName("", true));
    }

    // GetDisplayColor
    [Test]
    public void GetDisplayColor_NotHost_ReturnsWhite()
    {
        Assert.AreEqual(Color.white, NameplateDisplayFormatter.GetDisplayColor(false));
    }

    [Test]
    public void GetDisplayColor_IsHost_ReturnsRed()
    {
        Assert.AreEqual(Color.red, NameplateDisplayFormatter.GetDisplayColor(true));
    }

    // ComputeAlpha
    [Test]
    public void ComputeAlpha_AtFadeStart_ReturnsOne()
    {
        Assert.AreEqual(1f, NameplateDisplayFormatter.ComputeAlpha(15f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_AtFadeEnd_ReturnsZero()
    {
        Assert.AreEqual(0f, NameplateDisplayFormatter.ComputeAlpha(20f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_BeyondFadeEnd_ReturnsZero()
    {
        Assert.AreEqual(0f, NameplateDisplayFormatter.ComputeAlpha(25f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_BeforeFadeStart_ReturnsOne()
    {
        Assert.AreEqual(1f, NameplateDisplayFormatter.ComputeAlpha(5f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_AtMidpoint_ReturnsHalf()
    {
        Assert.AreEqual(0.5f, NameplateDisplayFormatter.ComputeAlpha(17.5f, 15f, 20f), 0.001f);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

Open Unity Test Runner → EditMode → run `NameplateDisplayFormatterTests`. Expected: all fail with type-not-found.

- [ ] **Step 3: Implement `NameplateDisplayFormatter` in `Assets/Scripts/Player/NameplateDisplayFormatter.cs`**

Static class, three static methods matching the signatures above. `ComputeAlpha` uses `Mathf.InverseLerp(fadeStartDistance, fadeEndDistance, distance)` then `1f - t` (close = opaque).

- [ ] **Step 4: Run tests to confirm they pass**

Expected: all 11 tests pass.

- [ ] **Step 5: Commit**

```
git add Assets/Scripts/Player/NameplateDisplayFormatter.cs Assets/Scripts/Player/NameplateDisplayFormatter.cs.meta Assets/Tests/EditMode/NameplateDisplayFormatterTests.cs Assets/Tests/EditMode/NameplateDisplayFormatterTests.cs.meta
git commit -m "feat: add NameplateDisplayFormatter with host prefix, color, and alpha fade logic"
```

---

### Task 2: PlayerNameplate NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/Player/PlayerNameplate.cs`

**Interfaces:**
- Consumes: `NameplateDisplayFormatter.FormatDisplayName`, `GetDisplayColor`, `ComputeAlpha` from Task 1; `TokenAPI.Instance.GetUsername()`; `NetworkManager.Singleton.IsHost`
- Produces: `PlayerNameplate` component (added to player prefab by Task 3); `[SerializeField] private TextMeshPro nameplateLabel` wired by `SetupExampleScene`

- [ ] **Step 1: Add `PlayerNameplate.cs` to `Assets/Scripts/Player/`**

Class signature: `public class PlayerNameplate : NetworkBehaviour`

Usings: `UnityEngine`, `Unity.Netcode`, `Unity.Collections`, `TMPro`

- [ ] **Step 2: Declare the NetworkVariable**

```csharp
public NetworkVariable<FixedString64Bytes> Username = new NetworkVariable<FixedString64Bytes>(
    default,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Owner
);
```

- [ ] **Step 3: Implement `OnNetworkSpawn`**

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    Username.OnValueChanged += OnUsernameChanged;

    if (IsOwner)
    {
        string localUsername = TokenAPI.Instance.GetUsername();
        Username.Value = new FixedString64Bytes(localUsername);
    }
    else
    {
        UpdateNameplateDisplay(Username.Value.ToString());
    }
}
```

- [ ] **Step 4: Implement `OnNetworkDespawn`**

```csharp
public override void OnNetworkDespawn()
{
    Username.OnValueChanged -= OnUsernameChanged;
    base.OnNetworkDespawn();
}
```

- [ ] **Step 5: Implement `OnUsernameChanged` and `UpdateNameplateDisplay`**

`OnUsernameChanged(FixedString64Bytes previous, FixedString64Bytes current)` calls `UpdateNameplateDisplay(current.ToString())`.

`UpdateNameplateDisplay(string username)`:
- Guard: `if (nameplateLabel == null) throw new InvalidOperationException("PlayerNameplate: nameplateLabel is not assigned");`
- `bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && IsOwner;`
  - Note: only the host's own nameplate shows `[HOST]` — `IsOwner` scopes it correctly.
  - Actually: the host player's nameplate should show `[HOST]` to ALL clients, not just the host themselves. The host's `IsOwner` is true only on their own client, but ALL clients run `UpdateNameplateDisplay` when they receive the value. Fix: check if this is the *host's avatar*, not if the *current machine* is the host. Use `OwnerClientId == NetworkManager.Singleton?.ServerClientId`.
  - `bool isHostPlayer = NetworkManager.Singleton != null && OwnerClientId == NetworkManager.Singleton.ServerClientId;`
- `nameplateLabel.text = NameplateDisplayFormatter.FormatDisplayName(username, isHostPlayer);`
- `Color displayColor = NameplateDisplayFormatter.GetDisplayColor(isHostPlayer);`
- `nameplateLabel.color = new Color(displayColor.r, displayColor.g, displayColor.b, nameplateLabel.color.a);`
  (preserve current alpha — fade is handled in LateUpdate)

- [ ] **Step 6: Declare constants and SerializeField**

```csharp
private const float FADE_START_DISTANCE = 15f;
private const float FADE_END_DISTANCE = 20f;

[SerializeField] private TextMeshPro nameplateLabel;
```

- [ ] **Step 7: Implement `LateUpdate`**

```csharp
private void LateUpdate()
{
    if (nameplateLabel == null) return;
    if (Camera.main == null) return;

    // Billboard: face camera
    nameplateLabel.transform.LookAt(
        nameplateLabel.transform.position + Camera.main.transform.rotation * Vector3.forward,
        Camera.main.transform.rotation * Vector3.up
    );

    // Alpha fade by distance
    float distanceToCamera = Vector3.Distance(transform.position, Camera.main.transform.position);
    float alpha = NameplateDisplayFormatter.ComputeAlpha(distanceToCamera, FADE_START_DISTANCE, FADE_END_DISTANCE);
    Color currentColor = nameplateLabel.color;
    nameplateLabel.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha);
}
```

- [ ] **Step 8: Refresh Unity and confirm zero compilation errors**

Use `refresh_unity` MCP tool, then `read_console` filtering for errors.

- [ ] **Step 9: Commit**

```
git add Assets/Scripts/Player/PlayerNameplate.cs Assets/Scripts/Player/PlayerNameplate.cs.meta
git commit -m "feat: add PlayerNameplate NetworkBehaviour with username sync and world-space billboard"
```

---

### Task 3: Wire PlayerNameplate into SetupExampleScene

**Files:**
- Modify: `Assets/Editor/SetupExampleScene.cs` — `CreatePlayerPrefab()` method (~line 278)

**Interfaces:**
- Consumes: `PlayerNameplate` from Task 2

- [ ] **Step 1: Add `PlayerNameplate` component after `ClientNetworkAnimator` in `CreatePlayerPrefab()`**

After line `root.AddComponent<ClientNetworkAnimator>()` (currently ~line 292), add:

```csharp
// Nameplate child: world-space TMP above the avatar's head
var nameplateGo = new GameObject("Nameplate");
nameplateGo.transform.SetParent(root.transform, false);
// Local y=0.65 × scale 3.4 ≈ 2.2 m world height — above the head
nameplateGo.transform.localPosition = new Vector3(0f, 0.65f, 0f);
var tmpLabel = nameplateGo.AddComponent<TextMeshPro>();
tmpLabel.text = "";
tmpLabel.fontSize = 3f;
tmpLabel.alignment = TextAlignmentOptions.Center;
tmpLabel.color = Color.white;

var nameplateComponent = root.AddComponent<PlayerNameplate>();
var nameplateSo = new SerializedObject(nameplateComponent);
nameplateSo.FindProperty("nameplateLabel").objectReferenceValue = tmpLabel;
nameplateSo.ApplyModifiedPropertiesWithoutUndo();
```

- [ ] **Step 2: Run `Tools/Setup Example Scene` in Unity**

Menu → Tools → Setup Example Scene. Confirm scene rebuilds without errors. Confirm player prefab has `PlayerNameplate` component with `nameplateLabel` wired to the `Nameplate` child's `TextMeshPro`.

- [ ] **Step 3: Refresh and check console**

`read_console` — expect zero errors.

- [ ] **Step 4: Commit**

```
git add Assets/Editor/SetupExampleScene.cs
git commit -m "feat: wire PlayerNameplate into player prefab via SetupExampleScene"
```

---

### Task 4: Play-test verification

- [ ] **Step 1: Enter Play Mode in Unity**

Confirm: the game connects (host or client). A username nameplate appears above the local player's avatar. Text matches the username entered at login.

- [ ] **Step 2: Test host label**

Start as host. Confirm nameplate reads `[HOST] <username>` in red.

- [ ] **Step 3: Test second client**

Open a second build or editor instance as a client. Confirm the host's nameplate shows `[HOST]` prefix and red text on the client's screen. Confirm the client's own nameplate shows white text with no prefix.

- [ ] **Step 4: Test fade**

Walk away from an avatar. Confirm the nameplate fades out beyond 15 m and disappears at 20 m.

- [ ] **Step 5: Run EditMode tests**

Unity Test Runner → EditMode → Run All. Confirm all tests pass including new `NameplateDisplayFormatterTests`.

- [ ] **Step 6: Final commit if any fixups were needed**
