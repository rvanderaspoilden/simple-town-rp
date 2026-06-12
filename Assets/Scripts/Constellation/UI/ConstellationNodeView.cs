using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim.Constellation {
    // Carte d'un nœud. Le visuel est piloté par l'état de dépense. Le bouton Débloquer
    // est embarqué dans la carte ; le survol affiche un tooltip flottant (géré ailleurs).
    // Le scale (au survol, au déblocage) s'applique à `scaleTarget` (= Card, pivot
    // centré), pas à la racine — ainsi le layout top-anchored du Root n'est pas affecté
    // et la carte grossit visuellement du centre.
    public class ConstellationNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
        [Header("Refs prefab")]
        [SerializeField] private RectTransform scaleTarget;   // Card (pivot 0.5, 0.5)
        [SerializeField] private Image cardBg;
        [SerializeField] private Image borderImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private GameObject progressBarRoot;  // ProgressBarBg (masqué si débloqué/centre)
        [SerializeField] private Image progressFill;
        [SerializeField] private Button unlockButton;
        [SerializeField] private TextMeshProUGUI unlockButtonLabel;
        [SerializeField] private Image unlockButtonFill;        // overlay Filled Horizontal, fillAmount=0 au repos

        [Header("Affichage du coût (au-dessus du bouton)")]
        [SerializeField] private GameObject costDisplay;        // root toggled visible quand verrouillé
        [SerializeField] private Image primaryCostIcon;
        [SerializeField] private TextMeshProUGUI primaryCostText;
        [SerializeField] private GameObject secondaryCostGroup;
        [SerializeField] private Image secondaryCostIcon;
        [SerializeField] private TextMeshProUGUI secondaryCostText;
        [SerializeField] private GameObject professionCostGroup;
        [SerializeField] private Image professionCostIcon;
        [SerializeField] private TextMeshProUGUI professionCostText;

        [Header("Transparence verrouillé")]
        [SerializeField] private CanvasGroup cardCanvasGroup;       // alpha 0.6 si verrouillé
        [SerializeField] private float lockedAlpha = 0.6f;

        [Header("Audio")]
        // Les SFX passent par la source UI partagée de HUDManager (pas d'AudioSource par node).
        [SerializeField] private AudioClip clickSfx;
        [SerializeField] private AudioClip absorbSfx;          // joué à chaque arrivée d'icône
        [SerializeField] private AudioClip unlockSfx;
        [SerializeField] private float clickFillDuration = 0.55f;
        [SerializeField] private float absorbSfxVolume = 0.55f; // un peu plus discret car répété

        [Header("Couleurs")]
        [SerializeField] private Color lockedBg   = new Color(0.15f, 0.13f, 0.12f, 0.96f);
        [SerializeField] private Color unlockedBg = new Color(0.20f, 0.17f, 0.13f, 0.98f);
        [SerializeField] private Color dimBorder  = new Color(0.30f, 0.30f, 0.34f, 1f);

        [Header("Échelles")]
        [SerializeField] private float lockedScale = 0.96f;
        [SerializeField] private float unlockedScale = 1f;
        [SerializeField] private float centerScale = 1.1f;
        [SerializeField] private float hoverScale = 1.06f;

        public ConstellationNodeData Node { get; private set; }
        public NodeState State { get; private set; }

        public event Action<ConstellationNodeData> UnlockRequested;
        public event Action<ConstellationNodeView> HoverEnter;
        public event Action<ConstellationNodeView> HoverExit;
        // Émis quand le joueur clique sur la carte HORS du bouton « Débloquer ».
        // Le bouton, étant un Selectable, capte ses propres clics — l'évènement
        // ne fuite vers IPointerClickHandler que si le hit est sur la carte elle-même.
        public event Action<ConstellationNodeView> Clicked;

        private float _baseScale = 1f;
        private string _branchLabel;
        private ConstellationState _state;
        private bool _canUnlock;
        private bool _absorbing;

        private void Awake() {
            if (scaleTarget == null) scaleTarget = (RectTransform)transform;
            if (unlockButton != null) unlockButton.onClick.AddListener(OnUnlockClicked);
        }

        public void Setup(ConstellationNodeData node, ConstellationState state, Color branchColor, string branchLabel) {
            Node = node;
            _state = state;
            _branchLabel = branchLabel;
            State = state.GetNodeState(node);
            // Tout changement d'état met fin à toute absorption en cours (le pop final
            // sera joué par PlayUnlockPop sur le scaleTarget réinitialisé).
            _absorbing = false;

            bool center = node.isCenter;
            bool unlocked = State != NodeState.Locked;
            _canUnlock = state.CanUnlock(node);

            if (titleText != null) {
                titleText.text = node.displayName;
                titleText.color = unlocked ? new Color(0.96f, 0.95f, 0.92f)
                                           : new Color(0.78f, 0.78f, 0.82f);
            }

            if (iconImage != null) {
                if (node.icon != null) {
                    iconImage.sprite = node.icon;
                    iconImage.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                } else {
                    iconImage.color = unlocked
                        ? branchColor
                        : new Color(branchColor.r, branchColor.g, branchColor.b, 0.45f);
                }
            }

            if (cardBg != null) cardBg.color = unlocked ? unlockedBg : lockedBg;

            if (borderImage != null) {
                Color border = center ? new Color(0.95f, 0.95f, 1f)
                    : unlocked ? branchColor
                    : _canUnlock ? Color.Lerp(branchColor, Color.white, 0.25f)
                    : dimBorder;
                borderImage.color = border;
            }

            // Barre de progression : visible uniquement quand le nœud est encore verrouillé
            // (et n'est pas le centre). Sinon, on la retire du flux pour que la carte
            // rétrécisse via le VerticalLayoutGroup.
            bool showProgress = !unlocked && !center;
            if (progressBarRoot != null) progressBarRoot.SetActive(showProgress);
            if (showProgress && progressFill != null) {
                progressFill.fillAmount = state.GetUnlockProgress(node);
                progressFill.color = new Color(branchColor.r, branchColor.g, branchColor.b, 0.85f);
            }

            RefreshUnlockButton(unlocked, center, _canUnlock);
            RefreshCostDisplay(unlocked, center);

            // Transparence des cartes verrouillées non débloquables : opacité réduite pour
            // signaler « pas accessible ». Les cartes débloquables (canUnlock = parent OK +
            // budget OK) restent à 100 % comme les débloquées, car elles invitent au clic.
            if (cardCanvasGroup != null) cardCanvasGroup.alpha = (unlocked || center || _canUnlock) ? 1f : lockedAlpha;

            // Reset l'overlay de remplissage (anim de clic) à chaque rafraîchissement.
            if (unlockButtonFill != null) unlockButtonFill.fillAmount = 0f;

            _baseScale = center ? centerScale : unlocked ? unlockedScale : lockedScale;
            // On tue toute anim en cours et on remet l'échelle de la cible au repos.
            if (scaleTarget != null) {
                scaleTarget.DOKill();
                scaleTarget.localScale = Vector3.one * _baseScale;
            }
        }

        // Force le bouton Débloquer en (non-)interactif. Utilisé par MapView pour
        // verrouiller la carte entière pendant une anim de déblocage en cours.
        public void SetUnlockButtonInteractable(bool interactable) {
            if (unlockButton != null) unlockButton.interactable = interactable;
        }

        private void RefreshUnlockButton(bool unlocked, bool center, bool canUnlock) {
            if (unlockButton == null) return;
            if (center || unlocked) { unlockButton.gameObject.SetActive(false); return; }

            unlockButton.gameObject.SetActive(true);
            // Le bouton ne porte plus le coût (affiché au-dessus via CostDisplay) : il
            // n'indique que l'action ou l'indisponibilité. « Verrouillé » si parent OU un
            // prérequis additionnel n'est pas débloqué.
            bool prereqsMet = _state.IsParentUnlocked(Node) && _state.AreExtraPrereqsUnlocked(Node);
            string label = prereqsMet ? "Débloquer" : "Verrouillé";
            bool interactable = prereqsMet && _state.CanAfford(Node);
            unlockButton.interactable = interactable;
            if (unlockButtonLabel != null) unlockButtonLabel.text = label;
        }

        // Affiche le coût (« icône colorée + nombre ») au-dessus du bouton. Caché si le
        // nœud est débloqué ou central. Les entrées de `cost` (liste agnostique) sont
        // mappées sur les 3 slots fixes du prefab par index : slot 0 = primaire, slot 1 =
        // secondaire, slot 2 = profession. Chaque slot prend la couleur de SA devise.
        private void RefreshCostDisplay(bool unlocked, bool center) {
            if (costDisplay == null) return;
            bool show = !unlocked && !center;
            costDisplay.SetActive(show);
            if (!show) return;

            var entries = new System.Collections.Generic.List<CostEntry>(_state.CostsOf(Node));

            // Slot 0 (primaire) : icône + texte togglés individuellement (pas de groupe).
            CostEntry e0 = entries.Count > 0 ? entries[0] : null;
            bool s0 = e0 != null;
            if (primaryCostIcon != null) {
                primaryCostIcon.gameObject.SetActive(s0);
                if (s0) primaryCostIcon.color = e0.branch.color;
            }
            if (primaryCostText != null) {
                primaryCostText.gameObject.SetActive(s0);
                if (s0) { primaryCostText.text = e0.amount.ToString(); primaryCostText.color = e0.branch.color; }
            }

            // Slot 1 (secondaire) : groupe.
            CostEntry e1 = entries.Count > 1 ? entries[1] : null;
            bool s1 = e1 != null;
            if (secondaryCostGroup != null) secondaryCostGroup.SetActive(s1);
            if (s1) {
                if (secondaryCostIcon != null) secondaryCostIcon.color = e1.branch.color;
                if (secondaryCostText != null) { secondaryCostText.text = e1.amount.ToString(); secondaryCostText.color = e1.branch.color; }
            }

            // Slot 2 (profession/3e devise) : groupe.
            CostEntry e2 = entries.Count > 2 ? entries[2] : null;
            bool s2 = e2 != null;
            if (professionCostGroup != null) professionCostGroup.SetActive(s2);
            if (s2) {
                if (professionCostIcon != null) professionCostIcon.color = e2.branch.color;
                if (professionCostText != null) { professionCostText.text = e2.amount.ToString(); professionCostText.color = e2.branch.color; }
            }
        }

        private void OnUnlockClicked() {
            if (Node == null) return;
            if (clickSfx != null) HUDManager.Instance?.PlaySound(clickSfx, 1f);

            // Bouton non-interactif dès le clic ; la barre se remplira par tics au fur
            // et à mesure que les icônes arrivent (driven par l'UI parente, pas le temps).
            if (unlockButton != null) unlockButton.interactable = false;
            PrepareAbsorption();

            UnlockRequested?.Invoke(Node);
        }

        // Réinitialise la barre du bouton à 0 avant que les icônes ne commencent à arriver.
        // Bloque aussi le scale à hoverScale pour que la carte reste « gonflée » pendant
        // toute l'animation d'absorption — même si le pointeur quitte la carte.
        public void PrepareAbsorption() {
            _absorbing = true;
            if (unlockButtonFill != null) {
                unlockButtonFill.DOKill();
                unlockButtonFill.fillAmount = 0f;
            }
            if (scaleTarget != null) {
                scaleTarget.DOComplete();
                scaleTarget.DOScale(_baseScale * hoverScale, 0.12f).SetEase(Ease.OutBack);
            }
        }

        // À appeler à chaque arrivée d'une icône « point ». Avance la barre de fillDelta
        // (∈ ]0,1]) + mini pulse d'échelle + son court (via la source UI partagée du HUD).
        public void AbsorbOnePoint(float fillDelta) {
            if (unlockButtonFill != null) {
                unlockButtonFill.fillAmount = Mathf.Clamp01(unlockButtonFill.fillAmount + fillDelta);
            }
            if (scaleTarget != null) {
                scaleTarget.DOComplete();
                scaleTarget.DOPunchScale(Vector3.one * 0.06f, 0.18f, vibrato: 1, elasticity: 0.4f);
            }
            if (absorbSfx != null) HUDManager.Instance?.PlaySound(absorbSfx, absorbSfxVolume);
        }

        // Joué quand l'absorption est terminée et que l'unlock vient de se valider.
        public void PlayUnlockSfx() {
            if (unlockSfx != null) HUDManager.Instance?.PlaySound(unlockSfx, 1f);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            // Scale uniquement sur les cartes débloquées ou actuellement dépensables.
            // Plus de son au survol (retiré sur demande).
            bool hasFeedback = State != NodeState.Locked || _canUnlock;
            if (hasFeedback && scaleTarget != null) {
                scaleTarget.DOComplete();
                scaleTarget.DOScale(_baseScale * hoverScale, 0.15f).SetEase(Ease.OutBack);
            }
            HoverEnter?.Invoke(this);
        }

        public void OnPointerClick(PointerEventData eventData) {
            // Clic gauche uniquement ; les clics secondaires sont réservés (futur menu
            // contextuel éventuel). N'arrive ici que pour les hits sur la carte hors
            // bouton — un clic sur le bouton va à `unlockButton.onClick` exclusivement.
            if (eventData.button != PointerEventData.InputButton.Left) return;
            // Sécurité : si l'utilisateur a draggé pendant ce pointer-press (pan de
            // la carte initié sur cette carte), Unity considère parfois que c'est
            // toujours un clic. On filtre via le flag dragging du eventData.
            if (eventData.dragging) return;
            Clicked?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData) {
            // Pendant une absorption en cours, on ne redescend PAS l'échelle même si le
            // pointeur quitte la carte (immersion : la carte reste « tendue »).
            if (_absorbing) { HoverExit?.Invoke(this); return; }
            bool hasFeedback = State != NodeState.Locked || _canUnlock;
            if (hasFeedback && scaleTarget != null) {
                scaleTarget.DOComplete();
                scaleTarget.DOScale(_baseScale, 0.15f).SetEase(Ease.OutCubic);
            }
            HoverExit?.Invoke(this);
        }

        // Pop joué juste après un déblocage. Le _baseScale est déjà à l'état déverrouillé
        // (provider lève OnStateChanged AVANT OnNodeUnlocked, voir MockConstellationDataProvider).
        public void PlayUnlockPop() {
            if (scaleTarget == null) return;
            scaleTarget.DOComplete();
            scaleTarget.localScale = Vector3.one * (_baseScale * 0.5f);
            scaleTarget.DOScale(_baseScale, 0.5f).SetEase(Ease.OutBack);
        }
    }
}
