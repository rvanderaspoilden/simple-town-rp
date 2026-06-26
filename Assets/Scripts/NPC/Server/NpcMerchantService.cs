using System.Collections.Generic;
using Mirror;
using Sim.Entities.Persistence;
using UnityEngine;

/// <summary>
/// Logique serveur des achats marchands. Plain C# singleton (comme NpcServerManager) :
/// Mirror n'est qu'un transport, les NPC n'ont pas de NetworkIdentity. Les handlers sont
/// enregistrés/désenregistrés par <see cref="NpcSystemBootstrap"/>.
///
/// Deux points d'entrée C2S :
///   • <see cref="HandleCatalogRequest"/> — à l'ouverture de la boutique, renvoie le catalogue.
///   • <see cref="HandleBuy"/> — achat d'un item ; revalide tout côté serveur (autorité), débite
///     via le ledger, remet l'item en main, puis renvoie un résultat.
///
/// Validation calquée sur le distributeur (PropInteractionRouter.HandleDispenser) : prechecks
/// fonds + mission-item + capacité en main AVANT le débit → grant synchrone, jamais d'échec
/// post-débit. Aucun toast émis ici : le client affiche le feedback (évite les doublons).
/// </summary>
public class NpcMerchantService {
    private static NpcMerchantService _instance;
    public static  NpcMerchantService Instance => _instance ??= new NpcMerchantService();

    // counterpartyId libre côté backend (@IsOptional) → pas de migration. Réutilise ShopPurchase.
    private const string MerchantCounterparty = "MERCHANT";

    // Codes de refus (cf. S2C_MerchantBuyResult.ReasonCode).
    private const byte ReasonOk          = 0;
    private const byte ReasonUnavailable = 1; // hors catalogue / mal configuré
    private const byte ReasonFunds       = 2;
    private const byte ReasonHandsFull   = 3; // mains pleines ou item de mission en main
    private const byte ReasonNoMerchant  = 4; // pas un marchand / absent / hors-room

    // ── Catalogue ───────────────────────────────────────────────────────────────

    public void HandleCatalogRequest(NetworkConnectionToClient conn, C2S_RequestMerchantCatalog msg) {
        if (conn?.identity == null) return;

        if (!TryResolveMerchant(conn, msg.NpcId, out NpcAIController npc)) {
            // NPC introuvable / pas marchand / hors-room : clic mort silencieux (l'action BUY
            // n'est proposée qu'en mode Merchant ; ce cas n'arrive qu'en course/despawn).
            return;
        }

        IReadOnlyList<ItemPrice> catalog = npc.Merchant.Catalog;
        var entries = new List<MerchantCatalogEntry>(catalog.Count);
        foreach (ItemPrice ip in catalog) {
            if (ip.item == null) continue;
            entries.Add(new MerchantCatalogEntry {
                ItemConfigId = ip.item.ID,
                Price        = ip.price,
                Label        = ip.item.Label
            });
        }

        conn.Send(new S2C_MerchantCatalog {
            NpcId         = msg.NpcId,
            MerchantLabel = npc.Merchant.MerchantLabel,
            Entries       = entries.ToArray()
        });
    }

    // ── Achat ─────────────────────────────────────────────────────────────────────

    public void HandleBuy(NetworkConnectionToClient conn, C2S_MerchantBuy msg) {
        try {
            if (conn?.identity == null) return;

            if (!TryResolveMerchant(conn, msg.NpcId, out NpcAIController npc)) {
                Reply(conn, msg, false, ReasonNoMerchant);
                return;
            }

            if (!npc.Merchant.TryGetPrice(msg.ItemConfigId, out int price, out ItemConfig cfg)
                || cfg.Prefab == null) {
                Reply(conn, msg, false, ReasonUnavailable);
                return;
            }

            PlayerBankAccount bank = conn.identity.GetComponent<PlayerBankAccount>();
            if (bank == null) {
                Reply(conn, msg, false, ReasonNoMerchant);
                return;
            }

            if (bank.Money < price) {
                Reply(conn, msg, false, ReasonFunds);
                return;
            }

            // Mission-item en main OU pas de place : on refuse AVANT tout débit.
            uint netId = conn.identity.netId;
            if (ServerItemManager.Instance.IsHoldingMissionItem(netId)
                || !ServerItemManager.Instance.CanFitInHand(netId, cfg)) {
                Reply(conn, msg, false, ReasonHandsFull);
                return;
            }

            // Débit (ledger) → remise immédiate en main (synchrone, atomique côté flux).
            bank.PostLedger(-price, LedgerReason.ShopPurchase, LedgerCounterparty.System,
                MerchantCounterparty, configId: cfg.ID);

            string roomId = PlayerRoomTracker.Instance.GetRoom(conn) ?? "city";
            ServerItemManager.Instance.SpawnItemInHand(roomId, cfg.ID, conn, cfg);

            Reply(conn, msg, true, ReasonOk);
        }
        catch (System.Exception ex) {
            Debug.LogError($"[NpcMerchantService] ERROR during buy npc={msg.NpcId} item={msg.ItemConfigId}: {ex}");
            try { Reply(conn, msg, false, ReasonUnavailable); } catch { /* conn may be closing */ }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Résout un NPC marchand valide pour ce joueur : existe (poolé/actif), est marchand, et le
    /// joueur est dans la même room. Gate de room obligatoire (rejet hors-room).
    /// </summary>
    private static bool TryResolveMerchant(NetworkConnectionToClient conn, int npcId,
                                           out NpcAIController npc) {
        if (!NpcAIController.TryGet(npcId, out npc) || npc == null) return false;
        if (!npc.IsMerchant) return false;
        return PlayerRoomTracker.Instance.GetRoom(conn) == npc.RoomId;
    }

    private static void Reply(NetworkConnectionToClient conn, C2S_MerchantBuy msg,
                              bool success, byte reason) {
        conn.Send(new S2C_MerchantBuyResult {
            NpcId        = msg.NpcId,
            ItemConfigId = msg.ItemConfigId,
            Success      = success,
            ReasonCode   = reason
        });
    }
}
