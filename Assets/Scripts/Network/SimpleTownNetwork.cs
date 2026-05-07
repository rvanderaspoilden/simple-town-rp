using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Building;
using Sim.Entities;
using Sim.Logging;
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
        OnPlayerDisconnected?.Invoke(conn);
        base.OnServerDisconnect(conn);
    }

    #endregion

    #region Client System Callbacks

    public override void OnClientConnect()
    {
        base.OnClientConnect();

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
            characterId = this.characterData.Id
        };
        NetworkClient.Send(characterMsg);
        ClientLogger.Network("SendCreateCharacter {UserId} {CharacterId}", characterMsg.userId,
            characterMsg.characterId);

        LoadingManager.Instance.Hide();
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
        NetworkServer.RegisterHandler<SpawnItemMessage>(OnSpawnItem);
        GameLogger.Network.Debug("HandlersRegistered {Count} handlers", 4);

        PropSystemBootstrap.OnServerStart();
        NpcSystemBootstrap.OnServerStart();

        StartCoroutine(this.RetrieveCityData());
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
        NetworkClient.RegisterHandler<S2C_HallSpawn>(OnHallSpawn);
        NetworkClient.RegisterHandler<S2C_HallDespawn>(OnHallDespawn);
        NetworkClient.RegisterHandler<S2C_ApartmentSpawn>(OnApartmentSpawn);
        ClientLogger.NetworkDebug("HandlersRegistered {Count} handlers", 7);

        PropSystemBootstrap.OnClientStart();
        NpcSystemBootstrap.OnClientStart();
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
        NetworkServer.UnregisterHandler<SpawnItemMessage>();

        NpcSystemBootstrap.OnServerStop();
        PropSystemBootstrap.OnServerStop();

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
        NetworkClient.UnregisterHandler<S2C_HallSpawn>();
        NetworkClient.UnregisterHandler<S2C_HallDespawn>();
        NetworkClient.UnregisterHandler<S2C_ApartmentSpawn>();

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
    private void OnSpawnItem(NetworkConnectionToClient conn, SpawnItemMessage request)
    {
        GameLogger.Network.Info("SpawnItemRequest {ConnectionId} {ItemId} {Position}",
            conn.connectionId, request.itemId, request.position);

        ItemConfig itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == request.itemId);

        if (!itemConfig)
        {
            GameLogger.Network.Error(null, "SpawnItemConfigNotFound {ConnectionId} {ItemId}",
                conn.connectionId, request.itemId);
            return;
        }

        GameObject item = Instantiate(itemConfig.Prefab.gameObject, request.position, Quaternion.identity);

        NetworkServer.Spawn(item);

        GameLogger.Network.Info("ItemSpawned {ConnectionId} {ItemId} {ItemNetId} {Position}",
            conn.connectionId, request.itemId, item.GetComponent<NetworkIdentity>()?.netId ?? 0, request.position);
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

        if (ServerApartmentRegistry.Instance.TryGetByTenant(request.recipientId, out ApartmentController apt) &&
            apt.DeliveryBoxPropId > 0)
            PropInteractionDispatcher.Instance?.RefreshDeliveryBoxCount(apt.DeliveryBoxPropId, apt.RoomId,
                request.recipientId);
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
    public void OnCityDataUpdatedResponse(UpdateCityDataMessage message)
    {
        ClientLogger.Network("CityDataUpdated {CityId} {Timestamp}", message.City.ID, message.City.last_timestamp);
        this.cityData = message.City;
        TimeManager.StartTimestamp = this.cityData.last_timestamp;
        LoadingManager.Instance.Hide();
    }

    private void OnCreateCharacter(NetworkConnectionToClient conn, CreateCharacterMessage message)
    {
        GameLogger.Network.Info("CreateCharacterRequest {ConnectionId} {UserId} {CharacterId}",
            conn.connectionId, message.userId, message.characterId);
        StartCoroutine(SetupCharacterCoroutine(conn, message.userId));
    }

    [ClientCallback]
    private void OnHallSpawn(S2C_HallSpawn msg)
    {
        ClientLogger.NetworkDebug("HallSpawn {Street} {Floor}", msg.Street, msg.FloorNumber);
        if (BuildingBehavior.TryGetBuilding(msg.Street, out var building))
        {
            building.OnClientHallSpawn(msg);
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
        }

        StartCoroutine(this.TeleportCoroutine(message.destination));
    }

    private IEnumerator TeleportCoroutine(Vector3 destination)
    {
        LoadingManager.Instance.Show(true);
        yield return new WaitForSeconds(1f);
        PlayerController.Local.ResetGeographicArea();
        PlayerController.Local.NavMeshAgent.enabled = false;
        PlayerController.Local.transform.position = destination;
        PlayerController.Local.NavMeshAgent.enabled = true;
        yield return new WaitForSeconds(2f);
        LoadingManager.Instance.Hide();
    }

    [Server]
    private IEnumerator SetupCharacterCoroutine(NetworkConnectionToClient conn, string userId)
    {
        GameLogger.Network.Debug("SetupCharacterStart {ConnectionId} {UserId}", conn.connectionId, userId);

        UnityWebRequest characterRequest = ApiManager.Instance.RetrieveCharacterByUserIdRequest(userId);

        yield return characterRequest.SendWebRequest();

        if (characterRequest.responseCode == 200)
        {
            CharacterResponse characterResponse =
                JsonUtility.FromJson<CharacterResponse>(characterRequest.downloadHandler.text);

            GameLogger.Network.Info("CharacterRetrieved {ConnectionId} {UserId} {CharacterName}",
                conn.connectionId, userId, characterResponse.Characters[0].Identity.FullName);

            GameObject go = Instantiate(this.playerPrefab, startPositions[0].transform.position, Quaternion.identity);

            PlayerController player = go.GetComponent<PlayerController>();
            player.SetRawCharacterData(JsonUtility.ToJson(characterResponse.Characters[0]));

            go.name = $"Player [conn={conn.connectionId}] [{characterResponse.Characters[0].Identity.FullName}]";

            // Retrieve home and teleport

            UnityWebRequest homeRequest =
                ApiManager.Instance.RetrieveHomesByCharacterRequest(characterResponse.Characters[0]);

            yield return homeRequest.SendWebRequest();

            if (homeRequest.responseCode == 200)
            {
                HomeResponse homeResponse = JsonUtility.FromJson<HomeResponse>(homeRequest.downloadHandler.text);

                if (homeResponse.Homes.Length > 0)
                {
                    player.SetRawCharacterHome(JsonUtility.ToJson(homeResponse.Homes[0]));

                    Address address = homeResponse.Homes[0].Address;

                    GameLogger.Network.Debug("HomeRetrieved {ConnectionId} {Street} {DoorNumber}",
                        conn.connectionId, address.street, address.doorNumber);

                    BuildingBehavior buildingBehavior =
                        FindObjectsOfType<BuildingBehavior>().FirstOrDefault(x => x.Match(address));

                    if (buildingBehavior)
                    {
                        buildingBehavior.TeleportToApartment(address.doorNumber, conn);
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

            NetworkServer.AddPlayerForConnection(conn, go);

            uint playerNetId = go.GetComponent<NetworkIdentity>()?.netId ?? 0;
            GameLogger.Network.Info("PlayerSpawned {ConnectionId} {UserId} {PlayerNetId} {CharacterName}",
                conn.connectionId, userId, playerNetId, characterResponse.Characters[0].Identity.FullName);

            conn.Send(new UpdateCityDataMessage() { City = this.cityData });
            GameLogger.Network.Debug("CityDataSent {ConnectionId}", conn.connectionId);
        }
        else
        {
            GameLogger.Network.Error(null, "CharacterRetrievalFailed {ConnectionId} {UserId} {ResponseCode}",
                conn.connectionId, userId, characterRequest.responseCode);
            conn.Disconnect();
        }
    }

    #endregion
}