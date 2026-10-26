using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Identity;
using Unity.Netcode;
using UnityEngine;

namespace MultiplayerLiveOpsToolkit.Netcode
{
    /// <summary>The same peer/session owner gates equipment before spawn and subsequent publication.</summary>
    public sealed partial class NetcodeIdentityGate
    {
        private const int MAX_EQUIPMENT_PROOF_BYTES = 32768;
        private const string EQUIPMENT_CHALLENGE = "liveops.equipment.challenge.v1";
        private const string EQUIPMENT_PROOF = "liveops.equipment.proof.v1";
        private const string EQUIPMENT_ACCEPTED = "liveops.equipment.accepted.v1";
        private long lastAcceptedEquipmentRevision = -1;
        private bool hasPendingEquipmentPublication;
        private const string EQUIPMENT_REQUEST = "liveops.equipment.request.v1";
        private readonly Dictionary<ulong, IdentityChallenge> equipmentChallenges = new Dictionary<ulong, IdentityChallenge>();
        private readonly Dictionary<ulong, PlayerIdentity> pendingEquipmentIdentities = new Dictionary<ulong, PlayerIdentity>();
        private readonly Dictionary<ulong, EquipmentLoadout> equipmentByPeer = new Dictionary<ulong, EquipmentLoadout>();
        private EquipmentProofVerifier equipmentVerifier;
        private IEquipmentProofBackend equipmentBackend;
        private IEquipmentService equipmentService;
        private Stack<IEnumerator> equipmentRequestStack;
        private long lastRequestedRevision = -1;
        public event Action<ulong, EquipmentLoadout> EquipmentVerified;
        public void ReportLocalConfigurationFailure(string reason)
        { if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Configuration diagnostic required.", nameof(reason)); FailLocal(reason); }
        public bool EquipmentEnabled => equipmentVerifier != null;
        public bool TryGetEquipment(ulong peerId, out EquipmentLoadout loadout) => equipmentByPeer.TryGetValue(peerId, out loadout);
        public void ConfigureEquipment(EquipmentProofVerifier proofVerifier, IEquipmentProofBackend proofBackend, IEquipmentService service = null)
        {
            if (manager.IsListening || manager.ShutdownInProgress) throw new InvalidOperationException("Configure equipment before starting a network session.");
            equipmentVerifier = proofVerifier ?? throw new ArgumentNullException(nameof(proofVerifier));
            equipmentBackend = proofBackend;
            equipmentService = service;
        }
        private void RegisterEquipmentMessages()
        {
            if (!EquipmentEnabled) return; // Explicitly disabled feature has no handlers/subscriptions.
            messages.RegisterNamedMessageHandler(EQUIPMENT_CHALLENGE, OnEquipmentChallenge);
            messages.RegisterNamedMessageHandler(EQUIPMENT_PROOF, OnEquipmentProof);
            messages.RegisterNamedMessageHandler(EQUIPMENT_REQUEST, OnEquipmentRequest);
            messages.RegisterNamedMessageHandler(EQUIPMENT_ACCEPTED, OnEquipmentAccepted);
            if (equipmentService != null) equipmentService.StateChanged += OnEquipmentStateChanged;
        }
        public void RequestEquipmentPublication()
        {
            if (!EquipmentEnabled) throw new InvalidOperationException("Configure equipment verification before requesting publication.");
            if (!manager.IsClient || !manager.IsListening || !IsLocalVerified) return; // Initial handshake owns the first publication.
            if (equipmentRequestStack != null) { hasPendingEquipmentPublication = true; return; }
            hasPendingEquipmentPublication = false;
            SendText(EQUIPMENT_REQUEST, NetworkManager.ServerClientId, "1");
        }
        private void OnEquipmentStateChanged()
        {
            if (!equipmentService.HasSnapshot || equipmentService.IsBusy || equipmentService.RequiresRefresh) return;
            long revision = equipmentService.Snapshot.Loadout.Revision;
            if (revision == lastRequestedRevision) return;
            lastRequestedRevision = revision;
            RequestEquipmentPublication();
        }
        private void OnEquipmentRequest(ulong peerId, FastBufferReader reader)
        {
            if (!manager.IsServer) return;
            if (!identitiesByPeer.ContainsKey(peerId)) { RejectPeer(peerId); return; }
            try { if (ReadText(reader, 1) != "1") { RejectPeer(peerId); return; } }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException) { RejectPeer(peerId); return; }
            // Coalesce requests while the backend is signing a current snapshot; no mutation is retried.
            if (!equipmentChallenges.ContainsKey(peerId)) SendEquipmentChallenge(peerId);
        }
        private void SendEquipmentChallenge(ulong peerId)
        {
            var challenge = IdentityChallenge.Create(sessionId, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timeoutSeconds);
            equipmentChallenges.Add(peerId, challenge);
            SendText(EQUIPMENT_CHALLENGE, peerId, challenge.SessionId + ":" + challenge.Nonce);
        }
        private void OnEquipmentChallenge(ulong senderId, FastBufferReader reader)
        {
            if (!manager.IsClient || senderId != NetworkManager.ServerClientId) return;
            if (equipmentBackend == null || equipmentRequestStack != null) { FailLocal("Equipment proof capability or phase is invalid."); return; }
            string text;
            try
            {
                text = ReadText(reader, 200);
                if (text.Length != 76 || text[32] != ':' || !Guid.TryParseExact(text.Substring(0, 32), "N", out _)) throw new FormatException();
                new IdentityChallenge(text.Substring(0, 32), text.Substring(33), DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timeoutSeconds);
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException) { FailLocal("Malformed equipment challenge."); return; }
            var operations = new Stack<IEnumerator>();
            operations.Push(RequestEquipmentProof(text.Substring(0, 32), text.Substring(33), generation));
            equipmentRequestStack = operations;
            StartCoroutine(DriveEquipmentRequest(operations));
        }
        private IEnumerator RequestEquipmentProof(string challengeSession, string nonce, int requestGeneration)
        {
            string assertion = null; IdentityProofRequestFailure? failure = null; int completions = 0;
            yield return equipmentBackend.RequestProof(challengeSession, nonce,
                value => { if (requestGeneration == generation) { assertion = value; completions++; } },
                error => { if (requestGeneration == generation) { failure = error; completions++; } }) ?? throw new InvalidOperationException("Equipment proof backend returned no operation.");
            if (requestGeneration != generation || !manager.IsListening) yield break;
            if (completions != 1 || failure.HasValue || string.IsNullOrEmpty(assertion) || assertion.Length > MAX_EQUIPMENT_PROOF_BYTES)
            { FailLocal("Equipment proof could not be confirmed. Check backend availability and configuration."); yield break; }
            SendText(EQUIPMENT_PROOF, NetworkManager.ServerClientId, assertion);
        }
        private void OnEquipmentProof(ulong peerId, FastBufferReader reader)
        {
            if (!manager.IsServer) return;
            if (!equipmentChallenges.TryGetValue(peerId, out var challenge)) { RejectPeer(peerId); return; }
            equipmentChallenges.Remove(peerId);
            bool initial = pendingEquipmentIdentities.TryGetValue(peerId, out var identity);
            if (!initial && !identitiesByPeer.TryGetValue(peerId, out identity)) { RejectPeer(peerId); return; }
            string assertion;
            try { assertion = ReadText(reader, MAX_EQUIPMENT_PROOF_BYTES); }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException) { RejectPeer(peerId); return; }
            equipmentByPeer.TryGetValue(peerId, out var previous);
            if (!equipmentVerifier.TryVerify(assertion, challenge, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), identity.PlayerId, previous, out var loadout))
            { RejectPeer(peerId); return; }
            equipmentByPeer[peerId] = loadout;
            SendText(EQUIPMENT_ACCEPTED, peerId, loadout.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { EquipmentVerified?.Invoke(peerId, loadout); }
            catch (Exception) { RejectPeer(peerId); return; }
            if (initial) { pendingEquipmentIdentities.Remove(peerId); CompletePeerVerification(peerId, identity); }
        }
        private void OnEquipmentAccepted(ulong senderId, FastBufferReader reader)
        {
            if (!manager.IsClient || senderId != NetworkManager.ServerClientId) return;
            try
            {
                string value = ReadText(reader, 20);
                if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long revision) || revision < lastAcceptedEquipmentRevision || revision >= long.MaxValue) throw new FormatException();
                lastAcceptedEquipmentRevision = revision;
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException) { FailLocal("Malformed equipment publication acknowledgement."); return; }
            RequestNewerEquipmentIfNeeded();
        }
        private void RequestNewerEquipmentIfNeeded()
        {
            if (equipmentService != null && equipmentService.HasSnapshot && !equipmentService.IsBusy && !equipmentService.RequiresRefresh
                && equipmentService.Snapshot.Loadout.Revision > lastAcceptedEquipmentRevision) RequestEquipmentPublication();
        }
        private IEnumerator DriveEquipmentRequest(Stack<IEnumerator> operations)
        {
            try
            {
                while (equipmentRequestStack == operations && operations.Count > 0)
                {
                    var operation = operations.Peek(); bool hasNext = false; bool hasFailed = false; object instruction = null;
                    try { hasNext = operation.MoveNext(); if (hasNext) instruction = operation.Current; } catch (Exception) { hasFailed = true; }
                    if (hasFailed) { FailLocal("Equipment proof operation failed."); yield break; }
                    if (equipmentRequestStack != operations) yield break;
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally
            {
                bool wasCurrent = equipmentRequestStack == operations;
                if (wasCurrent) equipmentRequestStack = null;
                DisposeOperations(operations);
                // Host loopback delivers acknowledgements synchronously inside SendText. A
                // follow-up challenge must wait until the completed proof runner releases ownership.
                if (wasCurrent && hasPendingEquipmentPublication) RequestEquipmentPublication();
            }
        }
        private void ExpireEquipmentChallenges(long now)
        {
            expiredPeers.Clear();
            foreach (var pending in equipmentChallenges) if (now >= pending.Value.ExpiresAt) expiredPeers.Add(pending.Key);
            foreach (ulong peerId in expiredPeers) RejectPeer(peerId);
        }
        private void ClearEquipmentPeer(ulong peerId)
        { equipmentChallenges.Remove(peerId); pendingEquipmentIdentities.Remove(peerId); equipmentByPeer.Remove(peerId); }
        private void ClearEquipmentSession()
        {
            equipmentChallenges.Clear(); pendingEquipmentIdentities.Clear(); equipmentByPeer.Clear(); lastRequestedRevision = -1; lastAcceptedEquipmentRevision = -1; hasPendingEquipmentPublication = false;
            if (equipmentService != null) equipmentService.StateChanged -= OnEquipmentStateChanged;
            if (messages != null && EquipmentEnabled)
            { messages.UnregisterNamedMessageHandler(EQUIPMENT_CHALLENGE); messages.UnregisterNamedMessageHandler(EQUIPMENT_PROOF); messages.UnregisterNamedMessageHandler(EQUIPMENT_REQUEST); messages.UnregisterNamedMessageHandler(EQUIPMENT_ACCEPTED); }
            var operations = equipmentRequestStack; equipmentRequestStack = null;
            if (operations != null) DisposeOperations(operations);
        }
    }
}
