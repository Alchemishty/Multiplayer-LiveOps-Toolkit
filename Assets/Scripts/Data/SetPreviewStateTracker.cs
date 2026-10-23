using System;
using System.Collections.Generic;

public class SetPreviewStateTracker
{
    private int previewedSetId = -1;
    private readonly Dictionary<AccessoryType, SetPreviewSlot> previewedSlots = new();

    public bool IsSetPreviewActive => previewedSetId > 0;
    public int PreviewedSetId => previewedSetId;

    public event Action SetPreviewChanged;

    public void StartSetPreview(int setId, List<SetPreviewSlotInput> slots)
    {
        if (setId <= 0)
            throw new ArgumentException("setId must be positive", nameof(setId));
        if (slots == null || slots.Count == 0)
            throw new ArgumentException("slots must not be null or empty", nameof(slots));

        previewedSetId = setId;
        previewedSlots.Clear();

        foreach (var slotInput in slots)
        {
            previewedSlots[slotInput.slot] = new SetPreviewSlot
            {
                previewedItemId = slotInput.itemId,
                previouslyEquippedItemId = slotInput.currentEquippedItemId,
            };
        }

        SetPreviewChanged?.Invoke();
    }

    public ClearSetResult ClearSetPreview()
    {
        if (!IsSetPreviewActive)
            return ClearSetResult.NoPreviewActive;

        var clearedSlots = new List<ClearedSlotInfo>();
        foreach (var kvp in previewedSlots)
        {
            clearedSlots.Add(new ClearedSlotInfo
            {
                clearedSlot = kvp.Key,
                clearedItemId = kvp.Value.previewedItemId,
                restoreItemId = kvp.Value.previouslyEquippedItemId,
            });
        }

        var result = new ClearSetResult
        {
            WasActive = true,
            ClearedSetId = previewedSetId,
            ClearedSlots = clearedSlots,
        };

        previewedSetId = -1;
        previewedSlots.Clear();
        SetPreviewChanged?.Invoke();

        return result;
    }

    public IReadOnlyDictionary<AccessoryType, SetPreviewSlot> GetPreviewedSlots()
    {
        return previewedSlots;
    }

    public struct SetPreviewSlot
    {
        public int previewedItemId;
        public int previouslyEquippedItemId;
    }

    public struct SetPreviewSlotInput
    {
        public AccessoryType slot;
        public int itemId;
        public int currentEquippedItemId;
    }

    public struct ClearedSlotInfo
    {
        public AccessoryType clearedSlot;
        public int clearedItemId;
        public int restoreItemId;
    }

    public struct ClearSetResult
    {
        public bool WasActive;
        public int ClearedSetId;
        public List<ClearedSlotInfo> ClearedSlots;

        public static ClearSetResult NoPreviewActive => new() { WasActive = false };
    }
}
