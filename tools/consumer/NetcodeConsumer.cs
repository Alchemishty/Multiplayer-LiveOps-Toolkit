using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using System;
using System.Collections;
using System.IO;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.Netcode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>Native release probe: real Django proofs across independent processes, no demo dependency.</summary>
public sealed class NetcodeConsumer : MonoBehaviour
{
    [SerializeField] private GameObject avatarPrefab;
    private PlayerContext context;
    private NetworkManager manager;
    private NetcodeIdentityGate gate;
    private string failure;
    private bool hasFinished;
    private string mode;
    private void Awake() => Application.logMessageReceived += OnLog;
    private void OnDestroy() => Application.logMessageReceived -= OnLog;
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) failure = message;
    }
    private IEnumerator Start()
    {
        mode = Environment.GetEnvironmentVariable("SMOKE_MODE");
        var trust = IdentityTrustConfiguration.CreateVerifier(File.ReadAllText(Environment.GetEnvironmentVariable("SMOKE_TRUST")));
        var equipmentTrust = new EquipmentProofVerifier(trust);
        manager = gameObject.AddComponent<NetworkManager>();
        var transport = gameObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", ushort.Parse(Environment.GetEnvironmentVariable("SMOKE_PORT")));
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false, PlayerPrefab = avatarPrefab };
        gate = gameObject.AddComponent<NetcodeIdentityGate>();
        gameObject.AddComponent<AvatarRegistry>();
        gameObject.AddComponent<VerifiedPlayerSpawner>();
        gate.LocalFailure += OnFailure;
        EquipmentModule equipment = null;
        if (mode != "server")
        {
            string url = Environment.GetEnvironmentVariable("SMOKE_BACKEND");
            var session = gameObject.AddComponent<DjangoPlayerSession>();
            context = new PlayerContext(session, new DjangoIdentityProofAPI(url, session));
            var inventory = new InventoryModule(new DjangoInventoryBackend(url, session), exception => failure = exception.Message);
            equipment = new EquipmentModule(new DjangoEquipmentBackend(url, session), inventory, exception => failure = exception.Message);
            yield return context.Initialize(new DjangoPasswordIdentityProvider(Environment.GetEnvironmentVariable("SMOKE_USER"), Environment.GetEnvironmentVariable("SMOKE_PASSWORD")), new DjangoIdentityBackend(url, session), () => { }, error => failure = error.Message, new ILiveOpsModule[] { equipment, inventory });
            if (!Check(failure == null && context.IsAuthenticated, "backend login")) yield break;
            yield return equipment.Clear(() => { }, error => failure = error.Message);
            if (!Check(failure == null, "clear initial equipment")) yield break;
            yield return equipment.ApplyItems(new[] { "1" }, () => { }, error => failure = error.Message);
            if (!Check(failure == null, "initial equipment")) yield break;
            gate.ConfigureEquipment(equipmentTrust, new DjangoEquipmentProofAPI(url, session), equipment);
        }
        else gate.ConfigureEquipment(equipmentTrust, null);
        bool started = mode == "server" ? gate.StartServer(trust) : mode == "host" ? gate.StartHost(trust, context.IdentityProofs) : gate.StartClient(context.IdentityProofs);
        if (!Check(started, "network start")) yield break;
        if (mode != "client")
        {
            if (mode == "host")
            {
                float deadline = Time.realtimeSinceStartup + 20;
                while (!gate.IsLocalVerified && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
                if (!Check(gate.IsLocalVerified && failure == null, "local host verification")) yield break;
            }
            File.WriteAllText(Marker("ready"), "ready");
            float remoteDeadline = Time.realtimeSinceStartup + 35;
            bool hasObserved = false;
            while (!hasObserved && failure == null && Time.realtimeSinceStartup < remoteDeadline)
            {
                foreach (ulong peerId in manager.ConnectedClientsIds)
                {
                    if (peerId == NetworkManager.ServerClientId) continue;
                    if (gate.TryGetIdentity(peerId, out var identity) && gate.TryGetEquipment(peerId, out var loadout) && loadout.ItemsBySlot.TryGetValue("mainhand", out string itemId) && itemId == "2")
                    {
                        if (!Check(identity.DisplayName == "release-client", "backend public identity")) yield break;
                        var avatar = GetComponent<AvatarRegistry>().GetAvatar(peerId);
                        if (avatar == null || Vector3.Distance(avatar.transform.position, new Vector3(3, 0, 4)) > 0.1f
                            || Mathf.Abs(avatar.GetComponent<Animator>().GetFloat("Speed") - 0.5f) > 0.01f) continue;
                        hasObserved = true;
                    }
                }
                yield return null;
            }
            if (!Check(hasObserved && failure == null, "remote signed updated equipment, owner movement and animation")) yield break;
            File.WriteAllText(Marker("observed"), "observed");
            while (!File.Exists(Marker("client-done")) && Time.realtimeSinceStartup < remoteDeadline) yield return null;
            if (!Check(File.Exists(Marker("client-done")), "client teardown")) yield break;
            Finish("PASS " + mode + ": real Django identity, signed remote equipment revision, owner movement/animation" + (mode == "server" ? ", no local server account" : ", verified local host account"), 0);
        }
        else
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!gate.IsLocalVerified && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Check(gate.IsLocalVerified && failure == null, "remote verification")) yield break;
            while ((manager.LocalClient == null || manager.LocalClient.PlayerObject == null) && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Check(manager.LocalClient != null && manager.LocalClient.PlayerObject != null && GetComponent<AvatarRegistry>().LocalAvatar != null, "packaged avatar spawn and registry")) yield break;
            if (Environment.GetEnvironmentVariable("SMOKE_EXPECT_HOST") == "1")
            {
                var avatars = GetComponent<AvatarRegistry>();
                while (avatars.GetAvatar(NetworkManager.ServerClientId) == null && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
                var hostAvatar = avatars.GetAvatar(NetworkManager.ServerClientId);
                if (!Check(hostAvatar != null && hostAvatar.GetComponent<NetworkPlayerIdentity>().Username.Value.ToString() == "release-host", "existing verified host avatar visible to joining client")) yield break;
            }
            var localAvatar = manager.LocalClient.PlayerObject;
            if (!Check(localAvatar.GetComponent<NetworkPlayerIdentity>().PlayerId.Value.ToString() == context.CurrentPlayer.PlayerId, "packaged identity replication")) yield break;
            localAvatar.transform.position = new Vector3(3, 0, 4);
            localAvatar.GetComponent<Animator>().SetFloat("Speed", 0.5f);
            localAvatar.GetComponent<PlayerAccessory>().BindPresentation(equipment);
            equipment.Preview(new[] { "2" });
            if (!Check(EquipmentPublicState.Parse(localAvatar.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).ItemsBySlot["headwear"] == "1", "preview remains local")) yield break;
            if (!Check(localAvatar.transform.Find("Cosmetic_2") != null && localAvatar.transform.Find("Cosmetic_2").gameObject.activeSelf, "packaged local preview renderer")) yield break;
            equipment.ClearPreview();
            if (!Check(!localAvatar.transform.Find("Cosmetic_2").gameObject.activeSelf, "preview restores canonical appearance")) yield break;
            yield return equipment.ApplyItems(new[] { "2" }, () => { }, error => failure = error.Message);
            if (!Check(failure == null, "equipment mutation")) yield break;
            while (!File.Exists(Marker("observed")) && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Check(File.Exists(Marker("observed")) && failure == null, "server accepted latest proof")) yield break;
            float publicationDeadline = Time.realtimeSinceStartup + 5;
            while ((!EquipmentPublicState.Parse(localAvatar.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).ItemsBySlot.ContainsKey("mainhand")) && Time.realtimeSinceStartup < publicationDeadline) yield return null;
            if (!Check(EquipmentPublicState.Parse(localAvatar.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).ItemsBySlot["mainhand"] == "2", "packaged equipment replication")) yield break;
            if (!Check(localAvatar.transform.Find("Cosmetic_2").gameObject.activeSelf, "verified equipment renders")) yield break;
            context.Dispose();
            if (!Check(!equipment.HasSnapshot, "private context teardown")) yield break;
            File.WriteAllText(Marker("client-done"), "done");
            Finish("PASS client: backend login, signed equipment handshake/update, packaged avatars/preview/replication, private teardown", 0);
        }
    }
    private static string Marker(string name) => Environment.GetEnvironmentVariable("SMOKE_PREFIX") + name;
    private void OnFailure(string message) => failure = message;
    private bool Check(bool condition, string step) { if (condition) return true; Finish("FAIL " + mode + " " + step + ": " + failure, 1); return false; }
    private void Finish(string result, int code)
    {
        if (hasFinished) return;
        hasFinished = true;
        if (gate != null) gate.LocalFailure -= OnFailure;
        if (manager != null && manager.IsListening) manager.Shutdown();
        if (context != null) context.Dispose();
        File.WriteAllText(Environment.GetEnvironmentVariable("SMOKE_RESULT"), result);
        Application.Quit(code);
    }
}
