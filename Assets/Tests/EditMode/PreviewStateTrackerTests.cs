using System;
using NUnit.Framework;

[TestFixture]
public class PreviewStateTrackerTests
{
    private PreviewStateTracker CreateTracker()
    {
        return new PreviewStateTracker();
    }

    [Test]
    public void IsPreviewActive_WhenNewlyCreated_ReturnsFalse()
    {
        var tracker = CreateTracker();

        Assert.IsFalse(tracker.IsPreviewActive);
        Assert.AreEqual(-1, tracker.PreviewedItemId);
        Assert.AreEqual(AccessoryType.None, tracker.PreviewedSlot);
    }

    [Test]
    public void StartPreview_WithValidItem_SetsPreviewState()
    {
        var tracker = CreateTracker();

        tracker.StartPreview(42, AccessoryType.Headwear, -1);

        Assert.IsTrue(tracker.IsPreviewActive);
        Assert.AreEqual(42, tracker.PreviewedItemId);
        Assert.AreEqual(AccessoryType.Headwear, tracker.PreviewedSlot);
    }

    [Test]
    public void StartPreview_WithExistingEquippedItem_StoresPreviousItemId()
    {
        var tracker = CreateTracker();

        tracker.StartPreview(42, AccessoryType.Headwear, 10);

        Assert.AreEqual(10, tracker.PreviouslyEquippedItemId);
    }

    [Test]
    public void StartPreview_WithInvalidItemId_ThrowsArgumentException()
    {
        var tracker = CreateTracker();

        Assert.Throws<ArgumentException>(() =>
            tracker.StartPreview(0, AccessoryType.Headwear, -1));
        Assert.Throws<ArgumentException>(() =>
            tracker.StartPreview(-1, AccessoryType.Headwear, -1));
    }

    [Test]
    public void StartPreview_WithNoneSlot_ThrowsArgumentException()
    {
        var tracker = CreateTracker();

        Assert.Throws<ArgumentException>(() =>
            tracker.StartPreview(42, AccessoryType.None, -1));
    }

    [Test]
    public void StartPreview_SwappingToSameSlot_UpdatesItemId()
    {
        var tracker = CreateTracker();

        tracker.StartPreview(42, AccessoryType.Headwear, 10);
        tracker.StartPreview(99, AccessoryType.Headwear, 10);

        Assert.AreEqual(99, tracker.PreviewedItemId);
        Assert.AreEqual(AccessoryType.Headwear, tracker.PreviewedSlot);
        Assert.AreEqual(10, tracker.PreviouslyEquippedItemId);
    }

    [Test]
    public void StartPreview_SwappingToDifferentSlot_ClearsPreviousSlot()
    {
        var tracker = CreateTracker();

        tracker.StartPreview(42, AccessoryType.Headwear, 10);
        tracker.StartPreview(99, AccessoryType.MainHand, 5);

        Assert.AreEqual(99, tracker.PreviewedItemId);
        Assert.AreEqual(AccessoryType.MainHand, tracker.PreviewedSlot);
        Assert.AreEqual(5, tracker.PreviouslyEquippedItemId);
    }

    [Test]
    public void ClearPreview_WhenActive_ReturnsClearResultWithRestoreInfo()
    {
        var tracker = CreateTracker();
        tracker.StartPreview(42, AccessoryType.Headwear, 10);

        var result = tracker.ClearPreview();

        Assert.IsTrue(result.WasActive);
        Assert.AreEqual(AccessoryType.Headwear, result.ClearedSlot);
        Assert.AreEqual(42, result.ClearedItemId);
        Assert.AreEqual(10, result.RestoreItemId);
        Assert.IsFalse(tracker.IsPreviewActive);
    }

    [Test]
    public void ClearPreview_WhenNoPreviousEquipped_ReturnsNegativeRestoreId()
    {
        var tracker = CreateTracker();
        tracker.StartPreview(42, AccessoryType.Headwear, -1);

        var result = tracker.ClearPreview();

        Assert.IsTrue(result.WasActive);
        Assert.AreEqual(-1, result.RestoreItemId);
    }

    [Test]
    public void ClearPreview_WhenNotActive_ReturnsNotActive()
    {
        var tracker = CreateTracker();

        var result = tracker.ClearPreview();

        Assert.IsFalse(result.WasActive);
    }

    [Test]
    public void IsItemPreviewed_WhenMatchingItem_ReturnsTrue()
    {
        var tracker = CreateTracker();
        tracker.StartPreview(42, AccessoryType.Headwear, -1);

        Assert.IsTrue(tracker.IsItemPreviewed(42));
    }

    [Test]
    public void IsItemPreviewed_WhenDifferentItem_ReturnsFalse()
    {
        var tracker = CreateTracker();
        tracker.StartPreview(42, AccessoryType.Headwear, -1);

        Assert.IsFalse(tracker.IsItemPreviewed(99));
    }

    [Test]
    public void IsItemPreviewed_WhenNoPreview_ReturnsFalse()
    {
        var tracker = CreateTracker();

        Assert.IsFalse(tracker.IsItemPreviewed(42));
    }

    [Test]
    public void PreviewChanged_FiredOnStartPreview()
    {
        var tracker = CreateTracker();
        int fireCount = 0;
        tracker.PreviewChanged += () => fireCount++;

        tracker.StartPreview(42, AccessoryType.Headwear, -1);

        Assert.AreEqual(1, fireCount);
    }

    [Test]
    public void PreviewChanged_FiredOnClearPreview()
    {
        var tracker = CreateTracker();
        tracker.StartPreview(42, AccessoryType.Headwear, -1);
        int fireCount = 0;
        tracker.PreviewChanged += () => fireCount++;

        tracker.ClearPreview();

        Assert.AreEqual(1, fireCount);
    }

    [Test]
    public void PreviewChanged_NotFiredOnClearWhenNoPreview()
    {
        var tracker = CreateTracker();
        int fireCount = 0;
        tracker.PreviewChanged += () => fireCount++;

        tracker.ClearPreview();

        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void StartPreview_SwappingDifferentSlot_FiresPreviewChangedOnce()
    {
        var tracker = CreateTracker();
        tracker.StartPreview(42, AccessoryType.Headwear, -1);
        int fireCount = 0;
        tracker.PreviewChanged += () => fireCount++;

        tracker.StartPreview(99, AccessoryType.MainHand, 5);

        Assert.AreEqual(1, fireCount);
    }
}
