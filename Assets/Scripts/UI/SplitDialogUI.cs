using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Modale « Diviser » : permet de choisir une quantité à extraire d'une pile (1..max-1)
    /// via slider + champ texte synchronisés. Validation envoie la quantité choisie au callback ;
    /// Annuler ou Échap referme sans appel. Authored une fois dans le HUD (singleton).
    /// </summary>
    public class SplitDialogUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private TMP_Text       titleLabel;
        [SerializeField] private Slider         quantitySlider;
        [SerializeField] private TMP_InputField quantityInput;
        [SerializeField] private Button         confirmButton;
        [SerializeField] private Button         cancelButton;

        public static SplitDialogUI Instance;

        private System.Action<int> _onConfirm;
        private int _minValue = 1;
        private int _maxValue = 1;
        private bool _syncing; // garde anti-récursion slider ↔ input

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            // Sous-canvas dédié avec tri prioritaire : la modale doit s'afficher AU-DESSUS
            // de l'inventaire ouvert (sinon elle apparaît derrière son HUD). Même pattern
            // que InventoryActionMenu pour éviter d'être masqué par les sœurs canvas.
            Canvas own = GetComponent<Canvas>();
            if (own == null) {
                own = gameObject.AddComponent<Canvas>();
                if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                    gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            own.overrideSorting = true;
            own.sortingOrder    = 900; // au-dessus du HUD (inventaire), sous InventoryActionMenu (1000)

            if (this.confirmButton != null) this.confirmButton.onClick.AddListener(this.Confirm);
            if (this.cancelButton  != null) this.cancelButton.onClick.AddListener(this.Hide);
            if (this.quantitySlider != null) {
                this.quantitySlider.wholeNumbers = true;
                this.quantitySlider.onValueChanged.AddListener(this.OnSliderChanged);
            }
            if (this.quantityInput != null) {
                this.quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                this.quantityInput.onValueChanged.AddListener(this.OnInputChanged);
                this.quantityInput.onSubmit.AddListener(_ => this.Confirm());
            }

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>Ouvre la modale. <paramref name="onConfirm"/> reçoit la quantité choisie.
        /// Fail open (callback non appelé) si pas d'instance autorée → caller ne reste pas bloqué.</summary>
        public static void Request(string title, int min, int max, int defaultValue, System.Action<int> onConfirm) {
            if (Instance == null) return;
            Instance.Show(title, min, max, defaultValue, onConfirm);
        }

        private void Show(string title, int min, int max, int defaultValue, System.Action<int> onConfirm) {
            this._onConfirm = onConfirm;
            this._minValue = Mathf.Max(1, min);
            this._maxValue = Mathf.Max(this._minValue, max);
            int clamped = Mathf.Clamp(defaultValue, this._minValue, this._maxValue);

            if (this.titleLabel != null) this.titleLabel.text = title;
            if (this.quantitySlider != null) {
                this._syncing = true;
                this.quantitySlider.minValue = this._minValue;
                this.quantitySlider.maxValue = this._maxValue;
                this.quantitySlider.value = clamped;
                this._syncing = false;
            }
            if (this.quantityInput != null) this.quantityInput.text = clamped.ToString();

            this.gameObject.SetActive(true);
            if (this.quantityInput != null) {
                this.quantityInput.Select();
                this.quantityInput.ActivateInputField();
            }
        }

        private void OnSliderChanged(float v) {
            if (this._syncing) return;
            int q = Mathf.Clamp(Mathf.RoundToInt(v), this._minValue, this._maxValue);
            this._syncing = true;
            if (this.quantityInput != null) this.quantityInput.text = q.ToString();
            this._syncing = false;
        }

        private void OnInputChanged(string txt) {
            if (this._syncing) return;
            if (!int.TryParse(txt, out int q)) return;
            q = Mathf.Clamp(q, this._minValue, this._maxValue);
            this._syncing = true;
            if (this.quantitySlider != null) this.quantitySlider.value = q;
            this._syncing = false;
        }

        public void Confirm() {
            int chosen = this._minValue;
            if (this.quantityInput != null && int.TryParse(this.quantityInput.text, out int parsed))
                chosen = Mathf.Clamp(parsed, this._minValue, this._maxValue);
            else if (this.quantitySlider != null)
                chosen = Mathf.Clamp(Mathf.RoundToInt(this.quantitySlider.value), this._minValue, this._maxValue);
            System.Action<int> cb = this._onConfirm;
            this.Hide();
            cb?.Invoke(chosen);
        }

        public void Hide() {
            this._onConfirm = null;
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
