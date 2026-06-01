using System.Collections;
using System.Collections.Generic;
using Mirror;
using Sim.Entities;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Wires the "make acquaintance / reveal identity" flow into Mirror's
    /// lifecycle (same pattern as RentSystemBootstrap / JobSystemBootstrap).
    /// Server relays request → target, persists on accept, and reveals the name
    /// to both sides. Client hydrates + maintains ClientRelationshipManager.
    /// </summary>
    public static class AcquaintanceSystemBootstrap {
        private const float GreetDurationSeconds = 1.5f;

        // ── Server ──────────────────────────────────────────────────────────
        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_AcquaintanceRequest>(OnRequest);
            NetworkServer.RegisterHandler<C2S_AcquaintanceResponse>(OnResponse);
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_AcquaintanceRequest>();
            NetworkServer.UnregisterHandler<C2S_AcquaintanceResponse>();
        }

        private static void OnRequest(NetworkConnectionToClient conn, C2S_AcquaintanceRequest msg) {
            if (conn?.identity == null) return;
            if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity targetIdentity)) return;

            NetworkConnectionToClient targetConn = targetIdentity.connectionToClient;
            if (targetConn == null || targetConn == conn) return; // invalid / self

            // Greeting wave from the initiator, visible to everyone.
            NetworkServer.SendToAll(new S2C_PlayGreet { netId = conn.identity.netId });
            // Forward the request to the target (B).
            targetConn.Send(new S2C_AcquaintanceRequest { fromNetId = conn.identity.netId });

            GameLogger.Network.Info("AcquaintanceRequest {From} {To}", conn.identity.netId, msg.targetNetId);
        }

        private static void OnResponse(NetworkConnectionToClient conn, C2S_AcquaintanceResponse msg) {
            if (conn?.identity == null) return;
            if (!NetworkServer.spawned.TryGetValue(msg.fromNetId, out NetworkIdentity aIdentity)) return;

            NetworkConnectionToClient aConn = aIdentity.connectionToClient;
            PlayerController a = aIdentity.GetComponent<PlayerController>();
            PlayerController b = conn.identity.GetComponent<PlayerController>();
            if (a == null || b == null) return;

            if (!msg.accepted) {
                aConn?.Send(new S2C_AcquaintanceResult { accepted = false });
                return;
            }

            string aId = a.CharacterData?.Id;
            string bId = b.CharacterData?.Id;
            string aName = a.CharacterData?.Identity.FullName;
            string bName = b.CharacterData?.Identity.FullName;
            if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId)) return;

            // Persist the mutual acquaintance, then reveal to both sides.
            ApiManager.Instance.StartCoroutine(ApiManager.Instance.CreateRelationshipCoroutine(aId, bId, () => {
                NetworkServer.SendToAll(new S2C_PlayGreet { netId = a.netId });
                NetworkServer.SendToAll(new S2C_PlayGreet { netId = b.netId });

                aConn?.Send(new S2C_RelationshipUpdate {
                    otherCharacterId = bId, otherFullName = bName, state = (byte)RelationshipState.Acquaintance,
                });
                conn.Send(new S2C_RelationshipUpdate {
                    otherCharacterId = aId, otherFullName = aName, state = (byte)RelationshipState.Acquaintance,
                });
                GameLogger.Network.Info("AcquaintanceAccepted {A} {B}", a.netId, b.netId);
            }));
        }

        // ── Client ──────────────────────────────────────────────────────────
        public static void OnClientStart() {
            NetworkClient.RegisterHandler<S2C_AcquaintanceRequest>(OnRequestReceived);
            NetworkClient.RegisterHandler<S2C_AcquaintanceResult>(OnResultReceived);
            NetworkClient.RegisterHandler<S2C_RelationshipUpdate>(OnRelationshipUpdate);
            NetworkClient.RegisterHandler<S2C_PlayGreet>(OnPlayGreet);
            ApiManager.OnRelationshipsRetrieved += OnRelationshipsHydrated;
        }

        public static void OnClientStop() {
            NetworkClient.UnregisterHandler<S2C_AcquaintanceRequest>();
            NetworkClient.UnregisterHandler<S2C_AcquaintanceResult>();
            NetworkClient.UnregisterHandler<S2C_RelationshipUpdate>();
            NetworkClient.UnregisterHandler<S2C_PlayGreet>();
            ApiManager.OnRelationshipsRetrieved -= OnRelationshipsHydrated;
            ClientRelationshipManager.Instance.Clear();
        }

        private static void OnRelationshipsHydrated(List<RelationshipData> relationships) {
            ClientRelationshipManager.Instance.Hydrate(relationships);
        }

        private static void OnRequestReceived(S2C_AcquaintanceRequest msg) {
            AcquaintanceRequestUI.Instance?.ShowRequest(msg.fromNetId);
        }

        private static void OnResultReceived(S2C_AcquaintanceResult msg) {
            if (!msg.accepted) {
                NotificationManager.Instance?.AddNotification("Votre demande a été refusée.", NotificationType.SUPPORT);
            }
        }

        private static void OnRelationshipUpdate(S2C_RelationshipUpdate msg) {
            ClientRelationshipManager.Instance.Set(msg.otherCharacterId, (RelationshipState)msg.state);
            NotificationManager.Instance?.AddNotification(
                $"Vous avez fait connaissance avec {msg.otherFullName}.", NotificationType.BANK);
        }

        private static void OnPlayGreet(S2C_PlayGreet msg) {
            if (!NetworkClient.spawned.TryGetValue(msg.netId, out NetworkIdentity id)) return;
            PlayerAnimator animator = id.GetComponent<PlayerAnimator>();
            if (animator == null) return;
            animator.SetAction(CharacterAnimatorAction.GREET);
            // Reset on a persistent runner (ApiManager is DontDestroyOnLoad).
            if (ApiManager.Instance != null) ApiManager.Instance.StartCoroutine(ResetGreet(animator));
        }

        private static IEnumerator ResetGreet(PlayerAnimator animator) {
            yield return new WaitForSeconds(GreetDurationSeconds);
            if (animator != null) animator.SetAction(CharacterAnimatorAction.NONE);
        }
    }
}
