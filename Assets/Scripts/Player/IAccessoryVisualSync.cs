using UnityEngine;

public interface IAccessoryVisualSync
{
    GameObject Equip(int itemId, AccessoryType slotType, GameObject prefab, Transform avatarRoot);
    void Unequip(AccessoryType slotType);
    bool IsSlotActive(AccessoryType slotType);
    int GetActiveItemId(AccessoryType slotType);
}
