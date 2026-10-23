using System;

public class PreviewStateTracker
{
    private AccessoryType previewedSlot = AccessoryType.None;
    private int previewedItemId = -1;
    private int previouslyEquippedItemId = -1;
    private AccessoryType displacedConflictSlot = AccessoryType.None;
    private int displacedConflictItemId = -1;

    public bool IsPreviewActive => previewedSlot != AccessoryType.None;
    public int PreviewedItemId => previewedItemId;
    public AccessoryType PreviewedSlot => previewedSlot;
    public int PreviouslyEquippedItemId => previouslyEquippedItemId;

    public event Action PreviewChanged;

    public void StartPreview(int itemId, AccessoryType slot, int currentEquippedItemId,
        AccessoryType conflictSlot = AccessoryType.None, int conflictItemId = -1)
    {
        if (itemId <= 0)
            throw new ArgumentException("itemId must be positive", nameof(itemId));
        if (slot == AccessoryType.None)
            throw new ArgumentException("slot cannot be None", nameof(slot));

        if (previewedSlot != AccessoryType.None && previewedSlot != slot)
            ClearPreviewState();

        previouslyEquippedItemId = currentEquippedItemId;
        previewedSlot = slot;
        previewedItemId = itemId;
        displacedConflictSlot = conflictSlot;
        displacedConflictItemId = conflictItemId;
        PreviewChanged?.Invoke();
    }

    public ClearResult ClearPreview()
    {
        if (!IsPreviewActive)
            return ClearResult.NoPreviewActive;

        var result = new ClearResult
        {
            WasActive = true,
            ClearedSlot = previewedSlot,
            ClearedItemId = previewedItemId,
            RestoreItemId = previouslyEquippedItemId,
            DisplacedConflictSlot = displacedConflictSlot,
            DisplacedConflictItemId = displacedConflictItemId
        };

        ClearPreviewState();
        PreviewChanged?.Invoke();
        return result;
    }

    public bool IsItemPreviewed(int itemId)
    {
        return IsPreviewActive && previewedItemId == itemId;
    }

    private void ClearPreviewState()
    {
        previewedSlot = AccessoryType.None;
        previewedItemId = -1;
        previouslyEquippedItemId = -1;
        displacedConflictSlot = AccessoryType.None;
        displacedConflictItemId = -1;
    }

    public struct ClearResult
    {
        public bool WasActive;
        public AccessoryType ClearedSlot;
        public int ClearedItemId;
        public int RestoreItemId;
        public AccessoryType DisplacedConflictSlot;
        public int DisplacedConflictItemId;

        public static ClearResult NoPreviewActive => new ClearResult { WasActive = false };
    }
}
