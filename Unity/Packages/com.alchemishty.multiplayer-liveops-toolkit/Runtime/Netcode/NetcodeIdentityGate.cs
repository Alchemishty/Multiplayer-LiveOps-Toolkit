using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MultiplayerLiveOpsToolkit.Identity;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MultiplayerLiveOpsToolkit.Netcode
{
    /// <summary>Owns the bounded unverified phase of one Netcode session. Never creates player objects.</summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed partial class NetcodeIdentityGate : MonoBehaviour
    {
        private const string CHALLENGE_MESSAGE = "liveops.identity.challenge.v1";
        private const string PROOF_MESSAGE = "liveops.identity.proof.v1";
        private const string VERIFIED_MESSAGE = "liveops.identity.verified.v1";
        private const int MAX_PENDING_PEERS = 32;
        private const int MAX_MESSAGE_BYTES = 8192;
        private readonly Dictionary<ulong, IdentityChallenge> challengesByPeer = new Dictionary<ulong, IdentityChallenge>();
        private readonly Dictionary<ulong, PlayerIdentity> identitiesByPeer = new Dictionary<ulong, PlayerIdentity>();
        private readonly List<ulong> expiredPeers = new List<ulong>();
        private NetworkManager manager;
        private CustomMessagingManager messages;
        private IdentityProofVerifier verifier;
        private IIdentityProofBackend backend;
        private string sessionId;
        private int timeoutSeconds;
        private int generation;
        private bool hasReceivedChallenge;
        private bool hasReportedFailure;
        private float clientDeadline;
        private Stack<IEnumerator> requestStack;
        public event Action<ulong, PlayerIdentity> PeerVerified;
        public event Action<string> LocalFailure;
        public bool IsLocalVerified { get; private set; }

        private void Awake() => manager = GetComponent<NetworkManager>();
        private void OnEnable()
        {
            manager.OnServerStarted += OnNetworkStarted;
            manager.OnClientStarted += OnNetworkStarted;
            manager.OnClientConnectedCallback += OnPeerConnected;
            manager.OnClientDisconnectCallback += OnPeerDisconnected;
            manager.OnServerStopped += OnNetworkStopped;
            manager.OnClientStopped += OnNetworkStopped;
        }
        private void OnDisable()
        {
            manager.OnServerStarted -= OnNetworkStarted;
            manager.OnClientStarted -= OnNetworkStarted;
            manager.OnClientConnectedCallback -= OnPeerConnected;
            manager.OnClientDisconnectCallback -= OnPeerDisconnected;
            manager.OnServerStopped -= OnNetworkStopped;
            manager.OnClientStopped -= OnNetworkStopped;
            try { ClearSession(); }
            finally
            {
                try { if (manager.IsListening) manager.Shutdown(); }
                finally { if (manager.ConnectionApprovalCallback == ApproveWithoutPlayer) manager.ConnectionApprovalCallback = null; }
            }
        }

        public bool TryGetIdentity(ulong peerId, out PlayerIdentity identity) => identitiesByPeer.TryGetValue(peerId, out identity);

        public bool StartHost(IdentityProofVerifier proofVerifier, IIdentityProofBackend proofBackend, int timeoutSeconds = 15)
        {
            if (proofVerifier == null) throw new ArgumentNullException(nameof(proofVerifier));
            if (proofBackend == null) throw new ArgumentNullException(nameof(proofBackend));
            Prepare(proofVerifier, proofBackend, timeoutSeconds);
            return manager.StartHost();
        }
        public bool StartServer(IdentityProofVerifier proofVerifier, int timeoutSeconds = 15)
        {
            if (proofVerifier == null) throw new ArgumentNullException(nameof(proofVerifier));
            Prepare(proofVerifier, null, timeoutSeconds);
            return manager.StartServer();
        }
        public bool StartClient(IIdentityProofBackend proofBackend, int timeoutSeconds = 15)
        {
            if (proofBackend == null) throw new ArgumentNullException(nameof(proofBackend));
            Prepare(null, proofBackend, timeoutSeconds);
            return manager.StartClient();
        }
        private void Prepare(IdentityProofVerifier proofVerifier, IIdentityProofBackend proofBackend, int seconds)
        {
            if (!isActiveAndEnabled || manager.IsListening || manager.ShutdownInProgress)
                throw new InvalidOperationException("Identity gate must be enabled with a stopped NetworkManager before starting a session.");
            if (seconds < 1 || seconds > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (manager.ConnectionApprovalCallback != null && manager.ConnectionApprovalCallback != ApproveWithoutPlayer)
                throw new InvalidOperationException("Identity gate requires ownership of Netcode connection approval.");
            ClearSession();
            verifier = proofVerifier;
            backend = proofBackend;
            timeoutSeconds = seconds;
            sessionId = Guid.NewGuid().ToString("N");
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = ApproveWithoutPlayer;
            hasReportedFailure = false;
        }
        private void ApproveWithoutPlayer(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Approved = verifier != null && challengesByPeer.Count + equipmentChallenges.Count < MAX_PENDING_PEERS && request.Payload.Length == 0;
            response.Pending = false;
            response.Reason = response.Approved ? "" : "Identity verification is unavailable.";
        }
        private void OnNetworkStarted()
        {
            if (messages != null) return; // Host raises both start events for one messaging manager.
            messages = manager.CustomMessagingManager ?? throw new InvalidOperationException("Netcode identity messaging is unavailable.");
            messages.RegisterNamedMessageHandler(CHALLENGE_MESSAGE, OnChallenge);
            messages.RegisterNamedMessageHandler(PROOF_MESSAGE, OnProof);
            messages.RegisterNamedMessageHandler(VERIFIED_MESSAGE, OnVerified);
            RegisterEquipmentMessages();
            clientDeadline = Time.realtimeSinceStartup + timeoutSeconds;
        }
        private void OnPeerConnected(ulong peerId)
        {
            if (!manager.IsServer) return;
            if (verifier == null) { FailLocal("Server identity trust is not configured."); return; }
            if (challengesByPeer.Count + equipmentChallenges.Count >= MAX_PENDING_PEERS) { RejectPeer(peerId); return; }
            var challenge = IdentityChallenge.Create(sessionId, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timeoutSeconds);
            challengesByPeer.Add(peerId, challenge);
            SendText(CHALLENGE_MESSAGE, peerId, challenge.SessionId + ":" + challenge.Nonce);
        }
        private void OnChallenge(ulong senderId, FastBufferReader reader)
        {
            if (!manager.IsClient || senderId != NetworkManager.ServerClientId) return;
            if (hasReceivedChallenge || backend == null) { FailLocal("Invalid identity challenge phase."); return; }
            string text;
            try { text = ReadText(reader, 200); }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException)
            { FailLocal("Malformed identity challenge."); return; }
            // Session IDs generated here are hex GUIDs; nonce is canonical base64url.
            if (text.Length != 76 || text[32] != ':' || !Guid.TryParseExact(text.Substring(0, 32), "N", out _))
            { FailLocal("Malformed identity challenge."); return; }
            string nonce = text.Substring(33);
            try { new IdentityChallenge(text.Substring(0, 32), nonce, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timeoutSeconds); }
            catch (ArgumentException) { FailLocal("Malformed identity challenge."); return; }
            hasReceivedChallenge = true;
            clientDeadline = Time.realtimeSinceStartup + timeoutSeconds;
            int requestGeneration = generation;
            requestStack = new Stack<IEnumerator>();
            try
            {
                requestStack.Push(RequestProofWithRecovery(text.Substring(0, 32), nonce, requestGeneration));
            }
            catch (Exception) { FailLocal("Identity proof backend could not start."); }
            if (requestStack != null) StartCoroutine(DriveRequest(requestStack));
        }
        private IEnumerator RequestProofWithRecovery(string challengeSessionId, string nonce, int requestGeneration)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool hasCompleted = false;
                string assertion = null;
                IdentityProofRequestFailure? failure = null;
                yield return backend.RequestProof(challengeSessionId, nonce,
                    value => { if (generation == requestGeneration && !hasCompleted) { hasCompleted = true; assertion = value; } },
                    error => { if (generation == requestGeneration && !hasCompleted) { hasCompleted = true; failure = error; } })
                    ?? throw new InvalidOperationException("Identity proof backend returned no operation.");
                if (generation != requestGeneration || !manager.IsListening) yield break;
                if (!hasCompleted) { FailLocal("Identity proof backend completed without a result."); yield break; }
                if (failure == IdentityProofRequestFailure.Unavailable && attempt == 0)
                { yield return new WaitForSecondsRealtime(1); continue; }
                if (failure.HasValue) { FailLocal("Identity proof request failed: " + failure.Value + "."); yield break; }
                if (string.IsNullOrEmpty(assertion) || assertion.Length > MAX_MESSAGE_BYTES)
                { FailLocal("Backend returned an invalid identity proof."); yield break; }
                SendText(PROOF_MESSAGE, NetworkManager.ServerClientId, assertion);
                yield break;
            }
        }
        private void OnProof(ulong peerId, FastBufferReader reader)
        {
            if (!manager.IsServer) return;
            if (!challengesByPeer.TryGetValue(peerId, out var challenge)) { RejectPeer(peerId); return; }
            challengesByPeer.Remove(peerId);
            string assertion;
            try { assertion = ReadText(reader, MAX_MESSAGE_BYTES); }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException)
            { RejectPeer(peerId); return; }
            if (!verifier.TryVerify(assertion, challenge, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var identity, out _))
            { RejectPeer(peerId); return; }
            if (equipmentVerifier != null)
            {
                pendingEquipmentIdentities.Add(peerId, identity);
                SendEquipmentChallenge(peerId);
            }
            else CompletePeerVerification(peerId, identity);
        }
        private void CompletePeerVerification(ulong peerId, PlayerIdentity identity)
        {
            identitiesByPeer.Add(peerId, identity);
            SendText(VERIFIED_MESSAGE, peerId, "1");
            try { PeerVerified?.Invoke(peerId, identity); }
            catch (Exception) { FailLocal("Verified player composition failed. Check required player bindings."); }
        }
        private void OnVerified(ulong senderId, FastBufferReader reader)
        {
            if (!manager.IsClient || senderId != NetworkManager.ServerClientId) return;
            try
            {
                if (!hasReceivedChallenge || IsLocalVerified || ReadText(reader, 1) != "1")
                { FailLocal("Invalid identity verification acknowledgement."); return; }
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException)
            { FailLocal("Malformed identity acknowledgement."); return; }
            if (EquipmentEnabled && lastAcceptedEquipmentRevision < 0)
            { FailLocal("Equipment verification is required by this client but was omitted by the server."); return; }
            IsLocalVerified = true;
            if (EquipmentEnabled) RequestNewerEquipmentIfNeeded();
        }
        private void Update()
        {
            if (!manager.IsListening) return;
            if (manager.IsClient && !IsLocalVerified && Time.realtimeSinceStartup >= clientDeadline)
            { FailLocal("Identity verification timed out."); return; }
            if (!manager.IsServer) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            expiredPeers.Clear();
            foreach (var pending in challengesByPeer) if (now >= pending.Value.ExpiresAt) expiredPeers.Add(pending.Key);
            foreach (ulong peerId in expiredPeers) RejectPeer(peerId);
            ExpireEquipmentChallenges(now);
        }
        private IEnumerator DriveRequest(Stack<IEnumerator> operations)
        {
            try
            {
                while (operations.Count > 0 && requestStack == operations)
                {
                    var operation = operations.Peek();
                    bool hasNext = false;
                    bool hasFailed = false;
                    object instruction = null;
                    try { hasNext = operation.MoveNext(); if (hasNext) instruction = operation.Current; }
                    catch (Exception) { hasFailed = true; }
                    if (hasFailed) { FailLocal("Identity proof operation failed."); yield break; }
                    if (requestStack != operations) yield break;
                    if (!hasNext)
                    {
                        operations.Pop();
                        (operation as IDisposable)?.Dispose();
                    }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction; // Let Unity honour every supported yield instruction.
                }
            }
            finally
            {
                if (requestStack == operations) requestStack = null;
                DisposeOperations(operations);
            }
        }
        private static void DisposeOperations(Stack<IEnumerator> operations)
        {
            bool hasFailed = false;
            while (operations.Count > 0)
            {
                try { (operations.Pop() as IDisposable)?.Dispose(); }
                catch (Exception) { hasFailed = true; }
            }
            if (hasFailed) throw new InvalidOperationException("Identity backend operation failed during disposal.");
        }
        private void RejectPeer(ulong peerId)
        {
            challengesByPeer.Remove(peerId);
            ClearEquipmentPeer(peerId);
            identitiesByPeer.Remove(peerId);
            if (manager.IsHost && peerId == manager.LocalClientId) FailLocal("Local player identity verification failed.");
            else manager.DisconnectClient(peerId, "Identity verification failed.");
        }
        private void OnPeerDisconnected(ulong peerId)
        {
            challengesByPeer.Remove(peerId);
            ClearEquipmentPeer(peerId);
            identitiesByPeer.Remove(peerId);
            if (manager.IsClient && !manager.IsServer && peerId == manager.LocalClientId && hasReceivedChallenge && !manager.ShutdownInProgress)
                FailLocal("Identity session disconnected.");
        }
        private void FailLocal(string reason)
        {
            if (hasReportedFailure) return;
            hasReportedFailure = true;
            try { ClearSession(); }
            finally
            {
                try { if (manager.IsListening) manager.Shutdown(); }
                finally { LocalFailure?.Invoke(reason); }
            }
        }
        private void OnNetworkStopped(bool wasHost) => ClearSession();
        private void ClearSession()
        {
            generation++;
            Exception equipmentCleanupFailure = null;
            try { ClearEquipmentSession(); } catch (Exception exception) { equipmentCleanupFailure = exception; }
            IsLocalVerified = false;
            hasReceivedChallenge = false;
            challengesByPeer.Clear();
            identitiesByPeer.Clear();
            if (messages != null)
            {
                messages.UnregisterNamedMessageHandler(CHALLENGE_MESSAGE);
                messages.UnregisterNamedMessageHandler(PROOF_MESSAGE);
                messages.UnregisterNamedMessageHandler(VERIFIED_MESSAGE);
                messages = null;
            }
            var pending = requestStack;
            requestStack = null;
            try { if (pending != null) DisposeOperations(pending); }
            finally { StopAllCoroutines(); }
            if (equipmentCleanupFailure != null) throw new InvalidOperationException("Equipment proof cleanup failed.", equipmentCleanupFailure);
        }
        private void SendText(string messageName, ulong peerId, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var writer = new FastBufferWriter(bytes.Length + 4, Allocator.Temp))
            {
                writer.WriteValueSafe(bytes.Length);
                writer.WriteBytesSafe(bytes);
                messages.SendNamedMessage(messageName, peerId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }
        private static string ReadText(FastBufferReader reader, int maximumBytes)
        {
            int remaining = reader.Length - reader.Position;
            if (remaining < 4 || remaining > maximumBytes + 4) throw new FormatException("Invalid identity message size.");
            reader.ReadValueSafe(out int length);
            if (length < 1 || length > maximumBytes || reader.Length - reader.Position != length) throw new FormatException("Invalid identity message length.");
            byte[] bytes = new byte[length];
            reader.ReadBytesSafe(ref bytes, length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
    }
}
