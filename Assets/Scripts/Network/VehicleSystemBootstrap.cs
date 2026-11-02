using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Entities.Persistence;
using Sim.NPC;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Logique serveur du système véhicule (concessionnaire NPC + porte de garage). Ces objets
    /// n'ont pas de NetworkIdentity : ils envoient des messages C2S, gérés ici (pattern
    /// AcquaintanceSystemBootstrap / SmsSystemBootstrap).
    ///
    /// • Catalogue : renvoie les véhicules en vente du NPC vendeur (VehicleVendorNpcConfig).
    /// • Achat : valide vendeur + catalogue + fonds, débite l'acheteur (prix issu de la config)
    ///   puis crée un véhicule possédé rangé dans son garage virtuel (place dédiée).
    /// • Sortir : valide la propriété (REST) et spawne le véhicule conduisible à la porte.
    /// • Ranger : détruit le véhicule possédé du joueur le plus proche de la porte.
    /// </summary>
    public static class VehicleSystemBootstrap {
        // Codes de refus (cf. S2C_VehicleBuyResult.reasonCode — alignés sur le marchand).
        private const byte ReasonOk          = 0;
        private const byte ReasonUnavailable = 1;
        private const byte ReasonFunds       = 2;
        private const byte ReasonNoVendor    = 4;

        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_RequestVehicleCatalog>(OnCatalogRequest);
            NetworkServer.RegisterHandler<C2S_BuyVehicle>(OnBuy);
            NetworkServer.RegisterHandler<C2S_TakeOutVehicle>(OnTakeOut);
            NetworkServer.RegisterHandler<C2S_StoreVehicle>(OnStore);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_RequestVehicleCatalog>();
            NetworkServer.UnregisterHandler<C2S_BuyVehicle>();
            NetworkServer.UnregisterHandler<C2S_TakeOutVehicle>();
            NetworkServer.UnregisterHandler<C2S_StoreVehicle>();

            // À l'arrêt : on « range » tous les véhicules sortis en persistant vie + essence
            // (fire-and-forget, comme le timestamp ville). Le coffre (place DB) persiste seul.
            if (ApiManager.Instance == null) return;
            foreach (var id in NetworkServer.spawned.Values) {
                if (id == null) continue;
                var vc = id.GetComponent<VehicleController>();
                if (vc != null && !string.IsNullOrEmpty(vc.VehicleDbId))
                    ApiManager.Instance.SaveVehicleStateNow(vc.VehicleDbId, vc.ServerHealth, vc.ServerFuel);
            }
        }

        // ── Catalogue (concessionnaire NPC) ─────────────────────────────────────────
        private static void OnCatalogRequest(NetworkConnectionToClient conn, C2S_RequestVehicleCatalog msg) {
            if (conn?.identity == null) return;
            if (!TryResolveVendor(conn, msg.npcId, out VehicleVendorNpcConfig vendor)) return;

            IReadOnlyList<VehicleConfig> catalog = vendor.VehicleCatalog;
            var entries = new List<VehicleCatalogEntry>(catalog.Count);
            foreach (VehicleConfig v in catalog) {
                if (v == null) continue;
                entries.Add(new VehicleCatalogEntry {
                    configId = v.id,
                    price    = v.price,
                    label    = v.modelName
                });
            }

            conn.Send(new S2C_VehicleCatalog {
                npcId       = msg.npcId,
                vendorLabel = vendor.MerchantLabel,
                entries     = entries.ToArray()
            });
        }

        // ── Achat (concessionnaire NPC) ─────────────────────────────────────────────
        private static void OnBuy(NetworkConnectionToClient conn, C2S_BuyVehicle msg) {
            if (conn?.identity == null || string.IsNullOrEmpty(msg.configId)) return;
            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            string charId = pc?.CharacterData?.Id;
            if (bank == null || string.IsNullOrEmpty(charId)) return;

            if (!TryResolveVendor(conn, msg.npcId, out VehicleVendorNpcConfig vendor)) {
                ReplyBuy(conn, msg, false, ReasonNoVendor);
                return;
            }

            // Autorité : le véhicule doit être au catalogue DU vendeur (prix lu dans la config).
            if (!vendor.TryGetVehicle(msg.configId, out VehicleConfig config) || config.prefab == null) {
                ReplyBuy(conn, msg, false, ReasonUnavailable);
                return;
            }

            int price = config.price;
            if (bank.Money < price) {
                ReplyBuy(conn, msg, false, ReasonFunds);
                return;
            }

            // On stocke l'ID de config dans `model_id` → permet de retrouver le prefab à la sortie.
            string vehId = config.id;
            bank.PostLedger(-price, LedgerReason.ShopPurchase, LedgerCounterparty.System, "CONCESSION");

            // Succès annoncé dès le débit (validation synchrone terminée, pattern marchand) ;
            // la création REST du véhicule garé suit en fire-and-forget.
            ReplyBuy(conn, msg, true, ReasonOk);

            var placeBody = new CreatePlaceBody { placeKey = $"garage:{charId}", type = "garage", ownerId = charId };
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.CreatePlaceCoroutine(placeBody, placeId => {
                    if (string.IsNullOrEmpty(placeId)) {
                        Debug.LogError($"[VehicleSystemBootstrap] Garage place creation failed for char={charId} (vehicle {vehId} paid but not created)");
                        return;
                    }
                    ApiManager.Instance.StartCoroutine(
                        ApiManager.Instance.CreateGaragedVehicleCoroutine(charId, vehId, placeId, v => {
                            if (v == null)
                                Debug.LogError($"[VehicleSystemBootstrap] Garaged vehicle creation failed for char={charId} model={vehId}");
                        }));
                }));
        }

        /// <summary>
        /// Résout un NPC concessionnaire valide pour ce joueur : existe (poolé/actif), porte une
        /// VehicleVendorNpcConfig, et le joueur est dans la même room (pattern NpcMerchantService).
        /// </summary>
        private static bool TryResolveVendor(NetworkConnectionToClient conn, int npcId,
                                             out VehicleVendorNpcConfig vendor) {
            vendor = null;
            if (!NpcAIController.TryGet(npcId, out NpcAIController npc) || npc == null) return false;
            vendor = npc.Merchant as VehicleVendorNpcConfig;
            if (vendor == null) return false;
            return PlayerRoomTracker.Instance.GetRoom(conn) == npc.RoomId;
        }

        private static void ReplyBuy(NetworkConnectionToClient conn, C2S_BuyVehicle msg,
                                     bool success, byte reason) {
            conn.Send(new S2C_VehicleBuyResult {
                npcId      = msg.npcId,
                configId   = msg.configId,
                success    = success,
                reasonCode = reason
            });
        }

        // ── Sortir du garage ────────────────────────────────────────────────────────
        private static void OnTakeOut(NetworkConnectionToClient conn, C2S_TakeOutVehicle msg) {
            if (conn?.identity == null || string.IsNullOrEmpty(msg.vehicleId)) return;
            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            string charId = pc?.CharacterData?.Id;
            if (string.IsNullOrEmpty(charId)) return;
            if (!GarageDoor.TryGet(msg.doorKey, out GarageDoor door)) return;

            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.RetrieveOwnedVehiclesCoroutine(charId, vehicles => {
                    Sim.Entities.VehicleData v = vehicles.FirstOrDefault(x => x != null && x.id == msg.vehicleId);
                    if (v == null || string.IsNullOrEmpty(v.placeId) || v.ownerCharacterId != charId) return;
                    if (GarageDoor.IsVehicleOut(v.id)) return; // déjà dehors
                    door.ServerSpawnVehicle(v);
                }));
        }

        // ── Ranger ──────────────────────────────────────────────────────────────────
        private static void OnStore(NetworkConnectionToClient conn, C2S_StoreVehicle msg) {
            if (conn?.identity == null) return;
            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            string charId = pc?.CharacterData?.Id;
            if (string.IsNullOrEmpty(charId)) return;
            if (!GarageDoor.TryGet(msg.doorKey, out GarageDoor door)) return;
            door.ServerStoreNearest(charId);
        }
    }
}
