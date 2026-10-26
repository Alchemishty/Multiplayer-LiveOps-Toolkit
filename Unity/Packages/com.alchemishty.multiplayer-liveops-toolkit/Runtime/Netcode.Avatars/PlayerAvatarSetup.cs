using System;
using Unity.Netcode;
namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
    public class PlayerAvatarSetup : NetworkBehaviour
    {
        private AvatarRegistry registry;
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            registry = NetworkManager.GetComponent<AvatarRegistry>() ?? throw new InvalidOperationException("PlayerAvatarSetup requires AvatarRegistry on its NetworkManager.");
            registry.RegisterAvatar(OwnerClientId, gameObject, IsClient && IsOwner);
        }
        public override void OnNetworkDespawn()
        {
            if (registry != null) registry.RemoveAvatar(OwnerClientId, gameObject);
            registry = null;
            base.OnNetworkDespawn();
        }
    }
}
