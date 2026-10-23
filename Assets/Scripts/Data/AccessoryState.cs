using Unity.Netcode;

/// <summary>
/// Network-serializable accessory state. Wraps a JSON string like
/// {"Head":5,"Body":12} and syncs it across all connected clients
/// via NetworkVariable. Owner writes, everyone reads.
/// </summary>
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
