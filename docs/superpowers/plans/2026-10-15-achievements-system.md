# Achievements System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an event-driven achievements system with data-driven conditions, backend persistence, authority abstraction, toast notifications, and an achievements panel.

**Architecture:** Pure C# data types and evaluation in Data layer (TDD). API client + TrustClientAuthority in API layer. AchievementService singleton in Registry layer coordinates loading, event processing, and sync. Toast + panel in Example layer. Integration via single-line ReportEvent calls in existing purchase/login flows.

**Tech Stack:** Unity, C#, NUnit, Newtonsoft.Json, TMPro, UnityEngine.UI

**Spec:** `docs/superpowers/specs/2026-10-15-achievements-system-design.md`

## Global Constraints

- All work is local — no git operations
- Unity compiler must show zero errors after each task
- All existing tests must continue to pass
- Layer imports: Data must not reference API/Registry/Player/Example
- Fully descriptive variable names, no abbreviations
- Never silently consume null — throw when non-null is required
- Achievement system is optional — null-conditional on all ReportEvent calls
- Every code change must include tests where applicable

## Review Focus

1. **ProcessEvent with CurrencySpent and null targetCurrency** — should count ALL currencies, not skip. Add test pinning this.
2. **ProcessEvent called before SetDefinitions** — should return empty list, not throw. Add test.
3. **AchievementProgressTracker.LoadProgress with achievementId not in definitions** — should ignore gracefully. Add test.
4. **TrustClientAuthority.RequestUnlock when AchievementAPI.Instance is null** — should call onRejected, not throw. Add test comment noting this is integration-only.
5. **Multiple CurrencySpent events in one purchase** (gold + gems for same item) — tracker must handle both independently. Add test.

---

### Task 1: Data Enums + GameEventContext + AchievementProgress

**Files:**
- Create: `Assets/Scripts/Data/AchievementConditionType.cs`
- Create: `Assets/Scripts/Data/GameEventType.cs`
- Create: `Assets/Scripts/Data/GameEventContext.cs`
- Create: `Assets/Scripts/Data/AchievementProgress.cs`

**Interfaces:**
- Consumes: `AccessoryType` (existing enum)
- Produces: `AchievementConditionType` enum, `GameEventType` enum, `GameEventContext` struct, `AchievementProgress` class

- [ ] **Step 1: Create all four files**

```csharp
// Assets/Scripts/Data/AchievementConditionType.cs
public enum AchievementConditionType
{
    ThresholdCounter,
    CollectionCompletion,
    OneTimeTrigger
}
```

```csharp
// Assets/Scripts/Data/GameEventType.cs
public enum GameEventType
{
    Purchase,
    SetPurchase,
    Login,
    CurrencySpent
}
```

```csharp
// Assets/Scripts/Data/GameEventContext.cs
public struct GameEventContext
{
    public int itemId;
    public int setId;
    public string currencyName;
    public int amount;
}
```

```csharp
// Assets/Scripts/Data/AchievementProgress.cs
public class AchievementProgress
{
    public string achievementId;
    public int currentCount;
    public bool isUnlocked;
    public long unlockedAtTimestamp;
}
```

- [ ] **Step 2: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

---

### Task 2: AchievementDefinition + Tests

**Files:**
- Create: `Assets/Scripts/Data/AchievementDefinition.cs`
- Create: `Assets/Tests/EditMode/AchievementDefinitionTests.cs`

**Interfaces:**
- Consumes: `AchievementConditionType`, `GameEventType`, `AccessoryType`
- Produces: `AchievementDefinition` class with `Validate()` method

- [ ] **Step 1: Write failing tests**

