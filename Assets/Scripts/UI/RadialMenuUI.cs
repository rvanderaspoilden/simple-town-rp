using System.Collections.Generic;
using DG.Tweening;
using Sim.Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class RadialMenuUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private RadialMenuButton radialMenuButtonPrefab;

        [SerializeField]
        private Image radialImage;

        [SerializeField]
        private RectTransform radialRectTransform;

        [SerializeField]
        private float radius;

        [SerializeField]
        private float backgroundRadiusOffset;

        [Tooltip("Décalage ajouté au rayon de placement des boutons (pour les aligner sur la bordure du radial). Peut être négatif.")]
        [SerializeField]
        private float buttonRadiusOffset = 0f;

        [SerializeField]
        private TextMeshProUGUI actionText;

        [Tooltip("Conteneur (fond + label) du nom de la cible, affiché AU-DESSUS du menu radial (autoré dans le prefab).")]
        [SerializeField]
        private RectTransform targetNamePlate;

        [Tooltip("Label du nom de la cible (enfant du NamePlate).")]
        [SerializeField]
        private TextMeshProUGUI targetNameText;

        [Tooltip("Décalage vertical (px écran) du nom au-dessus du centre du radial (cas multi-actions).")]
        [SerializeField]
        private float nameAboveOffset = 120f;

        [Tooltip("Décalage vertical (px écran) du label SOUS le bouton (cas action unique).")]
        [SerializeField]
        private float nameBelowOffset = 90f;

        [Header("SFX")]
        [SerializeField]
        private AudioClip hoverSfx;

        [SerializeField] [Range(0f, 1f)]
        private float hoverSfxVolume = 0.6f;

        [Header("Only for debug")]
        [SerializeField]
        private List<RadialMenuButton> radialMenuButtons;

        private Transform currentTarget;

        private Collider currentTargetCollider;

        private Color radialBaseColor = Color.white;

        // true = plate sous le bouton (action unique) ; false = plate au-dessus (multi-actions).
        private bool plateBelow;

        private void Awake() {
            this.radialMenuButtons = new List<RadialMenuButton>();
            this.actionText.enabled = false;
            // Couleur autorée du fond radial (préservée : l'anim d'apparition fade vers elle au lieu d'un blanc hardcodé).
            this.radialBaseColor = this.radialImage.color;
            if (this.targetNamePlate != null) this.targetNamePlate.gameObject.SetActive(false);
            this.radialImage.gameObject.SetActive(false);
        }

        private void Start() {
            this.radialRectTransform = this.radialImage.GetComponent<RectTransform>();
        }

        private void OnEnable() {
            RadialMenuButton.OnClicked += OnRadialButtonClicked;
            RadialMenuButton.OnHover += OnRadialButtonHover;
            RadialMenuButton.OnExit += OnRadialButtonExit;
        }

        private void OnDisable() {
            RadialMenuButton.OnClicked -= OnRadialButtonClicked;
            RadialMenuButton.OnHover -= OnRadialButtonHover;
            RadialMenuButton.OnExit -= OnRadialButtonExit;
        }

        private void Update() {
            if (this.currentTarget) {
                this.Center();
            } else if(this.gameObject.activeSelf){
                this.Close();
                return;
            }

            // Close-on-outside-click : si l'utilisateur clique (gauche ou droit) hors du
            // rayon des boutons, on ferme. Le frame d'ouverture est ignoré pour ne pas se
            // refermer aussitôt avec le clic qui a ouvert le menu.
            if (!this.gameObject.activeSelf) return;
            if (Time.frameCount == this._openedFrame) return;
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;

            Vector3 origin = this.GetPosition();
            Vector2 mouse = Input.mousePosition;
            // Marge de tolérance : on accepte les clics sur les boutons (positionnés à
            // distance ≈ radius) plus une marge pour leur taille propre.
            float clickRadius = this.radius + Mathf.Abs(this.buttonRadiusOffset) + 40f;
            if (Vector2.Distance(new Vector2(origin.x, origin.y), mouse) > clickRadius) {
                this.Close();
            }
        }

        // Frame d'ouverture pour éviter qu'un clic d'ouverture referme aussitôt le menu.
        private int _openedFrame = -1;

        public void Center() {
            float radiansOfSeparation = (Mathf.PI * 2) / this.radialMenuButtons.Count;

            Vector3 origin = this.GetPosition();

            if (this.radialMenuButtons.Count > 1) {
                this.radialImage.transform.position = origin;
                this.radialRectTransform.sizeDelta = new Vector2((radius + backgroundRadiusOffset) * 2f, (radius + backgroundRadiusOffset) * 2f);
            }

            if (this.targetNamePlate != null && this.targetNamePlate.gameObject.activeSelf) {
                float dy = this.plateBelow ? -this.nameBelowOffset : this.nameAboveOffset;
                this.targetNamePlate.position = origin + new Vector3(0f, dy, 0f);
            }

            for (int i = 0; i < this.radialMenuButtons.Count; i++) {
                RadialMenuButton button = this.radialMenuButtons[i];

                button.transform.position = origin;

                RectTransform buttonRectTransform = button.RectTransform;

                if (this.radialMenuButtons.Count > 1) {
                    float r = this.radius + this.buttonRadiusOffset;
                    float x = buttonRectTransform.anchoredPosition.x + Mathf.Cos(radiansOfSeparation * i) * r;
                    float y = buttonRectTransform.anchoredPosition.y + Mathf.Sin(radiansOfSeparation * i) * r;

                    buttonRectTransform.anchoredPosition = new Vector2(x, y);
                }
            }
        }

        private Vector3 GetPosition() {
            Vector3 position = Input.mousePosition;

            if (currentTargetCollider) {
                position = currentTargetCollider.bounds.center;
            }

            return CameraManager.Instance.Camera.WorldToScreenPoint(position);
        }

        public void Setup(Transform target, Action[] actions, bool withPriority = false) {
            this.gameObject.SetActive(true);
            
            this.currentTarget = target;

            this.currentTargetCollider = currentTarget.GetComponent<Collider>();

            this.ClearButtons();

            this.ClearText();

            if (withPriority && actions.Length > 1) {
                actions = new[] {actions[0]};
            }

            // Plate (label + fond) :
            //  - multi-actions  → nom du prop/item ciblé, AU-DESSUS du radial, affiché en permanence.
            //  - action unique  → label de l'action, SOUS le bouton, affiché UNIQUEMENT au survol.
            bool multi = actions.Length > 1;
            this.plateBelow = !multi;
            string plateLabel = multi
                ? this.ResolveTargetName()
                : (actions.Length == 1 ? actions[0].Label : null);
            if (this.targetNameText != null) this.targetNameText.text = plateLabel ?? string.Empty;
            if (this.targetNamePlate != null)
                this.targetNamePlate.gameObject.SetActive(multi && !string.IsNullOrEmpty(plateLabel));

            if (multi) {
                this.radialImage.gameObject.SetActive(true);

                this.radialImage.color = new Color(this.radialBaseColor.r, this.radialBaseColor.g, this.radialBaseColor.b, 0f);
                this.radialImage.DOComplete();
                this.radialImage.DOColor(this.radialBaseColor, .3f).SetEase(Ease.OutQuad);
            } else {
                this.radialImage.gameObject.SetActive(false);
            }


            for (int i = 0; i < actions.Length; i++) {
                Action action = actions[i];
                RadialMenuButton button = Instantiate(this.radialMenuButtonPrefab, this.transform);

                button.Setup(action);
                button.GetComponent<Image>().sprite = action.Icon;

                RectTransform rectTransform = button.GetComponent<RectTransform>();

                rectTransform.DOComplete();

                rectTransform.localScale = Vector2.zero;
                rectTransform.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad).SetDelay(0.05F * i);

                this.radialMenuButtons.Add(button);
            }

            // Positionne tout sur la frame d'apparition pour éviter un « téléport » visible
            // depuis l'ancienne position (sinon le 1er placement n'a lieu qu'au prochain Update).
            this.Center();
            this._openedFrame = Time.frameCount;
        }

        private void ClearButtons() {
            this.radialMenuButtons.ForEach(x => x.GetComponent<RectTransform>().DOComplete());
            
            foreach (Transform child in this.transform) {
                if (child == this.radialImage.transform) continue;
                if (this.targetNamePlate != null && child == this.targetNamePlate.transform) continue;
                Destroy(child.gameObject);
            }

            this.radialMenuButtons.Clear();
        }

        /// <summary>Nom à afficher pour la cible : prop (PropsConfig) ou item (ItemConfig), sinon null.</summary>
        private string ResolveTargetName() {
            if (this.currentTarget == null) return null;

            PropBehaviourBase pb = this.currentTarget.GetComponentInParent<PropBehaviourBase>();
            var propCfg = pb != null ? pb.GetConfiguration() : null;
            if (propCfg != null) return propCfg.GetDisplayName();

            ItemBehaviour ib = this.currentTarget.GetComponentInParent<ItemBehaviour>();
            if (ib != null && ib.Configuration != null) return ib.Configuration.Label;

            return null;
        }

        private void ClearText() {
            this.actionText.text = string.Empty;
            this.actionText.enabled = false;
        }

        private void OnRadialButtonHover(Action action) {
            if (this.plateBelow) {
                // Action unique : la plate (label sous le bouton) n'apparaît qu'au survol.
                if (this.targetNamePlate != null && !string.IsNullOrEmpty(this.targetNameText != null ? this.targetNameText.text : null)) {
                    this.targetNamePlate.gameObject.SetActive(true);
                    this.Center();
                }
            } else {
                this.actionText.enabled = true;
                this.actionText.text = action.Label;
            }

            if (this.hoverSfx != null) Sim.HUDManager.Instance?.PlaySound(this.hoverSfx, this.hoverSfxVolume);
        }

        private void OnRadialButtonExit(Action action) {
            if (this.plateBelow && this.targetNamePlate != null)
                this.targetNamePlate.gameObject.SetActive(false);
            this.ClearText();
        }

        private void OnRadialButtonClicked(Action action) {
            action.Execute();
            this.Close();
        }

        public void Close() {
            this.currentTarget = null;
            this.currentTargetCollider = null;
            this.ClearButtons();
            this.ClearText();
            if (this.targetNamePlate != null) this.targetNamePlate.gameObject.SetActive(false);
            this.gameObject.SetActive(false);
        }
    }
}