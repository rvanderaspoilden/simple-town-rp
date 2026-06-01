using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using Sim;
using Sim.Building;
using Sim.Entities;
using Sim.Entities.Persistence;
using Sim.Deployment;
using Sim.Logging;
#if STRESS_TEST_BOTS
using Sim.StressTest;
#endif
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/*
    Documentation: https://mirror-networking.com/docs/Components/NetworkManager.html
    API Reference: https://mirror-networking.com/docs/api/Mirror.NetworkManager.html
*/

public class SimpleTownNetwork : NetworkManager
{
    [Header("Settings")] [SerializeField] private string cityName = "Simple Town";

    [SerializeField] private bool useElbloodyAccount;

    [SerializeField] private bool useSpectusAccount;

    [Header("Debug")] [SerializeField] private CharacterData characterData;

    [SerializeField] private List<Home> characterHomes;

    [SerializeField] private City cityData;

    public delegate void PlayerDisconnected(NetworkConnectionToClient conn);

    public static event PlayerDisconnected OnPlayerDisconnected;

    public CharacterData CharacterData
    {
        get => characterData;
        set => characterData = value;
    }

    public List<Home> CharacterHomes
    {
        get => characterHomes;
        set => characterHomes = value;
    }

    public City CityData => cityData;

#if UNITY_EDITOR
    /// <summary>
    /// Dev-only: force one of the hardcoded accounts so OnClientConnect auto-sends the
    /// CreateCharacterMessage without going through the Main Menu login. Used by DevQuickPlay
    /// to launch + auto-connect from any scene. Editor-only — never shipped in builds.
    /// </summary>
    /// <param name="spectus">true → Spectus, false → Elbloody.</param>
    public void EditorSetDevAccount(bool spectus)
    {
        useSpectusAccount  = spectus;
        useElbloodyAccount = !spectus;
    }
#endif

    /// <summary>
    /// User IDs whose SetupCharacterCoroutine is in flight (player GO not yet
    /// added to a NetworkConnection). Used together with NetworkServer.connections
    /// to reject a duplicate connect from the same account before either side
    /// has a spawned identity.
    /// </summary>
    private readonly HashSet<string> connectingUserIds = new HashSet<string>();

    #region Unity Callbacks

    public override void OnValidate()
    {
        base.OnValidate();
    }

    /// <summary>
    /// Runs on both Server and Client
    /// Networking is NOT initialized when this fires
    /// </summary>
    public override void Awake()
    {
        base.Awake();

        // Step 1 — runtime default from EnvironmentSelector (driven by the
        // Launcher dropdown, PlayerPrefs-backed). The Launcher will override
        // this again after Awake when it applies the dropdown selection, but
        // setting it here means scenes loaded without the Launcher (e.g.
        // BotRunner stress tests) still pick up whatever env the dev set.
        this.networkAddress = EnvironmentSelector.Current.MirrorAddress;

        // Deployment override: client builds running off the VPS can be
        // pointed at the remote Mirror server without rebuilding by setting
        // MIRROR_HOST. Server-side it's a no-op (Mirror binds to 0.0.0.0 by
        // default for incoming connections; the field is the *address* the
        // client connects to).
        string envHost = System.Environment.GetEnvironmentVariable("MIRROR_HOST");
        if (!string.IsNullOrEmpty(envHost)) {
            this.networkAddress = envHost;
        }

        Application.targetFrameRate = 144;
    }

    /// <summary>
    /// Runs on both Server and Client
    /// Networking is NOT initialized when this fires
    /// </summary>
    public override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// Runs on both Server and Client
    /// </summary>
    public override void LateUpdate()
    {
        base.LateUpdate();
    }

    /// <summary>
    /// Runs on both Server and Client
    /// </summary>
    public override void OnDestroy()
    {
        base.OnDestroy();
    }

    #endregion

    #region Start & Stop

    /// <summary>
    /// called when quitting the application by closing the window / pressing stop in the editor
    /// </summary>
    public override void OnApplicationQuit()
    {
        base.OnApplicationQuit();
    }

    #endregion

    #region Scene Management

    /// <summary>
    /// This causes the server to switch scenes and sets the networkSceneName.
    /// <para>Clients that connect to this server will automatically switch to this scene. This is called automatically if onlineScene or offlineScene are set, but it can be called from user code to switch scenes again while the game is in progress. This automatically sets clients to be not-ready. The clients must call NetworkClient.Ready() again to participate in the new scene.</para>
    /// </summary>
    /// <param name="newSceneName"></param>
    public override void ServerChangeScene(string newSceneName)
    {
        base.ServerChangeScene(newSceneName);
    }

    /// <summary>
    /// Called from ServerChangeScene immediately before SceneManager.LoadSceneAsync is executed
    /// <para>This allows server to do work / cleanup / prep before the scene changes.</para>
    /// </summary>
    /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
    public override void OnServerChangeScene(string newSceneName)
    {
    }

    /// <summary>
    /// Called on the server when a scene is completed loaded, when the scene load was initiated by the server with ServerChangeScene().
    /// </summary>
    /// <param name="sceneName">The name of the new scene.</param>
    public override void OnServerSceneChanged(string sceneName)
    {
    }