```csharp
// Assets/Tests/EditMode/AchievementDefinitionTests.cs
using NUnit.Framework;

[TestFixture]
public class AchievementDefinitionTests
{
    [Test]
    public void Validate_NullId_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = null,
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = GameEventType.Purchase,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_EmptyId_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "",
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = GameEventType.Purchase,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_ThresholdCounter_ZeroTarget_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "buy-5",
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = GameEventType.Purchase,
            targetCount = 0,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_ThresholdCounter_NegativeTarget_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "buy-neg",
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = GameEventType.Purchase,
            targetCount = -1,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_CollectionCompletion_NoneSlot_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "collect-none",
            conditionType = AchievementConditionType.CollectionCompletion,
            triggerEvent = GameEventType.Purchase,
            targetSlot = AccessoryType.None,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_ValidOneTimeTrigger_DoesNotThrow()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "first-purchase",
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = GameEventType.Purchase,
        };
        Assert.DoesNotThrow(() => definition.Validate());
    }

    [Test]
    public void Validate_ValidThresholdCounter_DoesNotThrow()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "buy-5",
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = GameEventType.Purchase,
            targetCount = 5,
        };
        Assert.DoesNotThrow(() => definition.Validate());
    }

    [Test]
    public void Validate_ValidCollectionCompletion_DoesNotThrow()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "all-headwear",
            conditionType = AchievementConditionType.CollectionCompletion,
            triggerEvent = GameEventType.Purchase,
            targetSlot = AccessoryType.Headwear,
        };
        Assert.DoesNotThrow(() => definition.Validate());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `mcp: run_tests with testMode=EditMode`
Expected: FAIL — `AchievementDefinition` does not exist

- [ ] **Step 3: Write implementation**

```csharp
// Assets/Scripts/Data/AchievementDefinition.cs
[System.Serializable]
public class AchievementDefinition
{
    public string achievementId;
    public string displayName;
    public string description;
    public AchievementConditionType conditionType;
    public GameEventType triggerEvent;
    public int targetCount;
    public AccessoryType targetSlot;
    public string targetCurrency;
    public string iconName;

