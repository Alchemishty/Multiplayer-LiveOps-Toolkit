using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
    /// <summary>One network session's avatar lookup; late despawns cannot unregister replacements.</summary>
    public class AvatarRegistry : MonoBehaviour
    {
        private readonly Dictionary<ulong, GameObject> avatarsByPeerId = new Dictionary<ulong, GameObject>();
        private ulong? localPeerId;
        public GameObject LocalAvatar { get; private set; }
        public event Action LocalAvatarChanged;

        public void RegisterAvatar(ulong peerId, GameObject avatar, bool isOwner)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            if (isOwner && localPeerId.HasValue && localPeerId.Value != peerId)
                throw new InvalidOperationException("Remove the previous local avatar before registering a different local peer.");
            avatarsByPeerId[peerId] = avatar;
            if (isOwner && LocalAvatar != avatar)
            {
                localPeerId = peerId;
                LocalAvatar = avatar;
                NotifyLocalAvatarChanged();
            }
        }
        public void RemoveAvatar(ulong peerId, GameObject expectedAvatar)
        {
            if (expectedAvatar == null) throw new ArgumentNullException(nameof(expectedAvatar));
            if (!avatarsByPeerId.TryGetValue(peerId, out var current) || current != expectedAvatar) return; // A late despawn belongs to the previous avatar.
            avatarsByPeerId.Remove(peerId);
            if (localPeerId == peerId)
            {
                localPeerId = null; LocalAvatar = null;
                NotifyLocalAvatarChanged();
            }
        }
        public GameObject GetAvatar(ulong peerId) => avatarsByPeerId.TryGetValue(peerId, out var avatar) ? avatar : null;
        public void Clear()
        {
            bool hadLocalAvatar = localPeerId.HasValue;
            avatarsByPeerId.Clear(); localPeerId = null; LocalAvatar = null;
            if (hadLocalAvatar) NotifyLocalAvatarChanged();
        }
        private void NotifyLocalAvatarChanged()
        {
            var subscribers = LocalAvatarChanged;
            if (subscribers == null) return; // No view has selected avatar observation.
            foreach (Action subscriber in subscribers.GetInvocationList())
            { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
        }
        protected virtual void OnDisable() => Clear();
    }
}