    /// <summary>
    /// Called from ClientChangeScene immediately before SceneManager.LoadSceneAsync is executed
    /// <para>This allows client to do work / cleanup / prep before the scene changes.</para>
    /// </summary>
    /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
    /// <param name="sceneOperation">Scene operation that's about to happen</param>
    /// <param name="customHandling">true to indicate that scene loading will be handled through overrides</param>
    public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
    {
    }

    #endregion

    #region Server System Callbacks

    /// <summary>
    /// Called on the server when a client disconnects.
    /// <para>This is called on the Server when a Client disconnects from the Server. Use an override to decide what should happen when a disconnection is detected.</para>
    /// </summary>
    /// <param name="conn">Connection from client.</param>
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        GameLogger.Network.Info("ServerDisconnect {ConnectionId} {Address} {IdentityNetId}",
            conn.connectionId, conn.address, conn.identity?.netId ?? 0);

        // Mark the character offline. Read the id before the identity GO is torn
        // down by base.OnServerDisconnect — fire-and-forget so a slow/missing API
        // doesn't block the disconnect.
        if (conn.identity != null) {
            var player = conn.identity.GetComponent<Sim.PlayerController>();
            string characterId = player != null && player.CharacterData != null ? player.CharacterData.Id : null;
            if (!string.IsNullOrEmpty(characterId)) {
                StartCoroutine(UpdateOnlineStateCoroutine(characterId, false));
            }
        }

        // Cleanup jobs/board DÉTERMINISTE ici, AVANT base.OnServerDisconnect ne
        // détruise l'identity. Sinon on dépend entièrement de
        // PlayerController.OnStopServer → JobTargetHooks.UnregisterPlayer, et la
        // moindre fragilité (timing, exception en amont, identity déjà disposed)
        // laisse les missions actives et leurs items en monde (sort packages,
        // pickup, etc.). Les méthodes appelées sont idempotentes : si la chaîne
        // OnStopServer ré-appelle ces handlers ensuite, c'est un no-op.
        uint disconnectingNetId = conn.identity != null ? conn.identity.netId : 0u;
        if (disconnectingNetId != 0u) {
            Sim.Jobs.JobServerManager.Instance.OnPlayerDisconnected(disconnectingNetId);
            Sim.Jobs.JobBoardServer.Instance.OnPlayerDisconnected(conn);
        }

