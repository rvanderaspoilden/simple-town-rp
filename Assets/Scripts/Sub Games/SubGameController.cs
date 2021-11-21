using System;
using System.Collections;
using Mirror;
using Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public class SubGameController : NetworkBehaviour {
    private AbstractSubGameManager _activeSubGame;
    private SubGameConfiguration _activeSubGameConfiguration;

    public static SubGameController Instance;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this.gameObject);
        } else {
            Instance = this;
        }
    }

    public void LaunchSubGame(SubGameConfiguration configuration, bool force = false) {
        if (_activeSubGame) {
            Debug.LogError($"[SubGameController] A sub game is already active !");
            return;
        }

        if (configuration.HasIntermediateValidatorButton && !force) {
            SubGamePanelUI.Instance.Open(configuration);
            return;
        }
        
        StartCoroutine(this.LoadSubGameScene(configuration));
    }

    public void StopSubGame() {
        if (!_activeSubGame) return;

        SubGamePanelUI.Instance.Close();

        StartCoroutine(this.UnLoadSubGameScene());
    }

    private IEnumerator LoadSubGameScene(SubGameConfiguration configuration) {
        AsyncOperation operation = SceneManager.LoadSceneAsync(configuration.SceneName, LoadSceneMode.Additive);

        while (!operation.isDone) yield return null;

        this._activeSubGameConfiguration = configuration;

        Debug.Log($"[SubGameController] Sub Game ({this._activeSubGameConfiguration.SubGameType.ToString()}) has been loaded");

        this._activeSubGame = FindObjectOfType<AbstractSubGameManager>();
        this._activeSubGame.SetCameraRenderType(CameraRenderType.Overlay);
        CameraManager.Instance.GetCameraData().cameraStack.Add(this._activeSubGame.Camera);
        this._activeSubGame.Init(OnSubGameStart(), OnSubGameStop());
        this._activeSubGame.StartGame();
    }

    private IEnumerator UnLoadSubGameScene() {
        AsyncOperation operation = SceneManager.UnloadSceneAsync(this._activeSubGameConfiguration.SceneName, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);

        while (!operation.isDone) yield return null;

        Debug.Log($"[SubGameController] Sub Game ({this._activeSubGameConfiguration.SubGameType.ToString()}) has been unloaded");
        this._activeSubGameConfiguration = null;
        this._activeSubGame = null;
        Resources.UnloadUnusedAssets();
    }

    private Action OnSubGameStart() => () => {
        Debug.Log($"[SubGameController] Sub Game ({this._activeSubGameConfiguration.SubGameType.ToString()}) started");
    };

    private Action OnSubGameStop() => () => {
        Debug.Log($"[SubGameController] Sub Game ({this._activeSubGameConfiguration.SubGameType.ToString()}) stopped");
        this.StopSubGame();
    };
}