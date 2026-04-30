using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim;
using Sim.Building;
using Sim.Entities;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/*
	Documentation: https://mirror-networking.com/docs/Components/NetworkManager.html
	API Reference: https://mirror-networking.com/docs/api/Mirror.NetworkManager.html
*/

public class SimpleTownNetwork : NetworkManager {
    [Header("Settings")]
    [SerializeField]
    private string cityName = "Simple Town";

    [SerializeField]
    private bool useElbloodyAccount;

    [SerializeField]
    private bool useSpectusAccount;

    [Header("Debug")]
    [SerializeField]
    private CharacterData characterData;

    [SerializeField]
    private List<Home> characterHomes;

    [SerializeField]
    private City cityData;

    public delegate void PlayerDisconnected(NetworkConnectionToClient conn);

    public static event PlayerDisconnected OnPlayerDisconnected;

    public CharacterData CharacterData {
        get => characterData;
        set => characterData = value;
    }

    public List<Home> CharacterHomes {
        get => characterHomes;
        set => characterHomes = value;
    }

    #region Unity Callbacks

    public override void OnValidate() {
        base.OnValidate();
    }

    /// <summary>
    /// Runs on both Server and Client
    /// Networking is NOT initialized when this fires
    /// </summary>
    public override void Awake() {
        base.Awake();

        Application.targetFrameRate = 144;
    }

    /// <summary>
    /// Runs on both Server and Client
    /// Networking is NOT initialized when this fires
    /// </summary>
    public override void Start() {
        base.Start();
    }

    /// <summary>
    /// Runs on both Server and Client
    /// </summary>
    public override void LateUpdate() {
        base.LateUpdate();
    }

    /// <summary>
    /// Runs on both Server and Client
    /// </summary>
    public override void OnDestroy() {
        base.OnDestroy();
    }

    #endregion

    #region Start & Stop
    
    /// <summary>
    /// called when quitting the application by closing the window / pressing stop in the editor
    /// </summary>
    public override void OnApplicationQuit() {
        base.OnApplicationQuit();
    }

    #endregion

    #region Scene Management

    /// <summary>
    /// This causes the server to switch scenes and sets the networkSceneName.
    /// <para>Clients that connect to this server will automatically switch to this scene. This is called automatically if onlineScene or offlineScene are set, but it can be called from user code to switch scenes again while the game is in progress. This automatically sets clients to be not-ready. The clients must call NetworkClient.Ready() again to participate in the new scene.</para>
    /// </summary>
    /// <param name="newSceneName"></param>
    public override void ServerChangeScene(string newSceneName) {
        base.ServerChangeScene(newSceneName);
    }

    /// <summary>
    /// Called from ServerChangeScene immediately before SceneManager.LoadSceneAsync is executed
    /// <para>This allows server to do work / cleanup / prep before the scene changes.</para>
    /// </summary>
    /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
    public override void OnServerChangeScene(string newSceneName) { }

    /// <summary>
    /// Called on the server when a scene is completed loaded, when the scene load was initiated by the server with ServerChangeScene().
    /// </summary>
    /// <param name="sceneName">The name of the new scene.</param>
    public override void OnServerSceneChanged(string sceneName) { }

