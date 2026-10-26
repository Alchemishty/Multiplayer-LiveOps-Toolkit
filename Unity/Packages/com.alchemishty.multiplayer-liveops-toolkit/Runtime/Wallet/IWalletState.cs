namespace MultiplayerLiveOpsToolkit.Wallet
{
    /// <summary>Shop's confirmed-state write boundary. Stage both account states before ending either update.</summary>
    public interface IWalletState : IWalletService
    {
        void BeginUpdate();
        void StageSnapshot(WalletSnapshot snapshot);
        void EndUpdate();
    }
}
