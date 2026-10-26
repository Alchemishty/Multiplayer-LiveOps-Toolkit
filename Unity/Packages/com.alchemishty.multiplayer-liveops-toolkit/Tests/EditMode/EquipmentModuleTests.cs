using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Inventory;
using NUnit.Framework;

public class EquipmentModuleTests
{
    private EquipmentModule equipment;
    private InventoryModule inventory;
    private Backend backend;
    public static EquipmentState State(long revision = 0, params string[] ids)
    {
        var slots = new[] { new EquipmentSlot("hat", new[] { "hair" }), new EquipmentSlot("hair", new[] { "hat" }), new EquipmentSlot("hand", Array.Empty<string>()) };
        var mappings = new Dictionary<string,string> { {"cap","hat"}, {"wig","hair"}, {"sword","hand"}, {"unowned","hat"} };
        var loadout = new Dictionary<string,string>(); foreach (string id in ids) loadout.Add(mappings[id],id);
        return new EquipmentState(slots, mappings, new EquipmentLoadout(revision,loadout));
    }
    [SetUp] public void SetUp()
    {
        inventory = new InventoryModule(new InventoryBackend(), exception => Assert.Fail(exception.Message));
        WalletModuleTests.Drain(inventory.Initialize());
        backend = new Backend();
        equipment = new EquipmentModule(backend, inventory, exception => Assert.Fail(exception.Message));
        WalletModuleTests.Drain(equipment.Initialize());
    }
    [TearDown] public void TearDown() { equipment.Shutdown(); inventory.Shutdown(); }
    [Test] public void Initialize_IsIndependentAndHasCanonicalDefinitions() { Assert.That(equipment.Snapshot.Loadout.Revision,Is.Zero); Assert.That(equipment.Snapshot.Slots.Count,Is.EqualTo(3)); }
    [Test] public void ApplyItems_UsesConfiguredConflictsAndOneBackendUpdate()
    {
        Run(equipment.ApplyItems(new[]{"wig"},()=>{},Fail));
        Run(equipment.ApplyItems(new[]{"cap","sword"},()=>{},Fail));
        Assert.That(equipment.Snapshot.Loadout.ItemsBySlot.Values,Is.EquivalentTo(new[]{"cap","sword"}));
        Assert.That(backend.Writes,Is.EqualTo(2)); Assert.That(backend.LastRevision,Is.EqualTo(1));
    }
    [Test] public void UnownedAndConflictingSet_RejectBeforeDispatch()
    {
        EquipmentError error=null;
        Run(equipment.ApplyItems(new[]{"unowned"},()=>Assert.Fail(),value=>error=value));
        Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.Rejected));
        Run(equipment.ApplyItems(new[]{"cap","wig"},()=>Assert.Fail(),value=>error=value));
        Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.Rejected)); Assert.That(backend.Writes,Is.Zero);
    }
    [Test] public void Preview_IsLocalCanBeUnownedAndRestoresLatestCanonical()
    {
        equipment.Preview(new[]{"unowned"});
        Assert.That(equipment.Presentation.ItemsBySlot["hat"],Is.EqualTo("unowned"));
        Assert.That(equipment.Snapshot.Loadout.ItemsBySlot,Is.Empty); Assert.That(backend.Writes,Is.Zero);
        Run(equipment.ApplyItems(new[]{"sword"},()=>{},Fail));
        Assert.That(equipment.IsPreviewActive,Is.False); Assert.That(equipment.Presentation.ItemsBySlot["hand"],Is.EqualTo("sword"));
    }
    [Test] public void UnknownOutcome_BlocksMutationsUntilRefreshWithoutRetry()
    {
        backend.LoseResponse=true; EquipmentError error=null;
        Run(equipment.ApplyItems(new[]{"cap"},()=>Assert.Fail(),value=>error=value));
        Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.UnknownOutcome)); Assert.That(equipment.RequiresRefresh,Is.True);
        Run(equipment.Clear(()=>Assert.Fail(),value=>error=value)); Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.RefreshRequired)); Assert.That(backend.Writes,Is.EqualTo(1));
        backend.LoseResponse=false; Run(equipment.Refresh(()=>{},Fail)); Assert.That(equipment.RequiresRefresh,Is.False);
    }
    [Test] public void OlderBackendState_CannotReplaceCanonicalLoadout()
    {
        Run(equipment.ApplyItems(new[]{"cap"},()=>{},Fail)); backend.Current=State(); EquipmentError error=null;
        Run(equipment.Refresh(()=>Assert.Fail(),value=>error=value)); Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.InvalidResponse)); Assert.That(equipment.Snapshot.Loadout.Revision,Is.EqualTo(1));
    }
    [Test] public void SameRevisionDifferentLoadout_IsRejected()
    {
        Run(equipment.ApplyItems(new[]{"cap"},()=>{},Fail)); backend.Current=State(1,"sword"); EquipmentError error=null;
        Run(equipment.Refresh(()=>Assert.Fail("Revision reused for different equipment"),value=>error=value));
        Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.InvalidResponse));Assert.That(equipment.Snapshot.Loadout.ItemsBySlot["hat"],Is.EqualTo("cap"));
    }
    [Test] public void PublicState_RoundTripsOnlyEquippedIdsAndRejectsMalformedOrExtraFields()
    {
        var initial=State(3,"cap").Loadout;
        var parsed=EquipmentPublicState.Parse(EquipmentPublicState.Serialize(initial));
        Assert.That(parsed.Revision,Is.EqualTo(3));Assert.That(parsed.ItemsBySlot["hat"],Is.EqualTo("cap"));
        Assert.Throws<FormatException>(()=>EquipmentPublicState.Parse("{\"revision\":3,\"items\":{},\"inventory\":[]}"));
        Assert.Throws<Newtonsoft.Json.JsonReaderException>(()=>EquipmentPublicState.Parse("{\"revision\":3,\"revision\":4,\"items\":{}}"));
    }
    [Test] public void DisposingUnstartedAndPendingOperations_CancelsExactlyOnce()
    {
        int callbacks=0; var operation=equipment.Clear(()=>Assert.Fail(),error=>callbacks++); ((IDisposable)operation).Dispose(); Assert.That(callbacks,Is.EqualTo(1)); Assert.That(equipment.IsBusy,Is.False);
        backend.Wait=true; operation=equipment.Clear(()=>Assert.Fail(),error=>callbacks++); Assert.That(operation.MoveNext(),Is.True); equipment.Shutdown(); ((IDisposable)operation).Dispose(); Assert.That(callbacks,Is.EqualTo(2)); Assert.That(equipment.HasSnapshot,Is.False);
    }
    [Test] public void UnequipAndClear_PersistWholeLoadouts()
    {
        Run(equipment.ApplyItems(new[]{"cap","sword"},()=>{},Fail)); Run(equipment.Unequip(new[]{"hat"},()=>{},Fail)); Assert.That(equipment.Snapshot.Loadout.ItemsBySlot.Values,Is.EquivalentTo(new[]{"sword"})); Run(equipment.Clear(()=>{},Fail)); Assert.That(equipment.Snapshot.Loadout.ItemsBySlot,Is.Empty);
    }
    [Test] public void Models_RejectAsymmetricConflictsAndDuplicateItems()
    {
        Assert.Throws<ArgumentException>(()=>new EquipmentState(new[]{new EquipmentSlot("hat",new[]{"hair"}),new EquipmentSlot("hair",Array.Empty<string>())},new Dictionary<string,string>(),new EquipmentLoadout(0,new Dictionary<string,string>())));
        Assert.Throws<ArgumentException>(()=>new EquipmentLoadout(0,new Dictionary<string,string>{{"hat","cap"},{"hand","cap"}}));
    }
    private static void Run(IEnumerator routine)=>WalletModuleTests.Drain(routine);
    private static void Fail(EquipmentError error)=>Assert.Fail(error.Message);
    private sealed class Backend : IEquipmentBackend
    {
        public EquipmentState Current=State(); public int Writes; public long LastRevision; public bool LoseResponse; public bool Wait;
        public IEnumerator Fetch(Action<EquipmentState> success,Action<EquipmentError> failure) { success(Current); yield break; }
        public IEnumerator Replace(IReadOnlyList<string> ids,long revision,Action<EquipmentState> success,Action<EquipmentError> failure)
        {
            Writes++; LastRevision=revision; while(Wait) yield return null;
            if(LoseResponse) { failure(new EquipmentError(EquipmentErrorCode.UnknownOutcome,"Lost response")); yield break; }
            var copy=new List<string>(ids); Current=State(revision+1,copy.ToArray());success(Current);
        }
    }
    private sealed class InventoryBackend : IInventoryBackend
    {
        public IEnumerator Fetch(Action<InventorySnapshot> success,Action<InventoryError> failure) { success(new InventorySnapshot(new[]{new CosmeticItem("cap","Cap","hat"),new CosmeticItem("wig","Wig","hair"),new CosmeticItem("sword","Sword","hand")},Array.Empty<CosmeticSet>()));yield break; }
    }
}
