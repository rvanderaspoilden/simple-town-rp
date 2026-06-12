using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using Sim.Entities;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Wires the social "make acquaintance / add to contacts / reveal identity"
    /// flow into Mirror's lifecycle (pattern of RentSystemBootstrap). Server relays
    /// a request → target, persists on accept, reveals identities to both sides,
    /// plays the handshake VFX + greet animation, and enforces a refusal cooldown.
    /// Client maintains ClientRelationshipManager + popups + notifications.
    /// </summary>
    public static class AcquaintanceSystemBootstrap {
        private const float GreetDurationSeconds = 1.5f;
        private const double RefusalCooldownSeconds = 30.0;

        // Per requester→target refusal cooldown (server clock). Key "{from}:{to}".
        private static readonly Dictionary<string, double> _refusalUntil = new Dictionary<string, double>();

        // ── Server ──────────────────────────────────────────────────────────
        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_AcquaintanceRequest>(OnAcquaintanceRequest);
            NetworkServer.RegisterHandler<C2S_AcquaintanceResponse>(OnAcquaintanceResponse);
            NetworkServer.RegisterHandler<C2S_ContactRequest>(OnContactRequest);
            NetworkServer.RegisterHandler<C2S_ContactResponse>(OnContactResponse);
            NetworkServer.RegisterHandler<C2S_RemoveContact>(OnRemoveContact);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_AcquaintanceRequest>();
            NetworkServer.UnregisterHandler<C2S_AcquaintanceResponse>();
            NetworkServer.UnregisterHandler<C2S_ContactRequest>();
            NetworkServer.UnregisterHandler<C2S_ContactResponse>();
            NetworkServer.UnregisterHandler<C2S_RemoveContact>();
            _refusalUntil.Clear();
        }

        // Relay a request from `conn` to the player owning `targetNetId`. Returns
        // the target connection, or null when invalid / on cooldown.
        private static NetworkConnectionToClient RelayRequest(
            NetworkConnectionToClient conn, uint targetNetId, AcquaintanceRequestKind kind) {
            if (conn?.identity == null) return null;
            if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity)) return null;

            NetworkConnectionToClient targetConn = targetIdentity.connectionToClient;
            if (targetConn == null || targetConn == conn) return null; // invalid / self

            string key = conn.identity.netId + ":" + targetNetId;
            if (_refusalUntil.TryGetValue(key, out double until) && NetworkTime.time < until) {
                // Still cooling down — bounce back a refusal without bothering the target.
                if (kind == AcquaintanceRequestKind.Acquaintance) conn.Send(new S2C_AcquaintanceResult { accepted = false });
                else conn.Send(new S2C_ContactResult { accepted = false });
                return null;
            }

            return targetConn;
        }

        private static void OnAcquaintanceRequest(NetworkConnectionToClient conn, C2S_AcquaintanceRequest msg) {
            NetworkConnectionToClient target = RelayRequest(conn, msg.targetNetId, AcquaintanceRequestKind.Acquaintance);
            if (target == null) return;
            target.Send(new S2C_AcquaintanceRequest { fromNetId = conn.identity.netId });
            GameLogger.Network.Info("AcquaintanceRequest {From} {To}", conn.identity.netId, msg.targetNetId);
        }

        private static void OnContactRequest(NetworkConnectionToClient conn, C2S_ContactRequest msg) {
            NetworkConnectionToClient target = RelayRequest(conn, msg.targetNetId, AcquaintanceRequestKind.Contact);
            if (target == null) return;
            target.Send(new S2C_ContactRequest { fromNetId = conn.identity.netId });
            GameLogger.Network.Info("ContactRequest {From} {To}", conn.identity.netId, msg.targetNetId);
        }

        private static void OnAcquaintanceResponse(NetworkConnectionToClient conn, C2S_AcquaintanceResponse msg) {
            HandleResponse(conn, msg.fromNetId, msg.accepted, AcquaintanceRequestKind.Acquaintance);
        }

        private static void OnContactResponse(NetworkConnectionToClient conn, C2S_ContactResponse msg) {
            HandleResponse(conn, msg.fromNetId, msg.accepted, AcquaintanceRequestKind.Contact);
        }

        private static void HandleResponse(
            NetworkConnectionToClient conn, uint fromNetId, bool accepted, AcquaintanceRequestKind kind) {
            if (conn?.identity == null) return;
            if (!NetworkServer.spawned.TryGetValue(fromNetId, out NetworkIdentity aIdentity)) return;

            NetworkConnectionToClient aConn = aIdentity.connectionToClient;
            PlayerController a = aIdentity.GetComponent<PlayerController>();   // requester
            PlayerController b = conn.identity.GetComponent<PlayerController>(); // responder
            if (a == null || b == null) return;

            if (!accepted) {
                // Arm the refusal cooldown for A→B and notify A.
                _refusalUntil[a.netId + ":" + b.netId] = NetworkTime.time + RefusalCooldownSeconds;
                if (kind == AcquaintanceRequestKind.Acquaintance) aConn?.Send(new S2C_AcquaintanceResult { accepted = false });
                else aConn?.Send(new S2C_ContactResult { accepted = false });
                return;
            }

            string aId = a.CharacterData?.Id;
            string bId = b.CharacterData?.Id;
            if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId)) return;

            byte state = (byte)(kind == AcquaintanceRequestKind.Contact
                ? RelationshipState.Contact : RelationshipState.Acquaintance);
            string nowIso = DateTime.UtcNow.ToString("o");

            Action reveal = () => {
                NetworkServer.SendToAll(new S2C_PlayGreet { netId = a.netId });
                NetworkServer.SendToAll(new S2C_PlayGreet { netId = b.netId });
                BroadcastHandshake(a.netId, b.netId);

                aConn?.Send(new S2C_RelationshipUpdate {
                    otherCharacterId = bId, otherFullName = b.CharacterData?.Identity.FullName,
                    state = state, jobProfessionId = b.CharacterData?.CurrentProfessionId ?? "", metAt = nowIso,
                    online = true,
                });
                conn.Send(new S2C_RelationshipUpdate {
                    otherCharacterId = aId, otherFullName = a.CharacterData?.Identity.FullName,
                    state = state, jobProfessionId = a.CharacterData?.CurrentProfessionId ?? "", metAt = nowIso,
                    online = true,
                });
                GameLogger.Network.Info("RelationshipAccepted {A} {B} {State}", a.netId, b.netId, state);
            };

            // Persist, then reveal. Acquaintance inserts; Contact promotes.
            IEnumerator persist = kind == AcquaintanceRequestKind.Contact
                ? ApiManager.Instance.PromoteContactCoroutine(aId, bId, () => reveal())
                : ApiManager.Instance.CreateRelationshipCoroutine(aId, bId, () => reveal());
            ApiManager.Instance.StartCoroutine(persist);
        }

        private static void BroadcastHandshake(uint aNetId, uint bNetId) {
            NetworkServer.SendToAll(new S2C_WorldToast { anchorNetId = aNetId, title = "🤝", subtitle = "", delay = 0f });
            NetworkServer.SendToAll(new S2C_WorldToast { anchorNetId = bNetId, title = "🤝", subtitle = "", delay = 0f });
        }

        // Remove the relationship + conversation (full, mutual). Persist via REST,
        // then notify both the requester and the peer (if online) so their stores
        // and UIs drop the relationship live.
        private static void OnRemoveContact(NetworkConnectionToClient conn, C2S_RemoveContact msg) {
            if (conn?.identity == null) return;
            PlayerController requester = conn.identity.GetComponent<PlayerController>();
            if (requester?.CharacterData == null) return;

            string requesterId = requester.CharacterData.Id;
            string otherId = msg.characterId;
            if (string.IsNullOrEmpty(requesterId) || string.IsNullOrEmpty(otherId) || requesterId == otherId) return;

            ApiManager.Instance.StartCoroutine(
                ApiManager.Instance.RemoveContactCoroutine(requesterId, otherId, () => {
                    conn.Send(new S2C_RelationshipRemoved { otherCharacterId = otherId });
                    FindConnectionByCharacter(otherId)?.Send(new S2C_RelationshipRemoved { otherCharacterId = requesterId });
                    GameLogger.Network.Info("RelationshipRemoved {A} {B}", requesterId, otherId);
                }));
        }

        private static NetworkConnectionToClient FindConnectionByCharacter(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return null;
            foreach (NetworkConnectionToClient c in NetworkServer.connections.Values) {
                if (c?.identity == null) continue;
                PlayerController player = c.identity.GetComponent<PlayerController>();
                if (player?.CharacterData != null && player.CharacterData.Id == characterId) return c;
            }
            return null;
        }

        // ── Client ──────────────────────────────────────────────────────────
        public static void OnClientStart() {
            NetworkClient.RegisterHandler<S2C_AcquaintanceRequest>(OnAcquaintanceRequestReceived);
            NetworkClient.RegisterHandler<S2C_AcquaintanceResult>(OnAcquaintanceResult);
            NetworkClient.RegisterHandler<S2C_ContactRequest>(OnContactRequestReceived);
            NetworkClient.RegisterHandler<S2C_ContactResult>(OnContactResult);
            NetworkClient.RegisterHandler<S2C_RelationshipUpdate>(OnRelationshipUpdate);
            NetworkClient.RegisterHandler<S2C_RelationshipRemoved>(OnRelationshipRemoved);
            NetworkClient.RegisterHandler<S2C_PlayGreet>(OnPlayGreet);
            NetworkClient.RegisterHandler<S2C_ContactPresence>(OnContactPresence);
            ApiManager.OnRelationshipsRetrieved += OnRelationshipsHydrated;
        }

        public static void OnClientStop() {
            NetworkClient.UnregisterHandler<S2C_AcquaintanceRequest>();
            NetworkClient.UnregisterHandler<S2C_AcquaintanceResult>();
            NetworkClient.UnregisterHandler<S2C_ContactRequest>();
            NetworkClient.UnregisterHandler<S2C_ContactResult>();
            NetworkClient.UnregisterHandler<S2C_RelationshipUpdate>();
            NetworkClient.UnregisterHandler<S2C_RelationshipRemoved>();
            NetworkClient.UnregisterHandler<S2C_PlayGreet>();
            NetworkClient.UnregisterHandler<S2C_ContactPresence>();
            ApiManager.OnRelationshipsRetrieved -= OnRelationshipsHydrated;
            ClientRelationshipManager.Instance.Clear();
        }

        private static void OnContactPresence(S2C_ContactPresence msg) {
            // Only act when we actually know this character (filter out broadcasts
            // for non-relations to keep the local store tight).
            if (!ClientRelationshipManager.Instance.TryGet(msg.characterId, out _)) return;
            ClientRelationshipManager.Instance.SetOnline(msg.characterId, msg.online);
        }

        private static void OnRelationshipsHydrated(List<RelationshipData> relationships) {
            ClientRelationshipManager.Instance.Hydrate(relationships);
        }

        private static void OnAcquaintanceRequestReceived(S2C_AcquaintanceRequest msg) {
            AcquaintanceRequestUI.Instance?.ShowRequest(msg.fromNetId, AcquaintanceRequestKind.Acquaintance);
        }

        private static void OnContactRequestReceived(S2C_ContactRequest msg) {
            AcquaintanceRequestUI.Instance?.ShowRequest(msg.fromNetId, AcquaintanceRequestKind.Contact);
        }

        private static void OnAcquaintanceResult(S2C_AcquaintanceResult msg) {
            if (!msg.accepted) NotificationManager.Instance?.AddNotification("Votre demande a été refusée.", NotificationType.SUPPORT);
        }

        private static void OnContactResult(S2C_ContactResult msg) {
            if (!msg.accepted) NotificationManager.Instance?.AddNotification("Votre demande de contact a été refusée.", NotificationType.SUPPORT);
        }

        private static void OnRelationshipUpdate(S2C_RelationshipUpdate msg) {
            RelationshipState state = (RelationshipState)msg.state;
            ClientRelationshipManager.Instance.Set(msg.otherCharacterId, state, msg.otherFullName, msg.jobProfessionId, msg.metAt, msg.online);

            string text = state == RelationshipState.Contact
                ? $"{msg.otherFullName} et vous êtes désormais contacts."
                : $"Vous avez fait connaissance avec {msg.otherFullName}.";
            NotificationManager.Instance?.AddNotification(text, NotificationType.BANK);
        }

        private static void OnRelationshipRemoved(S2C_RelationshipRemoved msg) {
            ClientRelationshipManager.Instance.Remove(msg.otherCharacterId);
            NotificationManager.Instance?.AddNotification("Contact retiré.", NotificationType.BANK);
        }

        private static void OnPlayGreet(S2C_PlayGreet msg) {
            if (!NetworkClient.spawned.TryGetValue(msg.netId, out NetworkIdentity id)) return;

            // Only wave if the right hand is free (no item held there). PlayerHands
            // is replicated on every client, so this gate is consistent for all.
            PlayerHands hands = id.GetComponent<PlayerHands>();
            if (hands != null && hands.RightEntityId != -1) return;

            PlayerAnimator animator = id.GetComponent<PlayerAnimator>();
            if (animator == null) return;
            animator.SetAction(CharacterAnimatorAction.GREET);
            if (ApiManager.Instance != null) ApiManager.Instance.StartCoroutine(ResetGreet(animator));
        }

        private static IEnumerator ResetGreet(PlayerAnimator animator) {
            yield return new WaitForSeconds(GreetDurationSeconds);
            if (animator != null) animator.SetAction(CharacterAnimatorAction.NONE);
        }
    }
}
