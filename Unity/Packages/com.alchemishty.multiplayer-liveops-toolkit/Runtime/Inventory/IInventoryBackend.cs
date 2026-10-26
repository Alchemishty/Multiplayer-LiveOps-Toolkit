using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Inventory
{
    public interface IInventoryBackend
    {
        IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure);
    }
}