        OnPlayerDisconnected?.Invoke(conn);
        base.OnServerDisconnect(conn);
    }

    private IEnumerator UpdateOnlineStateCoroutine(string characterId, bool online) {
        UnityWebRequest req = ApiManager.Instance.UpdateCharacterOnlineStateRequest(characterId, online);
        yield return req.SendWebRequest();
        if (req.responseCode != 200) {
            GameLogger.Network.Warning("UpdateOnlineStateFailed {CharacterId} {Online} {ResponseCode}",
                characterId, online, req.responseCode);
        } else {
            GameLogger.Network.Debug("UpdateOnlineState {CharacterId} {Online}", characterId, online);
        }
    }

    private IEnumerator ResetAllOnlineStatesCoroutine() {
        UnityWebRequest req = ApiManager.Instance.ResetAllOnlineStateRequest();
        yield return req.SendWebRequest();
        if (req.responseCode != 200 && req.responseCode != 201) {
            GameLogger.Network.Warning("ResetAllOnlineStatesFailed {ResponseCode} {Body}",
                req.responseCode, req.downloadHandler.text);
        } else {
            GameLogger.Network.Info("ResetAllOnlineStates {Body}", req.downloadHandler.text);
        }
    }

    #endregion

    #region Client System Callbacks

    public override void OnClientConnect()
    {
        base.OnClientConnect();

        // Do NOT hide loading here. The loading screen stays visible until the
        // TeleportCoroutine places the player at their apartment (or until
        // UpdateCityDataMessage arrives with ShouldHideLoading=true for city-spawn players).

        if (useSpectusAccount)
        {
            var msg = new CreateCharacterMessage
            {
                userId = "b6844cac-a0aa-416f-9129-d5896437ed38",
                characterId = "b6f052bd-79a4-482b-9075-351db5ca852d"
            };
            NetworkClient.Send(msg);
            ClientLogger.Network("SendCreateCharacter {UserId} {CharacterId} (Spectus)", msg.userId, msg.characterId);
            return;
        }

        if (useElbloodyAccount)
        {
            var msg = new CreateCharacterMessage
            {
                userId = "60468a665ebca93ebc11975a",
                characterId = "6064dcaa84de3905a65c94b0"
            };
            NetworkClient.Send(msg);
            ClientLogger.Network("SendCreateCharacter {UserId} {CharacterId} (Elbloody)", msg.userId, msg.characterId);
            return;
        }

        var characterMsg = new CreateCharacterMessage
        {
            userId = this.characterData.UserId,
            characterId = this.characterData.Id,
#if STRESS_TEST_BOTS
            // Stress-test bots skip the apartment instantiation to save server RAM.
            spawnInCity = CommandLineArgs.BotMode,
#endif
        };
        NetworkClient.Send(characterMsg);
        ClientLogger.Network("SendCreateCharacter {UserId} {CharacterId}", characterMsg.userId,
            characterMsg.characterId);
    }

    #endregion

    #region Start & Stop Callbacks

    // Since there are multiple versions of StartServer, StartClient and StartHost, to reliably customize
    // their functionality, users would need override all the versions. Instead these callbacks are invoked
    // from all versions, so users only need to implement this one case.

    /// <summary>
    /// This is invoked when a host is started.
    /// <para>StartHost has multiple signatures, but they all cause this hook to be called.</para>
    /// </summary>
    public override void OnStartHost()
    {
    }

    /// <summary>
    /// This is invoked when a server is started - including when a host is started.
    /// <para>StartServer has multiple signatures, but they all cause this hook to be called.</para>
    /// </summary>
    public override void OnStartServer()
    {
        GameLogger.Network.Info("ServerStarting {Address}",
            networkAddress);

        NetworkServer.RegisterHandler<CreateCharacterMessage>(OnCreateCharacter);
        NetworkServer.RegisterHandler<CreateDeliveryRequest>(OnCreateDelivery);
        NetworkServer.RegisterHandler<TeleportMessage>(OnPlayerTeleportTo);
        NetworkServer.RegisterHandler<UserSettingsSyncMessage>(OnUserSettingsSync);
        GameLogger.Network.Debug("HandlersRegistered {Count} handlers", 4);

        PropSystemBootstrap.OnServerStart();
        NpcSystemBootstrap.OnServerStart();
        ItemSystemBootstrap.OnServerStart();
        Sim.Jobs.JobSystemBootstrap.OnServerStart();
        Sim.RentSystemBootstrap.OnServerStart();

        // Initialize all BuildingBehavior instances (scene objects, possibly inactive).
        // Replaces the former NetworkBehaviour.OnStartServer hook on each building.
        foreach (BuildingBehavior bb in FindObjectsByType<BuildingBehavior>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            bb.ServerInit();
        }

        StartCoroutine(this.RetrieveCityData());

        // Phase 1: cache the UUID of the system "transit" place so the buy flow
        // can POST /props with placeId = TransitPlaceId. Idempotent — runs once
        // per server boot.
        StartCoroutine(Sim.ApiManager.Instance.EnsureTransitPlace());

        // Recover from stale online flags left by a previous crash. The login
        // endpoint refuses to issue a JWT while any of the user's characters
        // is flagged online, so without this step a crashed session locks
        // users out until manual intervention.
        StartCoroutine(ResetAllOnlineStatesCoroutine());

        GameLogger.Network.Info("ServerStarted {Active}", NetworkServer.active);
    }

    /// <summary>
    /// This is invoked when the client is started.
    /// </summary>
    public override void OnStartClient()
    {
        ClientLogger.Network("ClientStarting {ServerAddress}", networkAddress);

        NetworkClient.RegisterHandler<TeleportMessage>(OnTeleportPlayer);
        NetworkClient.RegisterHandler<ShopResponseMessage>(OnShopResponse);
        NetworkClient.RegisterHandler<UpdateCityDataMessage>(OnCityDataUpdatedResponse);
        NetworkClient.RegisterHandler<NotificationMessage>(OnNotificationReceived);
        NetworkClient.RegisterHandler<ToastNotificationMessage>(OnToastNotificationReceived);
        NetworkClient.RegisterHandler<S2C_WorldToast>(OnWorldToastReceived);
        NetworkClient.RegisterHandler<S2C_HallSpawn>(OnHallSpawn);
        NetworkClient.RegisterHandler<S2C_HallDespawn>(OnHallDespawn);
        NetworkClient.RegisterHandler<S2C_ApartmentSpawn>(OnApartmentSpawn);
        ClientLogger.NetworkDebug("HandlersRegistered {Count} handlers", 7);

        PropSystemBootstrap.OnClientStart();
        NpcSystemBootstrap.OnClientStart();
        ItemSystemBootstrap.OnClientStart();
        Sim.Jobs.JobSystemBootstrap.OnClientStart();
        ClientLogger.Network("ClientStarted {Active}", NetworkClient.active);
    }

    /// <summary>
    /// This is called when a host is stopped.
    /// </summary>
    public override void OnStopHost()
    {
    }

    /// <summary>
    /// This is called when a server is stopped - including when a host is stopped.
    /// </summary>
    public override void OnStopServer()
    {
        GameLogger.Network.Info("ServerStopping {ActiveConnections}", NetworkServer.connections.Count);

        NetworkServer.UnregisterHandler<CreateCharacterMessage>();
        NetworkServer.UnregisterHandler<CreateDeliveryRequest>();
        NetworkServer.UnregisterHandler<TeleportMessage>();

        Sim.RentSystemBootstrap.OnServerStop();
        Sim.Jobs.JobSystemBootstrap.OnServerStop();
        ItemSystemBootstrap.OnServerStop();
        NpcSystemBootstrap.OnServerStop();
        PropSystemBootstrap.OnServerStop();

        foreach (BuildingBehavior bb in FindObjectsByType<BuildingBehavior>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            bb.ServerShutdown();
        }

        this.UpdateTimestamp();
        GameLogger.Network.Info("ServerStopped");
    }

    /// <summary>
    /// This is called when a client is stopped.
    /// </summary>
    public override void OnStopClient()
    {
        ClientLogger.Network("ClientStopping {WasConnected}", NetworkClient.isConnected);

        NetworkClient.UnregisterHandler<TeleportMessage>();
        NetworkClient.UnregisterHandler<ShopResponseMessage>();
        NetworkClient.UnregisterHandler<UpdateCityDataMessage>();
        NetworkClient.UnregisterHandler<NotificationMessage>();
        NetworkClient.UnregisterHandler<ToastNotificationMessage>();
        NetworkClient.UnregisterHandler<S2C_WorldToast>();
        NetworkClient.UnregisterHandler<S2C_HallSpawn>();
        NetworkClient.UnregisterHandler<S2C_HallDespawn>();
        NetworkClient.UnregisterHandler<S2C_ApartmentSpawn>();

        Sim.Jobs.JobSystemBootstrap.OnClientStop();
        ItemSystemBootstrap.OnClientStop();
        NpcSystemBootstrap.OnClientStop();
        PropSystemBootstrap.OnClientStop();

        SceneManager.LoadScene("Main Menu");
        ClientLogger.Network("ClientStopped");
    }

    #endregion

    private IEnumerator RetrieveCityData()
    {
        UnityWebRequest request = ApiManager.Instance.RetrieveCityRequest(this.cityName);

        Debug.Log($"[Server] Retrieve city data of {cityName}");

        yield return request.SendWebRequest();

        if (request.responseCode == 200)
        {
            this.cityData = JsonUtility.FromJson<City>(request.downloadHandler.text);
            Debug.Log("[Server] City data has been retrieved !");
            TimeManager.StartTimestamp = this.cityData.last_timestamp;
        }
        else
        {
            Debug.LogError($"[Server] Cannot retrieve city data");
        }
    }

    private void UpdateTimestamp()
    {
        UnityWebRequest request = ApiManager.Instance.UpdateCityTimestampRequest(new CityUpdateTimestampRequest()
            { id = this.cityData._id, newTimestamp = (long)TimeManager.CurrentTime.TotalSeconds });

        Debug.Log($"[Server] Try to save timestamp {(long)TimeManager.CurrentTime.TotalSeconds}");

        request.SendWebRequest();
    }

    #region Custom Register Handler Callback

    [ServerCallback]
    private void OnPlayerTeleportTo(NetworkConnectionToClient conn, TeleportMessage request)
    {
        GameLogger.Network.Info("TeleportRequest {ConnectionId} {PlayerName} {Destination} {NewRoomId}",
            conn.connectionId, conn.identity?.gameObject.name ?? "unknown", request.destination, request.NewRoomId);
        conn.Send(request);
        GameLogger.Network.Debug("TeleportSent {ConnectionId}", conn.connectionId);
    }

    [ServerCallback]
    private void OnUserSettingsSync(NetworkConnectionToClient conn, UserSettingsSyncMessage msg)
    {
        if (conn?.identity == null || string.IsNullOrEmpty(msg.dataJson)) return;
        var player = conn.identity.GetComponent<Sim.PlayerController>();
        if (player == null) return;
        var data = JsonUtility.FromJson<UserSettingsData>(msg.dataJson);
        if (data != null) player.UserSettings = data;
    }


    [ServerCallback]
    private void OnCreateDelivery(NetworkConnectionToClient conn, CreateDeliveryRequest request)
    {
        GameLogger.Network.Info("CreateDeliveryRequest {ConnectionId} {RecipientId}",
            conn.connectionId, request.recipientId);
        StartCoroutine(CreateDeliveryCoroutine(conn, request));
    }

    private IEnumerator CreateDeliveryCoroutine(NetworkConnectionToClient conn, CreateDeliveryRequest request)
    {
        GameLogger.Network.Debug("CreateDeliveryStart {ConnectionId} {RecipientId}", conn.connectionId,
            request.recipientId);

        // Shop purchases are paid. Resolve the item price and verify the buyer's
        // funds up front (the buyer is the connection that sent the request).
        // The actual debit happens once the prop + delivery are committed below.
        int price = ResolveShopPrice(request);
        PlayerBankAccount buyerBank = conn?.identity != null ? conn.identity.GetComponent<PlayerBankAccount>() : null;
        GameLogger.Network.Info("ShopPurchasePrice {ConnectionId} {Type} {PropsConfigId} {PaintConfigId} {Price} {Money}",
            conn.connectionId, request.type, request.propsConfigId, request.paintConfigId, price,
            buyerBank != null ? buyerBank.Money : -1);
        if (price > 0 && (buyerBank == null || buyerBank.Money < price))
        {
            GameLogger.Network.Info("ShopPurchaseInsufficientFunds {ConnectionId} {Price} {Money}",
                conn.connectionId, price, buyerBank != null ? buyerBank.Money : -1);
            if (conn != null && conn.isReady)
            {
                conn.Send(new ToastNotificationMessage {
                    text = $"Fonds insuffisants ({price} €).",
                    typeByte = (byte)NotificationType.BANK,
                    worldToast = true,
                });
                conn.Send(new ShopResponseMessage { isSuccess = false });
            }
            yield break;
        }

        // Phase 1: materialize the bought prop in DB at buy time. The prop sits in
        // the system "transit" place until the recipient consumes its delivery and
        // builds it into their apartment. The returned UUID is stored on the
        // delivery row so the build flow can PATCH the existing prop instead of
        // spawning a new one.
        string propId = null;
        yield return CreatePropInTransit(request, id => propId = id);
        request.propId = propId;                                // safe: clients can't tamper, propId is set server-side here

        UnityWebRequest req = ApiManager.Instance.CreateDeliveryRequest(request);
        yield return req.SendWebRequest();

        bool success = req.responseCode == 200 || req.responseCode == 201;

        GameLogger.Network.Info("CreateDeliveryResult {ConnectionId} {RecipientId} {Success} {ResponseCode}",
            conn.connectionId, request.recipientId, success, req.responseCode);

        if (conn != null && conn.isReady)
            conn.Send(new ShopResponseMessage { isSuccess = success });

        if (!success)
        {
            GameLogger.Network.Warning("CreateDeliveryFailed {ConnectionId} {RecipientId} {ResponseCode}",
                conn.connectionId, request.recipientId, req.responseCode);
            yield break;
        }

        // Payment: debit the buyer now that the prop + delivery are committed, and
        // record it in the ledger (counterparty = SHOP). Always recorded — even a
        // price-0 purchase writes a shop_purchase line (amount 0) for traceability.
        if (buyerBank != null)
            buyerBank.PostLedger(-price, LedgerReason.ShopPurchase, LedgerCounterparty.System,
                LedgerCounterparty.Shop,
                configId: request.type == DeliveryType.COVER ? request.paintConfigId : request.propsConfigId);

        if (ServerApartmentRegistry.Instance.TryGetByTenant(request.recipientId, out ApartmentController apt) &&
            apt.DeliveryBoxPropId > 0)
            PropInteractionDispatcher.Instance?.RefreshDeliveryBoxCount(apt.DeliveryBoxPropId, apt.RoomId,
                request.recipientId);
    }

    /// <summary>Resolve the catalog price of a shop purchase: cover deliveries are
    /// priced by their CoverConfig, all other props by their PropsConfig. Returns 0
    /// when the config can't be resolved (treated as free, never blocks the buy).</summary>
    private static int ResolveShopPrice(CreateDeliveryRequest request)
    {
        if (request.type == DeliveryType.COVER)
        {
            var cover = DatabaseManager.GetPaintById(request.paintConfigId);
            return cover != null ? cover.Price : 0;
        }

        var prop = DatabaseManager.GetPropsById(request.propsConfigId);
        return prop != null ? prop.Price : 0;
    }

    /// <summary>
    /// Phase 1 — materialize the prop in the new persistence model at the moment
    /// of buy. The prop ends up in the system "transit" place with a fresh UUID,
    /// owned by the recipient. Position is null until the prop is built into an
    /// apartment (a future PATCH on this UUID).
    /// </summary>
    /// <param name="onCreated">Invoked with the new prop UUID on success; not called on failure.</param>
    private IEnumerator CreatePropInTransit(CreateDeliveryRequest request, System.Action<string> onCreated)
    {
        string transitId = ApiManager.Instance.TransitPlaceId;
        if (string.IsNullOrEmpty(transitId)) {
            GameLogger.Network.Warning("CreatePropInTransitSkipped: transit place not ready yet");
            yield break;
        }

        // isBuilt is left to the backend default (true); the build-mode flow
        // will adjust it via PATCH at placement time for "toBuild" prop configs.
        CreatePropBody body = ShopPurchaseHelper.BuildTransitPropBody(
            transitId, request.propsConfigId, request.recipientId, request.propsPresetId);

        // Cover deliveries (paint buckets) carry their paint config + color — bake
        // those into state_data so the bucket prop is fully specified at buy.
        if (request.type == DeliveryType.COVER) {
            body.stateData = new Dictionary<string, object> {
                { "kind",          "bucket" },
                { "paintConfigId", request.paintConfigId },
                { "color",         request.color ?? new float[] { 1, 1, 1, 1 } }
            };
        }

        UnityWebRequest req = ApiManager.Instance.CreatePropRequest(body);
        yield return req.SendWebRequest();

        if (req.responseCode != 200 && req.responseCode != 201) {
            GameLogger.Network.Warning("CreatePropInTransitFailed {ResponseCode} {Body}",
                req.responseCode, req.downloadHandler.text);
            yield break;
        }

        PropJson prop = JsonConvert.DeserializeObject<PropJson>(req.downloadHandler.text);
        if (prop != null && !string.IsNullOrEmpty(prop.Id)) {
            onCreated?.Invoke(prop.Id);
            GameLogger.Network.Info("CreatedPropInTransit {PropId} {ConfigId} {Recipient}",
                prop.Id, request.propsConfigId, request.recipientId);
        }
    }

    [ClientCallback]
    public void OnShopResponse(ShopResponseMessage message)
    {
        ClientLogger.Network("ShopResponse {Success}", message.isSuccess);
        ShopUI.Instance.OnBuyResponse(message.isSuccess);
    }

    [ClientCallback]
    private void OnNotificationReceived(NotificationMessage message)
    {
        ClientLogger.Network("NotificationReceived {Code}", message.code);

        switch (message.code)
        {
            case NotificationCode.ITEM_DESTROYED:
                ClientLogger.UI("InventoryUpdateTriggered");
                HUDManager.Instance.InventoryUI.UpdateUI();
                break;
        }
    }

    [ClientCallback]
    private void OnToastNotificationReceived(ToastNotificationMessage message)
    {
        if (string.IsNullOrEmpty(message.text)) return;

        if (message.worldToast)
        {
            // Feedback d'action banal (mains pleines, fonds insuffisants…) → toast flottant.
            WorldToastManager.Show(message.text);
        }
        else if (NotificationManager.Instance != null)
        {
            // Messages persistants / périodiques (salaire, etc.) → notification coin d'écran.
            NotificationManager.Instance.AddNotification(message.text, message.Type);
        }
    }

    [ClientCallback]
    private void OnWorldToastReceived(S2C_WorldToast message)
    {
        // Toast world-space synchronisé (option) : affiché au-dessus du joueur ciblé,
        // visible par tous les clients qui reçoivent ce message.
        if (string.IsNullOrEmpty(message.title)) return;
        WorldToastManager.ShowAbove(message.anchorNetId, message.title, message.subtitle, message.delay);
    }

    [ClientCallback]
    public void OnCityDataUpdatedResponse(UpdateCityDataMessage message)
    {
        ClientLogger.Network("CityDataUpdated {CityId} {Timestamp} {ShouldHideLoading}",
            message.City.ID, message.City.last_timestamp, message.ShouldHideLoading);
        this.cityData = message.City;
        TimeManager.StartTimestamp = this.cityData.last_timestamp;

        // Only hide loading for players that spawn directly in the city (no apartment).
        // Apartment players have a TeleportMessage following immediately — TeleportCoroutine
        // manages the loading screen for them.
        if (message.ShouldHideLoading)
            LoadingManager.Instance.Hide();
    }

