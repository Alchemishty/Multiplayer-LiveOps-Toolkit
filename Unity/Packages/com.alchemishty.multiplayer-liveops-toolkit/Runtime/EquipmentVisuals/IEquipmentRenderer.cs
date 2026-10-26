using MultiplayerLiveOpsToolkit.Equipment;
namespace MultiplayerLiveOpsToolkit.EquipmentVisuals
{
    public interface IEquipmentRenderer
    {
        void Render(EquipmentLoadout loadout);
        void Clear();
    }
}
