using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class SetPreviewStateTrackerTests
{
    [Test]
    public void StartSetPreview_StoresSetIdAndSlots()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
            new() { slot = AccessoryType.MainHand, itemId = 2, currentEquippedItemId = 5 },
        };

        tracker.StartSetPreview(10, slots);

        Assert.IsTrue(tracker.IsSetPreviewActive);
        Assert.AreEqual(10, tracker.PreviewedSetId);
        var previewedSlots = tracker.GetPreviewedSlots();
        Assert.AreEqual(2, previewedSlots.Count);
        Assert.AreEqual(1, previewedSlots[AccessoryType.Headwear].previewedItemId);
        Assert.AreEqual(2, previewedSlots[AccessoryType.MainHand].previewedItemId);
        Assert.AreEqual(-1, previewedSlots[AccessoryType.Headwear].previouslyEquippedItemId);
        Assert.AreEqual(5, previewedSlots[AccessoryType.MainHand].previouslyEquippedItemId);
    }

    [Test]
    public void StartSetPreview_InvalidSetId_Throws()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
        };

        Assert.Throws<System.ArgumentException>(() => tracker.StartSetPreview(0, slots));
        Assert.Throws<System.ArgumentException>(() => tracker.StartSetPreview(-1, slots));
    }

    [Test]
    public void StartSetPreview_EmptySlots_Throws()
    {
        var tracker = new SetPreviewStateTracker();

        Assert.Throws<System.ArgumentException>(
            () => tracker.StartSetPreview(1, new List<SetPreviewStateTracker.SetPreviewSlotInput>()));
        Assert.Throws<System.ArgumentException>(
            () => tracker.StartSetPreview(1, null));
    }

    [Test]
    public void ClearSetPreview_WhenActive_ReturnsAllSlotsToRestore()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = 3 },
            new() { slot = AccessoryType.MainHand, itemId = 2, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(10, slots);

        var result = tracker.ClearSetPreview();

        Assert.IsTrue(result.WasActive);
        Assert.AreEqual(10, result.ClearedSetId);
        Assert.AreEqual(2, result.ClearedSlots.Count);
        Assert.IsFalse(tracker.IsSetPreviewActive);
        Assert.AreEqual(-1, tracker.PreviewedSetId);
    }

    [Test]
    public void ClearSetPreview_WhenInactive_ReturnsNotActive()
    {
        var tracker = new SetPreviewStateTracker();

        var result = tracker.ClearSetPreview();

        Assert.IsFalse(result.WasActive);
    }

    [Test]
    public void ClearSetPreview_RestoresCorrectPreviousItemIds()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = 50 },
            new() { slot = AccessoryType.Back, itemId = 2, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(10, slots);

        var result = tracker.ClearSetPreview();

        var headwearSlot = result.ClearedSlots.Find(s => s.clearedSlot == AccessoryType.Headwear);
        var backSlot = result.ClearedSlots.Find(s => s.clearedSlot == AccessoryType.Back);
        Assert.AreEqual(50, headwearSlot.restoreItemId);
        Assert.AreEqual(-1, backSlot.restoreItemId);
    }

    [Test]
    public void IsSetPreviewActive_ReflectsState()
    {
        var tracker = new SetPreviewStateTracker();
        Assert.IsFalse(tracker.IsSetPreviewActive);

        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(5, slots);
        Assert.IsTrue(tracker.IsSetPreviewActive);

        tracker.ClearSetPreview();
        Assert.IsFalse(tracker.IsSetPreviewActive);
    }

    [Test]
    public void SetPreviewChanged_FiresOnStartAndClear()
    {
        var tracker = new SetPreviewStateTracker();
        int fireCount = 0;
        tracker.SetPreviewChanged += () => fireCount++;

        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(5, slots);
        Assert.AreEqual(1, fireCount);

        tracker.ClearSetPreview();
        Assert.AreEqual(2, fireCount);
    }

    [Test]
    public void SetPreviewChanged_DoesNotFireOnInactiveClear()
    {
        var tracker = new SetPreviewStateTracker();
        int fireCount = 0;
        tracker.SetPreviewChanged += () => fireCount++;

        tracker.ClearSetPreview();
        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void ClearSetPreview_SlotsWithNoPrevious_RestoreIdIsNegative()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Facewear, itemId = 7, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(3, slots);

        var result = tracker.ClearSetPreview();

        Assert.AreEqual(-1, result.ClearedSlots[0].restoreItemId);
    }
}
