using MultiplayerLiveOpsToolkit.Backends.Django;
#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class IdentityBootstrapTests
{
    private GameObject bootstrapObject;
    private DjangoPlayerSession playerSession;
    private GameObject canvasObject;
    private HttpListener listener;
    private Task responseTask;
    private string originalUrl;
    private TMP_InputField passwordInput;
    private string requestPath;
    private string requestBody;
    private string accessToken;
    private GameObject networkObject;
    private GameObject avatarRegistryObject;
    private GameObject avatarObject;

    [SetUp]
    public void SetUp()
    {
        originalUrl = ApiConfig.BASE_URL;
        accessToken = SessionRenewalTests.Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (bootstrapObject != null) Object.DestroyImmediate(bootstrapObject);
        if (networkObject != null)
        {
            var manager = networkObject.GetComponent<NetworkManager>();
            manager.Shutdown();
            float deadline = Time.realtimeSinceStartup + 3;
            while ((manager.IsListening || manager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
            Object.DestroyImmediate(networkObject);
        }
        if (avatarObject != null) Object.DestroyImmediate(avatarObject);
        if (avatarRegistryObject != null) Object.DestroyImmediate(avatarRegistryObject);
        if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        listener?.Close();
        if (responseTask != null)
        {
            try { responseTask.GetAwaiter().GetResult(); }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }
        ApiConfig.SetBaseUrl(originalUrl);
        yield break;
    }

    [UnityTest]
    public IEnumerator Login_ValidResponse_AuthenticatesThenClearsPrivateSessionOnDestroy()
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Verified name\",\"player_id\":\"42\"}");
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.False);
        usernameInput.text = "Requested name";
        passwordInput.text = "sample password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.True, statusLabel.text);
        Assert.That(bootstrap.Context.CurrentPlayer.DisplayName, Is.EqualTo("Verified name"));
        Assert.That(playerSession.GetAccessToken(), Is.EqualTo(accessToken));
        Assert.That(requestPath, Is.EqualTo("/api/auth/password/"));
        Assert.That((string)JObject.Parse(requestBody)["password"], Is.EqualTo("sample password"));
        Assert.That(passwordInput.text, Is.Empty);
        Object.DestroyImmediate(bootstrapObject);
        Assert.That(DevAuthBootstrap.Instance, Is.Null);
        Assert.That(playerSession.GetAccessToken(), Is.Empty);
        Assert.That(bootstrap.Context.IsAuthenticated, Is.False);
        usernameInput.text = "After teardown";
        Assert.That(playButton.interactable, Is.False, "Destroyed bootstrap must remove UI listeners.");
    }

    [UnityTest]
    public IEnumerator Login_MissingStableIdentity_ReportsErrorWithoutPublishingTokens()
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Name\"}");
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "Requested name";
        passwordInput.text = "sample password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while ((string.IsNullOrEmpty(statusLabel.text) || statusLabel.text == "Authenticating...")
               && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.False);
        Assert.That(usernameInput.interactable, Is.True, statusLabel.text);
        Assert.That(playerSession.GetAccessToken(), Is.Empty);
        Assert.That(statusLabel.text, Does.Contain("player"));
    }

    [UnityTest]
    public IEnumerator Disable_AuthenticatedBootstrap_ClearsSessionBeforeObjectDestruction()
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Name\",\"player_id\":\"42\"}");
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "Name";
        passwordInput.text = "sample password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.True, statusLabel.text);
        bootstrapObject.SetActive(false);
        Assert.That(bootstrap.IsAuthenticated, Is.False);
        Assert.That(playerSession.GetAccessToken(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Login_BlankPassword_DoesNotEnableSubmission()
    {
        CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "player";
        Assert.That(playButton.interactable, Is.False);
        passwordInput.text = "password";
        Assert.That(playButton.interactable, Is.True);
        passwordInput.text = "";
        Assert.That(playButton.interactable, Is.False);
    }

    [UnityTest]
    public IEnumerator Login_RejectedCredentials_ClearsPasswordWithoutDisplayingRawResponse()
    {
        StartServer("{\"message\":\"private-server-response\"}", 401);
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "player";
        passwordInput.text = "wrong password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!statusLabel.text.Contains("Invalid username or password") && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.False);
        Assert.That(passwordInput.text, Is.Empty);
        Assert.That(statusLabel.text, Does.Contain("Invalid username or password"));
        Assert.That(statusLabel.text, Does.Not.Contain("private-server-response"));
        Assert.That(playerSession.GetAccessToken(), Is.Empty);
        passwordInput.text = "corrected password";
        Assert.That(playButton.interactable, Is.True);
    }

    [UnityTest]
    public IEnumerator Renewal_RejectedAfterLogin_StopsSampleAuthentication()
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Name\",\"player_id\":\"42\"}", rejectRenewal: true);
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "Name";
        passwordInput.text = "password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.True, statusLabel.text);
        yield return playerSession.RefreshToken();
        Assert.That(bootstrap.IsAuthenticated, Is.False);
        Assert.That(playerSession.GetAccessToken(), Is.Empty);
        Assert.That(playButton.interactable, Is.False);
        Assert.That(statusLabel.text, Does.Contain("session"));
    }

    [UnityTest]
    public IEnumerator Disable_ConnectedBootstrap_ShutsDownHost()
    {
        yield return VerifyHostTeardown(false);
    }

    [UnityTest]
    public IEnumerator Destroy_ConnectedBootstrap_ShutsDownHost()
    {
        yield return VerifyHostTeardown(true);
    }

    private IEnumerator VerifyHostTeardown(bool shouldDestroy)
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Name\",\"player_id\":\"42\"}");
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "Name";
        passwordInput.text = "password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.True, statusLabel.text);
        var manager = CreateNetworkManager();
        Assert.That(manager.StartHost(), Is.True);
        yield return null;
        Assert.That(manager.IsListening, Is.True);
        if (shouldDestroy) Object.DestroyImmediate(bootstrapObject);
        else bootstrapObject.SetActive(false);
        deadline = Time.realtimeSinceStartup + 2;
        while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.IsListening, Is.False, "Identity teardown must also terminate its multiplayer session.");
    }

    [UnityTest]
    public IEnumerator Login_ThrowingAuthenticationSubscriber_DeliversLaterSubscriberAndHidesPanel()
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Name\",\"player_id\":\"42\"}");
        avatarRegistryObject = new GameObject("Avatar registry under test");
        var registry = avatarRegistryObject.AddComponent<ClientPlayerAvatars>();
        avatarObject = new GameObject("Local avatar under test");
        registry.RegisterAvatar(0, avatarObject, true);
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        bool hasReceivedAuthentication = false;
        Action throwingSubscriber = () => throw new InvalidOperationException("Expected subscriber failure");
        Action laterSubscriber = () => hasReceivedAuthentication = true;
        bootstrap.OnAuthenticated += throwingSubscriber;
        bootstrap.OnAuthenticated += laterSubscriber;
        try
        {
            yield return null;
            LogAssert.Expect(LogType.Exception, new Regex("Expected subscriber failure"));
            usernameInput.text = "Name";
            passwordInput.text = "password";
            playButton.onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 5;
            while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Assert.That(hasReceivedAuthentication, Is.True);
            Assert.That(canvasObject.transform.GetChild(0).gameObject.activeSelf, Is.False);
        }
        finally
        {
            bootstrap.OnAuthenticated -= throwingSubscriber;
            bootstrap.OnAuthenticated -= laterSubscriber;
        }
    }

    [UnityTest]
    public IEnumerator AutoConnect_AuthenticationLostDuringConnection_StopsClientAndNeverHosts()
    {
        yield return VerifyAutoConnectCancellation(false);
    }

    [UnityTest]
    public IEnumerator AutoConnect_AuthenticationLostBeforeHostFallback_DoesNotHost()
    {
        yield return VerifyAutoConnectCancellation(true);
    }

    [UnityTest]
    public IEnumerator AutoConnect_DisabledDuringConnection_StopsWithoutEndingAuthenticatedAccount()
    {
        yield return VerifyAutoConnectCancellation(false, true);
    }

    private IEnumerator VerifyAutoConnectCancellation(bool shouldWaitForFallback, bool shouldDisableConnection = false)
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Name\",\"player_id\":\"42\"}");
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        yield return null;
        usernameInput.text = "Name";
        passwordInput.text = "password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.True, statusLabel.text);
        var manager = CreateNetworkManager();
        var reservation = new UdpClient(0);
        ushort unusedPort = (ushort)((IPEndPoint)reservation.Client.LocalEndPoint).Port;
        reservation.Close();
        var autoConnect = networkObject.AddComponent<AutoConnect>();
        using (var signer = new System.Security.Cryptography.RSACryptoServiceProvider(2048))
        {
            autoConnect.ConfigureIdentity(new MultiplayerLiveOpsToolkit.Identity.IdentityProofVerifier(
                new System.Collections.Generic.Dictionary<string, System.Security.Cryptography.RSAParameters> { { "test", signer.ExportParameters(false) } },
                new MultiplayerLiveOpsToolkit.Identity.IdentityProofPolicy("issuer", "game", "test", "v1", 60, 0)),
                new NetcodeIdentityGateTests.SigningBackend(signer));
        }
        var settings = new SerializedObject(autoConnect);
        settings.FindProperty("authBootstrap").objectReferenceValue = bootstrap;
        settings.FindProperty("port").intValue = unusedPort;
        settings.FindProperty("connectionTimeout").floatValue = shouldWaitForFallback ? 0.05f : 2f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        deadline = Time.realtimeSinceStartup + 2;
        while (!manager.IsClient && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(manager.IsClient, Is.True);
        if (shouldWaitForFallback)
        {
            while (manager.IsListening && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsListening, Is.False, "Test must cancel while host fallback is pending.");
        }
        if (shouldDisableConnection) autoConnect.enabled = false;
        else bootstrapObject.SetActive(false);
        yield return new WaitForSecondsRealtime(0.75f);
        if (shouldDisableConnection) Assert.That(bootstrap.IsAuthenticated, Is.True);
        Assert.That(manager.IsListening, Is.False);
        Assert.That(manager.IsHost, Is.False);
    }

    private NetworkManager CreateNetworkManager()
    {
        networkObject = new GameObject("Network manager under test");
        var manager = networkObject.AddComponent<NetworkManager>();
        var transport = networkObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 0);
        manager.NetworkConfig = new NetworkConfig();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.EnableSceneManagement = false;
        return manager;
    }

    [UnityTest]
    public IEnumerator Login_InventorySelectedWithoutWallet_RefreshOutlivesDisabledView()
    {
        StartServer("{\"access\":\"sample-access\",\"refresh\":\"sample-refresh\",\"username\":\"Verified name\",\"player_id\":\"42\"}", serveInventory: true);
        var bootstrap = CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel);
        var fields = new SerializedObject(bootstrap);
        fields.FindProperty("enableInventory").boolValue = true;
        fields.ApplyModifiedPropertiesWithoutUndo();
        yield return null;
        usernameInput.text = "player";
        passwordInput.text = "sample password";
        playButton.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.IsAuthenticated && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.IsAuthenticated, Is.True, statusLabel.text);
        Assert.That(bootstrap.Wallet, Is.Null);
        Assert.That(bootstrap.Inventory.Snapshot.Items.Count, Is.Zero);
        bootstrap.RefreshInventory();
        canvasObject.SetActive(false);
        deadline = Time.realtimeSinceStartup + 5;
        while (!bootstrap.Inventory.Snapshot.Items.ContainsKey("hat") && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(bootstrap.Inventory.Snapshot.Items.ContainsKey("hat"), Is.True);
    }

    private DevAuthBootstrap CreateBootstrap(out TMP_InputField usernameInput, out Button playButton, out TMP_Text statusLabel)
    {
        canvasObject = new GameObject("Login test canvas", typeof(Canvas));
        var loginPanel = new GameObject("Login panel", typeof(RectTransform));
        loginPanel.transform.SetParent(canvasObject.transform);
        usernameInput = new GameObject("Username", typeof(RectTransform)).AddComponent<TMP_InputField>();
        usernameInput.transform.SetParent(loginPanel.transform);
        passwordInput = new GameObject("Password", typeof(RectTransform)).AddComponent<TMP_InputField>();
        passwordInput.transform.SetParent(loginPanel.transform);
        playButton = new GameObject("Play", typeof(RectTransform)).AddComponent<Button>();
        playButton.transform.SetParent(loginPanel.transform);
        statusLabel = new GameObject("Status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        statusLabel.transform.SetParent(loginPanel.transform);
        statusLabel.text = "";
        bootstrapObject = new GameObject("Bootstrap under test");
        bootstrapObject.SetActive(false);
        var bootstrap = bootstrapObject.AddComponent<DevAuthBootstrap>();
        playerSession = bootstrapObject.GetComponent<DjangoPlayerSession>();
        var serializedBootstrap = new SerializedObject(bootstrap);
        serializedBootstrap.FindProperty("enableWallet").boolValue = false;
        serializedBootstrap.FindProperty("enableInventory").boolValue = false;
        serializedBootstrap.FindProperty("enableShop").boolValue = false;
        serializedBootstrap.FindProperty("enableEquipment").boolValue = false;
        serializedBootstrap.FindProperty("enableAchievements").boolValue = false;
        serializedBootstrap.FindProperty("enableDailyRewards").boolValue = false;
        serializedBootstrap.FindProperty("loginPanel").objectReferenceValue = loginPanel;
        serializedBootstrap.FindProperty("usernameInput").objectReferenceValue = usernameInput;
        serializedBootstrap.FindProperty("passwordInput").objectReferenceValue = passwordInput;
        serializedBootstrap.FindProperty("playButton").objectReferenceValue = playButton;
        serializedBootstrap.FindProperty("statusLabel").objectReferenceValue = statusLabel;
        serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
        bootstrapObject.SetActive(true);
        return bootstrap;
    }

    private void StartServer(string responseBody, int responseCode = 200, bool rejectRenewal = false, bool serveInventory = false)
    {
        var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        int port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        string baseUrl = $"http://127.0.0.1:{port}";
        listener = new HttpListener();
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        ApiConfig.SetBaseUrl(baseUrl);
        responseTask = Task.Run(async () =>
        {
            for (int responseIndex = 0; responseIndex < (serveInventory ? 3 : rejectRenewal ? 2 : 1); responseIndex++)
            {
                var context = await listener.GetContextAsync();
                requestPath = context.Request.Url.AbsolutePath;
                using (var reader = new StreamReader(context.Request.InputStream)) requestBody = await reader.ReadToEndAsync();
                string body = responseBody.Replace("sample-access", accessToken);
                if (serveInventory && requestPath == "/api/inventory/")
                    body = responseIndex == 1 ? "{\"items\":[],\"sets\":[]}" : "{\"items\":[{\"item_id\":\"hat\",\"display_name\":\"Hat\",\"category_id\":\"headwear\"}],\"sets\":[]}";
                if (serveInventory && responseIndex == 2) await Task.Delay(150);
                byte[] responseBytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = rejectRenewal && responseIndex == 1 ? 401 : responseCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = responseBytes.Length;
                await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                context.Response.Close();
            }
        });
    }
}
#endif
