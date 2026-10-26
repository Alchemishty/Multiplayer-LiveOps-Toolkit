using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Wallet
{
    public interface IWalletBackend
    {
        IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure);
        IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure);
    }
}
