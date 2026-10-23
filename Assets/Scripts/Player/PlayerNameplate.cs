using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using TMPro;

public class PlayerNameplate : NetworkBehaviour
{
    private const float FADE_START_DISTANCE = 15f;
    private const float FADE_END_DISTANCE = 20f;

    [SerializeField] private TextMeshPro nameplateLabel;

    public NetworkVariable<FixedString64Bytes> Username = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Username.OnValueChanged += OnUsernameChanged;

        if (IsOwner)
        {
            string localUsername = NameplateDisplayFormatter.TruncateToUtf8ByteLimit(
                TokenAPI.Instance.GetUsername(), 63);
            Username.Value = new FixedString64Bytes(localUsername);
        }
        else
        {
            UpdateNameplateDisplay(Username.Value.ToString());
        }
    }

    public override void OnNetworkDespawn()
    {
        Username.OnValueChanged -= OnUsernameChanged;
        base.OnNetworkDespawn();
    }

    private void OnUsernameChanged(FixedString64Bytes previousValue, FixedString64Bytes currentValue)
    {
        UpdateNameplateDisplay(currentValue.ToString());
    }

    private void UpdateNameplateDisplay(string username)
    {
        if (nameplateLabel == null)
            throw new InvalidOperationException("PlayerNameplate: nameplateLabel is not assigned");

        bool isHostPlayer = NetworkManager.Singleton != null
            && OwnerClientId == NetworkManager.ServerClientId;

        nameplateLabel.text = NameplateDisplayFormatter.FormatDisplayName(username, isHostPlayer);
        Color displayColor = NameplateDisplayFormatter.GetDisplayColor(isHostPlayer);
        nameplateLabel.color = new Color(displayColor.r, displayColor.g, displayColor.b, nameplateLabel.color.a);
    }

    private void LateUpdate()
    {
        if (nameplateLabel == null) return;
        if (Camera.main == null) return;

        nameplateLabel.transform.LookAt(
            nameplateLabel.transform.position + Camera.main.transform.rotation * Vector3.forward,
            Camera.main.transform.rotation * Vector3.up
        );

        float distanceToCamera = Vector3.Distance(transform.position, Camera.main.transform.position);
        float alpha = NameplateDisplayFormatter.ComputeAlpha(distanceToCamera, FADE_START_DISTANCE, FADE_END_DISTANCE);
        Color currentColor = nameplateLabel.color;
        nameplateLabel.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha);
    }
}
