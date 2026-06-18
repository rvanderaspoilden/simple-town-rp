using Mirror;
using Sim.Entities.Persistence;
using Sim.Logging;

namespace Sim {
    /// <summary>
    /// Wires the player-to-player "give money" flow into Mirror's lifecycle
    /// (pattern of AcquaintanceSystemBootstrap). Validates the request, posts a
    /// ledger entry on each side via PlayerBankAccount.PostLedger
    /// (gift_sent / gift_received), then notifies both players with a BANK
    /// notification via the existing ToastNotificationMessage channel.
    /// </summary>
    public static class GiveMoneySystemBootstrap {

        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_GiveMoney>(OnGiveMoney);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_GiveMoney>();
        }

        private static void OnGiveMoney(NetworkConnectionToClient conn, C2S_GiveMoney msg) {
            if (conn?.identity == null) return;
            if (msg.amount <= 0) return;

            // Resolve sender.
            PlayerController sender = conn.identity.GetComponent<PlayerController>();
            PlayerBankAccount senderBank = conn.identity.GetComponent<PlayerBankAccount>();
            if (sender == null || senderBank == null || sender.CharacterData == null) return;

            // Resolve target (must be online and ≠ sender).
            if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity targetIdentity)) return;
            if (targetIdentity == conn.identity) return;

            PlayerController receiver = targetIdentity.GetComponent<PlayerController>();
            PlayerBankAccount receiverBank = targetIdentity.GetComponent<PlayerBankAccount>();
            NetworkConnectionToClient targetConn = targetIdentity.connectionToClient;
            if (receiver == null || receiverBank == null || receiver.CharacterData == null) return;

            // Funds check (authoritative server-side balance).
            if (senderBank.Money < msg.amount) {
                conn.Send(new ToastNotificationMessage {
                    text       = "Fonds insuffisants.",
                    appId      = PhoneAppIds.Bank,
                    worldToast = false,
                });
                return;
            }

            string senderCharId = sender.CharacterData.Id;
            string receiverCharId = receiver.CharacterData.Id;
            string senderName = sender.CharacterData.Identity.FullName;
            string receiverName = receiver.CharacterData.Identity.FullName;

            // Two-sided ledger post (mirrors PropInteractionDispatcher's P2P gift).
            senderBank.PostLedger(-msg.amount, LedgerReason.GiftSent,
                LedgerCounterparty.Player, receiverCharId);
            receiverBank.PostLedger(+msg.amount, LedgerReason.GiftReceived,
                LedgerCounterparty.Player, senderCharId);

            // Contextual BANK notifications (the generic "-X BC" toast on the
            // sender side is already fired automatically by PostLedger).
            conn.Send(new ToastNotificationMessage {
                text       = $"Argent envoyé à {receiverName} : -{msg.amount} BC",
                appId      = PhoneAppIds.Bank,
                worldToast = false,
            });
            targetConn?.Send(new ToastNotificationMessage {
                text       = $"Argent reçu de {senderName} : +{msg.amount} BC",
                appId      = PhoneAppIds.Bank,
                worldToast = false,
            });

            // Rencontres: offrir de l'argent crédite des points Sociable au donneur
            // si le nœud est débloqué. Récompense l'acte social.
            var senderPc = conn.identity.GetComponent<Sim.Player.PlayerConstellation>();
            if (senderPc != null) {
                int soc = Sim.Constellation.ConstellationPerks.GiftSociablePointsFor(senderPc.ServerHasUnlockedNode);
                if (soc > 0)
                    senderPc.GrantPoints(
                        new System.Collections.Generic.Dictionary<string, int> {
                            { Sim.Constellation.ConstellationPerks.SociableBranchId, soc }
                        }, "gift_money");
            }

            GameLogger.Network.Info("GiveMoney {From} {To} {Amount}",
                sender.netId, receiver.netId, msg.amount);
        }
    }
}
