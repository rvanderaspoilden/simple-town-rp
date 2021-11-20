using System;
using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SubGamePanelUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Button startBtn;

    private CanvasGroup _canvasGroup;

    private AbstractSubGameManager _currentSubGame;
    
    private void Awake() {
        this._canvasGroup = GetComponent<CanvasGroup>();
        this.startBtn.gameObject.SetActive(false);
        this.Close();
    }

    public void Init(SubGameType subGameType) {
        this._canvasGroup.alpha = 1;
        this._canvasGroup.interactable = true;
        this._canvasGroup.blocksRaycasts = true;
        
        switch (subGameType) {
            case SubGameType.DREAM:
                this.startBtn.GetComponentInChildren<TextMeshProUGUI>().text = "Rêver";
                this.startBtn.gameObject.SetActive(true);
                this.startBtn.onClick.AddListener(() => {
                    AsyncOperation operation = SceneManager.LoadSceneAsync("Dream", LoadSceneMode.Additive);
                    operation.completed += (asyncOperation) => {
                        this._currentSubGame = FindObjectOfType<AbstractSubGameManager>();
                        this._currentSubGame.SetCameraRenderType(CameraRenderType.Overlay);
                        CameraManager.Instance.GetCameraData().cameraStack.Add(this._currentSubGame.Camera);
                        this._currentSubGame.Init(OnGameStarted(), OnGameStopped());
                        this._currentSubGame.StartGame();
                    };
                    
                    this.startBtn.gameObject.SetActive(false);
                });
                break;
        }
    }

    public void Close() {
        this._canvasGroup.alpha = 0;
        this._canvasGroup.interactable = false;
        this._canvasGroup.blocksRaycasts = false;
    }

    private Action OnGameStarted() => () => {
        Debug.Log("Started");
    };

    private Action OnGameStopped() => () => {
        Debug.Log("Stopped");
        this.Close();
        SceneManager.UnloadSceneAsync("Dream");
    };
}
