using System;
using Mirror;
using Sim.Building;
using Sim.Entities.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    /// <summary>
    /// Detail card for a land parcel. AUTHORED in HUD Manager.prefab (pattern of
    /// <see cref="IdentityCardUI"/> / <see cref="ConfirmDialogUI"/>): the panel hosts this component,
    /// registers Instance + button listeners in Awake, then hides itself. Opened from the parcel's
    /// "Consulter" sign in EVERY state and adapts its actions to the current occupancy:
    ///   • Available → "Acheter" (<see cref="C2S_BuyLand"/>) and "Louer" (<see cref="C2S_RentLand"/>).
    ///   • Owned     → "Vendre", owner-only (<see cref="C2S_SellLand"/>, 70% refund, after confirm).
    ///   • Rented    → "Résilier le bail", tenant-only (<see cref="C2S_TerminateLandLease"/>, no refund,
    ///                 after confirm).
    /// All decisions are revalidated/executed server-side by LandSystemBootstrap.
    /// </summary>
    public class LandParcelInfoUI : MonoBehaviour {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text ownerText;     // occupancy line (owner / tenant / availability)
        [SerializeField] private TMP_Text dateText;      // price+rent (available) / date (owned/rented)
        [SerializeField] private Button   buyButton;     // available only
        [SerializeField] private TMP_Text buyLabel;
        [SerializeField] private Button   rentButton;    // available only
        [SerializeField] private TMP_Text rentLabel;
        [SerializeField] private Button   sellButton;    // owned: Vendre ; rented: Résilier le bail
        [SerializeField] private TMP_Text sellLabel;
        [SerializeField] private Button   closeButton;

        /// <summary>Must match LandSystemBootstrap.RefundRate (client display only — server is authoritative).</summary>
        private const float RefundRate = 0.7f;

        public static LandParcelInfoUI Instance;

        private LandParcelController _parcel;
        private int _currentParcelId = int.MinValue; // guards stale async date callbacks

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (buyButton   != null) buyButton.onClick.AddListener(OnBuyClicked);
            if (rentButton  != null) rentButton.onClick.AddListener(OnRentClicked);
            if (sellButton  != null) sellButton.onClick.AddListener(OnSellOrTerminateClicked);
            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        public void ShowFor(LandParcelController parcel) {
            if (parcel == null || parcel.Config == null) return;
            _parcel = parcel;
            _currentParcelId = parcel.ConfigId;

            string label = string.IsNullOrEmpty(parcel.Config.displayName) ? "Terrain" : parcel.Config.displayName;
            if (titleText != null) titleText.text = label;

            string localId = PlayerController.Local?.CharacterData?.Id;

            if (parcel.IsOwned)       ShowOwned(parcel, localId);
            else if (parcel.IsRented) ShowRented(parcel, localId);
            else                      ShowAvailable(parcel);

            this.gameObject.SetActive(true);
        }

        // ── State: Available (on sale / for rent) ───────────────────────────────
        private void ShowAvailable(LandParcelController parcel) {
            if (ownerText != null) ownerText.text = "Disponible — à vendre ou à louer";
            if (dateText  != null) {
                string surface = parcel.Config.surface > 0f ? $"\nSurface : {parcel.Config.surface:0} m²" : "";
                dateText.text = $"Prix d'achat : {parcel.Config.price}\nLoyer : {parcel.Config.rentPrice} / période{surface}";
            }

            SetButton(buyButton,  buyLabel,  true,  $"Acheter ({parcel.Config.price})");
            SetButton(rentButton, rentLabel, true,  $"Louer ({parcel.Config.rentPrice})");
            SetButton(sellButton, sellLabel, false, null);
        }

        // ── State: Owned ─────────────────────────────────────────────────────────
        private void ShowOwned(LandParcelController parcel, string localId) {
            string owner = string.IsNullOrEmpty(parcel.OwnerName) ? "Propriétaire inconnu" : parcel.OwnerName;
            if (ownerText != null) ownerText.text = $"Propriétaire : {owner}";
            if (dateText  != null) dateText.text = "Acheté le : …";

            SetButton(buyButton,  buyLabel,  false, null);
            SetButton(rentButton, rentLabel, false, null);

            // Only the owner can sell.
            bool isOwner = localId == parcel.OwnerCharId;
            int  refund  = Mathf.Max(0, Mathf.RoundToInt(parcel.Config.price * RefundRate));
            SetButton(sellButton, sellLabel, isOwner, $"Vendre ({refund})");

            FetchDate(parcel, "Acheté le");
        }

        // ── State: Rented ──────────────────────────────────────────────────────────
        private void ShowRented(LandParcelController parcel, string localId) {
            string tenant = string.IsNullOrEmpty(parcel.TenantName) ? "Locataire inconnu" : parcel.TenantName;
            if (ownerText != null) ownerText.text = $"Loué par : {tenant}\nLoyer : {parcel.Config.rentPrice} / période";
            if (dateText  != null) dateText.text = "Loué depuis le : …";

            SetButton(buyButton,  buyLabel,  false, null);
            SetButton(rentButton, rentLabel, false, null);

            // Only the tenant can terminate.
            bool isTenant = localId == parcel.TenantCharId;
            SetButton(sellButton, sellLabel, isTenant, "Résilier le bail");

            FetchDate(parcel, "Loué depuis le");
        }

        public void Hide() {
            _currentParcelId = int.MinValue;
            _parcel = null;
            this.gameObject.SetActive(false);
        }

        // ── Actions ──────────────────────────────────────────────────────────────
        private void OnBuyClicked() {
            if (_parcel == null || _parcel.Config == null) return;
            if (!HasFunds(_parcel.Config.price)) { WorldToastManager.ShowError("Fonds insuffisants"); return; }
            if (NetworkClient.active) NetworkClient.Send(new C2S_BuyLand { parcelId = _parcel.Config.id });
            Hide();
        }

        private void OnRentClicked() {
            if (_parcel == null || _parcel.Config == null) return;
            if (!HasFunds(_parcel.Config.rentPrice)) { WorldToastManager.ShowError("Fonds insuffisants"); return; }
            if (NetworkClient.active) NetworkClient.Send(new C2S_RentLand { parcelId = _parcel.Config.id });
            Hide();
        }

        // sellButton serves owner (sell) and tenant (terminate) — route by current occupancy.
        private void OnSellOrTerminateClicked() {
            if (_parcel == null || _parcel.Config == null) return;
            string label = string.IsNullOrEmpty(_parcel.Config.displayName) ? "ce terrain" : _parcel.Config.displayName;
            int parcelId = _parcel.Config.id;

            if (_parcel.IsRented) {
                ConfirmDialogUI.Request("Résilier le bail",
                    $"Rendre {label} à l'office foncier ?\nTes meubles seront renvoyés et les objets posés au sol seront détruits. Aucun remboursement.",
                    () => {
                        if (NetworkClient.active) NetworkClient.Send(new C2S_TerminateLandLease { parcelId = parcelId });
                    });
            } else {
                int refund = Mathf.Max(0, Mathf.RoundToInt(_parcel.Config.price * RefundRate));
                ConfirmDialogUI.Request("Vendre le terrain",
                    $"Revendre {label} à l'office foncier pour {refund} ?\nTes meubles seront renvoyés et les objets posés au sol seront détruits.",
                    () => {
                        if (NetworkClient.active) NetworkClient.Send(new C2S_SellLand { parcelId = parcelId });
                    });
            }
            Hide();
        }

        private void Update() {
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private static bool HasFunds(int amount) {
            PlayerBankAccount bank = PlayerController.Local != null
                ? PlayerController.Local.GetComponent<PlayerBankAccount>() : null;
            return bank == null || bank.Money >= amount; // null bank → let the server decide
        }

        private static void SetButton(Button button, TMP_Text label, bool visible, string text) {
            if (button != null) button.gameObject.SetActive(visible);
            if (label  != null && visible && text != null) label.text = text;
        }

        // Date = the land place's created_at. Fetched on ApiManager so the request survives the panel
        // being hidden; the callback is guarded against a parcel switch.
        private void FetchDate(LandParcelController parcel, string prefix) {
            if (ApiManager.Instance == null) return;
            int requestedId = _currentParcelId;
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.LookupPlaceByKeyCoroutine(parcel.Config.PlaceKey, place => {
                    if (_currentParcelId != requestedId || dateText == null) return; // stale / closed
                    dateText.text = $"{prefix} : {FormatDate(place?.createdAt)}";
                }));
        }

        private static string FormatDate(string iso) {
            if (string.IsNullOrEmpty(iso)) return "—";
            return DateTime.TryParse(iso, out DateTime d) ? d.ToLocalTime().ToString("dd/MM/yyyy") : iso;
        }
    }
}
