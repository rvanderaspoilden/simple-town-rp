using Mirror;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Flux serveur du distributeur (ATM) — pattern <see cref="GiveMoneySystemBootstrap"/>.
    /// Retrait : valide proximité ATM + solde + place en poche, crée une liasse
    /// <see cref="CashItem"/> du montant dans la POCHE (règle projet : un item donné au
    /// joueur passe par l'inventaire, jamais par les mains) puis débite le compte (ledger
    /// atm_withdraw, contrepartie BANK) une fois la liasse créée côté backend. Dépôt : montant saisi à l'écran — le serveur valide la proximité puis
    /// « fait la monnaie » dans les liasses du joueur (main + poche) via TakeCashAmount
    /// (liasses supprimées, au plus une décrémentée) et crédite ce qui a réellement été pris
    /// (ledger atm_deposit — posté dans le callback : les suppressions de liasses en poche
    /// sont des DELETE backend asynchrones). Feedback d'échec en world toast
    /// (résultat immédiat d'action) ; le crédit du dépôt a sa notification Banque explicite —
    /// le débit du retrait a déjà la sienne, émise automatiquement par PostLedger.
    /// </summary>
    public static class AtmSystemBootstrap {
        /// <summary>Portée serveur (m, planaire) : portée client (3 m) + tolérance réseau.</summary>
        private const float ServerRange = 5f;

        /// <summary>Garde-fou anti-valeur aberrante, retrait comme dépôt (le solde et le
        /// cash porté restent les vraies limites).</summary>
        private const int MaxAmount = 1_000_000;

        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_AtmWithdraw>(OnWithdraw);
            NetworkServer.RegisterHandler<C2S_AtmDeposit>(OnDeposit);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_AtmWithdraw>();
            NetworkServer.UnregisterHandler<C2S_AtmDeposit>();
        }

        private static void OnWithdraw(NetworkConnectionToClient conn, C2S_AtmWithdraw msg) {
            if (!TryResolve(conn, out PlayerController player, out PlayerBankAccount bank)) return;
            if (msg.amount <= 0 || msg.amount > MaxAmount) return;
            if (!AtmBehaviour.AnyInRange(player.transform.position, ServerRange)) {
                SendErrorToast(conn, "Trop loin du distributeur");
                return;
            }

            if (bank.Money < msg.amount) {
                SendErrorToast(conn, "Fonds insuffisants");
                return;
            }

            // La liasse va dans la POCHE, jamais dans les mains (règle projet : tout item
            // donné au joueur passe par l'inventaire). Création backend asynchrone → le
            // débit ne part qu'une fois la liasse réellement créée (pattern dépôt inverse).
            ServerItemManager.Instance.SpawnCashInPocket(conn, msg.amount,
                onSuccess: () => {
                    if (bank == null || conn.identity == null) {
                        Debug.LogWarning($"[AtmSystemBootstrap] Retrait de {msg.amount} orphelin (joueur parti pendant la création de la liasse) — liasse persistée SANS débit.");
                        return;
                    }
                    // La notification « -X € » du débit part automatiquement de PostLedger.
                    bank.PostLedger(-msg.amount, LedgerReason.AtmWithdraw,
                        LedgerCounterparty.System, LedgerCounterparty.Bank);
                    GameLogger.Network.Info("AtmWithdraw netId={NetId} amount={Amount}",
                        conn.identity.netId, msg.amount);
                },
                onFail: error => SendErrorToast(conn, error));
        }

        private static void OnDeposit(NetworkConnectionToClient conn, C2S_AtmDeposit msg) {
            if (!TryResolve(conn, out PlayerController player, out PlayerBankAccount bank)) return;
            if (msg.amount <= 0 || msg.amount > MaxAmount) return;
            if (!AtmBehaviour.AnyInRange(player.transform.position, ServerRange)) {
                SendErrorToast(conn, "Trop loin du distributeur");
                return;
            }

            ServerItemManager.Instance.TakeCashAmount(conn, msg.amount, (covered, taken) => {
                if (!covered && taken == 0) {
                    SendErrorToast(conn, "Pas assez d'espèces sur vous");
                    return;
                }
                // Déconnecté pendant les DELETE poche : plus de compte à créditer.
                if (bank == null || conn.identity == null) {
                    Debug.LogWarning($"[AtmSystemBootstrap] Dépôt de {taken} orphelin (joueur parti pendant le retrait des liasses).");
                    return;
                }

                // taken < demandé possible sur erreur backend partielle : on crédite ce qui
                // a réellement été pris — la notification porte le montant effectif, et le
                // joueur est prévenu que le dépôt n'est pas complet.
                if (!covered) SendErrorToast(conn, "Dépôt partiel — réessayez pour le reste");
                bank.PostLedger(+taken, LedgerReason.AtmDeposit,
                    LedgerCounterparty.System, LedgerCounterparty.Bank);

                // PostLedger ne notifie que les débits → notification Banque explicite du crédit.
                conn.Send(new ToastNotificationMessage {
                    text       = $"Dépôt : {MoneyFormat.FormatSigned(taken)}",
                    appId      = PhoneAppIds.Bank,
                    worldToast = false,
                });

                GameLogger.Network.Info("AtmDeposit netId={NetId} requested={Requested} taken={Taken}",
                    conn.identity.netId, msg.amount, taken);
            });
        }

        private static bool TryResolve(NetworkConnectionToClient conn,
            out PlayerController player, out PlayerBankAccount bank) {
            player = null;
            bank = null;
            if (conn?.identity == null) return false;
            player = conn.identity.GetComponent<PlayerController>();
            bank = conn.identity.GetComponent<PlayerBankAccount>();
            return player != null && bank != null && player.CharacterData != null;
        }

        private static void SendErrorToast(NetworkConnectionToClient conn, string text) {
            conn.Send(new ToastNotificationMessage {
                text       = text,
                worldToast = true,
                kindByte   = (byte)ToastKind.Error,
            });
        }
    }
}
