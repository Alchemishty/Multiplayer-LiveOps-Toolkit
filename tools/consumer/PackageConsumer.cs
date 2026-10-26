using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using System;
using System.Collections;
using System.IO;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.DailyRewards;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using MultiplayerLiveOpsToolkit.Backends.Django;
using UnityEngine;

public sealed class PackageConsumer : MonoBehaviour
{
    private PlayerContext context;
    private string failure;
    private IEnumerator Start()
    {
        string url = Environment.GetEnvironmentVariable("SMOKE_BACKEND");
        var session = gameObject.AddComponent<DjangoPlayerSession>();
        var playerModules = new DjangoPlayerModules(url, session, session, DjangoModules.All, exception => failure = exception.Message);
        context = playerModules.Context;
        var initialization = playerModules.Initialize(new DjangoPasswordIdentityProvider(Environment.GetEnvironmentVariable("SMOKE_USER"), Environment.GetEnvironmentVariable("SMOKE_PASSWORD")), () => { }, error => failure = error.Message);
        var wallet = playerModules.Wallet; var inventory = playerModules.Inventory; var shop = playerModules.Shop;
        var equipment = playerModules.Equipment; var achievements = playerModules.Achievements; var daily = playerModules.DailyRewards;
        yield return initialization;
        if (!Check(failure == null && context.IsAuthenticated && wallet.Snapshot.Balances["gold"].Amount == 100, "initialize selected modules")) yield break;
        yield return shop.Purchase("item:1", state => { }, error => failure = error.Message);
        if (!Check(failure == null && inventory.Snapshot.Items.ContainsKey("1") && wallet.Snapshot.Balances["gold"].Amount == 75, "purchase")) yield break;
        yield return equipment.ApplyItems(new[] { "1" }, () => { }, error => failure = error.Message);
        if (!Check(failure == null && equipment.Snapshot.Loadout.ItemsBySlot["headwear"] == "1", "equipment")) yield break;
        if (!Check(VerifyVisuals(equipment), "package visuals and local preview")) yield break;
        yield return achievements.Refresh(() => { }, error => failure = error.Message);
        yield return achievements.Claim("first", () => { }, error => failure = error.Message);
        if (!Check(failure == null && wallet.Snapshot.Balances["gold"].Amount == 85, "achievement claim")) yield break;
        yield return daily.ClaimToday(() => { }, error => failure = error.Message);
        if (!Check(failure == null && !daily.Snapshot.CanClaimToday && wallet.Snapshot.Balances["gold"].Amount == 135, "daily claim")) yield break;
        context.Dispose();
        if (!Check(!wallet.HasSnapshot && !inventory.HasSnapshot && !shop.HasSnapshot && !equipment.HasSnapshot && !achievements.HasSnapshot && !daily.HasSnapshot, "private teardown")) yield break;
        Finish("PASS clean-project installed package: all modules, package visuals/preview, real Django mutations, private teardown", 0);
    }
    private bool VerifyVisuals(IEquipmentService equipment)
    {
        var avatar = new GameObject("Independent cosmetic avatar");
        var prefab = new GameObject("Cosmetic template");
        var catalog = ScriptableObject.CreateInstance<CosmeticVisualCatalog>();
        try
        {
            var baseMesh = new GameObject("Base mesh"); baseMesh.transform.SetParent(avatar.transform, false);
            var mount = new GameObject("Custom mount"); mount.transform.SetParent(avatar.transform, false);
            var renderer = avatar.AddComponent<CosmeticRenderer>();
            renderer.catalog = catalog;
            renderer.slots = new[] { new CosmeticSlotBinding { slotId = "headwear", attachment = mount.transform, baseObjects = new[] { baseMesh } } };
            catalog.cosmetics = new[] { new CosmeticVisualDefinition { itemId = "1", slotId = "headwear", prefab = prefab } };
            using (var presentation = new EquipmentVisualPresenter(renderer, message => failure = message))
            {
                presentation.Bind(equipment);
                if (baseMesh.activeSelf || mount.transform.childCount != 1) return false;
                var original = mount.transform.GetChild(0);
                equipment.Preview(new[] { "1" }); equipment.ClearPreview();
                if (mount.transform.GetChild(0) != original || !original.gameObject.activeSelf) return false;
            }
            return baseMesh.activeSelf && !mount.transform.GetChild(0).gameObject.activeSelf && failure == null;
        }
        catch (Exception exception) { failure = exception.Message; return false; }
        finally { Destroy(avatar); Destroy(prefab); Destroy(catalog); }
    }
    private bool Check(bool condition, string step)
    {
        if (condition) return true;
        if (context != null) context.Dispose(); Finish("FAIL " + step + ": " + failure, 1); return false;
    }
    private static void Finish(string result, int code) { File.WriteAllText(Environment.GetEnvironmentVariable("SMOKE_RESULT"), result); Application.Quit(code); }
}
