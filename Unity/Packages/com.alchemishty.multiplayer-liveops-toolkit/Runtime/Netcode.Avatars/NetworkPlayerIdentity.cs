using System;
using MultiplayerLiveOpsToolkit.Identity;
using Unity.Collections;
using Unity.Netcode;

namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
    /// <summary>Backend-verified public identity replication, independent of nameplate/UI rendering.</summary>
    public sealed class NetworkPlayerIdentity : NetworkBehaviour
    {
        private PlayerIdentity verifiedIdentity;
        public NetworkVariable<FixedString4096Bytes> Username = new NetworkVariable<FixedString4096Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<FixedString512Bytes> PlayerId = new NetworkVariable<FixedString512Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public void InitializeVerifiedIdentity(PlayerIdentity identity)
        {
            var networkObject = GetComponent<NetworkObject>() ?? throw new InvalidOperationException("NetworkPlayerIdentity requires NetworkObject.");
            if (networkObject.IsSpawned || verifiedIdentity != null) throw new InvalidOperationException("Initialize verified identity once before spawn.");
            verifiedIdentity = identity ?? throw new ArgumentNullException(nameof(identity));
        }
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return; // Client identity comes exclusively from replicated server values.
            if (verifiedIdentity == null) throw new InvalidOperationException("NetworkPlayerIdentity requires verified identity before spawn.");
            Username.Value = new FixedString4096Bytes(verifiedIdentity.DisplayName);
            PlayerId.Value = new FixedString512Bytes(verifiedIdentity.PlayerId);
        }
        public override void OnNetworkDespawn()
        {
            verifiedIdentity = null;
            base.OnNetworkDespawn();
        }
    }
}
