using MultiplayerLiveOpsToolkit.Netcode;
using System;
using MultiplayerLiveOpsToolkit.Identity;
using Unity.Netcode;
using UnityEngine;

/// <summary>Sample player composition; the verification gate itself has no prefab/UI dependency.</summary>
namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
[RequireComponent(typeof(NetcodeIdentityGate))]
public sealed class VerifiedPlayerSpawner : MonoBehaviour
{
    private NetcodeIdentityGate gate;
    private NetworkManager manager;
    private void Awake()
    {
        gate = GetComponent<NetcodeIdentityGate>();
        manager = GetComponent<NetworkManager>();
    }
    private void OnEnable() { gate.PeerVerified += SpawnVerifiedPlayer; gate.EquipmentVerified += PublishEquipment; }
    private void OnDisable() { gate.PeerVerified -= SpawnVerifiedPlayer; gate.EquipmentVerified -= PublishEquipment; }
    private bool IsVerifiedObserver(ulong peerId) => gate.TryGetIdentity(peerId, out _);
    private void SpawnVerifiedPlayer(ulong peerId, PlayerIdentity identity)
    {
        if (!manager.IsServer || !gate.TryGetIdentity(peerId, out var verified) || !ReferenceEquals(verified, identity))
            throw new InvalidOperationException("VerifiedPlayerSpawner requires a server-verified peer identity.");
        var prefab = manager.NetworkConfig.PlayerPrefab;
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<NetworkPlayerIdentity>() == null)
            throw new InvalidOperationException("VerifiedPlayerSpawner requires a player prefab with NetworkObject and NetworkPlayerIdentity.");
        if (!manager.ConnectedClients.TryGetValue(peerId, out var peer) || peer.PlayerObject != null)
            throw new InvalidOperationException("VerifiedPlayerSpawner requires a connected peer without a player object.");
        var player = Instantiate(prefab);
        try
        {
            player.GetComponent<NetworkPlayerIdentity>().InitializeVerifiedIdentity(identity);
            if (gate.EquipmentEnabled)
            {
                if (!gate.TryGetEquipment(peerId, out var loadout)) throw new InvalidOperationException("Verified player requires approved equipment before spawn.");
                var equipment = player.GetComponent<PlayerAccessory>() ?? throw new InvalidOperationException("Equipment-enabled player prefab requires PlayerAccessory.");
                equipment.InitializeVerifiedEquipment(loadout);
            }
            player.SetActive(true);
            var playerNetworkObject = player.GetComponent<NetworkObject>();
            playerNetworkObject.CheckObjectVisibility = IsVerifiedObserver;
            playerNetworkObject.SpawnAsPlayerObject(peerId, true);
            // The newly verified peer can now see existing verified players as well.
            foreach (var existing in manager.SpawnManager.SpawnedObjectsList)
                if (existing != playerNetworkObject && existing.GetComponent<NetworkPlayerIdentity>() != null && !existing.IsNetworkVisibleTo(peerId))
                    existing.NetworkShow(peerId);
        }
        catch
        {
            Destroy(player);
            throw;
        }
    }
    private void PublishEquipment(ulong peerId, MultiplayerLiveOpsToolkit.Equipment.EquipmentLoadout loadout)
    {
        if (!manager.ConnectedClients.TryGetValue(peerId, out var peer)) throw new InvalidOperationException("Equipment publication requires a connected peer.");
        if (peer.PlayerObject == null) return; // Initial verified state is installed by SpawnVerifiedPlayer.
        var equipment = peer.PlayerObject.GetComponent<PlayerAccessory>() ?? throw new InvalidOperationException("Equipment-enabled player requires PlayerAccessory.");
        equipment.PublishVerifiedEquipment(loadout);
    }
}

}
