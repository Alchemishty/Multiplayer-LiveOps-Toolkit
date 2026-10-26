using System;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public sealed class PlayerIdentity
    {
        public string PlayerId { get; }
        public string DisplayName { get; }

        public PlayerIdentity(string playerId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player identity requires a stable backend player ID.", nameof(playerId));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Player identity requires a display name.", nameof(displayName));
            PlayerId = playerId;
            DisplayName = displayName;
        }
    }
}
