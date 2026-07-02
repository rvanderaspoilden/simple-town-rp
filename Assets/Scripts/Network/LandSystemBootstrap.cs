using System.Collections;
using System.Linq;
using Mirror;
using Newtonsoft.Json;
using Sim.Building;
using Sim.Entities;
using Sim.Entities.Persistence;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    /// <summary>
    /// Server + client logic for the land parcel system. Parcels are NON-networked scene objects
    /// (no NetworkIdentity), same pattern as <see cref="VehicleSystemBootstrap"/>: the For-Sale sign
    /// and the build pipeline emit C2S messages that are handled here.
    ///
    /// • Buy : claim the <c>land:{id}</c> place in DB (authoritative owner via idempotent create),
    ///   debit the buyer, set runtime ownership (spawns the delivery box), broadcast to everyone.
    /// • Build / Edit / Remove : land props share the visible "city" room, so they can't be routed
    ///   by connection like apartments — the client carries a parcelId, resolved via
    ///   <see cref="LandParcelController.ById"/>, gated by ownership + room membership, then delegated
    ///   to the shared <see cref="PropInteractionDispatcher"/> / <see cref="ServerPropManager"/>.
    /// • Ownership replication : a per-connection snapshot on spawn + a broadcast on each purchase
    ///   so every client's For-Sale sign hides once a parcel is owned.
    /// </summary>
    public static class LandSystemBootstrap {
        // ── Server lifecycle ──────────────────────────────────────────────────────
        /// <summary>Fraction of the purchase price refunded when a parcel is sold back to the
        /// Land Office.</summary>
        private const float RefundRate = 0.7f;

        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_BuyLand>(OnBuy);
            NetworkServer.RegisterHandler<C2S_SellLand>(OnSell);
            NetworkServer.RegisterHandler<C2S_RentLand>(OnRent);
            NetworkServer.RegisterHandler<C2S_TerminateLandLease>(OnTerminateLease);
            NetworkServer.RegisterHandler<C2S_BuildLandProp>(OnBuildLandProp);
            NetworkServer.RegisterHandler<C2S_EditLandProp>(OnEditLandProp);
            NetworkServer.RegisterHandler<C2S_RemoveLandProp>(OnRemoveLandProp);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_BuyLand>();
            NetworkServer.UnregisterHandler<C2S_SellLand>();
            NetworkServer.UnregisterHandler<C2S_RentLand>();
            NetworkServer.UnregisterHandler<C2S_TerminateLandLease>();
            NetworkServer.UnregisterHandler<C2S_BuildLandProp>();
            NetworkServer.UnregisterHandler<C2S_EditLandProp>();
            NetworkServer.UnregisterHandler<C2S_RemoveLandProp>();
        }

        // ── Client lifecycle ──────────────────────────────────────────────────────
        public static void OnClientStart() {
            NetworkClient.RegisterHandler<S2C_LandOwnership>(OnClientOwnership);
            NetworkClient.RegisterHandler<S2C_LandSnapshot>(OnClientSnapshot);
        }

        public static void OnClientStop() {
            NetworkClient.UnregisterHandler<S2C_LandOwnership>();
            NetworkClient.UnregisterHandler<S2C_LandSnapshot>();
        }

        // ── Buy ─────────────────────────────────────────────────────────────────
        private static void OnBuy(NetworkConnectionToClient conn, C2S_BuyLand msg) {
            if (conn?.identity == null) return;
            if (!LandParcelController.ById.TryGetValue(msg.parcelId, out var parcel) || parcel == null
                || parcel.Config == null) return;
            if (parcel.IsOwned || parcel.IsRented) return; // already occupied (runtime guard)

            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            string charId = pc?.CharacterData?.Id;
            if (bank == null || string.IsNullOrEmpty(charId)) return;
            if (bank.Money < parcel.Config.price) return; // server guard (client already toasted)

            string ownerName = pc.CharacterData.Identity.FullName;
            ApiManager.Instance.StartCoroutine(BuyLandCoroutine(conn, parcel, bank, charId, ownerName));
        }

        private static IEnumerator BuyLandCoroutine(NetworkConnectionToClient conn, LandParcelController parcel,
                PlayerBankAccount bank, string charId, string ownerName) {
            int price = parcel.Config.price;

            // 1. Claim ownership in DB first. findOrCreate only sets owner_id on INSERT, so the
            //    FIRST buyer wins and the response carries the authoritative owner — this both
            //    persists the purchase and resolves a same-frame double-buy before any debit.
            var placeBody = new CreatePlaceBody { placeKey = parcel.Config.PlaceKey, type = "land", ownerId = charId };
            UnityWebRequest req = ApiManager.Instance.CreatePlaceRequest(placeBody);
            yield return req.SendWebRequest();

            if (req.responseCode != 200 && req.responseCode != 201) {
                Debug.LogError($"[LandSystemBootstrap] CreatePlace failed code={req.responseCode} body={req.downloadHandler.text}");
                SendToast(conn, "Achat impossible.", ToastKind.Error, worldToast: true);
                yield break;
            }

            PlaceJson place = null;
            try { place = JsonConvert.DeserializeObject<PlaceJson>(req.downloadHandler.text); }
            catch (System.Exception e) { Debug.LogError($"[LandSystemBootstrap] CreatePlace parse failed: {e.Message}"); }
            if (place == null || string.IsNullOrEmpty(place.Id)) yield break;

            // 2. Someone else already owns it (lost the race / already sold) → abort, no debit.
            if (!string.IsNullOrEmpty(place.ownerId) && place.ownerId != charId) {
                SendToast(conn, "Ce terrain vient d'être vendu.", ToastKind.Error, worldToast: true);
                yield break;
            }
            // It's already rented (lost the race to a renter) → can't buy a leased parcel.
            if (!string.IsNullOrEmpty(place.tenantId)) {
                SendToast(conn, "Ce terrain vient d'être loué.", ToastKind.Error, worldToast: true);
                yield break;
            }

            // 3. We own it — debit now and record the purchase in the ledger.
            bank.PostLedger(-price, LedgerReason.LandPurchase, LedgerCounterparty.System,
                LedgerCounterparty.LandOffice, configId: parcel.Config.id);

            // 4. Apply runtime ownership (spawns the delivery box, loads any persisted props) and
            //    broadcast so every client's For-Sale sign hides.
            parcel.ServerSetOwnership(charId, ownerName, place.Id);
            BroadcastOwnership(parcel.ConfigId, charId, ownerName, "", "");

            string label = string.IsNullOrEmpty(parcel.Config.displayName) ? "Terrain" : parcel.Config.displayName;
            SendToast(conn, $"{label} acheté !", ToastKind.Success, worldToast: false);
        }

        // ── Sell ──────────────────────────────────────────────────────────────────
        private static void OnSell(NetworkConnectionToClient conn, C2S_SellLand msg) {
            if (conn?.identity == null) return;
            if (!LandParcelController.ById.TryGetValue(msg.parcelId, out var parcel) || parcel == null
                || parcel.Config == null) return;
            if (!parcel.IsOwned) return; // not owned → nothing to sell

            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            CharacterData character = pc?.CharacterData;
            if (bank == null || character == null) return;
            if (!parcel.CanBuild(character)) { // only the owner can sell
                Debug.LogWarning($"[LandSystemBootstrap] Sell denied: char {character.Id} is not the owner of parcel {msg.parcelId}");
                return;
            }

            ApiManager.Instance.StartCoroutine(SellLandCoroutine(conn, parcel, bank, character.Id));
        }

        private static IEnumerator SellLandCoroutine(NetworkConnectionToClient conn, LandParcelController parcel,
                PlayerBankAccount bank, string charId) {
            string placeId = parcel.PlaceId;
            int    refund  = Mathf.Max(0, Mathf.RoundToInt(parcel.Config.price * RefundRate));

            // No persisted place yet (parcel owned only at runtime) → just clear runtime + broadcast.
            if (!string.IsNullOrEmpty(placeId)) {
                // 1. Backend cleanup: props → transit, items destroyed, land place deleted (atomic RPC).
                UnityWebRequest req = ApiManager.Instance.SellLandRequest(placeId, charId);
                yield return req.SendWebRequest();

                if (req.responseCode < 200 || req.responseCode >= 300) {
                    Debug.LogError($"[LandSystemBootstrap] sell_land failed code={req.responseCode} body={req.downloadHandler?.text}");
                    SendToast(conn, "Vente impossible.", ToastKind.Error, worldToast: true);
                    yield break;
                }
            }

            // 2. Refund the seller (70% of the purchase price) through the ledger chokepoint.
            if (refund > 0)
                bank.PostLedger(refund, LedgerReason.LandSale, LedgerCounterparty.System,
                    LedgerCounterparty.LandOffice, configId: parcel.Config.id);

            // 3. Tear down runtime occupancy (despawn props + delivery box) and broadcast so every
            //    client's For-Sale sign returns to "À VENDRE".
            parcel.ServerVacate();
            BroadcastOwnership(parcel.ConfigId, "", "", "", "");

            string label = string.IsNullOrEmpty(parcel.Config.displayName) ? "Terrain" : parcel.Config.displayName;
            SendToast(conn, $"{label} vendu ({refund}) !", ToastKind.Success, worldToast: false);
        }

        // ── Rent ──────────────────────────────────────────────────────────────────
        private static void OnRent(NetworkConnectionToClient conn, C2S_RentLand msg) {
            if (conn?.identity == null) return;
            if (!LandParcelController.ById.TryGetValue(msg.parcelId, out var parcel) || parcel == null
                || parcel.Config == null) return;
            if (parcel.IsOwned || parcel.IsRented) return; // already occupied (runtime guard)

            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            string charId = pc?.CharacterData?.Id;
            if (bank == null || string.IsNullOrEmpty(charId)) return;
            if (bank.Money < parcel.Config.rentPrice) return; // server guard (client already toasted)

            string tenantName = pc.CharacterData.Identity.FullName;
            ApiManager.Instance.StartCoroutine(RentLandCoroutine(conn, parcel, charId, tenantName));
        }

        private static IEnumerator RentLandCoroutine(NetworkConnectionToClient conn, LandParcelController parcel,
                string charId, string tenantName) {
            // Seed the rent clock at "now" (in-game seconds) so the first charge lands one full
            // period later — same convention as apartments (no upfront charge).
            long now = (long)TimeManager.CurrentTime.TotalSeconds;

            // Claim tenancy in DB. findOrCreate only sets tenant_id + properties on INSERT, so the
            // FIRST renter wins and the response carries the authoritative tenant — this persists
            // the lease and resolves a same-frame double-rent before any further action.
            var placeBody = new CreatePlaceBody {
                placeKey = parcel.Config.PlaceKey,
                type     = "land",
                tenantId = charId,
                properties = new System.Collections.Generic.Dictionary<string, object> {
                    { "rent_price",              parcel.Config.rentPrice },
                    { "tenant_since",            now },
                    { "last_rent_collected_at",  now },
                },
            };
            UnityWebRequest req = ApiManager.Instance.CreatePlaceRequest(placeBody);
            yield return req.SendWebRequest();

            if (req.responseCode != 200 && req.responseCode != 201) {
                Debug.LogError($"[LandSystemBootstrap] CreatePlace (rent) failed code={req.responseCode} body={req.downloadHandler.text}");
                SendToast(conn, "Location impossible.", ToastKind.Error, worldToast: true);
                yield break;
            }

            PlaceJson place = null;
            try { place = JsonConvert.DeserializeObject<PlaceJson>(req.downloadHandler.text); }
            catch (System.Exception e) { Debug.LogError($"[LandSystemBootstrap] CreatePlace (rent) parse failed: {e.Message}"); }
            if (place == null || string.IsNullOrEmpty(place.Id)) yield break;

            // Lost the race / already owned or rented by someone else → abort, nothing charged.
            if (!string.IsNullOrEmpty(place.ownerId)) {
                SendToast(conn, "Ce terrain vient d'être vendu.", ToastKind.Error, worldToast: true);
                yield break;
            }
            if (!string.IsNullOrEmpty(place.tenantId) && place.tenantId != charId) {
                SendToast(conn, "Ce terrain vient d'être loué.", ToastKind.Error, worldToast: true);
                yield break;
            }

            // No upfront debit — the recurring rent tick (collect_land_rent) charges from now on.
            parcel.ServerSetTenancy(charId, tenantName, place.Id);
            BroadcastOwnership(parcel.ConfigId, "", "", charId, tenantName);

            string label = string.IsNullOrEmpty(parcel.Config.displayName) ? "Terrain" : parcel.Config.displayName;
            SendToast(conn, $"{label} loué !", ToastKind.Success, worldToast: false);
        }

        // ── Terminate lease ─────────────────────────────────────────────────────────
        private static void OnTerminateLease(NetworkConnectionToClient conn, C2S_TerminateLandLease msg) {
            if (conn?.identity == null) return;
            if (!LandParcelController.ById.TryGetValue(msg.parcelId, out var parcel) || parcel == null
                || parcel.Config == null) return;
            if (!parcel.IsRented) return; // not rented → nothing to terminate

            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            CharacterData character = pc?.CharacterData;
            if (character == null) return;
            if (!parcel.CanBuild(character)) { // only the tenant can terminate
                Debug.LogWarning($"[LandSystemBootstrap] Terminate denied: char {character.Id} is not the tenant of parcel {msg.parcelId}");
                return;
            }

            ApiManager.Instance.StartCoroutine(TerminateLeaseCoroutine(conn, parcel, character.Id));
        }

        private static IEnumerator TerminateLeaseCoroutine(NetworkConnectionToClient conn, LandParcelController parcel,
                string charId) {
            string placeId = parcel.PlaceId;

            // Backend cleanup reuses sell_land (props → transit, items destroyed, land place deleted).
            // No refund: terminating a lease returns the land with nothing back, like ending a tenancy.
            if (!string.IsNullOrEmpty(placeId)) {
                UnityWebRequest req = ApiManager.Instance.SellLandRequest(placeId, charId);
                yield return req.SendWebRequest();

                if (req.responseCode < 200 || req.responseCode >= 300) {
                    Debug.LogError($"[LandSystemBootstrap] terminate (sell_land) failed code={req.responseCode} body={req.downloadHandler?.text}");
                    SendToast(conn, "Résiliation impossible.", ToastKind.Error, worldToast: true);
                    yield break;
                }
            }

            parcel.ServerVacate();
            BroadcastOwnership(parcel.ConfigId, "", "", "", "");

            string label = string.IsNullOrEmpty(parcel.Config.displayName) ? "Terrain" : parcel.Config.displayName;
            SendToast(conn, $"Bail résilié — {label} rendu.", ToastKind.Success, worldToast: false);
        }

        // ── Build / Edit / Remove (land build mode) ───────────────────────────────
        private static void OnBuildLandProp(NetworkConnectionToClient conn, C2S_BuildLandProp msg) {
            if (!TryResolveBuildableParcel(conn, msg.ParcelId, out var parcel)) {
                if (conn != null && conn.isReady) conn.Send(new S2C_BuildAck { Success = false });
                return;
            }

            var build = new C2S_BuildProp {
                RoomId            = parcel.RoomId,
                DeliveryBoxPropId = msg.DeliveryBoxPropId,
                DeliveryId        = msg.DeliveryId,
                PropConfigId      = msg.PropConfigId,
                PresetId          = msg.PresetId,
                Position          = msg.Position,
                Rotation          = msg.Rotation,
                PaintConfigId     = msg.PaintConfigId,
                ColorR            = msg.ColorR,
                ColorG            = msg.ColorG,
                ColorB            = msg.ColorB,
            };
            PropInteractionDispatcher.Instance?.BuildProp(conn, build, parcel);
        }

        private static void OnEditLandProp(NetworkConnectionToClient conn, C2S_EditLandProp msg) {
            if (!TryResolveBuildableParcel(conn, msg.ParcelId, out var parcel)) return;
            if (!parcel.OwnsProp(msg.PropId)) {
                Debug.LogWarning($"[LandSystemBootstrap] EditProp denied: prop {msg.PropId} not on parcel {msg.ParcelId}");
                return;
            }

            ServerPropManager.Instance.UpdatePropTransform(parcel.RoomId, msg.PropId, msg.Position, msg.Rotation);
            PropInteractionDispatcher.Instance?.SyncPropTransform(msg.PropId, msg.Position, msg.Rotation);
        }

        private static void OnRemoveLandProp(NetworkConnectionToClient conn, C2S_RemoveLandProp msg) {
            if (!TryResolveBuildableParcel(conn, msg.ParcelId, out var parcel)) return;
            if (!parcel.OwnsProp(msg.PropId)) {
                Debug.LogWarning($"[LandSystemBootstrap] RemoveProp denied: prop {msg.PropId} not on parcel {msg.ParcelId}");
                return;
            }

            // DELETE in DB before wiping runtime state — the dispatcher reads the UUID bridge
            // from ServerPropManager, which RemoveProp clears.
            PropInteractionDispatcher.Instance?.SyncPropRemove(msg.PropId);
            ServerPropManager.Instance.RemoveProp(parcel.RoomId, msg.PropId);
            parcel.UntrackProp(msg.PropId);
        }

        /// <summary>Resolves the parcel for a build/edit/remove request and validates that the
        /// sender owns it AND is physically in the shared land room ("city").</summary>
        private static bool TryResolveBuildableParcel(NetworkConnectionToClient conn, int parcelId,
                out LandParcelController parcel) {
            parcel = null;
            if (conn?.identity == null) return false;
            if (!LandParcelController.ById.TryGetValue(parcelId, out parcel) || parcel == null) return false;

            PlayerController pc = conn.identity.GetComponent<PlayerController>();
            CharacterData character = pc?.CharacterData;
            if (character == null || !parcel.CanBuild(character)) {
                Debug.LogWarning($"[LandSystemBootstrap] Build denied: char {character?.Id} cannot build on parcel {parcelId}");
                return false;
            }
            if (PlayerRoomTracker.Instance.GetRoom(conn) != parcel.RoomId) {
                Debug.LogWarning($"[LandSystemBootstrap] Build denied: conn {conn.connectionId} not in room {parcel.RoomId}");
                return false;
            }
            return true;
        }

        // ── Occupancy replication ─────────────────────────────────────────────────
        /// <summary>Broadcasts a parcel's occupancy to every client. A parcel is owned XOR rented,
        /// so at most one of (owner, tenant) is non-empty; all four empty = back on sale / for rent.</summary>
        private static void BroadcastOwnership(int parcelId, string ownerId, string ownerName,
                string tenantId, string tenantName) {
            NetworkServer.SendToAll(new S2C_LandOwnership {
                parcelId   = parcelId,
                ownerId    = ownerId    ?? "",
                ownerName  = ownerName  ?? "",
                tenantId   = tenantId   ?? "",
                tenantName = tenantName ?? "",
            });
        }

        /// <summary>Sends the current occupancy of every claimed parcel (owned OR rented) to a single
        /// connection. Called from <see cref="SimpleTownNetwork.FinalizePlayerSpawn"/> once the
        /// player is spawned.</summary>
        public static void SendSnapshot(NetworkConnectionToClient conn) {
            if (conn == null) return;
            var claimed = LandParcelController.ById.Values.Where(p => p != null && (p.IsOwned || p.IsRented)).ToList();
            if (claimed.Count == 0) return;

            conn.Send(new S2C_LandSnapshot {
                parcelIds   = claimed.Select(p => p.ConfigId).ToArray(),
                ownerIds    = claimed.Select(p => (p.IsOwned ? p.OwnerCharId : "") ?? "").ToArray(),
                ownerNames  = claimed.Select(p => (p.IsOwned ? p.OwnerName   : "") ?? "").ToArray(),
                tenantIds   = claimed.Select(p => (p.IsRented ? p.TenantCharId : "") ?? "").ToArray(),
                tenantNames = claimed.Select(p => (p.IsRented ? p.TenantName   : "") ?? "").ToArray(),
            });
        }

        /// <summary>Tears down a rented parcel's runtime occupancy after the backend evicted its
        /// tenant (rent unpaid). Looked up by the land place id carried in the eviction payload, then
        /// broadcast so every client's For-Sale sign returns to available. Returns the parcel so the
        /// caller can toast the (online) tenant.</summary>
        public static LandParcelController ServerVacateByPlaceId(string placeId) {
            if (string.IsNullOrEmpty(placeId)) return null;
            LandParcelController parcel = LandParcelController.ById.Values
                .FirstOrDefault(p => p != null && p.PlaceId == placeId);
            if (parcel == null) return null;

            parcel.ServerVacate();
            BroadcastOwnership(parcel.ConfigId, "", "", "", "");
            return parcel;
        }

        // ── Client handlers ───────────────────────────────────────────────────────
        private static void OnClientOwnership(S2C_LandOwnership msg) {
            if (LandParcelController.ById.TryGetValue(msg.parcelId, out var parcel) && parcel != null)
                parcel.ClientApplyState(msg.ownerId, msg.ownerName, msg.tenantId, msg.tenantName);
        }

        private static void OnClientSnapshot(S2C_LandSnapshot msg) {
            if (msg.parcelIds == null) return;
            for (int i = 0; i < msg.parcelIds.Length; i++) {
                if (!LandParcelController.ById.TryGetValue(msg.parcelIds[i], out var parcel) || parcel == null) continue;
                string ownerId    = msg.ownerIds    != null && i < msg.ownerIds.Length    ? msg.ownerIds[i]    : "";
                string ownerName  = msg.ownerNames  != null && i < msg.ownerNames.Length  ? msg.ownerNames[i]  : "";
                string tenantId   = msg.tenantIds   != null && i < msg.tenantIds.Length   ? msg.tenantIds[i]   : "";
                string tenantName = msg.tenantNames != null && i < msg.tenantNames.Length ? msg.tenantNames[i] : "";
                parcel.ClientApplyState(ownerId, ownerName, tenantId, tenantName);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private static void SendToast(NetworkConnectionToClient conn, string text, ToastKind kind, bool worldToast) {
            if (conn == null || !conn.isReady) return;
            conn.Send(new ToastNotificationMessage {
                text = text, appId = PhoneAppIds.Bank, worldToast = worldToast, kindByte = (byte)kind,
            });
        }
    }
}
