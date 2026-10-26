using Unity.Netcode.Components;

namespace MultiplayerLiveOpsToolkit.CharacterControls
{
[UnityEngine.DisallowMultipleComponent]
public class ClientNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}

}
