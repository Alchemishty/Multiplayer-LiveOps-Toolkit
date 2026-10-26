using System;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json.Linq;

namespace MultiplayerLiveOpsToolkit.Equipment
{
    /// <summary>Validates private equipment proofs before publishing sanitized public loadouts.</summary>
    public sealed class EquipmentProofVerifier
    {
        private readonly SignedPlayerProofVerifier verifier;
        public EquipmentProofVerifier(IdentityProofVerifier trust)
        { verifier = (trust ?? throw new ArgumentNullException(nameof(trust))).PlayerProofs; }
        public bool TryVerify(string assertion, IdentityChallenge challenge, long now, string playerId, EquipmentLoadout previous, out EquipmentLoadout loadout)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Verified peer identity required.", nameof(playerId));
            EquipmentLoadout candidate = null;
            bool valid = verifier.TryVerify(assertion, "liveops-equipment+jwt", 2, 32768, claims =>
            {
                if ((string)claims["sub"] != playerId || claims["revision"]?.Type != JTokenType.Integer || !(claims["items"] is JObject items)) return false;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in items.Properties())
                {
                    if (item.Value.Type != JTokenType.String) return false;
                    values.Add(item.Name, (string)item.Value);
                }
                candidate = new EquipmentLoadout((long)claims["revision"], values);
                if (previous != null)
                {
                    if (candidate.Revision < previous.Revision) return false;
                    if (candidate.Revision == previous.Revision)
                    {
                        if (candidate.ItemsBySlot.Count != previous.ItemsBySlot.Count) return false;
                        foreach (var item in candidate.ItemsBySlot)
                            if (!previous.ItemsBySlot.TryGetValue(item.Key, out string id) || id != item.Value) return false;
                    }
                }
                return true;
            }, challenge, now, out _, out _);
            loadout = valid ? candidate : null;
            return valid;
        }
    }
}
