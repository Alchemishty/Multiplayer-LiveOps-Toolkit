

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    public interface ITokenStore
    {
        void SetTokens(string accessToken, string refreshToken, string renewalEndpoint);
        void ClearTokens();
    }
}
