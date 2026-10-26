using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using UnityEngine;

/// <summary>Legacy demo locator; session-local registry behavior is supplied by the package.</summary>
public class ClientPlayerAvatars : AvatarRegistry
{
    public static ClientPlayerAvatars Instance { get; private set; }
    public GameObject currentClientAvatar => LocalAvatar;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    public void RemoveAvatar(ulong peerId)
    {
        var avatar = GetAvatar(peerId);
        if (avatar != null) RemoveAvatar(peerId, avatar);
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
}
