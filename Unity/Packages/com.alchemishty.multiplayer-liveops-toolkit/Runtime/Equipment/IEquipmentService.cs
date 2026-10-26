using System;
using System.Collections;
using System.Collections.Generic;
namespace MultiplayerLiveOpsToolkit.Equipment
{
    public interface IEquipmentService
    {
        bool HasSnapshot { get; }
        bool IsBusy { get; }
        bool RequiresRefresh { get; }
        EquipmentState Snapshot { get; }
        EquipmentLoadout Presentation { get; }
        bool IsPreviewActive { get; }
        event Action StateChanged;
        event Action PreviewChanged;
        IEnumerator Refresh(Action onSuccess, Action<EquipmentError> onFailure);
        IEnumerator ApplyItems(IEnumerable<string> itemIds, Action onSuccess, Action<EquipmentError> onFailure);
        IEnumerator Unequip(IEnumerable<string> slotIds, Action onSuccess, Action<EquipmentError> onFailure);
        IEnumerator Clear(Action onSuccess, Action<EquipmentError> onFailure);
        void Preview(IEnumerable<string> itemIds);
        void ClearPreview();
    }
}