#if STRESS_TEST_BOTS
    private static readonly System.Collections.Generic.Dictionary<int, bool> _spawnInCityByConn = new System.Collections.Generic.Dictionary<int, bool>();
#endif

    private void OnCreateCharacter(NetworkConnectionToClient conn, CreateCharacterMessage message)
    {
        GameLogger.Network.Info("CreateCharacterRequest {ConnectionId} {UserId} {CharacterId}",
            conn.connectionId, message.userId, message.characterId);
#if STRESS_TEST_BOTS
        if (message.spawnInCity) {
            _spawnInCityByConn[conn.connectionId] = true;
        }
#endif
        StartCoroutine(SetupCharacterCoroutine(conn, message.userId));
    }

    [ClientCallback]
    private void OnHallSpawn(S2C_HallSpawn msg)
    {
        Debug.Log($"[RoomSnapshot] Received hall entity street={msg.Street} floor={msg.FloorNumber} pos={msg.Position}");
        ClientLogger.NetworkDebug("HallSpawn {Street} {Floor}", msg.Street, msg.FloorNumber);
        if (BuildingBehavior.TryGetBuilding(msg.Street, out var building))
        {
            building.OnClientHallSpawn(msg);
        }
        else
        {
            Debug.LogError($"[Hall] No BuildingBehavior registered for street={msg.Street} — cannot reconstruct hall client-side. " +
                           "Check City scene: 'Behaviour Manager' GameObject must contain a BuildingBehavior reachable via FindObjectsByType(Include).");
        }
    }

    [ClientCallback]
    private void OnHallDespawn(S2C_HallDespawn msg)
    {
        ClientLogger.NetworkDebug("HallDespawn {Street} {FloorNumber}", msg.Street, msg.FloorNumber);
        if (BuildingBehavior.TryGetBuilding(msg.Street, out var building))
        {
            building.OnClientHallDespawn(msg);
        }
    }

    [ClientCallback]
    private void OnApartmentSpawn(S2C_ApartmentSpawn msg)
    {
        ClientLogger.NetworkDebug("ApartmentSpawn {Street} {FloorNumber} {DoorNumber}", msg.Street, msg.FloorNumber,
            msg.DoorNumber);
        if (BuildingBehavior.TryGetBuilding(msg.Street, out var building))
        {
            building.OnClientApartmentSpawn(msg);
        }
    }

    [Client]
    private void OnTeleportPlayer(TeleportMessage message)
    {
        ClientLogger.Network("TeleportReceived {Destination} {NewRoomId}", message.destination, message.NewRoomId);
        if (!string.IsNullOrEmpty(message.NewRoomId))
        {
            ClientPropManager.Instance?.EnterRoom(message.NewRoomId);
            BuildingBehavior.ClientDespawnHallsExcept(message.NewRoomId);
        }

        StartCoroutine(this.TeleportCoroutine(message.destination));
    }

    private IEnumerator TeleportCoroutine(Vector3 destination)
    {
        LoadingManager.Instance.Show(true);
        // Hide the character on every client so remote clients don't see the
        // NetworkTransform interpolate the position jump (the player sliding).
        PlayerController.Local.CmdSetTeleporting(true);
        yield return new WaitForSeconds(1f);
        PlayerController.Local.ResetGeographicArea();
        PlayerController.Local.NavMeshAgent.enabled = false;
        PlayerController.Local.transform.position = destination;
        PlayerController.Local.NavMeshAgent.enabled = true;
        yield return new WaitForSeconds(2f);
        // Position has propagated by now — reveal the character at its destination.
        PlayerController.Local.CmdSetTeleporting(false);
        LoadingManager.Instance.Hide();
    }

    [Server]
    private IEnumerator SetupCharacterCoroutine(NetworkConnectionToClient conn, string userId)
    {
        GameLogger.Network.Debug("SetupCharacterStart {ConnectionId} {UserId}", conn.connectionId, userId);

        // Anti-cheat: only one live session per user. Reject if another
        // connection already owns a PlayerController for this userId, or if
        // another SetupCharacterCoroutine for the same userId is in flight
        // (race window between two CreateCharacterMessages arriving in the
        // same frame, before either has called AddPlayerForConnection).
        if (IsUserAlreadyConnected(userId, conn, out int otherConnectionId)) {
            GameLogger.Network.Warning("DuplicateUserConnectionRejected {ConnectionId} {UserId} {ExistingConnectionId}",
                conn.connectionId, userId, otherConnectionId);
            conn.Disconnect();
            yield break;
        }
        connectingUserIds.Add(userId);

        try {
            yield return SetupCharacterCoroutineInner(conn, userId);
        } finally {
            connectingUserIds.Remove(userId);
        }
    }

    private bool IsUserAlreadyConnected(string userId, NetworkConnectionToClient self, out int existingConnectionId) {
        existingConnectionId = 0;
        if (string.IsNullOrEmpty(userId)) return false;

        if (connectingUserIds.Contains(userId)) {
            existingConnectionId = -1; // -1 = in-progress, no connection id yet
            return true;
        }

        foreach (NetworkConnectionToClient existing in NetworkServer.connections.Values) {
            if (existing == null || existing == self) continue;
            if (existing.identity == null) continue;
            Sim.PlayerController player = existing.identity.GetComponent<Sim.PlayerController>();
            if (player?.CharacterData != null && player.CharacterData.UserId == userId) {
                existingConnectionId = existing.connectionId;
                return true;
            }
        }
        return false;
    }

    [Server]
    private IEnumerator SetupCharacterCoroutineInner(NetworkConnectionToClient conn, string userId)
    {
        UnityWebRequest characterRequest = ApiManager.Instance.RetrieveCharacterByUserIdRequest(userId);
        yield return characterRequest.SendWebRequest();

        if (characterRequest.responseCode != 200)
        {
            GameLogger.Network.Error(null, "CharacterRetrievalFailed {ConnectionId} {UserId} {ResponseCode}",
                conn.connectionId, userId, characterRequest.responseCode);
            conn.Disconnect();
            yield break;
        }

        CharacterResponse characterResponse =
            JsonUtility.FromJson<CharacterResponse>(characterRequest.downloadHandler.text);

        GameLogger.Network.Info("CharacterRetrieved {ConnectionId} {UserId} {CharacterName}",
            conn.connectionId, userId, characterResponse.Characters[0].Identity.FullName);

        // Hydrate the career rows (character_jobs) onto the CharacterData *before*
        // the SyncVar broadcast — clients then receive currentJob + jobs[] in one
        // shot through ParseCharacterData. Missing jobs (404/empty) is fine for
        // unemployed characters.
        UnityWebRequest jobsRequest =
            ApiManager.Instance.RetrieveCharacterJobsRequest(characterResponse.Characters[0].Id);
        yield return jobsRequest.SendWebRequest();
        if (jobsRequest.responseCode == 200) {
            CharacterJobResponse jobsResponse =
                JsonUtility.FromJson<CharacterJobResponse>(jobsRequest.downloadHandler.text);
            characterResponse.Characters[0].Jobs = jobsResponse?.CharacterJobs != null
                ? new List<CharacterJobData>(jobsResponse.CharacterJobs)
                : new List<CharacterJobData>();
        } else {
            GameLogger.Network.Warning("CharacterJobsRetrievalFailed {CharacterId} {ResponseCode}",
                characterResponse.Characters[0].Id, jobsRequest.responseCode);
        }

        // Prepare the player GO server-side but do NOT call AddPlayerForConnection yet.
        // The correct lifecycle is:
        //   LoadFloorData → BuildHall → RegisterEntities → AddPlayerForConnection → TeleportMessage
        // HallController.CheckGenerationState (or MoveToApartment for already-generated halls)
        // will call FinalizePlayerSpawn once the floor is ready.
        GameObject go = Instantiate(this.playerPrefab, startPositions[0].transform.position, Quaternion.identity);
        PlayerController player = go.GetComponent<PlayerController>();
        player.SetRawCharacterData(JsonUtility.ToJson(characterResponse.Characters[0]));

        // Hydrate the user's preferences (notif opt-ins, audio, graphics, …)
        // so server-side gates (e.g. JobServerManager.Publish notif filter)
        // can read them. Missing or failed fetch → leave defaults.
        UnityWebRequest settingsRequest = ApiManager.Instance.RetrieveUserSettingsRequest(userId);
        yield return settingsRequest.SendWebRequest();
        if (settingsRequest.responseCode == 200) {
            var settings = JsonUtility.FromJson<UserSettings>(settingsRequest.downloadHandler.text);
            if (settings != null && settings.Data != null) {
                player.UserSettings = settings.Data;
            }
        } else {
            GameLogger.Network.Warning("UserSettingsRetrievalFailed {UserId} {ResponseCode}",
                userId, settingsRequest.responseCode);
        }
        go.name = $"Player [conn={conn.connectionId}] [{characterResponse.Characters[0].Identity.FullName}]";

        // Provision the character's pocket + hand_left + hand_right places in DB
        // before the player enters any room. ServerItemManager.OnPlayerEnterRoom
        // relies on PlayerInventory.PlacesReady to restore held items, so this
        // MUST complete before the first room entry.
        PlayerInventory inventory = go.GetComponent<PlayerInventory>() ?? go.AddComponent<PlayerInventory>();
        string characterId = characterResponse.Characters[0].Id;
        if (!string.IsNullOrEmpty(characterId)) {
            yield return inventory.EnsurePlaces(characterId);
            // Pocket session: hydratée plus tard par ServerItemManager.OnPlayerEnterRoom,
            // qui fire après AddPlayerForConnection (conn.identity sera set à ce moment-là,
            // sinon EnsurePocketSession bailait silencieusement et la poche restait vide).
            // Mark the character as online once we own the player GO server-side.
            // Fire-and-forget — failure shouldn't block the connection flow.
            StartCoroutine(UpdateOnlineStateCoroutine(characterId, true));
        } else {
            GameLogger.Network.Warning("EnsurePlaces skipped — character id missing {ConnectionId}", conn.connectionId);
        }

#if STRESS_TEST_BOTS
        bool forceCitySpawn = _spawnInCityByConn.TryGetValue(conn.connectionId, out var flag) && flag;
        _spawnInCityByConn.Remove(conn.connectionId);

        if (forceCitySpawn) {
            // Stress-test bots: skip the apartment instantiation entirely. Saves the
            // server ~50-100 MB per connection that we'd otherwise spend on a hall +
            // room build the bot has zero use for.
            GameLogger.Network.Info("ForceCitySpawn {ConnectionId} {UserId} - skipping apartment lookup", conn.connectionId, userId);
            FinalizePlayerSpawn(conn, go, spawnInCity: true);
            yield break;
        }
#endif

        UnityWebRequest homeRequest =
            ApiManager.Instance.RetrieveHomesByCharacterRequest(characterResponse.Characters[0]);
        yield return homeRequest.SendWebRequest();

        bool sentToBuilding = false;

        if (homeRequest.responseCode == 200)
        {
            HomeResponse homeResponse = JsonUtility.FromJson<HomeResponse>(homeRequest.downloadHandler.text);

            if (homeResponse.Homes.Length > 0)
            {
                player.SetRawCharacterHome(JsonUtility.ToJson(homeResponse.Homes[0]));
                Address address = homeResponse.Homes[0].Address;

                GameLogger.Network.Debug("HomeRetrieved {ConnectionId} {Street} {DoorNumber}",
                    conn.connectionId, address.street, address.doorNumber);

                // Use inclusive find — the BuildingBehavior may sit on an inactive
                // GameObject in the scene (Behaviour Manager is shipped inactive).
                BuildingBehavior buildingBehavior =
                    FindObjectsByType<BuildingBehavior>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(x => x.Match(address));

                if (buildingBehavior != null)
                {
                    // Hand off to building — AddPlayerForConnection happens when hall is ready.
                    buildingBehavior.TeleportToApartment(address.doorNumber, conn, go);
                    sentToBuilding = true;
                    Debug.Log($"[Building] Loading floor data building={address.street} door={address.doorNumber}");
                }
                else
                {
                    GameLogger.Network.Error(null, "BuildingNotFound {ConnectionId} {Street}",
                        conn.connectionId, address.street);
                }
            }
            else
            {
                GameLogger.Network.Warning("NoHomeFound {ConnectionId} {UserId}", conn.connectionId, userId);
            }
        }

        if (!sentToBuilding)
        {
            // Fallback: no building/home found — spawn player in city immediately.
            FinalizePlayerSpawn(conn, go, spawnInCity: true);
        }
    }

    /// <summary>
    /// Called by HallController once the floor is fully loaded, or immediately
    /// when no building is found. Spawns the player for the connection and sends
    /// the current city snapshot.
    /// </summary>
    [Server]
    public void FinalizePlayerSpawn(NetworkConnectionToClient conn, GameObject playerGo, bool spawnInCity = false)
    {
        if (playerGo == null)
        {
            GameLogger.Network.Error(null, "FinalizePlayerSpawnNullGo {ConnectionId}", conn.connectionId);
            return;
        }

        NetworkServer.AddPlayerForConnection(conn, playerGo);

        uint netId = playerGo.GetComponent<NetworkIdentity>()?.netId ?? 0;
        GameLogger.Network.Info("PlayerSpawned {ConnectionId} {NetId} {SpawnInCity}", conn.connectionId, netId, spawnInCity);

        // ShouldHideLoading=true only for city-spawn players (no apartment follows).
        // Apartment players will receive a TeleportMessage right after; TeleportCoroutine
        // handles the loading screen for them.
        conn.Send(new UpdateCityDataMessage { City = this.cityData, ShouldHideLoading = spawnInCity });
        GameLogger.Network.Debug("CityDataSent {ConnectionId} {ShouldHideLoading}", conn.connectionId, spawnInCity);
    }

    #endregion
}