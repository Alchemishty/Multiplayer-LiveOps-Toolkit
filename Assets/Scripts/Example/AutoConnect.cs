using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Collections;

/// <summary>
/// Auto-networking: tries Client first, falls back to Host.
/// First build instance becomes Host, subsequent instances join as Client.
/// </summary>
public class AutoConnect : MonoBehaviour
{
    [SerializeField] private string address = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    [SerializeField] private float connectionTimeout = 3f;

    IEnumerator Start()
    {
        while (DevAuthBootstrap.Instance == null || !DevAuthBootstrap.Instance.IsAuthenticated)
            yield return null;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.ConnectionData.Address = address;
        transport.ConnectionData.Port = port;
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;

        NetworkManager.Singleton.StartClient();

        float timer = 0f;
        while (timer < connectionTimeout)
        {
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                yield break;
            }
            timer += Time.deltaTime;
            yield return null;
        }

        NetworkManager.Singleton.Shutdown();
        yield return new WaitForSeconds(0.5f);

        NetworkManager.Singleton.StartHost();
    }
}
