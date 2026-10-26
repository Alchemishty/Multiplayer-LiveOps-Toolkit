using Unity.Netcode.Components;

namespace MultiplayerLiveOpsToolkit.CharacterControls
{
[UnityEngine.DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}

}
