# Enforcement: Test Coverage

**Rule:** Every code change must include tests. No exceptions.

## When Tests Are Required

| Change Type | Required Test |
|---|---|
| New feature | Tests written before implementation (TDD) |
| Bug fix | Test that reproduces the bug before fixing it |
| Refactor | Tests proving behavior is preserved before and after |
| New class/method | At least one test per public method |

## Violations

### Bug fix without a reproduction test
```csharp
// VIOLATION — fix applied but no test proves the bug existed or is now fixed
// PlayerController.cs: moved subscription from Awake to OnNetworkSpawn
// No test file created

// REQUIRED — test that fails before the fix, passes after
[UnityTest]
public IEnumerator LateJoiningClient_ReceivesCurrentPlayerState()
{
    // Arrange: host sets state before client connects
    // Act: client joins
    // Assert: client has the correct state
}
```

### Refactor without preservation tests
```csharp
// VIOLATION — ShopController split into multiple classes
// No tests proving purchase flow still works

// REQUIRED — tests covering the behavior before refactoring
[Test]
public void PurchaseValidator_WithSufficientGold_ReturnsApproved()
{
    var result = PurchaseValidator.Validate("item_01", 100, 500);
    Assert.AreEqual(PurchaseValidationResult.Approved, result);
}
```

### New public method without test
```csharp
// VIOLATION — new method added, no test
public bool TryAddItem(string itemId) { ... }

// REQUIRED — at least happy path + error path
[Test]
public void TryAddItem_WithValidId_ReturnsTrue() { ... }

[Test]
public void TryAddItem_WithNullId_ThrowsArgumentException() { ... }

[Test]
public void TryAddItem_WhenFull_ReturnsFalse() { ... }
```

## Tier Classification

| Code Location | Test Tier | Why |
|---|---|---|
| Pure C# (no MonoBehaviour) | EditMode | Fast, no Unity runtime needed |
| MonoBehaviour (no Netcode) | PlayMode | Needs lifecycle callbacks |
| NetworkBehaviour | PlayMode | Needs NetworkManager + spawn cycle |
| Data validation, math, parsing | EditMode | Pure logic |
| UI interaction | PlayMode | Needs Canvas + EventSystem |

## Tests Must Be Behavioral, Not Structural

Tests must assert **observable behavior** — return values, thrown exceptions, state changes. Tests that check code structure are violations.

### Structural test (VIOLATION)
```csharp
// VIOLATION — checks that a method exists, not what it does
[Test]
public void PlayerController_OverridesOnNetworkSpawn()
{
    var method = typeof(PlayerController).GetMethod("OnNetworkSpawn",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert.IsNotNull(method, "PlayerController should override OnNetworkSpawn");
}

// VIOLATION — checks attribute presence, not behavior
[Test]
public void DoesNotSubscribeInAwake()
{
    var source = File.ReadAllText("Assets/Scripts/Player/PlayerController.cs");
    Assert.IsFalse(source.Contains("void Awake"));
}
```

### Behavioral test (REQUIRED)
```csharp
// CORRECT — tests what the code DOES, not how it's written
[UnityTest]
public IEnumerator LateJoiner_ReceivesCurrentState_WhenHostSetStateBeforeJoin()
{
    // Arrange: host sets player state
    // Act: client joins after state is set
    // Assert: client's callback received the state
    yield return null;
    Assert.AreEqual(expectedState, receivedState);
}

// CORRECT — tests the throw behavior
[Test]
public void GetItem_WithUninitializedRegistry_ThrowsInvalidOperationException()
{
    Assert.Throws<InvalidOperationException>(() => registry.GetItem("any_id"));
}
```

## Tests Must Fail First (Bug Fixes)

For bug fix tasks, the reproduction test MUST demonstrably fail before the fix is applied. If the test passes without the fix, it doesn't capture the bug — rewrite it.

1. Write the reproduction test
2. Run it — confirm it FAILS
3. Apply the fix
4. Run it again — confirm it PASSES
5. If step 2 passed, the test is wrong

## Checking

After every code change, verify:
1. At least one new test file or new test methods exist
2. Tests are in the correct tier (EditMode vs PlayMode)
3. Tests cover the specific change (not just generic smoke tests)
4. Tests assert behavior (return values, exceptions, state), not structure (reflection, source parsing)
5. Bug fix tests would fail if the fix were reverted
6. No test uses `typeof(...).GetMethod`, `BindingFlags`, `File.ReadAllText` on source files, or `Assembly.GetTypes` to verify code shape
