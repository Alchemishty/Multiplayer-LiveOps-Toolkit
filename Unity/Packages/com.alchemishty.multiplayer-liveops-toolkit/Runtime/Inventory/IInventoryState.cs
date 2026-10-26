namespace MultiplayerLiveOpsToolkit.Inventory
{
    /// <summary>Shop's confirmed-state write boundary. A null snapshot invalidates uncertain account state.</summary>
    public interface IInventoryState : IInventoryService
    {
        void BeginUpdate();
        void StageSnapshot(InventorySnapshot snapshot);
        void EndUpdate();
    }
}
