using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using MultiplayerLiveOpsToolkit.Netcode;
using System;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using Unity.Netcode;
using UnityEngine;

/// <summary>Replicates only gate-approved equipment; local previews never write network state.</summary>
namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
public class PlayerAccessory : NetworkBehaviour
{
    private bool hasEquipmentBinding;
    private EquipmentLoadout initialLoadout;
    private EquipmentLoadout publicLoadout;
    private IEquipmentService presentation;
    private EquipmentVisualPresenter visualPresenter;
    public event Action EquippedStateChanged;
    public NetworkVariable<AccessoryState> AccessoryJson = new NetworkVariable<AccessoryState>(new AccessoryState(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public void InitializeVerifiedEquipment(EquipmentLoadout loadout)
    {
        var networkObject = GetComponent<NetworkObject>() ?? throw new InvalidOperationException("PlayerAccessory requires NetworkObject.");
        if (networkObject.IsSpawned || initialLoadout != null) throw new InvalidOperationException("Initialize verified equipment once before spawning.");
        initialLoadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        var gate = NetworkManager.GetComponent<NetcodeIdentityGate>() ?? throw new InvalidOperationException("Player equipment requires the verification gate.");
        if (!gate.EquipmentEnabled) return;
        hasEquipmentBinding = true;
        if (IsServer)
        {
            if (initialLoadout == null) throw new InvalidOperationException("PlayerAccessory requires backend-verified equipment before spawn.");
            PublishVerifiedEquipment(initialLoadout);
        }
        AccessoryJson.OnValueChanged += OnPublicStateChanged;
        ReadPublicState(AccessoryJson.Value.Json);
    }
    public override void OnNetworkDespawn()
    {
        if (hasEquipmentBinding) AccessoryJson.OnValueChanged -= OnPublicStateChanged;
        hasEquipmentBinding = false;
        UnbindPresentation();
        initialLoadout = null; publicLoadout = null;
        base.OnNetworkDespawn();
    }
    public override void OnDestroy()
    {
        try { UnbindPresentation(); }
        finally { base.OnDestroy(); }
    }
    public void PublishVerifiedEquipment(EquipmentLoadout loadout)
    {
        if (!IsSpawned || !IsServer) throw new InvalidOperationException("Only the spawned server can publish equipment.");
        var gate = NetworkManager.GetComponent<NetcodeIdentityGate>() ?? throw new InvalidOperationException("Equipment publication requires the peer verification gate.");
        if (!gate.TryGetEquipment(OwnerClientId, out var approved) || !ReferenceEquals(approved, loadout)) throw new InvalidOperationException("Equipment publication requires this peer's current verified snapshot.");
        AccessoryJson.Value = new AccessoryState(EquipmentPublicState.Serialize(loadout));
    }
    public void BindPresentation(IEquipmentService service)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        UnbindPresentation(); presentation = service;
        EnsureVisualPresenter();
        if (publicLoadout != null) visualPresenter.ShowPublic(publicLoadout);
        visualPresenter.Bind(presentation);
    }
    public void UnbindPresentation()
    {
        if (visualPresenter != null) visualPresenter.Dispose();
        visualPresenter = null;
        presentation = null;
    }
    private void OnPublicStateChanged(AccessoryState previous, AccessoryState current) => ReadPublicState(current.Json);
    private void ReadPublicState(string json)
    {
        var received = EquipmentPublicState.Parse(json);
        if (publicLoadout != null && received.Revision < publicLoadout.Revision) throw new InvalidOperationException("Replicated equipment revision moved backwards.");
        publicLoadout = received;
        if (IsClient) RenderPresentation();
        var subscribers = EquippedStateChanged;
        if (subscribers != null) foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void RenderPresentation()
    {
        if (!IsSpawned || !IsClient || publicLoadout == null) return; // No public presentation exists before spawn or on a headless server.
        try
        {
            EnsureVisualPresenter();
            visualPresenter.ShowPublic(publicLoadout);
            if (presentation != null) visualPresenter.Bind(presentation);
        }
        catch (InvalidOperationException exception) { ReportVisualFailure(exception.Message); }
    }
    private void EnsureVisualPresenter()
    {
        if (visualPresenter != null) return;
        var renderer = GetComponent<IEquipmentRenderer>() ?? throw new InvalidOperationException("Equipment visuals require IEquipmentRenderer.");
        visualPresenter = new EquipmentVisualPresenter(renderer, ReportVisualFailure);
    }
    private void ReportVisualFailure(string message)
    {
        var gate = NetworkManager.GetComponent<NetcodeIdentityGate>() ?? throw new InvalidOperationException("Equipment failure requires the verification gate.");
        gate.ReportLocalConfigurationFailure(message);
    }
}

}
