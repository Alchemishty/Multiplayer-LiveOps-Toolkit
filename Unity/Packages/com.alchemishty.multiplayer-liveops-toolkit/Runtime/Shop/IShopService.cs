using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Shop
{
    public interface IShopService
    {
        bool HasSnapshot { get; }
        bool IsBusy { get; }
        bool RequiresRefresh { get; }
        ShopSnapshot Snapshot { get; }
        ShopError LastError { get; }
        event Action StateChanged;
        IEnumerator Refresh(Action onSuccess, Action<ShopError> onFailure);
        IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure);
    }
}
