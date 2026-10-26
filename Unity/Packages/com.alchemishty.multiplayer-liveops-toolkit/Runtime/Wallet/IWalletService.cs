using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Wallet
{
    public interface IWalletService
    {
        bool HasSnapshot { get; }
        bool IsBusy { get; }
        WalletSnapshot Snapshot { get; }
        event Action SnapshotChanged;
        IEnumerator Refresh(Action onSuccess, Action<WalletError> onFailure);
        IEnumerator Convert(string offerId, long amount, Action onSuccess, Action<WalletError> onFailure);
    }
}
