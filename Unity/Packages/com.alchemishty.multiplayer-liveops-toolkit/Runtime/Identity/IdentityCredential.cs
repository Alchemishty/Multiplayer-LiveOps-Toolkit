using System;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public sealed class IdentityCredential
    {
        public string ProviderId { get; }
        public string Value { get; }

        public IdentityCredential(string providerId, string value)
        {
            if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Identity credential requires a provider ID.", nameof(providerId));
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Identity credential requires a value.", nameof(value));
            ProviderId = providerId;
            Value = value;
        }
    }
}
