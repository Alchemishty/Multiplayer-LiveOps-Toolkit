using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Equipment;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;

public class InventoryPanelTests
{
    private GameObject root;
    private InventoryModule module;
    private Transform content;
    private BackendStub backend;
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        backend = new BackendStub();
        module = new InventoryModule(backend, exception => Assert.Fail(exception.Message));
        yield return module.Initialize();
        root = new GameObject("Inventory without Shop or Netcode", typeof(RectTransform));
        root.SetActive(false);
        content = new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(root.transform);
        var status = new GameObject("Status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        status.transform.SetParent(root.transform);
        var panel = root.AddComponent<InventoryPanel>();
#if UNITY_EDITOR
        var fields = new SerializedObject(panel);
        fields.FindProperty("contentContainer").objectReferenceValue = content;
        fields.FindProperty("statusText").objectReferenceValue = status;
        fields.ApplyModifiedPropertiesWithoutUndo();
#endif
        panel.Bind(module);
        root.SetActive(true);
        yield return null;
    }
    [TearDown]
    public void TearDown() { module.Shutdown(); Object.DestroyImmediate(root); }
    [UnityTest]
    public IEnumerator BoundService_RendersOwnedItemsAndSetsWithoutAvatar_AndDisablesEquip()
    {
        Assert.That(content.Find("Item_hat"), Is.Not.Null);
        Assert.That(content.Find("Set_outfit"), Is.Not.Null);
        foreach (var button in content.GetComponentsInChildren<Button>()) Assert.That(button.interactable, Is.False);
        yield break;
    }
    [UnityTest]
    public IEnumerator Refresh_ReplacesRowsAndShutdownClearsPrivateContent()
    {
        Assert.That(content.Find("Item_hat"), Is.Not.Null);
        backend.ShouldBeEmpty = true;
        yield return module.Refresh(() => { }, error => Assert.Fail(error.Message));
        yield return null;
        Assert.That(content.Find("Item_hat"), Is.Null);
        Assert.That(content.Find("Set_outfit"), Is.Null);
        module.Shutdown();
        yield return null;
        Assert.That(content.childCount, Is.Zero);
    }
    [UnityTest]
    public IEnumerator DisabledPanel_ReenableRendersCurrentSnapshotWithoutDuplicateRows()
    {
        Assert.That(content.Find("Item_hat"), Is.Not.Null);
        root.SetActive(false);
        backend.ShouldBeEmpty = true;
        yield return module.Refresh(() => { }, error => Assert.Fail(error.Message));
        root.SetActive(true);
        yield return null;
        Assert.That(content.Find("Item_hat"), Is.Null);
        yield return null;
        Assert.That(content.GetComponentsInChildren<Button>().Length, Is.Zero);
    }
    [UnityTest]
    public IEnumerator EquipmentBinding_EnablesCommandsWithoutAvatarAndSurvivesTabDisable()
    {
        var panel = root.GetComponent<InventoryPanel>();
        Assert.Throws<ArgumentNullException>(() => panel.BindEquipment(null));
        var equipmentBackend = new EquipmentBackend();
        var equipment = new EquipmentModule(equipmentBackend, module, exception => Assert.Fail(exception.Message));
        yield return equipment.Initialize();
        var owner = new GameObject("Durable equipment commands").AddComponent<AccessoryEquipService>();
        owner.Bind(equipment, module, new Session(), panel);
        try
        {
            var button = content.Find("Item_hat").GetComponentInChildren<Button>();
            Assert.That(button.interactable, Is.True);
            button.onClick.Invoke();
            root.SetActive(false);
            yield return null;
            equipmentBackend.Wait = false;
            float deadline = Time.realtimeSinceStartup + 2;
            while (equipment.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(equipment.Snapshot.Loadout.ItemsBySlot["headwear"], Is.EqualTo("hat"));
            root.SetActive(true);
            yield return null;
            Assert.That(content.Find("Item_hat").GetComponentInChildren<TMP_Text>().text, Is.Not.Empty);
        }
        finally { Object.DestroyImmediate(owner.gameObject); equipment.Shutdown(); }
    }
    [UnityTest]
    public IEnumerator UnknownEquipmentOutcome_OffersExplicitRefreshAndConfirmsCanonicalState()
    {
        var panel = root.GetComponent<InventoryPanel>();
        var backend = new EquipmentBackend { Wait = false, LoseResponse = true };
        var service = new EquipmentModule(backend, module, exception => Assert.Fail(exception.Message));
        yield return service.Initialize();
        var commands = new GameObject("Equipment refresh owner").AddComponent<AccessoryEquipService>();
        commands.Bind(service, module, new Session(), panel);
        try
        {
            LogAssert.Expect(LogType.Warning, "Equipment: Lost equipment response");
            content.Find("Item_hat").GetComponentInChildren<Button>().onClick.Invoke();
            yield return null;
            Assert.That(service.RequiresRefresh, Is.True);
            var refresh = content.Find("EquipmentRefresh");
            Assert.That(refresh, Is.Not.Null);
            backend.LoseResponse = false;
            refresh.GetComponentInChildren<Button>().onClick.Invoke();
            yield return null;
            Assert.That(service.RequiresRefresh, Is.False);
            Assert.That(service.Snapshot.Loadout.ItemsBySlot["headwear"], Is.EqualTo("hat"));
        }
        finally { Object.DestroyImmediate(commands.gameObject); service.Shutdown(); }
    }
    [UnityTest]
    public IEnumerator AvatarReplacement_RebindsPreviewAndRemovesOldSubscriptions()
    {
        var service = new PreviewBindingService();
        var commands = new GameObject("Equipment preview owner").AddComponent<AccessoryEquipService>();
        var avatars = new GameObject("Avatar lifecycle").AddComponent<ClientPlayerAvatars>();
        var first = new GameObject("First avatar", typeof(Unity.Netcode.NetworkObject), typeof(PlayerAccessory), typeof(EquipmentVisualTestDouble));
        var replacement = new GameObject("Replacement avatar", typeof(Unity.Netcode.NetworkObject), typeof(PlayerAccessory), typeof(EquipmentVisualTestDouble));
        commands.Bind(service, module, new Session());
        commands.BindAvatars(avatars);
        try
        {
            Assert.That(service.PreviewSubscribers, Is.EqualTo(1));
            avatars.RegisterAvatar(0, first, true);
            Assert.That(service.PreviewSubscribers, Is.EqualTo(2));
            avatars.RemoveAvatar(0);
            Assert.That(service.PreviewSubscribers, Is.EqualTo(1));
            avatars.RegisterAvatar(0, replacement, true);
            Assert.That(service.PreviewSubscribers, Is.EqualTo(2));
            commands.enabled = false;
            Assert.That(service.PreviewSubscribers, Is.Zero);
            commands.enabled = true;
            Assert.That(service.PreviewSubscribers, Is.EqualTo(2));
            yield return null;
        }
        finally { Object.DestroyImmediate(commands.gameObject); Object.DestroyImmediate(avatars.gameObject); Object.DestroyImmediate(first); Object.DestroyImmediate(replacement); }
    }
    [UnityTest]
    public IEnumerator UnspawnedAvatar_Destroyed_ReleasesPreviewSubscription()
    {
        var service = new PreviewBindingService();
        var avatar = new GameObject("Unspawned avatar", typeof(Unity.Netcode.NetworkObject), typeof(PlayerAccessory), typeof(EquipmentVisualTestDouble));
        avatar.GetComponent<PlayerAccessory>().BindPresentation(service);
        Assert.That(service.PreviewSubscribers, Is.EqualTo(1));
        Object.Destroy(avatar);
        yield return null;
        Assert.That(service.PreviewSubscribers, Is.Zero);
    }
    private sealed class PreviewBindingService : IEquipmentService
    {
        public int PreviewSubscribers; public bool HasSnapshot => false; public bool IsBusy => false; public bool RequiresRefresh => false; public bool IsPreviewActive => false;
        public EquipmentState Snapshot => throw new NotSupportedException(); public EquipmentLoadout Presentation => throw new NotSupportedException();
        public event Action StateChanged { add { } remove { } } public event Action PreviewChanged { add { PreviewSubscribers++; } remove { PreviewSubscribers--; } }
        public IEnumerator Refresh(Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator ApplyItems(IEnumerable<string> ids,Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator Unequip(IEnumerable<string> ids,Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator Clear(Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public void Preview(IEnumerable<string> ids)=>throw new NotSupportedException();public void ClearPreview() { }
    }
    private sealed class EquipmentBackend : IEquipmentBackend
    {
        public bool Wait = true;
        public bool LoseResponse;
        private EquipmentState current = State(0, false);
        private static EquipmentState State(long revision, bool equipped) => new EquipmentState(new[] { new EquipmentSlot("headwear", Array.Empty<string>()) }, new Dictionary<string,string>{{"hat","headwear"}}, new EquipmentLoadout(revision, equipped ? new Dictionary<string,string>{{"headwear","hat"}} : new Dictionary<string,string>()));
        public IEnumerator Fetch(Action<EquipmentState> success, Action<EquipmentError> failure) { success(current); yield break; }
        public IEnumerator Replace(IReadOnlyList<string> ids, long revision, Action<EquipmentState> success, Action<EquipmentError> failure) { while (Wait) yield return null; current = State(revision+1,ids.Count>0); if (LoseResponse) failure(new EquipmentError(EquipmentErrorCode.UnknownOutcome,"Lost equipment response")); else success(current); }
    }
    private sealed class Session : IAuthenticatedSession
    {
        public bool IsActive => true; public int SessionVersion => 1; public event Action<string> SessionEnded { add { } remove { } }
        public IEnumerator Send(UnityEngine.Networking.UnityWebRequest request, ApiResponse response, Action<bool> complete = null) { throw new NotSupportedException(); }
        public void RejectSession(int version, string reason = null) { Assert.Fail(reason); } public void Dispose() { }
    }
    private sealed class BackendStub : IInventoryBackend
    {
        public bool ShouldBeEmpty;
        public IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure)
        {
            onSuccess(ShouldBeEmpty ? new InventorySnapshot(Array.Empty<CosmeticItem>(), Array.Empty<CosmeticSet>()) : new InventorySnapshot(
                new[] { new CosmeticItem("hat", "Hat", "headwear") }, new[] { new CosmeticSet("outfit", "Outfit", new[] { "hat" }) }));
            yield break;
        }
    }
}
