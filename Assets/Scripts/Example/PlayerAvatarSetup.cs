using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Placed on the player avatar prefab. Registers with ClientPlayerAvatars
/// on spawn and removes the registration on despawn.
/// </summary>
public class PlayerAvatarSetup : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ClientPlayerAvatars.Instance.RegisterAvatar(OwnerClientId, gameObject, IsOwner);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (ClientPlayerAvatars.Instance != null)
            ClientPlayerAvatars.Instance.RemoveAvatar(OwnerClientId);
    }
}
