using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mirror;
using Sim;
using Sim.Building;
using Sim.Entities;
using Sim.Interactables;
using UnityEngine;
using UnityEngine.AI;
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

    public delegate void PlayerDisconnected(int connId);

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
    /// Set the frame rate for a headless server.
    /// <para>Override if you wish to disable the behavior or set your own tick rate.</para>
    /// </summary>
    public override void ConfigureServerFrameRate() {
        base.ConfigureServerFrameRate();
    }

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

    /// <summary>
    /// Called on clients when a scene has completed loaded, when the scene load was initiated by the server.
    /// <para>Scene changes can cause player objects to be destroyed. The default implementation of OnClientSceneChanged in the NetworkManager is to add a player object for the connection if no player object exists.</para>
    /// </summary>
    /// <param name="conn">The network connection that the scene change message arrived on.</param>
    public override void OnClientSceneChanged(NetworkConnection conn) {
        base.OnClientSceneChanged(conn);
    }

    #endregion

    #region Server System Callbacks

    /// <summary>
    /// Called on the server when a new client connects.
    /// <para>Unity calls this on the Server when a Client connects to the Server. Use an override to tell the NetworkManager what to do when a client connects to the server.</para>
    /// </summary>
    /// <param name="conn">Connection from client.</param>
    public override void OnServerConnect(NetworkConnection conn) { }

    /// <summary>
    /// Called on the server when a client is ready.
    /// <para>The default implementation of this function calls NetworkServer.SetClientReady() to continue the network setup process.</para>
    /// </summary>
    /// <param name="conn">Connection from client.</param>
    public override void OnServerReady(NetworkConnection conn) {
        base.OnServerReady(conn);
    }

    /// <summary>
    /// Called on the server when a client adds a new player with ClientScene.AddPlayer.
    /// <para>The default implementation for this function creates a new player object from the playerPrefab.</para>
    /// </summary>
    /// <param name="conn">Connection from client.</param>
    public override void OnServerAddPlayer(NetworkConnection conn) {
        base.OnServerAddPlayer(conn);
    }

    /// <summary>
    /// Called on the server when a client disconnects.
    /// <para>This is called on the Server when a Client disconnects from the Server. Use an override to decide what should happen when a disconnection is detected.</para>
    /// </summary>
    /// <param name="conn">Connection from client.</param>
    public override void OnServerDisconnect(NetworkConnection conn) {
        base.OnServerDisconnect(conn);

        Debug.Log($"[Server] A player has been disconnected {conn.connectionId}");
        OnPlayerDisconnected?.Invoke(conn.connectionId);
    }

    #endregion

    #region Client System Callbacks

    /// <summary>
    /// Called on the client when connected to a server.
    /// <para>The default implementation of this function sets the client as ready and adds a player. Override the function to dictate what happens when the client connects.</para>
    /// </summary>
    /// <param name="conn">Connection to the server.</param>
    public override void OnClientConnect(NetworkConnection conn) {
        base.OnClientConnect(conn);

        if (useSpectusAccount) {
            CreateCharacterMessage mock = new CreateCharacterMessage {
                userId = "60468a435ebca93ebc119758",
                characterId = "6064cd05b9d4fd3afca4a146"
            };
            conn.Send(mock);

            Debug.Log("Connect with Spectus account");

            return;
        } else if (useElbloodyAccount) {
            CreateCharacterMessage mock = new CreateCharacterMessage {
                userId = "60468a665ebca93ebc11975a",
                characterId = "6064dcaa84de3905a65c94b0"
            };
            conn.Send(mock);

            Debug.Log("Connect with Elbloody account");

            return;
        }

        CreateCharacterMessage characterMessage = new CreateCharacterMessage {
            userId = this.characterData.UserId,
            characterId = this.characterData.Id
        };

        conn.Send(characterMessage);
    }

    /// <summary>
    /// Called on clients when disconnected from a server.
    /// <para>This is called on the client when it disconnects from the server. Override this function to decide what happens when the client disconnects.</para>
    /// </summary>
    /// <param name="conn">Connection to the server.</param>
    public override void OnClientDisconnect(NetworkConnection conn) {
        base.OnClientDisconnect(conn);
    }

    /// <summary>
    /// Called on clients when a servers tells the client it is no longer ready.
    /// <para>This is commonly used when switching scenes.</para>
    /// </summary>
    /// <param name="conn">Connection to the server.</param>
    public override void OnClientNotReady(NetworkConnection conn) { }

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

        StartCoroutine(this.RetrieveCityData());
    }

    /// <summary>
    /// This is invoked when the client is started.
    /// </summary>
    public override void OnStartClient() {
        NetworkClient.RegisterHandler<TeleportMessage>(OnTeleportPlayer);
        NetworkClient.RegisterHandler<ShopResponseMessage>(OnShopResponse);
        NetworkClient.RegisterHandler<UpdateCityDataMessage>(OnCityDataUpdatedResponse);
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

        this.UpdateTimestamp();
    }

    /// <summary>
    /// This is called when a client is stopped.
    /// </summary>
    public override void OnStopClient() {
        NetworkClient.UnregisterHandler<TeleportMessage>();
        NetworkClient.UnregisterHandler<ShopResponseMessage>();
        NetworkClient.UnregisterHandler<UpdateCityDataMessage>();

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
    private void OnBuySomething(NetworkConnection conn, CreateDeliveryRequest request) {
        StartCoroutine(BuyCoroutine(conn, request));
    }

    private IEnumerator BuyCoroutine(NetworkConnection conn, CreateDeliveryRequest body) {
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
    public void OnCityDataUpdatedResponse(UpdateCityDataMessage message) {
        Debug.Log($"Client: city data has been updated");
        this.cityData = message.City;
        TimeManager.StartTimestamp = this.cityData.last_timestamp;
    }

    private void OnCreateCharacter(NetworkConnection conn, CreateCharacterMessage message) {
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
    private IEnumerator SetupCharacterCoroutine(NetworkConnection conn, string userId) {
        UnityWebRequest characterRequest = ApiManager.Instance.RetrieveCharacterRequest(userId);

        yield return characterRequest.SendWebRequest();

        if (characterRequest.responseCode == 200) {
            CharacterResponse characterResponse = JsonUtility.FromJson<CharacterResponse>(characterRequest.downloadHandler.text);

            GameObject go = Instantiate(this.playerPrefab, startPositions[0].transform.position, Quaternion.identity);

            PlayerController player = go.GetComponent<PlayerController>();
            player.RawCharacterData = JsonUtility.ToJson(characterResponse.Characters[0]);

            go.name = $"Player [conn={conn.connectionId}] [{characterResponse.Characters[0].Identity.FullName}]";

            // Retrieve home and teleport

            UnityWebRequest homeRequest = ApiManager.Instance.RetrieveHomesByCharacterRequest(characterResponse.Characters[0]);

            yield return homeRequest.SendWebRequest();

            if (homeRequest.responseCode == 200) {
                HomeResponse homeResponse = JsonUtility.FromJson<HomeResponse>(homeRequest.downloadHandler.text);

                if (homeResponse.Homes.Length > 0) {
                    player.RawCharacterHome = JsonUtility.ToJson(homeResponse.Homes[0]);

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