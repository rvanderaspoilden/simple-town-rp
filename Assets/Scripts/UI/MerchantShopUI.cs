using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Modale boutique d'un NPC marchand. Même pattern que <see cref="BuyConfirmUI"/> : singleton,
    /// abonnements en Awake (le panneau se cache lui-même → un abonnement OnEnable ne se déclencherait
    /// jamais), clonage d'un template de ligne désactivé autoré dans le prefab HUD.
    ///
    /// Flux : <see cref="ClientNpcManager.OnMerchantCatalogReceived"/> ouvre la modale et peuple la
    /// liste ; un clic « Acheter » envoie <c>C2S_MerchantBuy</c> ; <see cref="ClientNpcManager.OnMerchantBuyResult"/>
    /// affiche le toast (succès silencieux / erreur sonore) et rafraîchit l'affordabilité — la modale
    /// reste ouverte pour enchaîner les achats.
    /// </summary>
    public class MerchantShopUI : MonoBehaviour {
        [Header("Refs")]
        [SerializeField] private TMP_Text       titleLabel;
        [SerializeField] private TMP_Text       statusLabel;   // ligne de feedback optionnelle
        [SerializeField] private Button         closeButton;
        [SerializeField] private Transform      listContainer; // parent des lignes clonées
        [SerializeField] private MerchantShopRow rowTemplate;  // template désactivé

        public static MerchantShopUI Instance;

        private int _npcId = -1;
        private readonly List<MerchantShopRow> _rows = new List<MerchantShopRow>();

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;

            if (this.closeButton != null) this.closeButton.onClick.AddListener(this.Hide);
            if (this.rowTemplate != null) this.rowTemplate.gameObject.SetActive(false);

            ClientNpcManager.OnMerchantCatalogReceived += this.OnCatalog;
            ClientNpcManager.OnMerchantBuyResult       += this.OnBuyResult;
            PlayerBankAccount.OnLocalMoneyChanged      += this.OnLocalMoneyChanged;

            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            ClientNpcManager.OnMerchantCatalogReceived -= this.OnCatalog;
            ClientNpcManager.OnMerchantBuyResult       -= this.OnBuyResult;
            PlayerBankAccount.OnLocalMoneyChanged      -= this.OnLocalMoneyChanged;
            if (Instance == this) Instance = null;
        }

        private void OnCatalog(int npcId, string merchantLabel, MerchantCatalogEntry[] entries) {
            this.Show(npcId, merchantLabel, entries);
        }

        public void Show(int npcId, string merchantLabel, MerchantCatalogEntry[] entries) {
            this._npcId = npcId;
            if (this.titleLabel  != null) this.titleLabel.text  = string.IsNullOrEmpty(merchantLabel) ? "Marchand" : merchantLabel;
            if (this.statusLabel != null) this.statusLabel.text = string.Empty;

            this.BuildRows(entries);
            this.RefreshAffordability();
            this.gameObject.SetActive(true);
        }

        private void BuildRows(MerchantCatalogEntry[] entries) {
            this.ClearRows();
            if (this.rowTemplate == null || this.listContainer == null || entries == null) return;

            foreach (MerchantCatalogEntry e in entries) {
                MerchantShopRow row = Instantiate(this.rowTemplate, this.listContainer);
                row.gameObject.SetActive(true);
                row.Bind(e, this.OnRowBuyClicked);
                this._rows.Add(row);
            }
        }

        private void ClearRows() {
            foreach (MerchantShopRow r in this._rows) {
                if (r != null) Destroy(r.gameObject);
            }
            this._rows.Clear();
        }

        private void OnRowBuyClicked(int itemConfigId) {
            if (this._npcId < 0) return;
            if (this.statusLabel != null) this.statusLabel.text = "Achat…";
            ClientNpcManager.Instance?.RequestBuy(this._npcId, itemConfigId);
        }

        private void OnBuyResult(int npcId, int itemConfigId, bool success, byte reason) {
            if (npcId != this._npcId) return;

            if (success) {
                WorldToastManager.ShowSuccess("Achat réussi");
                if (this.statusLabel != null) this.statusLabel.text = string.Empty;
                this.RefreshAffordability();
                return;
            }

            string msg = ReasonText(reason);
            WorldToastManager.ShowError(msg);
            if (this.statusLabel != null) this.statusLabel.text = msg;
        }

        private void OnLocalMoneyChanged(int newAmount) {
            if (this.gameObject.activeSelf) this.RefreshAffordability();
        }

        private void RefreshAffordability() {
            int money = LocalMoney();
            foreach (MerchantShopRow r in this._rows) {
                if (r != null) r.SetAffordable(money >= r.Price);
            }
        }

        private static int LocalMoney() {
            PlayerController local = PlayerController.Local;
            if (local == null) return 0;
            PlayerBankAccount bank = local.GetComponent<PlayerBankAccount>();
            return bank != null ? bank.Money : 0;
        }

        private static string ReasonText(byte reason) {
            switch (reason) {
                case 1:  return "Cet objet n'est plus disponible.";
                case 2:  return "Fonds insuffisants.";
                case 3:  return "Tu ne peux pas porter ça maintenant.";
                case 4:  return "Le marchand n'est plus disponible.";
                default: return "Achat impossible.";
            }
        }

        public void Hide() {
            this._npcId = -1;
            this.ClearRows();
            this.gameObject.SetActive(false);
        }

        private void Update() {
            if (this.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) this.Hide();
        }
    }
}
