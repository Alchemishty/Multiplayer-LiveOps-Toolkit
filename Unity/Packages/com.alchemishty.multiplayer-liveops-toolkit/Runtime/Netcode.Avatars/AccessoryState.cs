using Unity.Netcode;

/// <summary>
/// Network-serializable accessory state. Wraps a JSON string like
/// {"revision":1,"items":{"headwear":"5"}} and syncs it across all connected clients
/// via NetworkVariable. Server writes verified state; everyone reads.
/// </summary>
namespace MultiplayerLiveOpsToolkit.Netcode.Avatars
{
public struct AccessoryState : INetworkSerializable
{
    public string Json;

    public AccessoryState(string json)
    {
        Json = json;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Json);
    }
}

}
