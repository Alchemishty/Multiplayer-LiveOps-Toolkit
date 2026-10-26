using System;
using System.Collections;
using System.Collections.Generic;
namespace MultiplayerLiveOpsToolkit.Equipment
{
    public interface IEquipmentBackend
    {
        IEnumerator Fetch(Action<EquipmentState> onSuccess, Action<EquipmentError> onFailure);
        IEnumerator Replace(IReadOnlyList<string> itemIds, long expectedRevision, Action<EquipmentState> onSuccess, Action<EquipmentError> onFailure);
    }
}
