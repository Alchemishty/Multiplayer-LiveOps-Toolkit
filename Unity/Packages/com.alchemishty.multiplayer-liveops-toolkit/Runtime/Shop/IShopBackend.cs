using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Shop
{
    public interface IShopBackend
    {
        IEnumerator Fetch(Action<ShopState> onSuccess, Action<ShopError> onFailure);
        IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure);
    }
}
