using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.DailyRewards;
namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    [Flags]
    public enum DjangoModules { Identity = 0, Wallet = 1, Inventory = 2, Shop = 4, Equipment = 8, Achievements = 16, DailyRewards = 32, All = 63 }
    public sealed class DjangoPlayerModules : IDisposable
    {
        private readonly string backendUrl;
        private readonly ITokenStore tokenStore;
        private readonly DjangoModules selection;
        private readonly Action<Exception> reportSubscriberException;
        public PlayerContext Context { get; private set; }
        public IWalletService Wallet { get; private set; }
        public IInventoryService Inventory { get; private set; }
        public IShopService Shop { get; private set; }
        public IEquipmentService Equipment { get; private set; }
        public IEquipmentProofBackend EquipmentProofs { get; private set; }
        public IAchievementService Achievements { get; private set; }
        public IDailyRewardService DailyRewards { get; private set; }
        public DjangoPlayerModules(string backendUrl, IAuthenticatedSession session, ITokenStore tokenStore, DjangoModules selection, Action<Exception> reportSubscriberException)
        {
            if ((selection & ~DjangoModules.All) != 0 || (selection.HasFlag(DjangoModules.Shop) && (!selection.HasFlag(DjangoModules.Wallet) || !selection.HasFlag(DjangoModules.Inventory)))
                || (selection.HasFlag(DjangoModules.Equipment) && !selection.HasFlag(DjangoModules.Inventory)))
                throw new ArgumentException("Select Wallet/Inventory for Shop and Inventory for Equipment; unknown modules are not supported.", nameof(selection));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
            this.backendUrl = backendUrl;
            this.selection = selection;
            Context = new PlayerContext(session, new DjangoIdentityProofAPI(backendUrl, session));
        }
        public IEnumerator Initialize(IIdentityProvider provider, Action onReady, Action<LiveOpsError> onFailure)
        {
            if (Context.IsAuthenticated) throw new InvalidOperationException("This account composition is already authenticated.");
            var modules = new List<ILiveOpsModule>();
            var wallet = selection.HasFlag(DjangoModules.Wallet) ? new WalletModule(new DjangoWalletBackend(backendUrl, Context.Session), reportSubscriberException) : null;
            var inventory = selection.HasFlag(DjangoModules.Inventory) ? new InventoryModule(new DjangoInventoryBackend(backendUrl, Context.Session), reportSubscriberException) : null;
            ShopModule shop = null; EquipmentModule equipment = null; AchievementsModule achievements = null; DailyRewardsModule daily = null;
            IEquipmentProofBackend equipmentProofs = null;
            if (wallet != null) modules.Add(wallet);
            if (inventory != null) modules.Add(inventory);
            if (selection.HasFlag(DjangoModules.Shop))
            {
                shop = new ShopModule(new DjangoShopBackend(backendUrl, Context.Session), wallet, inventory, reportSubscriberException);
                modules.Add(shop);
            }
            if (selection.HasFlag(DjangoModules.Equipment))
            {
                equipment = new EquipmentModule(new DjangoEquipmentBackend(backendUrl, Context.Session), inventory, reportSubscriberException);
                modules.Add(equipment);
                equipmentProofs = new DjangoEquipmentProofAPI(backendUrl, Context.Session);
            }
            if (selection.HasFlag(DjangoModules.Achievements))
            {
                achievements = new AchievementsModule(new DjangoAchievementBackend(backendUrl, Context.Session), reportSubscriberException, wallet, inventory);
                modules.Add(achievements);
            }
            if (selection.HasFlag(DjangoModules.DailyRewards))
            {
                daily = new DailyRewardsModule(new DjangoDailyRewardBackend(backendUrl, Context.Session), reportSubscriberException, wallet);
                modules.Add(daily);
            }
            var initialization = Context.Initialize(provider, new DjangoIdentityBackend(backendUrl, tokenStore), onReady, onFailure, modules);
            Wallet = wallet; Inventory = inventory; Shop = shop; Equipment = equipment;
            EquipmentProofs = equipmentProofs; Achievements = achievements; DailyRewards = daily;
            return initialization;
        }
        public void Dispose() => Context.Dispose();
    }
}
