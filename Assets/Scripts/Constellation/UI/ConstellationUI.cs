using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Constellation {
    // Contrôleur racine de la modale Constellation. Porté par le prefab racine (Canvas /
    // overlay assombri / carte / panneau détail / en-tête / recherche / carte de
    // déblocage). Non bloquant pour le jeu (multijoueur) ; l'overlay plein écran capte
    // les clics, ce qui neutralise naturellement le clic-pour-bouger.
    public class ConstellationUI : MonoBehaviour {
        [Header("Refs prefab")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private ConstellationMapView mapView;
        [SerializeField] private ConstellationDetailPanel detailPanel;
        [SerializeField] private ConstellationProfileHeader profileHeader;
        [SerializeField] private ConstellationUnlockCard unlockCard;
        [SerializeField] private ConstellationLayerBar layerBar;
        [SerializeField] private TMP_InputField searchField;
        [SerializeField] private Button closeButton;

        [Header("Anim de déblocage (vol des points)")]
        [SerializeField] private RectTransform fxLayer;        // parent transitoire des icônes volantes
        [SerializeField] private Sprite pointIconSprite;       // sprite du rond (Knob)
        [SerializeField] private int flyIconCount = 5;
        [SerializeField] private float flyDuration = 0.55f;
        [SerializeField] private float flyDelayBetween = 0.04f;
        [SerializeField] private Vector2 flyIconSize = new Vector2(18f, 18f);

        [Header("Debug")]
        [Tooltip("Touches 1/3/4 (quand ouvert) créditent des points aux branches dépensables (Créatif/Sportif/Sociable) ; 5 crédite la devise Livreur.")]
        [SerializeField] private bool enableDebugKeys = true;
        [SerializeField] private int debugPointsPerPress = 8;

        private IConstellationDataProvider _provider;
        private bool _built;
        private AudioListener _selfListener;

        public bool IsOpen { get; private set; }

        private void Awake() {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (searchField != null) searchField.onValueChanged.AddListener(OnSearchChanged);
        }

        private void OnEnable() {
            // The provider is owned by the local player's PlayerConstellation
            // component — pulled lazily here so the modal can be opened any
            // time after the player spawns (it can't exist before).
            if (_provider == null) _provider = ResolveProvider();
            if (_provider == null) {
                Debug.LogWarning("[ConstellationUI] No provider available — local player not ready. Closing modal.");
                gameObject.SetActive(false);
                return;
            }
            EnsureBuilt();

            _provider.OnNodeUnlocked += OnNodeUnlocked;
            _provider.OnStateChanged += OnStateChanged;

            // Réouverture : on repart toujours de la couche par défaut (1re branche).
            mapView.ShowDefault(true);
            profileHeader.Refresh(_provider);
            if (detailPanel != null) detailPanel.Hide();

            IsOpen = true;
            if (canvasGroup != null) {
                canvasGroup.alpha = 0f;
                canvasGroup.DOFade(1f, 0.25f);
            }
            EnsureAudioListener();
        }

        // Resolves the provider through the local PlayerController. Returns
        // null if the local player hasn't spawned yet (the modal can't open).
        private IConstellationDataProvider ResolveProvider() {
            var players = FindObjectsByType<Sim.Player.PlayerConstellation>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++) {
                var pc = players[i];
                if (pc != null && pc.isLocalPlayer && pc.Provider != null) return pc.Provider;
            }
            return null;
        }

        // Si la scène n'a pas d'AudioListener (cas vu en éditeur sur certaines scènes), on
        // en ajoute un transitoire sur la modale pour que les SFX (hover/clic/déblocage)
        // soient audibles. Retiré à OnDisable pour éviter les doublons en jeu.
        private void EnsureAudioListener() {
            var existing = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++) {
                var l = existing[i];
                if (l != null && l.enabled && l.gameObject.activeInHierarchy) return;
            }
            _selfListener = gameObject.AddComponent<AudioListener>();
        }

        private void OnDisable() {
            if (_provider != null) {
                _provider.OnNodeUnlocked -= OnNodeUnlocked;
                _provider.OnStateChanged -= OnStateChanged;
            }
            IsOpen = false;
            if (_selfListener != null) { Destroy(_selfListener); _selfListener = null; }
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Escape)) {
                Close();
                return;
            }

            if (enableDebugKeys && !IsSearchFocused()) {
                // Touches 1 / 3 / 4 = crédits de branche (Créatif / Sportif / Sociable).
                // La devise Ingénieux a été retirée (l'arbre Métier est gratuit).
                if (Input.GetKeyDown(KeyCode.Alpha1)) _provider.AddPoints("Creatif", debugPointsPerPress);
                if (Input.GetKeyDown(KeyCode.Alpha3)) _provider.AddPoints("Sportif", debugPointsPerPress);
                if (Input.GetKeyDown(KeyCode.Alpha4)) _provider.AddPoints("Sociable", debugPointsPerPress);
                // Touche 5 : crédite la sous-branche « Livreur » (delivery_driver).
                if (Input.GetKeyDown(KeyCode.Alpha5)) _provider.AddPoints("delivery_driver", debugPointsPerPress);
            }
        }

        public void Open() => gameObject.SetActive(true);
        public void Close() {
            gameObject.SetActive(false);
            // Si la constellation a été ouverte depuis le téléphone, on referme le téléphone
            // au même moment (no-op si le téléphone n'était pas ouvert).
            Sim.PhoneControllerUI.Instance?.ClosePhone();
        }
        public void Toggle() => gameObject.SetActive(!gameObject.activeSelf);

        private void EnsureBuilt() {
            if (_built) return;
            mapView.Build(_provider);
            mapView.NodeHoverEnter += OnNodeHoverEnter;
            mapView.NodeHoverExit  += OnNodeHoverExit;
            mapView.NodeUnlockRequested += OnNodeUnlockRequested;
            mapView.BackgroundClicked += () => { if (detailPanel != null) detailPanel.Hide(); };
            // Le détail se ferme quand on change de couche (le nœud survolé disparaît).
            mapView.LayerChanged += _ => { if (detailPanel != null) detailPanel.Hide(); };
            if (layerBar != null) layerBar.Initialize(mapView);
            _built = true;
        }

        private void OnNodeHoverEnter(ConstellationNodeView view) {
            if (detailPanel != null) detailPanel.ShowFor(view, _provider);
        }

        private void OnNodeHoverExit(ConstellationNodeView view) {
            if (detailPanel != null) detailPanel.Hide();
        }

        private void OnStateChanged() {
            // Lève d'abord le verrou des boutons (SetUnlockLock(false) appelle aussi
            // RefreshStates en interne avec les nouvelles dispos), puis re-peuple les
            // compteurs autoritatifs. L'ordre garantit que le rebuild du header
            // n'écrase pas l'anim avec un texte intermédiaire.
            mapView.SetUnlockLock(false);
            profileHeader.Refresh(_provider);
        }

        private void OnNodeUnlocked(ConstellationNodeData node) {
            mapView.PlayUnlock(node);
            unlockCard.Play(node);
            profileHeader.Refresh(_provider);
            // L'anim de vol des points est désormais déclenchée AVANT le déblocage,
            // par OnNodeUnlockRequested. Ici on ne fait que la finalisation.
            var view = mapView.GetNodeView(node.id);
            if (view != null) view.PlayUnlockSfx();
        }

        // Reçue quand le joueur clique sur « Débloquer ». Orchestre :
        // 1) spawn de N icônes par compteur source (branche primaire, branche secondaire
        //    pour les hybrides, devise métier si requise) ;
        // 2) chaque arrivée → AbsorbOnePoint(1/total) sur le nœud (avance la barre + pulse) ;
        //    à la dernière arrivée → provider.TryUnlock(node) qui lèvera OnNodeUnlocked et
        //    déclenchera le pop final + lignes illuminées.
        private void OnNodeUnlockRequested(ConstellationNodeData node) {
            if (fxLayer == null || pointIconSprite == null) { _provider.TryUnlock(node); return; }
            var nodeView = mapView.GetNodeView(node.id);
            if (nodeView == null) { _provider.TryUnlock(node); return; }
            var nodeRT = (RectTransform)nodeView.transform;
            var graph = _provider.Graph;
            var state = _provider.State;

            // On résout d'ABORD toutes les sources qui spawneront effectivement
            // (cost > 0 ET compteur visible dans le profil). C'est ce nombre qui
            // pilote totalIcons + perPoint : autrement les nœuds qui n'ont pas
            // de coût de branche (typique des sous-métiers : 0 Ingénieux + N
            // Livreur) sur-comptent et la barre reste à moitié — TryUnlock
            // n'est alors jamais appelé.
            var bursts = new System.Collections.Generic.List<BurstSource>(4);

            // Une source de burst par entrée de coût (devise) qui a un compteur visible.
            foreach (var e in state.CostsOf(node)) {
                var src = profileHeader.GetCounterRect(e.branch.id);
                if (src != null) bursts.Add(new BurstSource {
                    from = src, color = graph.GetBranchColor(e.branch),
                    branchId = e.branch.id, cost = e.amount,
                });
            }

            // Cas dégradé : aucun spawn possible (nœud gratuit ou compteurs absents).
            // On finalise immédiatement pour ne pas bloquer le déblocage.
            if (bursts.Count == 0) { _provider.TryUnlock(node); return; }

            // Verrou global : tous les boutons « Débloquer » deviennent non-cliquables
            // tant que l'animation tourne. Levé dans OnStateChanged (provider a appliqué
            // la dépense + le node est marqué unlocked → la passe RefreshStates ré-évalue
            // proprement chaque carte).
            mapView.SetUnlockLock(true);

            // Anim « le compteur fond » : on tween la valeur affichée par compteur source
            // vers (valeur - cost) sur la durée totale d'un burst — visible par le joueur
            // en parallèle des icônes qui s'envolent.
            float burstDuration = (flyIconCount - 1) * flyDelayBetween + flyDuration;
            foreach (var b in bursts) profileHeader.AnimateCounterDelta(b.branchId, b.cost, burstDuration);

            int totalIcons = flyIconCount * bursts.Count;
            float perPoint = 1f / Mathf.Max(1, totalIcons);
            int arrived = 0;

            System.Action onArrival = () => {
                arrived++;
                if (nodeView == null) return;
                nodeView.AbsorbOnePoint(perPoint);
                if (arrived >= totalIcons) {
                    // Si TryUnlock retourne false (cas dégradé : CanUnlock fluctue
                    // entre temps), aucun event ne sortira du provider — on doit
                    // relâcher le lock à la main pour ne pas figer l'UI.
                    bool accepted = _provider.TryUnlock(node);
                    if (!accepted) mapView.SetUnlockLock(false);
                }
            };

            foreach (var b in bursts) SpawnFlyBurst(b.from, nodeRT, b.color, onArrival);
        }

        // Source d'un burst : tracée par OnNodeUnlockRequested pour orchestrer
        // l'anim du compteur (cost + branche ou professionId) en parallèle des icônes.
        private struct BurstSource {
            public RectTransform from;
            public Color color;
            public int cost;
            public string branchId;
        }

        private void SpawnFlyBurst(RectTransform from, RectTransform to, Color color, System.Action onIconArrival) {
            Vector3 fromWorld = from.position;
            Vector3 toWorld = to.position;
            for (int i = 0; i < flyIconCount; i++) {
                float delay = i * flyDelayBetween;
                Vector2 jitter = new Vector2(Random.Range(-6f, 6f), Random.Range(-6f, 6f));

                var go = new GameObject("FlyIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(fxLayer, false);
                rt.sizeDelta = flyIconSize;
                var img = go.GetComponent<Image>();
                img.sprite = pointIconSprite;
                img.color = color;
                img.raycastTarget = false;
                rt.position = fromWorld;

                var seq = DOTween.Sequence();
                seq.AppendInterval(delay);
                seq.AppendCallback(() => { if (rt != null) rt.localScale = Vector3.one; });
                seq.Append(rt.DOMove(toWorld + (Vector3)jitter, flyDuration).SetEase(Ease.InQuad));
                seq.Join(rt.DOScale(0.35f, flyDuration).SetEase(Ease.InQuad));
                seq.OnComplete(() => {
                    onIconArrival?.Invoke();
                    if (go != null) Destroy(go);
                });
            }
        }

        private void OnSearchChanged(string query) {
            if (string.IsNullOrWhiteSpace(query)) return;
            string q = query.Trim().ToLowerInvariant();
            foreach (var node in _provider.Graph.nodes) {
                if (node.displayName != null && node.displayName.ToLowerInvariant().Contains(q)) {
                    mapView.RevealNode(node.id);
                    return;
                }
            }
        }

        private bool IsSearchFocused() => searchField != null && searchField.isFocused;
    }
}
