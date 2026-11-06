using System.Collections;
using System.Collections.Generic;
using Mirror;
using Sim.Entities.Persistence;
using Sim.Scriptables;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Flux de la cabine d'essayage (boutique de vêtements) — pattern <see cref="AtmSystemBootstrap"/>.
    /// Le client envoie son panier (<see cref="C2S_ClothingShopPurchase"/>) ; le serveur résout le
    /// portant le plus proche (registre <see cref="ClothingRackBehaviour.All"/> — la City est aussi
    /// chargée côté serveur), revalide catalogue + genre + fonds + place, débite UNE entrée ledger
    /// par article (reason shop_purchase, pattern marchand NPC), puis crée chaque vêtement :
    /// directement ÉQUIPÉ (place wearables, slot de sa catégorie libre) sinon via l'acquisition
    /// classique (poche d'abord). Un échec de création individuel est remboursé article par article.
    /// </summary>
    public static class ClothingShopSystemBootstrap {
        /// <summary>Portée serveur (m, planaire) : portée client (3 m) + tolérance réseau.</summary>
        private const float ServerRange = 5f;

        /// <summary>Un article par catégorie au plus côté client → panier borné aux 5 slots.</summary>
        private const int MaxCartSize = ClothingConfig.WearSlotCount;

        // counterpartyId libre côté backend (@IsOptional) — pattern MerchantCounterparty.
        private const string ShopCounterparty = "CLOTHING_SHOP";

        // Codes de refus (cf. S2C_ClothingShopPurchaseResult.ReasonCode).
        public const byte ReasonOk          = 0;
        public const byte ReasonUnavailable = 1;
        public const byte ReasonFunds       = 2;
        public const byte ReasonNoSpace     = 3;
        public const byte ReasonTooFar      = 4;

        // ── Client ──────────────────────────────────────────────────────────────────

        /// <summary>(success, reasonCode, totalPaid) — consommé par FittingRoomUI.</summary>
        public static event System.Action<bool, byte, int> OnPurchaseResult;

        public static void OnClientStart() {
            NetworkClient.RegisterHandler<S2C_ClothingShopPurchaseResult>(OnResult);
        }

        public static void OnClientStop() {
            NetworkClient.UnregisterHandler<S2C_ClothingShopPurchaseResult>();
        }

        private static void OnResult(S2C_ClothingShopPurchaseResult msg) {
            OnPurchaseResult?.Invoke(msg.Success, msg.ReasonCode, msg.TotalPaid);
        }

        // ── Serveur ─────────────────────────────────────────────────────────────────

        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_ClothingShopPurchase>(OnPurchase);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_ClothingShopPurchase>();
        }

        private static void OnPurchase(NetworkConnectionToClient conn, C2S_ClothingShopPurchase msg) {
            if (conn?.identity == null) return;
            var player = conn.identity.GetComponent<PlayerController>();
            var bank   = conn.identity.GetComponent<PlayerBankAccount>();
            if (player == null || bank == null) return;

            if (msg.ConfigIds == null || msg.ConfigIds.Length == 0 || msg.ConfigIds.Length > MaxCartSize) {
                Reply(conn, false, ReasonUnavailable, 0);
                return;
            }

            ClothingRackBehaviour rack =
                ClothingRackBehaviour.NearestInRange(conn.identity.transform.position, ServerRange);
            ClothingShopCatalogConfig catalog = rack != null ? rack.Catalog : null;
            if (catalog == null) {
                Reply(conn, false, ReasonTooFar, 0);
                return;
            }

            Gender gender = player.CharacterData.Style.gender;
            uint netId = conn.identity.netId;

            // Résolution + plan de placement : slot wearables libre → équipé direct ; slot
            // OCCUPÉ → REMPLACEMENT (l'ancien part en poche, sinon boîte de livraison) ; doublon
            // de catégorie dans le panier → acquisition classique. Pas de refus « pas de
            // place » : RÈGLE PROJET — un achat qui ne tient pas dans l'inventaire part dans
            // la boîte de livraison.
            var wearPlan    = new List<(ClothingConfig cfg, int price)>();
            var replacePlan = new List<(ClothingConfig cfg, int price)>();
            var pocketPlan  = new List<(ClothingConfig cfg, int price)>();
            var plannedSlots = new HashSet<int>();
            int total = 0;

            foreach (int id in msg.ConfigIds) {
                if (!catalog.TryGetPrice(id, out int price, out ItemConfig cfg)
                    || !(cfg is ClothingConfig clothing) || cfg.Prefab == null
                    || !clothing.SupportsGender(gender)) {
                    Reply(conn, false, ReasonUnavailable, 0);
                    return;
                }

                total += price;
                int slot = clothing.WearSlotIndex;
                if (plannedSlots.Contains(slot)) {
                    pocketPlan.Add((clothing, price));
                } else {
                    plannedSlots.Add(slot);
                    if (ServerItemManager.Instance.IsWearableSlotFree(netId, slot)) wearPlan.Add((clothing, price));
                    else replacePlan.Add((clothing, price));
                }
            }

            if (bank.Money < total) {
                Reply(conn, false, ReasonFunds, 0);
                return;
            }

            // Débit PAR ARTICLE (une entrée ledger chacun) AVANT les créations async ; tout
            // échec de création individuel est remboursé article par article dans la foulée.
            foreach ((ClothingConfig cfg, int price) in wearPlan)    Debit(bank, cfg, price);
            foreach ((ClothingConfig cfg, int price) in replacePlan) Debit(bank, cfg, price);
            foreach ((ClothingConfig cfg, int price) in pocketPlan)  Debit(bank, cfg, price);

            ApiManager.Instance.StartCoroutine(GrantCartCoroutine(conn, bank, wearPlan, replacePlan, pocketPlan, total));
        }

        private static IEnumerator GrantCartCoroutine(NetworkConnectionToClient conn, PlayerBankAccount bank,
            List<(ClothingConfig cfg, int price)> wearPlan, List<(ClothingConfig cfg, int price)> replacePlan,
            List<(ClothingConfig cfg, int price)> pocketPlan, int total)
        {
            foreach ((ClothingConfig cfg, int price) in wearPlan) {
                bool granted = false;
                yield return ServerItemManager.Instance.GrantClothingIntoWearables(conn, cfg, ok => granted = ok);
                if (!granted) FallbackAcquire(conn, bank, cfg, price);
            }

            foreach ((ClothingConfig cfg, int price) in replacePlan) {
                bool granted = false;
                yield return ServerItemManager.Instance.ReplaceWornClothing(conn, cfg, ok => granted = ok);
                if (!granted) FallbackAcquire(conn, bank, cfg, price);
            }

            foreach ((ClothingConfig cfg, int price) in pocketPlan) {
                FallbackAcquire(conn, bank, cfg, price);
            }

            if (conn != null && conn.identity != null) Reply(conn, true, ReasonOk, total);
        }

        /// <summary>Acquisition classique (poche d'abord) ; inventaire plein → boîte de
        /// livraison (RÈGLE PROJET) ; remboursement seulement si le dépôt échoue aussi.</summary>
        private static void FallbackAcquire(NetworkConnectionToClient conn, PlayerBankAccount bank,
            ClothingConfig cfg, int price) {
            ServerItemManager.Instance.SpawnItemIntoInventory(conn, cfg.ID, cfg, persistent: true,
                onFail: _ => ApiManager.Instance.StartCoroutine(
                    ServerItemManager.Instance.DepositPurchaseInDeliveryBox(conn, cfg,
                        deposited => { if (!deposited) Refund(bank, cfg, price); })));
        }

        private static void Debit(PlayerBankAccount bank, ClothingConfig cfg, int price) {
            bank.PostLedger(-price, LedgerReason.ShopPurchase, LedgerCounterparty.System,
                ShopCounterparty, configId: cfg.ID);
        }

        private static void Refund(PlayerBankAccount bank, ClothingConfig cfg, int price) {
            if (bank == null) return;
            bank.PostLedger(price, LedgerReason.ShopPurchase, LedgerCounterparty.System,
                ShopCounterparty, configId: cfg.ID);
        }

        private static void Reply(NetworkConnectionToClient conn, bool success, byte reason, int totalPaid) {
            conn.Send(new S2C_ClothingShopPurchaseResult {
                Success = success, ReasonCode = reason, TotalPaid = totalPaid,
            });
        }
    }
}
