using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Inventory
{
    public interface IInventoryService
    {
        bool HasSnapshot { get; }
        bool IsBusy { get; }
        InventorySnapshot Snapshot { get; }
        event Action SnapshotChanged;
        IEnumerator Refresh(Action onSuccess, Action<InventoryError> onFailure);
    }
}
