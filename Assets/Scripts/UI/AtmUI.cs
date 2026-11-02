using System.Collections;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Écran du distributeur (ATM), DIÉGÉTIQUE : un canvas WORLD-SPACE (prefab Resources
    /// Prefabs/UI/AtmScreenUI, construit par Tools ▸ The BROZ ▸ Build ATM UI) instancié à la
    /// demande et calé SUR la dalle écran de l'ATM utilisé (AtmBehaviour.ScreenAnchor) — pas
    /// un panneau du HUD 2D.
    ///
    /// Déroulé façon vrai ATM : à l'ouverture, une CB 3D (AtmBehaviour.CardVisual) apparaît à
    /// moitié insérée dans la fente, outline clignotant (HoverOutline) pour inciter au clic —
    /// le joueur CLIQUE dessus (AtmBehaviour.CardSlot, raycast depuis la caméra), la carte
    /// GLISSE dans la fente → « Lecture de la carte… » → écran de CHOIX (Retirer / Déposer)
    /// → écran d'OPÉRATION : les deux soldes (banque + espèces sur soi), un champ montant et
    /// des boutons de montants prédéfinis. Chaque opération anime la LIASSE 3D
    /// (AtmBehaviour.CashVisual) dans la fente à billets du bas : elle en SORT au retrait,
    /// y est AVALÉE au dépôt (même mécanique de glissement que la CB).
    /// « Rendre la carte » (depuis le choix) ÉJECTE d'abord la CB avant de fermer.
    ///
    /// Pendant la session : la vcam de l'ATM amène la caméra devant l'écran
    /// (AtmBehaviour.SetViewActive), le joueur local est masqué (ShadowsOnly, pattern lunette
    /// sniper — il serait entre la caméra et la machine), le curseur est libéré
    /// (CursorLock.IsBlockingUiOpen) et le déplacement TPS est neutralisé (garde dans
    /// PlayerController.HandleKeyboardMovementEntry). Fermeture : « Rendre la carte », Échap
    /// (qui remonte d'abord d'un écran), ou le joueur a été éloigné de l'ATM. Toute
    /// l'exécution est serveur (C2S_AtmWithdraw / C2S_AtmDeposit) ; le solde affiché suit
    /// PlayerBankAccount.OnLocalMoneyChanged (SyncVar) et se met à jour tout seul.
    /// </summary>
    public class AtmUI : MonoBehaviour {
        private const string PrefabResourcePath = "Prefabs/UI/AtmScreenUI";
        private const float  CardReadDuration   = 1.1f;

        // CB physique dans la fente (AtmBehaviour.CardVisual) : glissement le long du Z
        // local de la fente (+Z = hors machine), clignotement d'incitation via HoverOutline.
        private const float CardSlideDuration = 0.45f;
        private const float CardTravel        = 0.12f;
        private const float CardEjectPause    = 0.6f;
        private const float CardPulseOn       = 0.55f;
        private const float CardPulseOff      = 0.3f;

        // Liasse physique dans la fente à billets (AtmBehaviour.CashVisual) : même mécanique.
        private const float CashSlideDuration = 0.5f;
        private const float CashTravel        = 0.12f;
        private const float CashTakePause     = 1.0f;
        /// <summary>Délai max d'attente de la confirmation serveur d'un retrait (baisse du
        /// solde) avant d'abandonner l'animation de sortie des billets. Large : le débit ne
        /// part qu'après la création backend de la liasse en poche.</summary>
        private const float WithdrawConfirmTimeout = 4f;

        /// <summary>Durée d'affichage de la ligne de feedback en bas d'écran (erreurs…).</summary>
        private const float FeedbackDuration = 2.6f;

        // Palette des boutons de confirmation (miroir AtmUIBuilder).
        private static readonly Color WithdrawColor = new Color(0.12f, 0.45f, 0.24f, 1f);
        private static readonly Color DepositColor  = new Color(0.12f, 0.30f, 0.48f, 1f);

        [Header("Étape carte")]
        [SerializeField] private GameObject insertPanel;
        [SerializeField] private TMP_Text   insertLabel;

        [Header("Étape choix")]
        [SerializeField] private GameObject choicePanel;
        [SerializeField] private TMP_Text   choiceBalancesLabel;
        [SerializeField] private Button     chooseWithdrawButton;
        [SerializeField] private Button     chooseDepositButton;

        [Header("Étape opération (retrait / dépôt)")]
        [SerializeField] private GameObject     operationPanel;
        [SerializeField] private TMP_Text       operationTitle;
        [SerializeField] private TMP_Text       operationBalancesLabel;
        [SerializeField] private TMP_InputField amountInput;
        [SerializeField, Tooltip("Boutons de montants prédéfinis, parallèles à presetValues.")]
        private Button[] presetButtons;
        [SerializeField, Tooltip("Montant de chaque bouton prédéfini ; 0 = MAX (solde ou espèces selon l'écran).")]
        private int[] presetValues;
        [SerializeField] private Button   confirmButton;
        [SerializeField] private TMP_Text confirmLabel;

        [Header("Commun")]
        [SerializeField] private Button   closeButton;
        [SerializeField] private TMP_Text closeLabel;
        [SerializeField, Tooltip("Ligne de feedback en bas d'écran : les toasts locaux (erreurs de pré-check client ET refus serveur) y sont détournés pendant la session — un toast monde au-dessus du joueur serait hors champ de la vcam ATM.")]
        private TMP_Text feedbackLabel;

        public static AtmUI Instance;

        private enum Step { InsertCard, Reading, Choice, Withdraw, Deposit, Ejecting }

        private AtmBehaviour _atm;
        private Canvas _canvas;
        private Step _step;
        private Coroutine _readRoutine;
        private Coroutine _cardPulseRoutine;
        private Coroutine _ejectRoutine;
        private Coroutine _cashRoutine;
        private Vector3 _cardOutPos;   // localPosition autorée de la CB (à moitié sortie)
        private bool    _cardPosKnown;
        private Vector3 _cashOutPos;   // localPosition autorée de la liasse (dépassant de la fente)
        private bool    _cashPosKnown;
        private int _shownBank = int.MinValue; // cache d'affichage des soldes
        private int _shownCash = int.MinValue;
        private Coroutine _feedbackRoutine;
        private System.Func<string, string, ToastKind, bool> _toastInterceptor;
        private bool _errorShownSinceSend; // une erreur (serveur ou locale) a-t-elle suivi le dernier retrait ?

        public bool IsOpen => this.gameObject.activeSelf;

        /// <summary>Instance unique du canvas écran, instanciée à la demande depuis le prefab
        /// Resources (vit dans la scène courante — détruite/recréée au changement de scène).</summary>
        public static AtmUI GetOrCreate() {
            if (Instance != null) return Instance;
            AtmUI prefab = Resources.Load<AtmUI>(PrefabResourcePath);
            if (prefab == null) {
                Debug.LogError($"[AtmUI] Prefab introuvable : Resources/{PrefabResourcePath} — lancer Tools ▸ The BROZ ▸ Build ATM UI");
                return null;
            }
            // Instantiate déclenche Awake (racine active dans le prefab) → Instance posée + panneau caché.
            Instantiate(prefab);
            return Instance;
        }

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;
            this._canvas = GetComponent<Canvas>();

            if (this.chooseWithdrawButton != null) this.chooseWithdrawButton.onClick.AddListener(() => this.GoTo(Step.Withdraw));
            if (this.chooseDepositButton  != null) this.chooseDepositButton.onClick.AddListener(() => this.GoTo(Step.Deposit));
            if (this.confirmButton        != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.closeButton          != null) this.closeButton.onClick.AddListener(this.Back);
            if (this.presetButtons != null) {
                for (int i = 0; i < this.presetButtons.Length; i++) {
                    if (this.presetButtons[i] == null) continue;
                    int value = this.presetValues != null && i < this.presetValues.Length ? this.presetValues[i] : 0;
                    this.presetButtons[i].onClick.AddListener(() => this.OnPreset(value));
                }
            }
            if (this.amountInput != null) {
                this.amountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                this.amountInput.onSubmit.AddListener(_ => this.Confirm());
            }
            this._toastInterceptor = this.OnLocalToast;

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
            PlayerBankAccount.OnLocalMoneyChanged -= OnMoneyChanged;
            if (WorldToastManager.LocalToastInterceptor == this._toastInterceptor)
                WorldToastManager.LocalToastInterceptor = null;
            this.SetLocalPlayerHidden(false);
        }

        public void Open(AtmBehaviour atm) {
            if (atm == null) return;
            this._atm = atm;

            // Canvas world-space calé sur la dalle écran de CET ATM.
            Transform anchor = atm.ScreenAnchor;
            this.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            if (this._canvas != null) {
                this._canvas.worldCamera = CameraManager.Instance != null
                    ? CameraManager.Instance.Camera : Camera.main;
            }

            PlayerBankAccount.OnLocalMoneyChanged += OnMoneyChanged;
            if (this.amountInput != null) this.amountInput.text = string.Empty;
            this._shownBank = int.MinValue;
            this._shownCash = int.MinValue;
            // Les toasts locaux (pré-checks ET erreurs serveur) s'affichent SUR l'écran ATM :
            // la vcam de session cadre la machine, un toast au-dessus du joueur serait hors champ.
            WorldToastManager.LocalToastInterceptor = this._toastInterceptor;
            this.ClearFeedback();

            atm.SetViewActive(true);
            // Le joueur entre en état « occupé » le temps de la session ATM : hotbar / tir /
            // mêlée neutralisés. L'état se termine seul quand cet écran se referme (prédicat).
            PlayerController.Local?.BeginBusy(
                () => AtmUI.Instance != null && AtmUI.Instance.IsOpen,
                atm != null ? atm.transform : null);
            // La caméra vient se placer entre le joueur et la machine : on masque le corps
            // local (ombre conservée), sinon il bouche l'écran.
            this.SetLocalPlayerHidden(true);

            this._step = Step.InsertCard;
            this.gameObject.SetActive(true);
            this.ApplyStep();

            // CB à moitié sortie de la fente, outline clignotant pour inciter au clic.
            Transform card = atm.CardVisual;
            if (card != null) {
                this._cardOutPos = card.localPosition;
                this._cardPosKnown = true;
                card.gameObject.SetActive(true);
                this._cardPulseRoutine = StartCoroutine(this.CardPulseCoroutine(card));
            }
            Transform cash = atm.CashVisual;
            if (cash != null) {
                this._cashOutPos = cash.localPosition;
                this._cashPosKnown = true;
            }
        }

        /// <summary>Sortie de session : depuis le choix (carte avalée), la CB est d'abord
        /// ÉJECTÉE de la fente (« Récupérez votre carte ») avant la fermeture réelle ;
        /// partout ailleurs la fermeture est immédiate.</summary>
        public void Close() {
            if (this._step == Step.Ejecting) return; // éjection en cours — elle fermera
            bool cardSwallowed = this._step == Step.Choice || this._step == Step.Withdraw || this._step == Step.Deposit;
            if (cardSwallowed && this._atm != null && this._atm.CardVisual != null) {
                this._ejectRoutine = StartCoroutine(this.EjectCardCoroutine());
                return;
            }
            this.CloseNow();
        }

        /// <summary>Fermeture effective (immédiate) : stoppe les routines, remet la CB et la
        /// liasse dans leur état de repos (position autorée, cachées) et rend la vue.</summary>
        private void CloseNow() {
            if (this._readRoutine != null)  { StopCoroutine(this._readRoutine);  this._readRoutine = null; }
            if (this._ejectRoutine != null) { StopCoroutine(this._ejectRoutine); this._ejectRoutine = null; }
            if (this._cashRoutine != null)  { StopCoroutine(this._cashRoutine);  this._cashRoutine = null; }
            this.StopCardPulse();

            Transform card = this._atm != null ? this._atm.CardVisual : null;
            if (card != null) {
                if (this._cardPosKnown) card.localPosition = this._cardOutPos;
                card.gameObject.SetActive(false);
            }
            Transform cash = this._atm != null ? this._atm.CashVisual : null;
            if (cash != null) {
                if (this._cashPosKnown) cash.localPosition = this._cashOutPos;
                cash.gameObject.SetActive(false);
            }

            PlayerBankAccount.OnLocalMoneyChanged -= OnMoneyChanged;
            if (WorldToastManager.LocalToastInterceptor == this._toastInterceptor)
                WorldToastManager.LocalToastInterceptor = null;
            this.SetLocalPlayerHidden(false);
            if (this._atm != null) this._atm.SetViewActive(false);
            this._atm = null;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Escape)) { this.Back(); return; }

            // Session rompue si le joueur n'est plus devant l'ATM (téléport, poussée…) :
            // fermeture sèche, pas d'animation d'éjection pour un joueur déjà parti.
            if (this._atm == null || PlayerController.Local == null) { this.CloseNow(); return; }
            Vector3 d = PlayerController.Local.transform.position - this._atm.transform.position;
            d.y = 0f;
            float maxRange = this._atm.GetRange() + 1.5f;
            if (d.sqrMagnitude > maxRange * maxRange) { this.CloseNow(); return; }

            if (this._step == Step.InsertCard && Input.GetMouseButtonDown(0)) this.TryClickCardSlot();
            if (this._step == Step.Choice || this._step == Step.Withdraw || this._step == Step.Deposit)
                this.RefreshBalances(); // suit aussi les S2C_PocketSync (espèces sur soi)
        }

        // ── Étapes ──────────────────────────────────────────────────────────────

        /// <summary>Bouton bas d'écran / Échap : remonte d'un écran — opération → choix,
        /// choix → éjection de la carte, carte/lecture → fermeture sèche.</summary>
        private void Back() {
            switch (this._step) {
                case Step.Withdraw:
                case Step.Deposit:
                    if (this._cashRoutine != null) return; // billets en mouvement — on laisse finir
                    this.GoTo(Step.Choice);
                    break;
                case Step.Ejecting:
                    break; // sortie déjà engagée
                default:
                    this.Close();
                    break;
            }
        }

        private void GoTo(Step step) {
            this._step = step;
            this.ApplyStep();
        }

        private void ApplyStep() {
            if (this.insertPanel    != null) this.insertPanel.SetActive(this._step == Step.InsertCard || this._step == Step.Reading || this._step == Step.Ejecting);
            if (this.choicePanel    != null) this.choicePanel.SetActive(this._step == Step.Choice);
            if (this.operationPanel != null) this.operationPanel.SetActive(this._step == Step.Withdraw || this._step == Step.Deposit);

            if (this.insertLabel != null) {
                switch (this._step) {
                    case Step.Reading:  this.insertLabel.text = "Lecture de la carte…"; break;
                    case Step.Ejecting: this.insertLabel.text = "Récupérez votre carte\n<size=70%><alpha=#AA>Merci, à bientôt — BROZ BANK</size>"; break;
                    default:            this.insertLabel.text = "Insérez votre carte\n<size=70%><alpha=#AA>Cliquez sur la carte dans la fente  >></size>"; break;
                }
            }

            bool withdraw = this._step == Step.Withdraw;
            if (this.operationTitle != null) this.operationTitle.text = withdraw ? "RETRAIT" : "DÉPÔT";
            if (this.confirmLabel   != null) this.confirmLabel.text   = withdraw ? "Retirer" : "Déposer";
            if (this.confirmButton  != null) {
                Image img = this.confirmButton.GetComponent<Image>();
                if (img != null) img.color = withdraw ? WithdrawColor : DepositColor;
            }
            if (this.amountInput != null) this.amountInput.text = string.Empty;

            if (this.closeLabel != null) {
                switch (this._step) {
                    case Step.Choice:   this.closeLabel.text = "Rendre la carte"; break;
                    case Step.Withdraw:
                    case Step.Deposit:  this.closeLabel.text = "Retour"; break;
                    default:            this.closeLabel.text = "Annuler"; break;
                }
            }
            // Pendant l'éjection la sortie est déjà engagée : pas de bouton.
            if (this.closeButton != null) this.closeButton.gameObject.SetActive(this._step != Step.Ejecting);

            this._shownBank = int.MinValue; // force le rafraîchissement des soldes
            this._shownCash = int.MinValue;
            this.RefreshBalances();
            this.ClearFeedback(); // pas de message d'erreur périmé sur le nouvel écran
        }

        /// <summary>Clic pendant « Insérez votre carte » : raycast physique depuis la caméra —
        /// toucher la fente à carte (ou la CB qui en dépasse) lance la lecture. Le canvas ne
        /// recouvre pas la fente, le clic l'atteint directement.</summary>
        private void TryClickCardSlot() {
            if (this._atm == null || this._atm.CardSlot == null) return;
            Camera cam = this._canvas != null && this._canvas.worldCamera != null
                ? this._canvas.worldCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 6f, ~0, QueryTriggerInteraction.Collide)) {
                if (hit.collider != this._atm.CardSlot) continue;
                this._readRoutine = StartCoroutine(this.ReadCardCoroutine());
                return;
            }
        }

        private IEnumerator ReadCardCoroutine() {
            this._step = Step.Reading;
            this.ApplyStep();

            // La CB glisse dans la fente, puis disparaît (avalée par la machine).
            this.StopCardPulse();
            Transform card = this._atm != null ? this._atm.CardVisual : null;
            if (card != null) {
                yield return SlideVisual(card, this._cardOutPos, this.CardInPos(), CardSlideDuration);
                card.gameObject.SetActive(false);
            }

            yield return new WaitForSeconds(CardReadDuration);
            this._readRoutine = null;
            this.GoTo(Step.Choice);
        }

        // ── CB physique dans la fente ────────────────────────────────────────────

        private Vector3 CardInPos() => this._cardOutPos + Vector3.back * CardTravel;

        /// <summary>« Rendre la carte » : la CB ressort de la fente (écran « Récupérez votre
        /// carte »), petite pause le temps que le joueur la « prenne », puis fermeture.</summary>
        private IEnumerator EjectCardCoroutine() {
            this._step = Step.Ejecting;
            this.ApplyStep();

            Transform card = this._atm != null ? this._atm.CardVisual : null;
            if (card != null) {
                card.gameObject.SetActive(true);
                yield return SlideVisual(card, this.CardInPos(), this._cardOutPos, CardSlideDuration);
                yield return new WaitForSeconds(CardEjectPause);
            }

            this._ejectRoutine = null;
            this.CloseNow();
        }

        /// <summary>Glissement d'un visuel (CB, liasse) le long du Z local de sa fente
        /// (ease smoothstep).</summary>
        private static IEnumerator SlideVisual(Transform visual, Vector3 from, Vector3 to, float duration) {
            float t = 0f;
            while (t < duration && visual != null) {
                t += Time.deltaTime;
                visual.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            if (visual != null) visual.localPosition = to;
        }

        /// <summary>Outline clignotant sur la CB (HoverOutline du prefab AtmCard) tant que le
        /// joueur n'a pas cliqué — incite à cliquer sur la fente.</summary>
        private IEnumerator CardPulseCoroutine(Transform card) {
            HoverOutline outline = card != null ? card.GetComponent<HoverOutline>() : null;
            if (outline == null) yield break;
            while (true) {
                outline.Show();
                yield return new WaitForSeconds(CardPulseOn);
                outline.Hide();
                yield return new WaitForSeconds(CardPulseOff);
            }
        }

        private void StopCardPulse() {
            if (this._cardPulseRoutine != null) { StopCoroutine(this._cardPulseRoutine); this._cardPulseRoutine = null; }
            Transform card = this._atm != null ? this._atm.CardVisual : null;
            HoverOutline outline = card != null ? card.GetComponent<HoverOutline>() : null;
            if (outline != null) outline.Hide();
        }

        // ── Liasse physique dans la fente à billets ──────────────────────────────

        private Vector3 CashInPos() => this._cashOutPos + Vector3.back * CashTravel;

        /// <summary>Retrait confirmé par le serveur : les billets SORTENT de la fente du bas,
        /// restent à prendre un instant, puis disparaissent (rangés dans la poche).</summary>
        private IEnumerator CashOutCoroutine() {
            Transform cash = this._atm != null ? this._atm.CashVisual : null;
            if (cash != null) {
                cash.gameObject.SetActive(true);
                yield return SlideVisual(cash, this.CashInPos(), this._cashOutPos, CashSlideDuration);
                yield return new WaitForSeconds(CashTakePause);
                cash.gameObject.SetActive(false);
            }
            this._cashRoutine = null;
        }

        /// <summary>Dépôt : les billets apparaissent dans la fente et sont AVALÉS, puis
        /// seulement l'ordre part au serveur — le joueur « nourrit » la machine.</summary>
        private IEnumerator DepositCashCoroutine(int amount) {
            Transform cash = this._atm != null ? this._atm.CashVisual : null;
            if (cash != null) {
                cash.gameObject.SetActive(true);
                yield return SlideVisual(cash, this._cashOutPos, this.CashInPos(), CashSlideDuration);
                cash.gameObject.SetActive(false);
            }
            NetworkClient.Send(new C2S_AtmDeposit { amount = amount });
            this._cashRoutine = null;
        }

        /// <summary>Après l'envoi d'un retrait : attend la confirmation serveur (le solde
        /// SyncVar baisse) avant de jouer la sortie des billets ; abandonne au timeout.
        /// Si AUCUNE erreur n'a été affichée entre temps (refus serveur silencieux, message
        /// perdu…), un feedback générique part — le joueur ne doit jamais rester sans réponse.</summary>
        private IEnumerator WatchWithdrawCoroutine(int balanceBefore) {
            float deadline = Time.unscaledTime + WithdrawConfirmTimeout;
            while (Time.unscaledTime < deadline) {
                PlayerBankAccount bank = LocalBank();
                if (bank == null) break;
                if (bank.Money < balanceBefore) {
                    yield return this.CashOutCoroutine();
                    yield break;
                }
                yield return null;
            }
            this._cashRoutine = null;
            if (!this._errorShownSinceSend) WorldToastManager.ShowError("Le retrait n'a pas abouti");
        }

        // ── Feedback d'erreur SUR l'écran (détournement des toasts locaux) ───────

        /// <summary>Intercepteur posé sur <see cref="WorldToastManager.LocalToastInterceptor"/>
        /// pendant la session : tout toast local (pré-check client comme refus serveur) devient
        /// une ligne de feedback en bas de l'écran ATM — le son d'erreur reste joué par le
        /// manager. Retourne false hors session (toast monde normal).</summary>
        private bool OnLocalToast(string title, string subtitle, ToastKind kind) {
            if (!this.IsOpen || this.feedbackLabel == null) return false;
            string text = string.IsNullOrEmpty(subtitle) ? title : $"{title} — {subtitle}";
            this.ShowFeedback(text, kind);
            return true;
        }

        /// <summary>Affiche la ligne de feedback (rouge pour une erreur, vert pour un succès)
        /// pendant <see cref="FeedbackDuration"/>, puis l'efface. Un nouveau message remplace
        /// l'ancien et relance le compte à rebours.</summary>
        private void ShowFeedback(string text, ToastKind kind) {
            if (kind == ToastKind.Error) this._errorShownSinceSend = true;
            if (this.feedbackLabel == null) return;
            this.feedbackLabel.text = text;
            this.feedbackLabel.color = kind switch {
                ToastKind.Error   => new Color(1f, 0.42f, 0.38f, 1f),
                ToastKind.Success => new Color(0.49f, 0.99f, 0.60f, 1f),
                _                 => new Color(0.85f, 0.92f, 1f, 1f),
            };
            if (this._feedbackRoutine != null) StopCoroutine(this._feedbackRoutine);
            this._feedbackRoutine = StartCoroutine(this.HideFeedbackCoroutine());
        }

        private IEnumerator HideFeedbackCoroutine() {
            yield return new WaitForSeconds(FeedbackDuration);
            this._feedbackRoutine = null;
            this.ClearFeedback();
        }

        private void ClearFeedback() {
            if (this._feedbackRoutine != null) { StopCoroutine(this._feedbackRoutine); this._feedbackRoutine = null; }
            if (this.feedbackLabel != null) this.feedbackLabel.text = string.Empty;
        }

        // ── Masquage du joueur local (pattern TpsShoulderCamera / lunette) ───────

        private readonly List<KeyValuePair<Renderer, ShadowCastingMode>> _hiddenRenderers
            = new List<KeyValuePair<Renderer, ShadowCastingMode>>();
        private bool _playerHidden;

        private void SetLocalPlayerHidden(bool hidden) {
            if (hidden == this._playerHidden) return;
            this._playerHidden = hidden;

            if (hidden) {
                if (PlayerController.Local == null) return;
                this._hiddenRenderers.Clear();
                foreach (Renderer r in PlayerController.Local.GetComponentsInChildren<Renderer>()) {
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    this._hiddenRenderers.Add(new KeyValuePair<Renderer, ShadowCastingMode>(r, r.shadowCastingMode));
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            } else {
                foreach (KeyValuePair<Renderer, ShadowCastingMode> entry in this._hiddenRenderers) {
                    if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
                }
                this._hiddenRenderers.Clear();
            }
        }

        // ── Soldes / montants ────────────────────────────────────────────────────

        private void OnMoneyChanged(int _) => this.RefreshBalances();

        /// <summary>Rafraîchit les deux soldes (banque + espèces) sur l'écran courant.
        /// Appelé par événement ET par polling (les espèces suivent la poche) — le cache
        /// _shownBank/_shownCash évite de réécrire le TMP à l'identique chaque frame.</summary>
        private void RefreshBalances() {
            PlayerBankAccount bank = LocalBank();
            int bankMoney = bank != null ? bank.Money : -1;
            int cashTotal = LocalTotalCash();
            if (bankMoney == this._shownBank && cashTotal == this._shownCash) return;
            this._shownBank = bankMoney;
            this._shownCash = cashTotal;

            string bankText = bankMoney >= 0 ? MoneyFormat.FormatBalance(bankMoney) : "—";
            string line = $"En banque : <color=#7DFC99>{bankText}</color>   ·   Sur vous : <color=#7DFC99>{MoneyFormat.FormatBalance(cashTotal)}</color>";
            if (this.choiceBalancesLabel    != null) this.choiceBalancesLabel.text    = line;
            if (this.operationBalancesLabel != null) this.operationBalancesLabel.text = line;
        }

        private static PlayerBankAccount LocalBank() {
            return PlayerController.Local != null ? PlayerController.Local.GetComponent<PlayerBankAccount>() : null;
        }

        /// <summary>Total des espèces SUR le joueur local : liasses de la poche (snapshot
        /// S2C_PocketSync, champ CashAmount) + liasse tenue (CashItemBehaviour, S2C_ItemCash)
        /// si ce n'est PAS la projection hotbar d'un slot de poche déjà compté — même
        /// déduplication que le serveur (TakeCashAmount).</summary>
        private static int LocalTotalCash() {
            int total = 0;
            bool selectedSlotIsCash = false;

            if (ClientItemManager.LastPocketSnapshot.HasValue) {
                S2C_PocketSync snapshot = ClientItemManager.LastPocketSnapshot.Value;
                if (snapshot.Items != null) {
                    foreach (S2C_PocketItem item in snapshot.Items) {
                        if (item.ConfigId != CashItem.ConfigId) continue;
                        total += Mathf.Max(0, item.CashAmount);
                        if (item.SlotIndex == HotbarUI.SelectedSlotIndex) selectedSlotIsCash = true;
                    }
                }
            }

            PlayerHands hands = PlayerController.Local != null ? PlayerController.Local.PlayerHands : null;
            if (hands != null && !selectedSlotIsCash) {
                if (hands.RightHandItem is CashItemBehaviour right) total += Mathf.Max(0, right.Amount);
                else if (hands.LeftHandItem is CashItemBehaviour left) total += Mathf.Max(0, left.Amount);
            }
            return total;
        }

        /// <summary>Bouton de montant prédéfini : remplit le champ. 0 = MAX (tout le solde au
        /// retrait, toutes les espèces au dépôt).</summary>
        private void OnPreset(int value) {
            if (this.amountInput == null) return;
            if (value <= 0) {
                PlayerBankAccount bank = LocalBank();
                value = this._step == Step.Withdraw ? (bank != null ? bank.Money : 0) : LocalTotalCash();
            }
            this.amountInput.text = value > 0 ? value.ToString() : string.Empty;
        }

        private void Confirm() {
            if (this._cashRoutine != null) return; // billets en mouvement — pas de double ordre
            if (this._step == Step.Withdraw) this.ConfirmWithdraw();
            else if (this._step == Step.Deposit) this.ConfirmDeposit();
        }

        private int ReadAmount() {
            int amount = 0;
            if (this.amountInput != null) int.TryParse(this.amountInput.text, out amount);
            return amount;
        }

        private void ConfirmWithdraw() {
            int amount = this.ReadAmount();
            if (amount <= 0) {
                WorldToastManager.ShowError("Saisissez un montant");
                return;
            }

            // Pré-checks client (feedback immédiat) — le serveur revalide tout.
            PlayerBankAccount bank = LocalBank();
            if (bank == null) return;
            if (bank.Money < amount) {
                WorldToastManager.ShowError("Fonds insuffisants");
                return;
            }
            // La liasse arrive dans la POCHE (jamais dans les mains) : il faut un slot libre.
            if (ClientItemManager.LastPocketSnapshot.HasValue) {
                S2C_PocketSync snapshot = ClientItemManager.LastPocketSnapshot.Value;
                if (snapshot.SlotCount > 0 && snapshot.Items != null && snapshot.Items.Length >= snapshot.SlotCount) {
                    WorldToastManager.ShowError(InventoryToasts.NoSpaceInInventory);
                    return;
                }
            }

            this._errorShownSinceSend = false;
            NetworkClient.Send(new C2S_AtmWithdraw { amount = amount });
            if (this.amountInput != null) this.amountInput.text = string.Empty;
            this._cashRoutine = StartCoroutine(this.WatchWithdrawCoroutine(bank.Money));
        }

        private void ConfirmDeposit() {
            int amount = this.ReadAmount();
            if (amount <= 0) {
                WorldToastManager.ShowError("Saisissez un montant");
                return;
            }

            // Pré-check client (feedback immédiat) — le serveur revalide et fait la monnaie.
            if (LocalTotalCash() < amount) {
                WorldToastManager.ShowError("Pas assez d'espèces sur vous");
                return;
            }

            if (this.amountInput != null) this.amountInput.text = string.Empty;
            this._cashRoutine = StartCoroutine(this.DepositCashCoroutine(amount));
        }
    }
}
