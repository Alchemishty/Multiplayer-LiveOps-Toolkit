using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using MultiplayerLiveOpsToolkit.Netcode;
using System;
using System.Collections;
using System.IO;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.Equipment;
using UnityEngine;
using UnityEngine.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>Sample discovery policy. All session starts pass through the identity gate.</summary>
[RequireComponent(typeof(NetcodeIdentityGate), typeof(ClientHostConnection))]
public class AutoConnect : MonoBehaviour
{
    [SerializeField] private DevAuthBootstrap authBootstrap;
    [SerializeField] private string address = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private float connectionTimeout = 3f;
    private NetcodeIdentityGate gate;
    private NetworkManager manager;
    private IdentityProofVerifier verifier;
    private IIdentityProofBackend backend;
    private bool isDedicatedServer;

    private void Awake()
    {
        gate = GetComponent<NetcodeIdentityGate>();
        manager = GetComponent<NetworkManager>();
        isDedicatedServer = Array.IndexOf(Environment.GetCommandLineArgs(), "-liveops-server") >= 0;
    }
    private void OnEnable() => gate.LocalFailure += OnIdentityFailure;
    private void OnDisable() => gate.LocalFailure -= OnIdentityFailure;

    public void ConfigureIdentity(IdentityProofVerifier proofVerifier, IIdentityProofBackend proofBackend)
    {
        if (manager.IsListening) throw new InvalidOperationException("Configure sample identity before connecting.");
        verifier = proofVerifier ?? throw new ArgumentNullException(nameof(proofVerifier));
        backend = proofBackend ?? throw new ArgumentNullException(nameof(proofBackend));
    }

    private IEnumerator Start()
    {
        if (isDedicatedServer)
        {
            if (authBootstrap != null) authBootstrap.gameObject.SetActive(false);
        }
        else
        {
            if (authBootstrap == null) throw new InvalidOperationException("AutoConnect requires its player authBootstrap binding.");
            while (!authBootstrap.IsAuthenticated) yield return null;
        }

        if (verifier == null)
        {
            const string CONFIGURATION_FAILURE = "Identity configuration error: export and configure LiveOpsIdentityTrust.json before connecting.";
            string failure = null;
            string trustUrl = null;
            try
            {
                string path = Environment.GetEnvironmentVariable("LIVEOPS_IDENTITY_TRUST_FILE");
                if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.streamingAssetsPath, "LiveOpsIdentityTrust.json");
                trustUrl = path.Contains("://") ? path : new Uri(Path.GetFullPath(path)).AbsoluteUri;
                var location = new Uri(trustUrl);
                if (!location.IsFile && !trustUrl.StartsWith("jar:file://", StringComparison.Ordinal))
                    throw new ArgumentException("Public trust must be a local file or packaged asset.");
                if (location.IsFile && new FileInfo(location.LocalPath).Length > 16384)
                    throw new ArgumentException("Public trust file is oversized.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is UriFormatException)
            { failure = CONFIGURATION_FAILURE; }
            if (failure == null)
            {
                using (var request = UnityWebRequest.Get(trustUrl))
                {
                    request.timeout = 15;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone && request.downloadedBytes <= 16384) yield return null;
                    if (!operation.isDone) request.Abort();
                    if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 16384)
                        failure = CONFIGURATION_FAILURE;
                    else
                    {
                        try { verifier = IdentityTrustConfiguration.CreateVerifier(request.downloadHandler.text); }
                        catch (ArgumentException) { failure = CONFIGURATION_FAILURE; }
                    }
                }
            }
            if (!isActiveAndEnabled || (!isDedicatedServer && !authBootstrap.IsAuthenticated)) yield break;
            if (failure != null) { OnIdentityFailure(failure); yield break; }
        }
        if (isDedicatedServer) gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), null);
        else if (authBootstrap.Equipment != null) gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), authBootstrap.EquipmentProofs, authBootstrap.Equipment);
        var transport = manager.GetComponent<UnityTransport>() ?? throw new InvalidOperationException("AutoConnect requires UnityTransport.");
        transport.SetConnectionData(address, port, isDedicatedServer ? "0.0.0.0" : null);
        manager.NetworkConfig.NetworkTransport = transport;
        if (isDedicatedServer)
        {
            if (!gate.StartServer(verifier)) OnIdentityFailure("Dedicated identity server could not start.");
            yield break;
        }
        if (backend == null) backend = authBootstrap.Context.IdentityProofs;
        var connection = GetComponent<ClientHostConnection>();
        yield return connection.Connect(verifier, backend, () => isActiveAndEnabled && authBootstrap != null && authBootstrap.IsAuthenticated,
            OnIdentityFailure, address, port, connectionTimeout);
    }
    private void OnIdentityFailure(string reason)
    {
        if (isDedicatedServer) throw new InvalidOperationException(reason);
        var context = authBootstrap.Context ?? throw new InvalidOperationException("AutoConnect player context is not configured.");
        context.Session.RejectSession(context.Session.SessionVersion, reason);
    }
}
