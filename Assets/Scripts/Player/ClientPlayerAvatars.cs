using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Tracks spawned player avatars by Netcode client ID. Provides quick access to
/// the local client's avatar for shop/mirror interactions.
///
/// Wire this to your player spawn system: call RegisterAvatar() when a player
/// prefab spawns, and RemoveAvatar() on despawn. The local client's avatar is
/// auto-detected via IsOwner on the spawned NetworkObject.
///
/// For a full VR-Network-Avatar implementation with hand tracking, IK, and
/// voice, see the companion repository (linked in the project README).
/// </summary>
public class ClientPlayerAvatars : MonoBehaviour
{
    public static ClientPlayerAvatars Instance { get; private set; }

    /// <summary>The local player's avatar. Set automatically on spawn.</summary>
    public GameObject currentClientAvatar;

    /// <summary>All connected players' avatars keyed by Netcode client ID.</summary>
    public Dictionary<ulong, GameObject> avatarsByClientId = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Call from your player prefab's OnNetworkSpawn. Registers the avatar
    /// and sets currentClientAvatar if this is the local player.
    /// </summary>
    public void RegisterAvatar(ulong clientId, GameObject avatar, bool isOwner)
    {
        avatarsByClientId[clientId] = avatar;
        if (isOwner)
            currentClientAvatar = avatar;
    }

    /// <summary>Call from your player prefab's OnNetworkDespawn.</summary>
    public void RemoveAvatar(ulong clientId)
    {
        if (avatarsByClientId.TryGetValue(clientId, out var avatar))
        {
            if (avatar == currentClientAvatar)
                currentClientAvatar = null;
            avatarsByClientId.Remove(clientId);
        }
    }

    public GameObject GetAvatar(ulong clientId)
    {
        avatarsByClientId.TryGetValue(clientId, out var avatar);
        return avatar;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