    /// <summary>
    /// Called from ClientChangeScene immediately before SceneManager.LoadSceneAsync is executed
    /// <para>This allows client to do work / cleanup / prep before the scene changes.</para>
    /// </summary>
    /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
    /// <param name="sceneOperation">Scene operation that's about to happen</param>
    /// <param name="customHandling">true to indicate that scene loading will be handled through overrides</param>
    public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling) { }

    #endregion

    #region Server System Callbacks


    /// <summary>
    /// Called on the server when a client disconnects.
    /// <para>This is called on the Server when a Client disconnects from the Server. Use an override to decide what should happen when a disconnection is detected.</para>
    /// </summary>
    /// <param name="conn">Connection from client.</param>
    public override void OnServerDisconnect(NetworkConnectionToClient conn) {
        Debug.Log($"[Server] A player has been disconnected {conn.connectionId}");
        OnPlayerDisconnected?.Invoke(conn);
        base.OnServerDisconnect(conn);
    }

    #endregion

    #region Client System Callbacks

    public override void OnClientConnect() {
        base.OnClientConnect();

        if (useSpectusAccount) {
            NetworkClient.Send(new CreateCharacterMessage {
                userId = "60468a435ebca93ebc119758",
                characterId = "6064cd05b9d4fd3afca4a146"
            });
            Debug.Log("Connect with Spectus account");
            return;
        }

        if (useElbloodyAccount) {
            NetworkClient.Send(new CreateCharacterMessage {
                userId = "60468a665ebca93ebc11975a",
                characterId = "6064dcaa84de3905a65c94b0"
            });
            Debug.Log("Connect with Elbloody account");
            return;
        }

        NetworkClient.Send(new CreateCharacterMessage {
            userId = this.characterData.UserId,
            characterId = this.characterData.Id
        });
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
    public override void OnStartHost() { }

    /// <summary>
    /// This is invoked when a server is started - including when a host is started.
    /// <para>StartServer has multiple signatures, but they all cause this hook to be called.</para>
    /// </summary>
    public override void OnStartServer() {
        NetworkServer.RegisterHandler<CreateCharacterMessage>(OnCreateCharacter);
        NetworkServer.RegisterHandler<CreateDeliveryRequest>(OnBuySomething);
        NetworkServer.RegisterHandler<TeleportMessage>(OnPlayerTeleportTo);
        NetworkServer.RegisterHandler<SpawnItemMessage>(OnSpawnItem);

        StartCoroutine(this.RetrieveCityData());
    }

    /// <summary>
    /// This is invoked when the client is started.
    /// </summary>
    public override void OnStartClient() {
        NetworkClient.RegisterHandler<TeleportMessage>(OnTeleportPlayer);
        NetworkClient.RegisterHandler<ShopResponseMessage>(OnShopResponse);
        NetworkClient.RegisterHandler<UpdateCityDataMessage>(OnCityDataUpdatedResponse);
        NetworkClient.RegisterHandler<NotificationMessage>(OnNotificationReceived);
    }

    /// <summary>
    /// This is called when a host is stopped.
    /// </summary>
    public override void OnStopHost() { }

    /// <summary>
    /// This is called when a server is stopped - including when a host is stopped.
    /// </summary>
    public override void OnStopServer() {
        NetworkServer.UnregisterHandler<CreateCharacterMessage>();
        NetworkServer.UnregisterHandler<CreateDeliveryRequest>();
        NetworkServer.UnregisterHandler<TeleportMessage>();
        NetworkServer.UnregisterHandler<SpawnItemMessage>();

        this.UpdateTimestamp();
    }

    /// <summary>
    /// This is called when a client is stopped.
    /// </summary>
    public override void OnStopClient() {
        NetworkClient.UnregisterHandler<TeleportMessage>();
        NetworkClient.UnregisterHandler<ShopResponseMessage>();
        NetworkClient.UnregisterHandler<UpdateCityDataMessage>();
        NetworkClient.UnregisterHandler<NotificationMessage>();

        SceneManager.LoadScene("Main Menu");
    }

    #endregion

    private IEnumerator RetrieveCityData() {
        UnityWebRequest request = ApiManager.Instance.RetrieveCityRequest(this.cityName);

        Debug.Log($"[Server] Retrieve city data of {cityName}");

        yield return request.SendWebRequest();

        if (request.responseCode == 200) {
            this.cityData = JsonUtility.FromJson<City>(request.downloadHandler.text);
            Debug.Log("[Server] City data has been retrieved !");
            TimeManager.StartTimestamp = this.cityData.last_timestamp;
        } else {
            Debug.LogError($"[Server] Cannot retrieve city data");
        }
    }

    private void UpdateTimestamp() {
        UnityWebRequest request = ApiManager.Instance.UpdateCityTimestampRequest(new CityUpdateTimestampRequest()
            { id = this.cityData._id, newTimestamp = (long) TimeManager.CurrentTime.TotalSeconds });

        Debug.Log($"[Server] Try to save timestamp {(long) TimeManager.CurrentTime.TotalSeconds}");

        request.SendWebRequest();
    }

    #region Custom Register Handler Callback

    [ServerCallback]
    private void OnBuySomething(NetworkConnectionToClient conn, CreateDeliveryRequest request) {
        StartCoroutine(BuyCoroutine(conn, request));
    }

    [ServerCallback]
    private void OnPlayerTeleportTo(NetworkConnectionToClient conn, TeleportMessage request) {
        Debug.Log($"Player {conn.identity.gameObject.name} want to teleport");
        conn.Send(request);
    }

    [ServerCallback]
    private void OnSpawnItem(NetworkConnectionToClient conn, SpawnItemMessage request) {
        ItemConfig itemConfig = DatabaseManager.ItemConfigs.Find(x => x.ID == request.itemId);

        if (!itemConfig) {
            Debug.LogError($"[SimpleTownNetwork] [SpawnItem] Item [id={request.itemId}] not found in database");
            return;
        }

        GameObject item = Instantiate(itemConfig.Prefab.gameObject, request.position, Quaternion.identity);
        
        NetworkServer.Spawn(item);
        
        Debug.Log($"[SimpleTownNetwork] [SpawnItem] Player {conn.identity.gameObject.name} spawned an item [id={request.itemId}]");
    }

    private IEnumerator BuyCoroutine(NetworkConnectionToClient conn, CreateDeliveryRequest body) {
        Debug.Log($"Server: {body.recipientId} wants to buy props with config Id [{body.propsConfigId}]");

        UnityWebRequest request = ApiManager.Instance.CreateDeliveryRequest(body);

        yield return request.SendWebRequest();

        if (request.responseCode == 201) {
            Debug.Log($"Server: Props [{body.propsConfigId}] has been successfully bought");

            conn.Send(new ShopResponseMessage { isSuccess = true });

            foreach (var deliveryBox in FindObjectsOfType<DeliveryBox>()) {
                deliveryBox.CheckDeliveries();
            }
        } else {
            Debug.LogError($"Server: Props [{body.propsConfigId}] cannot be bought");

            conn.Send(new ShopResponseMessage { isSuccess = false });
        }
    }

    [ClientCallback]
    public void OnShopResponse(ShopResponseMessage message) {
        Debug.Log($"Client: shopResponse success is : {message.isSuccess}");
        ShopUI.Instance.OnBuyResponse(message.isSuccess);
    }
    
    [ClientCallback]
    private void OnNotificationReceived(NotificationMessage message) {
        Debug.Log($"[OnNotificationReceived] [code={message.code}]");

        switch (message.code) {
            case NotificationCode.ITEM_DESTROYED:
                HUDManager.Instance.InventoryUI.UpdateUI();
                break;
        }
    }

    [ClientCallback]
    public void OnCityDataUpdatedResponse(UpdateCityDataMessage message) {
        Debug.Log($"Client: city data has been updated");
        this.cityData = message.City;
        TimeManager.StartTimestamp = this.cityData.last_timestamp;
    }

    private void OnCreateCharacter(NetworkConnectionToClient conn, CreateCharacterMessage message) {
        Debug.Log($"Server: Retrieve character data for {message.characterId}");
        StartCoroutine(SetupCharacterCoroutine(conn, message.userId));
    }

    [Client]
    private void OnTeleportPlayer(TeleportMessage message) {
        StartCoroutine(this.TeleportCoroutine(message.destination));
    }

    private IEnumerator TeleportCoroutine(Vector3 destination) {
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
    private IEnumerator SetupCharacterCoroutine(NetworkConnectionToClient conn, string userId) {
        UnityWebRequest characterRequest = ApiManager.Instance.RetrieveCharacterByUserIdRequest(userId);

        yield return characterRequest.SendWebRequest();

        if (characterRequest.responseCode == 200) {
            CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(characterRequest.downloadHandler.text);

            GameObject go = Instantiate(this.playerPrefab, startPositions[0].transform.position, Quaternion.identity);

            PlayerController player = go.GetComponent<PlayerController>();
            player.SetRawCharacterData(JsonUtility.ToJson(characterResponse.Characters[0]));

            go.name = $"Player [conn={conn.connectionId}] [{characterResponse.Characters[0].Identity.FullName}]";

            // Retrieve home and teleport

            UnityWebRequest homeRequest = ApiManager.Instance.RetrieveHomesByCharacterRequest(characterResponse.Characters[0]);

            yield return homeRequest.SendWebRequest();

            if (homeRequest.responseCode == 200) {
                HomeResponse homeResponse = JsonUtility.FromJson<HomeResponse>(homeRequest.downloadHandler.text);

                if (homeResponse.Homes.Length > 0) {
                    player.SetRawCharacterHome(JsonUtility.ToJson(homeResponse.Homes[0]));

                    Address address = homeResponse.Homes[0].Address;

                    BuildingBehavior buildingBehavior = FindObjectsOfType<BuildingBehavior>().FirstOrDefault(x => x.Match(address));

                    if (buildingBehavior) {
                        buildingBehavior.TeleportToApartment(address.doorNumber, conn);
                    } else {
                        Debug.LogError($"Cannot find building with street name {address.street}");
                    }
                } else {
                    Debug.LogError($"Cannot find home for userId {userId}");
                }
            }

            NetworkServer.AddPlayerForConnection(conn, go);

            conn.Send(new UpdateCityDataMessage() { City = this.cityData });
        } else {
            Debug.LogError($"Cannot find character for userId {userId}");
            conn.Disconnect();
        }
    }

    #endregion
}