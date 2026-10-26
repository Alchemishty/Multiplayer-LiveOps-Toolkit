using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using TMPro;
using MultiplayerLiveOpsToolkit.Identity;

[RequireComponent(typeof(NetworkPlayerIdentity))]
public class PlayerNameplate : NetworkBehaviour
{
    private const float FADE_START_DISTANCE = 15f;
    private const float FADE_END_DISTANCE = 20f;

    [SerializeField] private TextMeshPro nameplateLabel;

    private Camera displayCamera;
    private NetworkPlayerIdentity publicIdentity;
    public NetworkVariable<FixedString4096Bytes> Username => Identity.Username;
    public NetworkVariable<FixedString512Bytes> PlayerId => Identity.PlayerId;
    private NetworkPlayerIdentity Identity => publicIdentity != null ? publicIdentity : publicIdentity = GetComponent<NetworkPlayerIdentity>();
    public void InitializeVerifiedIdentity(PlayerIdentity identity) => Identity.InitializeVerifiedIdentity(identity);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsClient)
        {
            if (nameplateLabel == null) throw new InvalidOperationException("PlayerNameplate: nameplateLabel is not assigned.");
            nameplateLabel.richText = false;
            displayCamera = Camera.main;
            Username.OnValueChanged += OnUsernameChanged;
            UpdateNameplateDisplay(Username.Value.ToString());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsClient) Username.OnValueChanged -= OnUsernameChanged;
        base.OnNetworkDespawn();
    }

    private void OnUsernameChanged(FixedString4096Bytes previousValue, FixedString4096Bytes currentValue)
    {
        UpdateNameplateDisplay(currentValue.ToString());
    }

    private void UpdateNameplateDisplay(string username)
    {
        if (nameplateLabel == null)
            throw new InvalidOperationException("PlayerNameplate: nameplateLabel is not assigned");

        bool isHostPlayer = OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;

        nameplateLabel.text = NameplateDisplayFormatter.FormatDisplayName(username, isHostPlayer);
        Color displayColor = NameplateDisplayFormatter.GetDisplayColor(isHostPlayer);
        nameplateLabel.color = new Color(displayColor.r, displayColor.g, displayColor.b, nameplateLabel.color.a);
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsClient) return;
        if (displayCamera == null) displayCamera = Camera.main;
        if (displayCamera == null) return; // Headless/client scenes may intentionally have no presentation camera.

        nameplateLabel.transform.LookAt(
            nameplateLabel.transform.position + displayCamera.transform.rotation * Vector3.forward,
            displayCamera.transform.rotation * Vector3.up
        );

        float distanceToCamera = Vector3.Distance(transform.position, displayCamera.transform.position);
        float alpha = NameplateDisplayFormatter.ComputeAlpha(distanceToCamera, FADE_START_DISTANCE, FADE_END_DISTANCE);
        Color currentColor = nameplateLabel.color;
        nameplateLabel.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha);
    }
}
