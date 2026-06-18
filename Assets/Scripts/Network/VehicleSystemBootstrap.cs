using System.Linq;
using Mirror;
using Sim.Entities.Persistence;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Logique serveur du système véhicule pour les objets de scène NON-réseau (concession +
    /// porte de garage). Ceux-ci n'ont pas de NetworkIdentity : ils envoient des messages C2S,
    /// gérés ici (pattern AcquaintanceSystemBootstrap / SmsSystemBootstrap).
    ///
    /// • Achat : débite l'acheteur (prix issu de la config) puis crée un véhicule possédé rangé
    ///   dans son garage virtuel (place dédiée).
    /// • Sortir : valide la propriété (REST) et spawne le véhicule conduisible à la porte.
    /// • Ranger : détruit le véhicule possédé du joueur le plus proche de la porte.
    /// </summary>
    public static class VehicleSystemBootstrap {
        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_BuyVehicle>(OnBuy);
            NetworkServer.RegisterHandler<C2S_TakeOutVehicle>(OnTakeOut);
            NetworkServer.RegisterHandler<C2S_StoreVehicle>(OnStore);
        }

        public static void OnServerStop() {
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

        // ── Achat (concession) ──────────────────────────────────────────────────────
        private static void OnBuy(NetworkConnectionToClient conn, C2S_BuyVehicle msg) {
            if (conn?.identity == null || string.IsNullOrEmpty(msg.configId)) return;
            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            string charId = pc?.CharacterData?.Id;
            if (bank == null || string.IsNullOrEmpty(charId)) return;

            VehicleConfig config = DatabaseManager.GetVehicleConfigById(msg.configId);
            if (config == null) return;
            int price = config.price;
            if (bank.Money < price) return; // garde serveur (le client a déjà affiché le toast)

            // On stocke l'ID de config dans `model_id` → permet de retrouver le prefab à la sortie.
            string vehId = config.id;
            bank.PostLedger(-price, LedgerReason.ShopPurchase, LedgerCounterparty.System, "CONCESSION");

            var placeBody = new CreatePlaceBody { placeKey = $"garage:{charId}", type = "garage", ownerId = charId };
            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.CreatePlaceCoroutine(placeBody, placeId => {
                    if (string.IsNullOrEmpty(placeId)) return;
                    ApiManager.Instance.StartCoroutine(
                        ApiManager.Instance.CreateGaragedVehicleCoroutine(charId, vehId, placeId, v => {
                            if (v != null && conn != null)
                                conn.Send(new ToastNotificationMessage {
                                    text = $"{config.modelName} acheté !", appId = PhoneAppIds.Bank, worldToast = false });
                        }));
                }));
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
