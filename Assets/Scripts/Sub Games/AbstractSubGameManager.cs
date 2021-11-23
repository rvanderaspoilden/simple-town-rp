using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public abstract class AbstractSubGameManager : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Camera camera;

    protected bool _gameStarted;
    protected bool _gameStopped;

    private Action onGameStopped;
    private Action onGameStarted;

    public virtual void StartGame() {
        this.onGameStarted?.Invoke();
    }

    public void Init(Action onStart, Action onStop) {
        this.onGameStarted = onStart;
        this.onGameStopped = onStop;
    }

    private void OnDestroy() {
        StopAllCoroutines();
    }

    public virtual void StopGame() {
        this._gameStopped = true;
        this.onGameStopped?.Invoke();
    }

    public void SetCameraRenderType(CameraRenderType renderType) {
        this.Camera.GetUniversalAdditionalCameraData().renderType = renderType;
    }

    public Camera Camera => camera;

    public bool IsGameStarted => _gameStarted;
}