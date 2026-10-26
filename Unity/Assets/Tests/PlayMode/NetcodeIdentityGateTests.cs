using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using MultiplayerLiveOpsToolkit.Netcode;
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.Equipment;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NetcodeIdentityGateTests
{
    private GameObject managerObject;
    private GameObject clientObject;
    private NetworkManager client;
    private string lastFailure;
    private NetworkManager manager;
    private NetcodeIdentityGate gate;
    private RSACryptoServiceProvider signer;
    private IdentityProofVerifier verifier;

    [SetUp]
    public void SetUp()
    {
        lastFailure = "";
        signer = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
        verifier = new IdentityProofVerifier(new Dictionary<string, RSAParameters> { { "test", signer.ExportParameters(false) } },
            new IdentityProofPolicy("issuer", "game", "test", "v1", 60, 0));
        managerObject = new GameObject("Handshake test manager");
        manager = managerObject.AddComponent<NetworkManager>();
        var transport = managerObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 0);
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        gate = managerObject.AddComponent<NetcodeIdentityGate>();
        gate.LocalFailure += CaptureFailure;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (client != null) client.Shutdown();
        manager.Shutdown();
        float deadline = Time.realtimeSinceStartup + 3;
        while ((manager.IsListening || manager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
        if (clientObject != null) { clientObject.GetComponent<NetcodeIdentityGate>().LocalFailure -= CaptureFailure; Object.DestroyImmediate(clientObject); }
        gate.LocalFailure -= CaptureFailure;
        Object.DestroyImmediate(managerObject);
        signer.Dispose();
    }

    [UnityTest]
    public IEnumerator Host_ValidProof_VerifiesBeforePublishingIdentity()
    {
        var backend = new SigningBackend(signer);
        int verifiedCount = 0;
        Action<ulong, PlayerIdentity> onVerified = (peer, identity) =>
        {
            verifiedCount++;
            Assert.That(peer, Is.EqualTo(NetworkManager.ServerClientId));
            Assert.That(identity.PlayerId, Is.EqualTo("42"));
            Assert.That(identity.DisplayName, Is.EqualTo("Backend player"));
        };
        gate.PeerVerified += onVerified;
        try
        {
            Assert.That(gate.StartHost(verifier, backend), Is.True);
            Assert.That(manager.LocalClient.PlayerObject, Is.Null);
            Assert.That(gate.IsLocalVerified, Is.False);
            float deadline = Time.realtimeSinceStartup + 4;
            while (verifiedCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(verifiedCount, Is.EqualTo(1));
            Assert.That(gate.IsLocalVerified, Is.True);
            Assert.That(backend.RequestCount, Is.EqualTo(1));
            Assert.That(gate.TryGetIdentity(0, out var identity), Is.True);
            Assert.That(identity.PlayerId, Is.EqualTo("42"));
        }
        finally { gate.PeerVerified -= onVerified; }
    }

    [UnityTest]
    public IEnumerator Host_InvalidProof_ShutsDownWithoutIdentity()
    {
        var backend = new SigningBackend(signer) { ShouldTamper = true };
        Assert.That(gate.StartHost(verifier, backend), Is.True);
        float deadline = Time.realtimeSinceStartup + 4;
        while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.IsListening, Is.False);
        Assert.That(gate.TryGetIdentity(0, out _), Is.False);
        Assert.That(gate.IsLocalVerified, Is.False);
    }

    [UnityTest]
    public IEnumerator Host_BackendNeverCompletes_TimesOutAndDisposesOperation()
    {
        var backend = new SigningBackend(signer) { ShouldWait = true };
        Assert.That(gate.StartHost(verifier, backend, 1), Is.True);
        float deadline = Time.realtimeSinceStartup + 4;
        while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.IsListening, Is.False);
        Assert.That(backend.HasDisposed, Is.True);
    }

    [UnityTest]
    public IEnumerator DedicatedServer_StartsWithoutBackendAccountOrPlayerPrefab()
    {
        Assert.That(gate.StartServer(verifier), Is.True);
        yield return null;
        Assert.That(manager.IsServer, Is.True);
        Assert.That(manager.IsClient, Is.False);
        Assert.That(gate.TryGetIdentity(0, out _), Is.False);
        Assert.That(gate.IsLocalVerified, Is.False);
        manager.Shutdown();
        yield return null;
        Assert.That(gate.TryGetIdentity(0, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator Disable_VerifiedHost_ClearsBindingsAndShutsDown()
    {
        Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
        float deadline = Time.realtimeSinceStartup + 4;
        while (!gate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.IsLocalVerified, Is.True);
        gate.enabled = false;
        while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.IsListening, Is.False);
        Assert.That(gate.TryGetIdentity(0, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator RemoteClient_ValidProof_BindsOnlyThatConnection()
    {
        var clientGate = CreateRemoteClient();
        Assert.That(gate.StartServer(verifier), Is.True);
        Assert.That(clientGate.StartClient(new SigningBackend(signer)), Is.True);
        float deadline = Time.realtimeSinceStartup + 6;
        while (!clientGate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(clientGate.IsLocalVerified, Is.True, lastFailure + "; Client listening=" + client.IsListening + ", server peers=" + manager.ConnectedClientsIds.Count + ", server identity=" + gate.TryGetIdentity(client.LocalClientId, out _));
        Assert.That(gate.TryGetIdentity(client.LocalClientId, out var identity), Is.True);
        Assert.That(identity.PlayerId, Is.EqualTo("42"));
        Assert.That(manager.ConnectedClients[client.LocalClientId].PlayerObject, Is.Null);
        client.Shutdown();
        while (manager.ConnectedClientsIds.Count != 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.ConnectedClientsIds.Count, Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator RemoteClient_InvalidProof_DisconnectsPeerWithoutStoppingServer()
    {
        var clientGate = CreateRemoteClient();
        Assert.That(gate.StartServer(verifier), Is.True);
        var backend = new SigningBackend(signer) { ShouldTamper = true };
        Assert.That(clientGate.StartClient(backend), Is.True);
        float deadline = Time.realtimeSinceStartup + 6;
        while ((backend.RequestCount == 0 || client.IsListening) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(backend.RequestCount, Is.EqualTo(1));
        Assert.That(client.IsListening, Is.False);
        Assert.That(manager.IsListening, Is.True);
        Assert.That(gate.TryGetIdentity(1, out _), Is.False);
    }

    private NetcodeIdentityGate CreateRemoteClient()
    {
        var reservation = new System.Net.Sockets.UdpClient(0);
        ushort port = (ushort)((System.Net.IPEndPoint)reservation.Client.LocalEndPoint).Port;
        reservation.Close();
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", port);
        clientObject = new GameObject("Remote handshake test client");
        client = clientObject.AddComponent<NetworkManager>();
        var transport = clientObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", port);
        client.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        var clientGate = clientObject.AddComponent<NetcodeIdentityGate>();
        clientGate.LocalFailure += CaptureFailure;
        return clientGate;
    }

    [UnityTest]
    public IEnumerator VerifiedHost_SpawnsPlayerWithServerOwnedIdentity()
    {
        var prefab = CreatePlayerTemplate();
        manager.NetworkConfig.PlayerPrefab = prefab;
        var spawner = managerObject.AddComponent<VerifiedPlayerSpawner>();
        try
        {
            Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
            Assert.That(manager.LocalClient.PlayerObject, Is.Null);
            float deadline = Time.realtimeSinceStartup + 4;
            while (manager.IsServer && manager.ConnectedClients[0].PlayerObject == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsServer, Is.True, lastFailure);
            Assert.That(manager.ConnectedClients[0].PlayerObject, Is.Not.Null, lastFailure);
            var spawned = manager.ConnectedClients[0].PlayerObject.GetComponent<PlayerNameplate>();
            Assert.That(spawned.Username.Value.ToString(), Is.EqualTo("Backend player"));
            Assert.That(spawned.Username.WritePerm, Is.EqualTo(NetworkVariableWritePermission.Server));
        }
        finally { Object.DestroyImmediate(prefab); }
    }

    [UnityTest]
    public IEnumerator PendingRemotePeer_CannotObservePlayers_UntilProofSucceeds()
    {
        var clientGate = CreateRemoteClient();
        var prefab = CreatePlayerTemplate();
        manager.NetworkConfig.PlayerPrefab = prefab;
        client.NetworkConfig.PlayerPrefab = prefab;
        managerObject.AddComponent<VerifiedPlayerSpawner>();
        var remoteBackend = new SigningBackend(signer) { ShouldWait = true };
        try
        {
            Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
            float deadline = Time.realtimeSinceStartup + 8;
            while (!gate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(gate.IsLocalVerified, Is.True);
            Assert.That(clientGate.StartClient(remoteBackend), Is.True);
            while (remoteBackend.RequestCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(remoteBackend.RequestCount, Is.EqualTo(1));
            Assert.That(clientGate.IsLocalVerified, Is.False);
            var hostPlayer = manager.ConnectedClients[0].PlayerObject;
            Assert.That(hostPlayer.IsNetworkVisibleTo(client.LocalClientId), Is.False);
            Assert.That(client.SpawnManager.SpawnedObjects.Count, Is.EqualTo(0));
            remoteBackend.ShouldWait = false;
            while ((!clientGate.IsLocalVerified || client.SpawnManager.SpawnedObjects.Count != 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(clientGate.IsLocalVerified, Is.True, lastFailure);
            Assert.That(client.SpawnManager.SpawnedObjects.Count, Is.EqualTo(2));
            Assert.That(client.LocalClient.PlayerObject.GetComponent<PlayerNameplate>().Username.Value.ToString(), Is.EqualTo("Backend player"));
        }
        finally { Object.DestroyImmediate(prefab); }
    }

    private GameObject CreatePlayerTemplate()
    {
        var prefab = new GameObject("Verified player template");
        prefab.AddComponent<NetworkObject>();
        var label = new GameObject("Nameplate").AddComponent<TMPro.TextMeshPro>();
        label.transform.SetParent(prefab.transform);
        var nameplate = prefab.AddComponent<PlayerNameplate>();
        var serialized = new UnityEditor.SerializedObject(nameplate);
        serialized.FindProperty("nameplateLabel").objectReferenceValue = label;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        typeof(NetworkObject).GetProperty("IsSceneObject").SetValue(prefab.GetComponent<NetworkObject>(), false);
        return prefab;
    }

    private void CaptureFailure(string reason) => lastFailure = reason;

    [UnityTest]
    public IEnumerator Host_BackendYieldInstruction_RespectsDelay()
    {
        float start = Time.realtimeSinceStartup;
        Assert.That(gate.StartHost(verifier, new SigningBackend(signer) { DelaySeconds = 0.5f }), Is.True);
        float deadline = start + 4;
        while (!gate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.IsLocalVerified, Is.True);
        Assert.That(Time.realtimeSinceStartup - start, Is.GreaterThanOrEqualTo(0.4f));
    }

    [UnityTest]
    public IEnumerator Host_TransientProofUnavailable_RetriesWithinHandshakeDeadline()
    {
        var backend = new SigningBackend(signer) { UnavailableResponsesRemaining = 1 };
        Assert.That(gate.StartHost(verifier, backend), Is.True);
        float deadline = Time.realtimeSinceStartup + 5;
        while (!gate.IsLocalVerified && manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.IsLocalVerified, Is.True, lastFailure);
        Assert.That(backend.RequestCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator RemoteClient_ReplayedProof_DisconnectsOnlyThatPeer()
    {
        var clientGate = CreateRemoteClient();
        Assert.That(gate.StartServer(verifier), Is.True);
        var backend = new SigningBackend(signer);
        Assert.That(clientGate.StartClient(backend), Is.True);
        float deadline = Time.realtimeSinceStartup + 6;
        while (!clientGate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(clientGate.IsLocalVerified, Is.True);
        ulong peerId = client.LocalClientId;
        byte[] bytes = Encoding.UTF8.GetBytes(backend.LastAssertion);
        using (var writer = new FastBufferWriter(bytes.Length + 4, Unity.Collections.Allocator.Temp))
        {
            writer.WriteValueSafe(bytes.Length);
            writer.WriteBytesSafe(bytes);
            client.CustomMessagingManager.SendNamedMessage("liveops.identity.proof.v1", 0, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
        while (client.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(client.IsListening, Is.False);
        Assert.That(manager.IsListening, Is.True);
        Assert.That(gate.TryGetIdentity(peerId, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator Disable_BackendDisposalThrows_StillShutsDownNetwork()
    {
        var backend = new SigningBackend(signer) { ShouldWait = true, ShouldThrowOnDispose = true };
        Assert.That(gate.StartHost(verifier, backend), Is.True);
        yield return null;
        LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Identity backend operation failed during disposal"));
        gate.enabled = false;
        float deadline = Time.realtimeSinceStartup + 3;
        while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.IsListening, Is.False);
        Assert.That(backend.HasDisposed, Is.True);
        Assert.That(gate.TryGetIdentity(0, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator Host_EquipmentProofGatesVerificationAndAcceptsOnlyNewerCanonicalState()
    {
        var equipmentBackend = new EquipmentSigningBackend(signer) { ShouldWait = true };
        gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), equipmentBackend);
        Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
        float deadline = Time.realtimeSinceStartup + 6;
        while (equipmentBackend.RequestCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(equipmentBackend.RequestCount, Is.EqualTo(1));
        Assert.That(gate.IsLocalVerified, Is.False);
        equipmentBackend.ShouldWait = false;
        while (!gate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.IsLocalVerified, Is.True, lastFailure);
        Assert.That(gate.TryGetEquipment(0, out var initial), Is.True);
        Assert.That(initial.Revision, Is.EqualTo(1));
        equipmentBackend.Revision = 2;
        gate.RequestEquipmentPublication();
        while ((!gate.TryGetEquipment(0, out var updated) || updated.Revision != 2) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.TryGetEquipment(0, out var final), Is.True);
        Assert.That(final.Revision, Is.EqualTo(2));
    }
    [UnityTest]
    public IEnumerator DedicatedServer_RejectsWrongPlayerEquipmentWithoutPublishingIdentity()
    {
        var clientGate = CreateRemoteClient();
        gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), null);
        clientGate.ConfigureEquipment(new EquipmentProofVerifier(verifier), new EquipmentSigningBackend(signer) { PlayerId = "other" });
        int published = 0;
        Action<ulong,PlayerIdentity> capture = (peer,identity) => published++;
        gate.PeerVerified += capture;
        try
        {
            Assert.That(gate.StartServer(verifier), Is.True);
            Assert.That(clientGate.StartClient(new SigningBackend(signer)), Is.True);
            float deadline = Time.realtimeSinceStartup + 6;
            while (client.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(clientGate.IsLocalVerified, Is.False);
            Assert.That(published, Is.Zero);
            Assert.That(manager.IsListening, Is.True);
        }
        finally { gate.PeerVerified -= capture; }
    }
    [UnityTest]
    public IEnumerator Host_MutationDuringProof_PublishesLatestRevisionAfterOldProofCompletes()
    {
        var service = new PublicationService();
        var backend = new EquipmentSigningBackend(signer);
        gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), backend, service);
        Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
        float deadline = Time.realtimeSinceStartup + 6;
        while (!gate.IsLocalVerified && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.IsLocalVerified, Is.True, lastFailure);
        backend.ShouldWait = true; backend.Revision = 2; service.Revision = 2; service.Notify();
        while (backend.RequestCount < 2 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(backend.RequestCount, Is.EqualTo(2));
        backend.Revision = 3; service.Revision = 3; service.Notify();
        yield return null; yield return null;
        backend.ShouldWait = false;
        while ((!gate.TryGetEquipment(0, out var value) || value.Revision != 3) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(gate.TryGetEquipment(0, out var result), Is.True);
        Assert.That(result.Revision, Is.EqualTo(3));
    }
    [UnityTest]
    public IEnumerator EquipmentDisabled_PlayerComponentHasNoEquipmentSideEffects()
    {
        var prefab = CreatePlayerTemplate();
        prefab.AddComponent<PlayerAccessory>();
        manager.NetworkConfig.PlayerPrefab = prefab;
        managerObject.AddComponent<VerifiedPlayerSpawner>();
        try
        {
            Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
            float deadline = Time.realtimeSinceStartup + 4;
            while (manager.LocalClient.PlayerObject == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(gate.IsLocalVerified, Is.True);
            Assert.That(manager.LocalClient.PlayerObject, Is.Not.Null);
            yield return null;
        }
        finally { Object.DestroyImmediate(prefab); }
    }
    [UnityTest]
    public IEnumerator DedicatedEquipment_ReplicatesUpdatesLateJoinRespawnAndDisconnect()
    {
        var clientGate = CreateRemoteClient();
        var hat = new GameObject("Equipment test hat");
        var crown = new GameObject("Equipment test crown");
        var catalog = ScriptableObject.CreateInstance<AccessorySO>();
        catalog.accessories = new[] { new AccessoryItemData { itemId = 1, accessoryType = AccessoryType.Headwear, prefab = hat }, new AccessoryItemData { itemId = 2, accessoryType = AccessoryType.Headwear, prefab = crown } };
        var registryObject = new GameObject("Equipment test mappings");
        registryObject.SetActive(false);
        var registry = registryObject.AddComponent<AccessoryRegistry>();
        var registryFields = new UnityEditor.SerializedObject(registry);
        registryFields.FindProperty("masterCatalogSO").objectReferenceValue = catalog;
        registryFields.ApplyModifiedPropertiesWithoutUndo();
        registryObject.SetActive(true);
        var prefab = CreatePlayerTemplate();
        prefab.AddComponent<PlayerAccessory>();
        prefab.AddComponent<EquipmentVisualTestDouble>();
        manager.NetworkConfig.PlayerPrefab = prefab;
        manager.AddNetworkPrefab(prefab); client.AddNetworkPrefab(prefab);
        managerObject.AddComponent<VerifiedPlayerSpawner>();
        var equipmentBackend = new EquipmentSigningBackend(signer);
        gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), null);
        clientGate.ConfigureEquipment(new EquipmentProofVerifier(verifier), equipmentBackend);
        GameObject lateObject = null;
        NetworkManager lateManager = null;
        try
        {
            Assert.That(gate.StartServer(verifier), Is.True);
            Assert.That(clientGate.StartClient(new SigningBackend(signer)), Is.True);
            float deadline = Time.realtimeSinceStartup + 10;
            while ((client.LocalClient == null || client.LocalClient.PlayerObject == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(client.LocalClient.PlayerObject, Is.Not.Null, lastFailure);
            ulong firstPeer = client.LocalClientId;
            var firstPlayer = client.LocalClient.PlayerObject;
            Assert.That(EquipmentPublicState.Parse(firstPlayer.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).ItemsBySlot["headwear"], Is.EqualTo("1"));
            Assert.That(firstPlayer.GetComponent<EquipmentVisualTestDouble>().GetActiveItemId(AccessoryType.Headwear), Is.EqualTo(1));
            equipmentBackend.Revision = 2; equipmentBackend.ItemId = "2";
            clientGate.RequestEquipmentPublication();
            while (EquipmentPublicState.Parse(firstPlayer.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).Revision != 2 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(firstPlayer.GetComponent<EquipmentVisualTestDouble>().GetActiveItemId(AccessoryType.Headwear), Is.EqualTo(2));

            lateObject = new GameObject("Late equipment observer");
            lateManager = lateObject.AddComponent<NetworkManager>();
            var lateTransport = lateObject.AddComponent<UnityTransport>();
            lateTransport.SetConnectionData("127.0.0.1", manager.GetComponent<UnityTransport>().ConnectionData.Port);
            lateManager.NetworkConfig = new NetworkConfig { NetworkTransport = lateTransport, EnableSceneManagement = false };
            lateManager.AddNetworkPrefab(prefab);
            var lateGate = lateObject.AddComponent<NetcodeIdentityGate>();
            lateGate.ConfigureEquipment(new EquipmentProofVerifier(verifier), new EquipmentSigningBackend(signer));
            Assert.That(lateGate.StartClient(new SigningBackend(signer)), Is.True);
            while ((!lateGate.IsLocalVerified || lateManager.SpawnManager.SpawnedObjects.Count != 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(lateManager.SpawnManager.SpawnedObjects.Count, Is.EqualTo(2), lastFailure);
            foreach (var observed in lateManager.SpawnManager.SpawnedObjectsList)
                if (observed.OwnerClientId == firstPeer)
                    Assert.That(EquipmentPublicState.Parse(observed.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).ItemsBySlot["headwear"], Is.EqualTo("2"));

            manager.ConnectedClients[firstPeer].PlayerObject.Despawn(true);
            var replacement = Object.Instantiate(prefab);
            gate.TryGetIdentity(firstPeer, out var identity);
            gate.TryGetEquipment(firstPeer, out var approved);
            replacement.GetComponent<PlayerNameplate>().InitializeVerifiedIdentity(identity);
            replacement.GetComponent<PlayerAccessory>().InitializeVerifiedEquipment(approved);
            replacement.GetComponent<NetworkObject>().CheckObjectVisibility = peer => gate.TryGetIdentity(peer, out _);
            replacement.GetComponent<NetworkObject>().SpawnAsPlayerObject(firstPeer, true);
            while ((client.LocalClient.PlayerObject == null || client.LocalClient.PlayerObject == firstPlayer) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(client.LocalClient.PlayerObject, Is.Not.Null);
            Assert.That(EquipmentPublicState.Parse(client.LocalClient.PlayerObject.GetComponent<PlayerAccessory>().AccessoryJson.Value.Json).Revision, Is.EqualTo(2));
            client.Shutdown();
            while (gate.TryGetEquipment(firstPeer, out _) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(gate.TryGetEquipment(firstPeer, out _), Is.False);
            lateManager.Shutdown(); manager.Shutdown();
            while ((manager.IsListening || lateManager.IsListening || client.IsListening) && Time.realtimeSinceStartup < deadline) yield return null;
        }
        finally
        {
            if (lateManager != null) lateManager.Shutdown();
            manager.Shutdown(); client.Shutdown();
            if (lateObject != null) Object.DestroyImmediate(lateObject);
            Object.DestroyImmediate(prefab); Object.DestroyImmediate(registryObject); Object.DestroyImmediate(catalog); Object.DestroyImmediate(hat); Object.DestroyImmediate(crown);
        }
    }
    [UnityTest]
    public IEnumerator MissingClientVisualMapping_IsFatalAndIdentifiesCosmeticAndRevision()
    {
        var catalog = ScriptableObject.CreateInstance<CosmeticVisualCatalog>();
        var prefab = CreatePlayerTemplate(); prefab.AddComponent<PlayerAccessory>();
        var renderer = prefab.AddComponent<CosmeticRenderer>();
        renderer.catalog = catalog;
        renderer.slots = new[] { new CosmeticSlotBinding { slotId = "headwear", attachment = prefab.transform } };
        manager.NetworkConfig.PlayerPrefab = prefab; manager.AddNetworkPrefab(prefab);
        managerObject.AddComponent<VerifiedPlayerSpawner>();
        gate.ConfigureEquipment(new EquipmentProofVerifier(verifier), new EquipmentSigningBackend(signer));
        try
        {
            Assert.That(gate.StartHost(verifier, new SigningBackend(signer)), Is.True);
            float deadline = Time.realtimeSinceStartup + 4;
            while (string.IsNullOrEmpty(lastFailure) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(lastFailure, Does.Contain("cosmetic 1").And.Contain("revision 1"));
            while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsListening, Is.False);
        }
        finally { manager.Shutdown(); Object.DestroyImmediate(prefab); Object.DestroyImmediate(catalog); }
    }
    [UnityTest]
    public IEnumerator EquipmentRequiredClient_RejectsIdentityOnlyServer()
    {
        var clientGate = CreateRemoteClient();
        clientGate.ConfigureEquipment(new EquipmentProofVerifier(verifier), new EquipmentSigningBackend(signer));
        Assert.That(gate.StartServer(verifier), Is.True);
        Assert.That(clientGate.StartClient(new SigningBackend(signer)), Is.True);
        float deadline = Time.realtimeSinceStartup + 4;
        while (client.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(clientGate.IsLocalVerified, Is.False);
        Assert.That(lastFailure, Does.Contain("Equipment"));
        Assert.That(manager.IsListening, Is.True);
    }
    private sealed class PublicationService : IEquipmentService
    {
        public long Revision = 1;
        public bool HasSnapshot => true; public bool IsBusy => false; public bool RequiresRefresh => false; public bool IsPreviewActive => false;
        public EquipmentState Snapshot => new EquipmentState(new[]{new EquipmentSlot("headwear",Array.Empty<string>())},new Dictionary<string,string>{{"1","headwear"}},new EquipmentLoadout(Revision,new Dictionary<string,string>{{"headwear","1"}}));
        public EquipmentLoadout Presentation => Snapshot.Loadout;
        public event Action StateChanged; public event Action PreviewChanged { add { } remove { } }
        public void Notify() => StateChanged?.Invoke();
        public IEnumerator Refresh(Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator ApplyItems(IEnumerable<string> ids,Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator Unequip(IEnumerable<string> ids,Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator Clear(Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public void Preview(IEnumerable<string> ids)=>throw new NotSupportedException(); public void ClearPreview() { }
    }
    private sealed class EquipmentSigningBackend : IEquipmentProofBackend
    {
        private readonly RSA signer;
        public bool ShouldWait; public long Revision = 1; public string PlayerId = "42"; public string ItemId = "1"; public int RequestCount;
        public EquipmentSigningBackend(RSA signer) { this.signer = signer; }
        public IEnumerator RequestProof(string sessionId, string challenge, Action<string> success, Action<IdentityProofRequestFailure> failure)
        {
            RequestCount++;
            long capturedRevision = Revision;
            string capturedItem = ItemId;
            while (ShouldWait) yield return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var claims = new JObject { ["iss"]="issuer",["aud"]="game",["env"]="test",["content"]="v1",["sub"]=PlayerId,["name"]="Backend player",["sid"]=sessionId,["nonce"]=challenge,["iat"]=now,["nbf"]=now,["exp"]=now+60,["jti"]=Guid.NewGuid().ToString("N"),["ver"]=1,["revision"]=capturedRevision,["items"]=new JObject{["headwear"]=capturedItem} };
            string content = EncodeEquipment(new JObject{["alg"]="RS256",["kid"]="test",["typ"]="liveops-equipment+jwt"}.ToString(Formatting.None))+"."+EncodeEquipment(claims.ToString(Formatting.None));
            success(content+"."+Convert.ToBase64String(signer.SignData(Encoding.ASCII.GetBytes(content),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)).TrimEnd('=').Replace('+','-').Replace('/','_'));
        }
        private static string EncodeEquipment(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+','-').Replace('/','_');
    }

    public sealed class SigningBackend : IIdentityProofBackend
    {
        private readonly RSA signer;
        public int RequestCount { get; private set; }
        public bool HasDisposed { get; private set; }
        public string LastAssertion { get; private set; }
        public bool ShouldTamper { get; set; }
        public bool ShouldWait { get; set; }
        public bool ShouldThrowOnDispose { get; set; }
        public float DelaySeconds { get; set; }
        public int UnavailableResponsesRemaining { get; set; }
        public SigningBackend(RSA signer) { this.signer = signer; }
        public IEnumerator RequestProof(string sessionId, string challenge, Action<string> onAssertion, Action<IdentityProofRequestFailure> onFailure)
        {
            RequestCount++;
            try
            {
                yield return null;
                if (DelaySeconds > 0) yield return new WaitForSeconds(DelaySeconds);
                while (ShouldWait) yield return null;
                if (UnavailableResponsesRemaining > 0)
                {
                    UnavailableResponsesRemaining--;
                    onFailure(IdentityProofRequestFailure.Unavailable);
                    yield break;
                }
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string header = Encode(JsonConvert.SerializeObject(new { alg = "RS256", typ = "liveops-identity+jwt", kid = "test" }));
                string payload = Encode(JsonConvert.SerializeObject(new { iss = "issuer", aud = "game", env = "test", content = "v1", sub = "42", name = "Backend player", sid = sessionId, nonce = challenge, iat = now, nbf = now, exp = now + 60, jti = Guid.NewGuid().ToString("N"), ver = 1 }));
                string input = header + "." + payload;
                byte[] signature = signer.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (ShouldTamper) signature[0] ^= 1;
                LastAssertion = input + "." + Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                onAssertion(LastAssertion);
            }
            finally { HasDisposed = true; if (ShouldThrowOnDispose) throw new InvalidOperationException("Expected backend disposal failure"); }
        }
        private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
public sealed class EquipmentVisualTestDouble : MonoBehaviour, IEquipmentRenderer
{
    private EquipmentLoadout displayed;
    public void Render(EquipmentLoadout loadout) { displayed = loadout; }
    public void Clear() { displayed = null; }
    public int GetActiveItemId(AccessoryType slot)
    {
        return displayed != null && displayed.ItemsBySlot.TryGetValue(slot.ToString().ToLowerInvariant(), out var itemId) ? int.Parse(itemId) : -1;
    }
}
#endif
