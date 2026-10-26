using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Identity;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
    /// <summary>Optional client-first/host-fallback policy; peer verification stays in the gate.</summary>
    [RequireComponent(typeof(NetcodeIdentityGate), typeof(UnityTransport))]
    public sealed class ClientHostConnection : MonoBehaviour
    {
        public IEnumerator Connect(IdentityProofVerifier verifier, IIdentityProofBackend backend, Func<bool> isAuthenticated, Action<string> onFailure,
            string address = "127.0.0.1", ushort port = 7777, float connectionTimeout = 3f)
        {
            if (verifier == null) throw new ArgumentNullException(nameof(verifier));
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (isAuthenticated == null) throw new ArgumentNullException(nameof(isAuthenticated));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            if (string.IsNullOrWhiteSpace(address) || port == 0 || float.IsNaN(connectionTimeout) || float.IsInfinity(connectionTimeout) || connectionTimeout <= 0)
                throw new ArgumentException("Configure a connection address, nonzero port and positive finite timeout.");
            return ConnectRoutine(verifier, backend, isAuthenticated, onFailure, address, port, connectionTimeout);
        }
        private IEnumerator ConnectRoutine(IdentityProofVerifier verifier, IIdentityProofBackend backend, Func<bool> isAuthenticated, Action<string> onFailure,
            string address, ushort port, float connectionTimeout)
        {
            var manager = GetComponent<NetworkManager>() ?? throw new InvalidOperationException("ClientHostConnection requires NetworkManager.");
            var gate = GetComponent<NetcodeIdentityGate>();
            if (manager.IsListening || manager.ShutdownInProgress) throw new InvalidOperationException("Stop the current network session before connecting.");
            if (!isAuthenticated()) yield break; // The account may have ended before the caller schedules this routine.
            var transport = GetComponent<UnityTransport>();
            transport.SetConnectionData(address, port);
            manager.NetworkConfig.NetworkTransport = transport;
            bool hasTransferredSession = false;
            try
            {
                if (!gate.StartClient(backend)) { onFailure("Identity client could not start."); yield break; }
                float deadline = Time.realtimeSinceStartup + connectionTimeout;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (!isActiveAndEnabled || !isAuthenticated()) yield break;
                    // A connected peer belongs to the gate, including an unverified/rejected handshake.
                    if (manager.IsConnectedClient) { hasTransferredSession = true; yield break; }
                    yield return null;
                }
                manager.Shutdown();
                while (manager.ShutdownInProgress || manager.IsListening) yield return null;
                yield return new WaitForSecondsRealtime(0.5f);
                if (!isActiveAndEnabled || !isAuthenticated()) yield break;
                hasTransferredSession = gate.StartHost(verifier, backend);
                if (!hasTransferredSession) onFailure("Identity host could not start.");
            }
            finally { if (!hasTransferredSession && manager != null && manager.IsListening) manager.Shutdown(); }
        }
    }
}
