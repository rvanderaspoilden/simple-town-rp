using System.Collections.Generic;
using DG.Tweening;
using Sim.Missions;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Minimap V1 — discrete spatial-awareness widget shown top-right (under the
    /// geographic-area location text). The map is fixed (north-up by texture
    /// convention), the player marker stays at the viewport center and a small
    /// direction arrow rotates with the player's heading.
    ///
    /// Per-room maps come from <see cref="MinimapRoomMapConfig"/> ScriptableObjects
    /// auto-loaded by <see cref="DatabaseManager"/> from
    /// <c>Resources/Configurations/Minimap</c>. When the local room changes
    /// (via <see cref="ClientPropManager.OnLocalRoomChanged"/>) the controller
    /// looks up the matching config and swaps the displayed sprite + calibration.
    /// If no config matches the current room id the minimap hides itself entirely.
    ///
    /// Destination: subscribes to MissionClientManager events and resolves the
    /// active job target via MissionPoint.ByPointId (same pattern as
    /// MissionActiveTargetIndicator). Out-of-viewport targets are shown as an edge
    /// arrow clamped to the rectangular viewport border.
    /// </summary>
    public class MinimapController : MonoBehaviour {
        [Header("UI references")]
        [Tooltip("CanvasGroup on the minimap root. Faded (alpha 0) when no room map matches, instead of deactivating the GameObject — keeps the controller alive to receive future OnLocalRoomChanged events.")]
        [SerializeField] private CanvasGroup contentCanvasGroup;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform mapImage;
        [SerializeField] private Image mapImageGraphic;
        [SerializeField] private RectTransform directionArrow;
        [SerializeField] private RectTransform targetMarker;
        [SerializeField] private RectTransform edgeArrow;

        [Header("Zoom")]
        [SerializeField] private Button zoomInButton;
        [SerializeField] private Button zoomOutButton;
        [SerializeField] private float minZoom = 0.6f;
        [SerializeField] private float maxZoom = 2.0f;
        [SerializeField] private float zoomStep = 0.2f;
        [SerializeField] private float defaultZoom = 1.0f;

        [Header("Fade")]
        [SerializeField] private float fadeDuration = 0.25f;

        [Header("Bounce (target marker)")]
        [SerializeField] private float bounceScale    = 1.18f;
        [SerializeField] private float bounceDuration = 0.55f;

        private float _zoom;
        private Transform _activeTarget;
        private string _currentInstanceId;
        private Vector2 _playerLocal;
        private MinimapRoomMapConfig _currentRoomMap;
        // True iff the widget is logically visible (map matches AND no roof covers
        // the player). Used to short-circuit LateUpdate so we don't dirty the UI
        // canvas every frame while invisible.
        private bool _visible;
        private Tween _bounceTween;

        // Pool of UI icons used to render the MinimapMarkers of the current room.
        // Items are parented to `viewport` so the circular Mask clips them naturally.
        private readonly List<RectTransform> _markerSlotsRt = new List<RectTransform>();
        private readonly List<Image>         _markerSlotsImg = new List<Image>();
        private readonly List<MinimapMarker> _activeMarkers = new List<MinimapMarker>();

        private void Awake() {
            _zoom = Mathf.Clamp(defaultZoom, minZoom, maxZoom);
            if (zoomInButton  != null) zoomInButton.onClick.AddListener(ZoomIn);
            if (zoomOutButton != null) zoomOutButton.onClick.AddListener(ZoomOut);
            HideTarget();

            // Continuous yoyo bounce on the target marker (purely visual). Uses
            // unscaled time so it keeps pulsing under pause / slow-mo. DOTween
            // animates localScale even when the target marker GameObject is
            // inactive — when it becomes active the scale is already in motion.
            if (targetMarker != null) {
                targetMarker.localScale = Vector3.one;
                _bounceTween = targetMarker
                    .DOScale(bounceScale, bounceDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
        }

        private void OnEnable() {
            var jcm = MissionClientManager.Instance;
            jcm.MissionOffered      += OnMissionOffered;
            jcm.MissionStepAdvanced += OnMissionStepAdvanced;
            jcm.MissionFinished     += OnMissionFinished;

            ClientPropManager.OnLocalRoomChanged += OnLocalRoomChanged;
            MinimapMarker.OnRegistryChanged     += RefreshMarkerSlots;
            Roof.OnMinimapCoverageChanged       += OnRoofCoverageChanged;

            // Pick up the current room immediately (the event may have already fired).
            string current = ClientPropManager.Instance != null ? ClientPropManager.Instance.CurrentRoomId : null;
            ApplyRoomMap(current);
            ApplyZoom();
        }

        private void OnDisable() {
            var jcm = MissionClientManager.Instance;
            jcm.MissionOffered      -= OnMissionOffered;
            jcm.MissionStepAdvanced -= OnMissionStepAdvanced;
            jcm.MissionFinished     -= OnMissionFinished;

            ClientPropManager.OnLocalRoomChanged -= OnLocalRoomChanged;
            MinimapMarker.OnRegistryChanged     -= RefreshMarkerSlots;
            Roof.OnMinimapCoverageChanged       -= OnRoofCoverageChanged;
        }

        private void OnRoofCoverageChanged(bool covered) => UpdateVisibility();

        private void OnDestroy() {
            if (zoomInButton  != null) zoomInButton.onClick.RemoveListener(ZoomIn);
            if (zoomOutButton != null) zoomOutButton.onClick.RemoveListener(ZoomOut);
            if (_bounceTween != null) { _bounceTween.Kill(); _bounceTween = null; }
        }

        // ── Room map switching ────────────────────────────────────────────────

        private void OnLocalRoomChanged(string roomId) => ApplyRoomMap(roomId);

        private void ApplyRoomMap(string roomId) {
            var cfg = DatabaseManager.GetMinimapRoomMapByRoomId(roomId);
            _currentRoomMap = (cfg != null && cfg.Sprite != null) ? cfg : null;

            if (_currentRoomMap != null) {
                if (mapImageGraphic != null) mapImageGraphic.sprite = _currentRoomMap.Sprite;
                if (mapImage != null) {
                    float s = _currentRoomMap.MapImagePixelSize;
                    mapImage.sizeDelta = new Vector2(s, s);
                }
            }

            UpdateVisibility();
            if (_currentRoomMap == null) HideTarget();
            RefreshMarkerSlots();
        }

        // Centralised: minimap is visible iff a room map matches AND the local
        // player is not standing under a hide-minimap roof. Fades smoothly via
        // DOTween (controller GameObject stays active throughout so it keeps
        // receiving room/coverage events).
        private void UpdateVisibility() {
            bool show = _currentRoomMap != null && !Roof.IsMinimapCovered;
            _visible = show;
            if (contentCanvasGroup == null) return;
            float target = show ? 1f : 0f;
            contentCanvasGroup.blocksRaycasts = show;
            contentCanvasGroup.interactable   = show;
            contentCanvasGroup.DOKill();
            if (fadeDuration > 0f) contentCanvasGroup.DOFade(target, fadeDuration);
            else                   contentCanvasGroup.alpha = target;
        }

        // ── POI marker pool ───────────────────────────────────────────────────

        private void RefreshMarkerSlots() {
            _activeMarkers.Clear();
            if (_currentRoomMap == null || viewport == null) {
                // Hide all slots when no map is shown.
                for (int i = 0; i < _markerSlotsRt.Count; i++) _markerSlotsRt[i].gameObject.SetActive(false);
                return;
            }

            var markers = MinimapMarker.GetMarkersForRoom(_currentRoomMap.RoomId);
            foreach (var m in markers) {
                if (m != null && m.Config != null && m.Config.Sprite != null) _activeMarkers.Add(m);
            }

            // Grow the pool on demand.
            while (_markerSlotsRt.Count < _activeMarkers.Count) {
                var go = new GameObject($"Marker {_markerSlotsRt.Count}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(viewport, false);
                var rt  = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                _markerSlotsRt.Add(rt);
                _markerSlotsImg.Add(img);
            }

            // Configure active slots, hide the rest.
            for (int i = 0; i < _markerSlotsRt.Count; i++) {
                if (i < _activeMarkers.Count) {
                    var cfg = _activeMarkers[i].Config;
                    _markerSlotsImg[i].sprite = cfg.Sprite;
                    _markerSlotsImg[i].color  = cfg.Color;
                    _markerSlotsRt[i].sizeDelta = new Vector2(cfg.PixelSize, cfg.PixelSize);
                    _markerSlotsRt[i].gameObject.SetActive(true);
                } else {
                    _markerSlotsRt[i].gameObject.SetActive(false);
                }
            }
        }

        private void UpdateMarkerPositions() {
            int n = Mathf.Min(_activeMarkers.Count, _markerSlotsRt.Count);
            for (int i = 0; i < n; i++) {
                var m = _activeMarkers[i];
                if (m == null) { _markerSlotsRt[i].gameObject.SetActive(false); continue; }
                Vector2 markerLocal = WorldToMapLocal(m.transform.position);
                _markerSlotsRt[i].anchoredPosition = (markerLocal - _playerLocal) * _zoom;
            }
        }

        // ── Update loop ───────────────────────────────────────────────────────

        private void LateUpdate() {
            // Critical perf gate: don't dirty the canvas when the widget is
            // invisible (no map for current room, or under a hiding roof).
            if (!_visible) return;

            var player = PlayerController.Local;
            if (player == null || mapImage == null) return;

            _playerLocal = WorldToMapLocal(player.transform.position);
            mapImage.anchoredPosition = -_playerLocal * _zoom;

            if (directionArrow != null) {
                // North-up map: world heading (Y euler, clockwise from +Z) → UI rotation around Z.
                // Unity UI Z+ rotation is CCW; to mirror a CW yaw we negate. Triangle texture is
                // authored tip-up (see MinimapBuildUI.MakeTriangle).
                float headingY = player.transform.eulerAngles.y;
                directionArrow.localEulerAngles = new Vector3(0f, 0f, -headingY);
            }

            UpdateDestination();
            UpdateMarkerPositions();
        }

        private void UpdateDestination() {
            if (_activeTarget == null) { HideTarget(); return; }

            Vector2 targetLocal = WorldToMapLocal(_activeTarget.position);
            Vector2 offset = (targetLocal - _playerLocal) * _zoom;

            // Circular clamping: the viewport is round, so the edge arrow must follow
            // the curve of the circle, not the inscribed rectangle. Radius is the
            // shorter half-size minus a small inset so the icon stays fully inside the mask.
            Vector2 viewportSize = viewport != null ? viewport.rect.size : new Vector2(240f, 240f);
            float radius = Mathf.Min(viewportSize.x, viewportSize.y) * 0.5f - 8f;
            if (radius < 1f) radius = 1f;

            float dist = offset.magnitude;
            bool inside = dist <= radius;
            if (inside) {
                ShowAt(targetMarker, offset);
                if (edgeArrow != null) edgeArrow.gameObject.SetActive(false);
            } else {
                // Project onto the circle of radius `radius`.
                Vector2 clamped = (dist > 0.001f) ? offset * (radius / dist) : Vector2.zero;
                if (targetMarker != null) targetMarker.gameObject.SetActive(false);
                if (edgeArrow != null) {
                    edgeArrow.gameObject.SetActive(true);
                    edgeArrow.anchoredPosition = clamped;
                    float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg - 90f;
                    edgeArrow.localEulerAngles = new Vector3(0f, 0f, angle);
                }
            }
        }

        private void ShowAt(RectTransform rt, Vector2 anchored) {
            if (rt == null) return;
            rt.gameObject.SetActive(true);
            rt.anchoredPosition = anchored;
        }

        private void HideTarget() {
            if (targetMarker != null) targetMarker.gameObject.SetActive(false);
            if (edgeArrow != null) edgeArrow.gameObject.SetActive(false);
        }

        // ── World → map local (px in MapImage local space) ────────────────────

        private Vector2 WorldToMapLocal(Vector3 w) {
            var m = _currentRoomMap;
            float u = 0.5f + (w.x - m.WorldCenterX) / (2f * m.WorldHalfSize);
            float v = 0.5f + (w.z - m.WorldCenterZ) / (2f * m.WorldHalfSize);
            return new Vector2((u - 0.5f) * m.MapImagePixelSize, (v - 0.5f) * m.MapImagePixelSize);
        }

        // ── Zoom ──────────────────────────────────────────────────────────────

        private void ZoomIn()  { _zoom = Mathf.Min(maxZoom, _zoom + zoomStep); ApplyZoom(); }
        private void ZoomOut() { _zoom = Mathf.Max(minZoom, _zoom - zoomStep); ApplyZoom(); }

        private void ApplyZoom() {
            if (mapImage == null) return;
            mapImage.localScale = new Vector3(_zoom, _zoom, 1f);
            if (zoomInButton  != null) zoomInButton.interactable  = _zoom < maxZoom - 1e-3f;
            if (zoomOutButton != null) zoomOutButton.interactable = _zoom > minZoom + 1e-3f;
        }

        // ── Job destination resolution (mirrors MissionActiveTargetIndicator) ─────

        private void OnMissionOffered(MissionClientState state) {
            _currentInstanceId = state.InstanceId;
            ApplyTarget(state);
        }

        private void OnMissionStepAdvanced(MissionClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            ApplyTarget(state);
        }

        private void OnMissionFinished(MissionClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentInstanceId = null;
            _activeTarget = null;
        }

        private void ApplyTarget(MissionClientState state) {
            if (state.Status != MissionStatus.Active || !state.ShowTargetBeacon) {
                _activeTarget = null;
                return;
            }
            if (!string.IsNullOrEmpty(state.CurrentTargetId)
                && MissionPoint.ByPointId.TryGetValue(state.CurrentTargetId, out var point)) {
                _activeTarget = point.transform;
            } else {
                _activeTarget = null;
            }
        }
    }
}