    public void Validate()
    {
        if (string.IsNullOrEmpty(achievementId))
            throw new System.ArgumentException(
                "AchievementDefinition: achievementId must not be null or empty");

        if (conditionType == AchievementConditionType.ThresholdCounter && targetCount <= 0)
            throw new System.ArgumentException(
                $"AchievementDefinition '{achievementId}': targetCount must be positive for ThresholdCounter");

        if (conditionType == AchievementConditionType.CollectionCompletion && targetSlot == AccessoryType.None)
            throw new System.ArgumentException(
                $"AchievementDefinition '{achievementId}': targetSlot must not be None for CollectionCompletion");
    }
}
```

- [ ] **Step 4: Verify compilation and run tests**

Run: `mcp: refresh_unity` then `mcp: read_console` — zero errors
Run: `mcp: run_tests with testMode=EditMode` — all pass

---

### Task 3: AchievementProgressTracker + Tests

**Files:**
- Create: `Assets/Scripts/Data/AchievementProgressTracker.cs`
- Create: `Assets/Tests/EditMode/AchievementProgressTrackerTests.cs`

**Interfaces:**
- Consumes: `AchievementDefinition`, `AchievementProgress`, `AchievementConditionType`, `GameEventType`, `GameEventContext`, `AccessoryType`
- Produces: `AchievementProgressTracker` — `SetDefinitions`, `LoadProgress`, `SetCollectionCompleteChecker`, `ProcessEvent`, `GetProgress`, `GetAllProgress`, `GetAllDefinitions`

- [ ] **Step 1: Write failing tests**

```csharp
// Assets/Tests/EditMode/AchievementProgressTrackerTests.cs
using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class AchievementProgressTrackerTests
{
    private AchievementProgressTracker tracker;

    [SetUp]
    public void SetUp()
    {
        tracker = new AchievementProgressTracker();
    }

    private AchievementDefinition MakeThreshold(string id, GameEventType trigger, int target,
        string targetCurrency = null)
    {
        return new AchievementDefinition
        {
            achievementId = id,
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = trigger,
            targetCount = target,
            targetCurrency = targetCurrency,
        };
    }

    private AchievementDefinition MakeOneTime(string id, GameEventType trigger)
    {
        return new AchievementDefinition
        {
            achievementId = id,
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = trigger,
        };
    }

    private AchievementDefinition MakeCollection(string id, GameEventType trigger, AccessoryType slot)
    {
        return new AchievementDefinition
        {
            achievementId = id,
            conditionType = AchievementConditionType.CollectionCompletion,
            triggerEvent = trigger,
            targetSlot = slot,
        };
    }

    // --- ThresholdCounter ---

    [Test]
    public void ThresholdCounter_IncrementsByOne_ForPurchaseEvent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition> { MakeThreshold("buy-3", GameEventType.Purchase, 3) });
        tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(1, tracker.GetProgress("buy-3").currentCount);
    }

    [Test]
    public void ThresholdCounter_UnlocksAtTarget()
    {
        tracker.SetDefinitions(new List<AchievementDefinition> { MakeThreshold("buy-2", GameEventType.Purchase, 2) });
        tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 2 });
        Assert.Contains("buy-2", unlocked);
        Assert.IsTrue(tracker.GetProgress("buy-2").isUnlocked);
    }

    [Test]
    public void ThresholdCounter_DoesNotDoubleUnlock()
    {
        tracker.SetDefinitions(new List<AchievementDefinition> { MakeThreshold("buy-1", GameEventType.Purchase, 1) });
        var first = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        var second = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 2 });
        Assert.AreEqual(1, first.Count);
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_IncrementsByAmount()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("spend-1000", GameEventType.CurrencySpent, 1000, "gold") });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = 400 });
        Assert.AreEqual(400, tracker.GetProgress("spend-1000").currentCount);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_FiltersByCurrency()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("spend-gold", GameEventType.CurrencySpent, 100, "gold") });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gems", amount = 500 });
        Assert.AreEqual(0, tracker.GetProgress("spend-gold").currentCount);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_NullTargetCountsAllCurrencies()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("spend-any", GameEventType.CurrencySpent, 100, null) });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = 60 });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gems", amount = 50 });
        Assert.AreEqual(110, tracker.GetProgress("spend-any").currentCount);
        Assert.IsTrue(tracker.GetProgress("spend-any").isUnlocked);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_MultipleCurrenciesIndependent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
        {
            MakeThreshold("spend-gold", GameEventType.CurrencySpent, 100, "gold"),
            MakeThreshold("spend-gems", GameEventType.CurrencySpent, 100, "gems"),
        });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = 100 });
        Assert.IsTrue(tracker.GetProgress("spend-gold").isUnlocked);
        Assert.IsFalse(tracker.GetProgress("spend-gems").isUnlocked);
    }

    // --- OneTimeTrigger ---

    [Test]
    public void OneTimeTrigger_UnlocksOnFirstEvent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-buy", GameEventType.Purchase) });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.Contains("first-buy", unlocked);
        Assert.IsTrue(tracker.GetProgress("first-buy").isUnlocked);
    }

    [Test]
    public void OneTimeTrigger_AlreadyUnlocked_NoRepeat()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-buy", GameEventType.Purchase) });
        tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        var second = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 2 });
        Assert.AreEqual(0, second.Count);
    }

    // --- CollectionCompletion ---

    [Test]
    public void CollectionCompletion_CheckerReturnsFalse_NoUnlock()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeCollection("all-headwear", GameEventType.Purchase, AccessoryType.Headwear) });
        tracker.SetCollectionCompleteChecker(slot => false);
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(0, unlocked.Count);
        Assert.IsFalse(tracker.GetProgress("all-headwear").isUnlocked);
    }

    [Test]
    public void CollectionCompletion_CheckerReturnsTrue_Unlocks()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeCollection("all-headwear", GameEventType.Purchase, AccessoryType.Headwear) });
        tracker.SetCollectionCompleteChecker(slot => slot == AccessoryType.Headwear);
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.Contains("all-headwear", unlocked);
        Assert.IsTrue(tracker.GetProgress("all-headwear").isUnlocked);
    }

    [Test]
    public void CollectionCompletion_AlreadyUnlocked_NoRepeat()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeCollection("all-headwear", GameEventType.Purchase, AccessoryType.Headwear) });
        tracker.SetCollectionCompleteChecker(slot => true);
        tracker.ProcessEvent(GameEventType.Purchase, default);
        var second = tracker.ProcessEvent(GameEventType.Purchase, default);
        Assert.AreEqual(0, second.Count);
    }

    // --- General ---

    [Test]
    public void ProcessEvent_NonMatchingEventType_ReturnsEmpty()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-buy", GameEventType.Purchase) });
        var unlocked = tracker.ProcessEvent(GameEventType.Login, default);
        Assert.AreEqual(0, unlocked.Count);
    }

    [Test]
    public void ProcessEvent_MultipleUnlocksFromOneEvent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
        {
            MakeOneTime("first-buy", GameEventType.Purchase),
            MakeThreshold("buy-1", GameEventType.Purchase, 1),
        });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(2, unlocked.Count);
    }

    [Test]
    public void LoadProgress_RestoresState()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("buy-5", GameEventType.Purchase, 5) });
        tracker.LoadProgress(new List<AchievementProgress>
        {
            new AchievementProgress { achievementId = "buy-5", currentCount = 3, isUnlocked = false }
        });
        Assert.AreEqual(3, tracker.GetProgress("buy-5").currentCount);
    }

    [Test]
    public void LoadProgress_AlreadyUnlocked_DoesNotReUnlock()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("buy-1", GameEventType.Purchase, 1) });
        tracker.LoadProgress(new List<AchievementProgress>
        {
            new AchievementProgress { achievementId = "buy-1", currentCount = 1, isUnlocked = true, unlockedAtTimestamp = 1000 }
        });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(0, unlocked.Count);
    }

    [Test]
    public void LoadProgress_UnknownAchievementId_Ignored()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("real", GameEventType.Purchase) });
        Assert.DoesNotThrow(() => tracker.LoadProgress(new List<AchievementProgress>
        {
            new AchievementProgress { achievementId = "nonexistent", currentCount = 5 }
        }));
    }

    [Test]
    public void GetProgress_UnknownId_ReturnsNull()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>());
        Assert.IsNull(tracker.GetProgress("nope"));
    }

    [Test]
    public void ProcessEvent_BeforeSetDefinitions_ReturnsEmpty()
    {
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, default);
        Assert.AreEqual(0, unlocked.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `mcp: run_tests with testMode=EditMode`
Expected: FAIL — `AchievementProgressTracker` does not exist

- [ ] **Step 3: Write implementation**

```csharp
// Assets/Scripts/Data/AchievementProgressTracker.cs
using System;
using System.Collections.Generic;

public class AchievementProgressTracker
{
    private Dictionary<string, AchievementDefinition> definitionsById = new();
    private Dictionary<string, AchievementProgress> progressById = new();
    private Func<AccessoryType, bool> collectionCompleteChecker;

    public void SetDefinitions(List<AchievementDefinition> definitions)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        definitionsById.Clear();
        foreach (var definition in definitions)
        {
            definition.Validate();
            definitionsById[definition.achievementId] = definition;
        }

        foreach (var definition in definitions)
        {
            if (!progressById.ContainsKey(definition.achievementId))
            {
                progressById[definition.achievementId] = new AchievementProgress
                {
                    achievementId = definition.achievementId,
                    currentCount = 0,
                    isUnlocked = false,
                    unlockedAtTimestamp = 0,
                };
            }
        }
    }

    public void LoadProgress(List<AchievementProgress> progressList)
    {
        if (progressList == null)
            throw new ArgumentNullException(nameof(progressList));

        foreach (var progress in progressList)
        {
            if (definitionsById.ContainsKey(progress.achievementId))
            {
                progressById[progress.achievementId] = progress;
            }
        }
    }

    public void SetCollectionCompleteChecker(Func<AccessoryType, bool> checker)
    {
        collectionCompleteChecker = checker
            ?? throw new ArgumentNullException(nameof(checker));
    }

    public List<string> ProcessEvent(GameEventType eventType, GameEventContext context)
    {
        var newlyUnlocked = new List<string>();

        foreach (var kvp in definitionsById)
        {
            var definition = kvp.Value;
            if (definition.triggerEvent != eventType)
                continue;

            var progress = progressById[definition.achievementId];
            if (progress.isUnlocked)
                continue;

            bool shouldUnlock = false;

            switch (definition.conditionType)
            {
                case AchievementConditionType.ThresholdCounter:
                    shouldUnlock = EvaluateThresholdCounter(definition, progress, context);
                    break;
                case AchievementConditionType.CollectionCompletion:
                    shouldUnlock = EvaluateCollectionCompletion(definition);
                    break;
                case AchievementConditionType.OneTimeTrigger:
                    shouldUnlock = true;
                    break;
            }

            if (shouldUnlock)
            {
                progress.isUnlocked = true;
                progress.unlockedAtTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                newlyUnlocked.Add(definition.achievementId);
            }
        }

        return newlyUnlocked;
    }

    private bool EvaluateThresholdCounter(
        AchievementDefinition definition, AchievementProgress progress, GameEventContext context)
    {
        if (definition.triggerEvent == GameEventType.CurrencySpent)
        {
            if (definition.targetCurrency != null && definition.targetCurrency != context.currencyName)
                return false;
            progress.currentCount += context.amount;
        }
        else
        {
            progress.currentCount += 1;
        }

        return progress.currentCount >= definition.targetCount;
    }

    private bool EvaluateCollectionCompletion(AchievementDefinition definition)
    {
        if (collectionCompleteChecker == null)
            return false;
        return collectionCompleteChecker(definition.targetSlot);
    }

    public AchievementProgress GetProgress(string achievementId)
    {
        return progressById.GetValueOrDefault(achievementId);
    }

    public List<AchievementProgress> GetAllProgress()
    {
        return new List<AchievementProgress>(progressById.Values);
    }

    public List<AchievementDefinition> GetAllDefinitions()
    {
        return new List<AchievementDefinition>(definitionsById.Values);
    }
}
```

- [ ] **Step 4: Verify compilation and run tests**

Run: `mcp: refresh_unity` then `mcp: read_console` — zero errors
Run: `mcp: run_tests with testMode=EditMode` — all pass

---

### Task 4: AchievementApiDTOs + IAchievementAuthority + AchievementAPI + TrustClientAuthority

**Files:**
- Create: `Assets/Scripts/Data/AchievementApiDTOs.cs`
- Create: `Assets/Scripts/Data/IAchievementAuthority.cs`
- Create: `Assets/Scripts/API/AchievementAPI.cs`
- Create: `Assets/Scripts/API/TrustClientAuthority.cs`

- [ ] **Step 1: Create all four files per spec**
- [ ] **Step 2: Verify compilation** — zero errors

---

### Task 5: AchievementCatalogSO

**Files:**
- Create: `Assets/Scripts/ScriptableObjects/AchievementCatalogSO.cs`

- [ ] **Step 1: Create ScriptableObject per spec**
- [ ] **Step 2: Verify compilation** — zero errors

---

### Task 6: AchievementService

**Files:**
- Create: `Assets/Scripts/Registry/AchievementService.cs`

- [ ] **Step 1: Create AchievementService per spec** — singleton, loads definitions+progress, processes events, syncs unlocks via authority, debounced progress sync
- [ ] **Step 2: Verify compilation** — zero errors

---

### Task 7: AchievementToast + AchievementsPanel

**Files:**
- Create: `Assets/Scripts/Example/AchievementToast.cs`
- Create: `Assets/Scripts/Example/UI/AchievementsPanel.cs`

- [ ] **Step 1: Create AchievementToast per spec** — queue-based, subscribes to AchievementUnlocked
- [ ] **Step 2: Create AchievementsPanel per spec** — tab index 4, unlocked + in-progress sections
- [ ] **Step 3: Verify compilation** — zero errors

---

### Task 8: Integration — ReportEvent calls in existing code

**Files:**
- Modify: `Assets/Scripts/Example/UI/ShopPanel.cs`
- Modify: `Assets/Scripts/Example/UI/SetsPanel.cs`
- Modify: `Assets/Scripts/Example/DevAuthBootstrap.cs`

- [ ] **Step 1: Add ReportEvent calls per spec** — null-conditional, Purchase + CurrencySpent in ShopPanel, SetPurchase + CurrencySpent in SetsPanel, Login in DevAuthBootstrap
- [ ] **Step 2: Verify compilation** — zero errors
- [ ] **Step 3: Run all tests** — all pass, no regressions
