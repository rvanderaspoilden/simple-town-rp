using System.Collections;
using System.Collections.Generic;
using Mirror;
using Sim.Enums;
using Sim.Logging;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Wires the 1-to-1 voice call flow into Mirror's lifecycle (pattern of
    /// SmsSystemBootstrap / AcquaintanceSystemBootstrap). Server relays
    /// invite → callee, tracks pending/active sessions, enforces a ringing
    /// timeout, and notifies on accept/decline/hangup/disconnect. Client drives
    /// the Contacts app's call sub-view and the Dissonance voice channel.
    /// In-game only — no persistence this lot.
    /// </summary>
    public static class CallSystemBootstrap {
        private const double RingingTimeoutSeconds = 30.0;

        private struct CallSession {
            public string PeerCharacterId;
            public bool Accepted;
            public double RingingDeadline;
            public bool IsCaller;
        }

        // One entry per participant, keyed by their own characterId.
        private static readonly Dictionary<string, CallSession> _calls = new Dictionary<string, CallSession>();
        private static Coroutine _timeoutLoop;

        // ── Server ──────────────────────────────────────────────────────────
        public static void OnServerStart() {
            NetworkServer.RegisterHandler<C2S_CallInvite>(OnCallInvite);
            NetworkServer.RegisterHandler<C2S_CallAccept>(OnCallAccept);
            NetworkServer.RegisterHandler<C2S_CallDecline>(OnCallDecline);
            NetworkServer.RegisterHandler<C2S_CallHangup>(OnCallHangup);
            _calls.Clear();
            if (ApiManager.Instance != null) _timeoutLoop = ApiManager.Instance.StartCoroutine(TimeoutLoop());
        }

        public static void OnServerStop() {
            NetworkServer.UnregisterHandler<C2S_CallInvite>();
            NetworkServer.UnregisterHandler<C2S_CallAccept>();
            NetworkServer.UnregisterHandler<C2S_CallDecline>();
            NetworkServer.UnregisterHandler<C2S_CallHangup>();
            if (_timeoutLoop != null && ApiManager.Instance != null) ApiManager.Instance.StopCoroutine(_timeoutLoop);
            _timeoutLoop = null;
            _calls.Clear();
        }

        private static void OnCallInvite(NetworkConnectionToClient conn, C2S_CallInvite msg) {
            if (conn?.identity == null) return;
            PlayerController caller = conn.identity.GetComponent<PlayerController>();
            if (caller?.CharacterData == null) return;

            string callerId = caller.CharacterData.Id;
            string targetId = msg.targetCharacterId;
            if (string.IsNullOrEmpty(callerId) || string.IsNullOrEmpty(targetId) || callerId == targetId) return;

            if (_calls.ContainsKey(callerId) || _calls.ContainsKey(targetId)) {
                conn.Send(new S2C_CallEnded { reason = (byte)CallEndReason.Busy });
                return;
            }

            NetworkConnectionToClient targetConn = FindConnectionByCharacter(targetId);
            if (targetConn?.identity == null) {
                conn.Send(new S2C_CallEnded { reason = (byte)CallEndReason.Unavailable });
                return;
            }
            PlayerController target = targetConn.identity.GetComponent<PlayerController>();
            if (target?.CharacterData == null) {
                conn.Send(new S2C_CallEnded { reason = (byte)CallEndReason.Unavailable });
                return;
            }

            double deadline = NetworkTime.time + RingingTimeoutSeconds;
            _calls[callerId] = new CallSession { PeerCharacterId = targetId, Accepted = false, RingingDeadline = deadline, IsCaller = true };
            _calls[targetId] = new CallSession { PeerCharacterId = callerId, Accepted = false, RingingDeadline = deadline, IsCaller = false };

            string callerName = caller.CharacterData.Identity.FullName;
            string targetName = target.CharacterData.Identity.FullName;

            targetConn.Send(new S2C_IncomingCall {
                callerCharacterId = callerId, callerName = callerName, callerNetId = caller.netId,
            });
            conn.Send(new S2C_CallRinging { calleeCharacterId = targetId, calleeName = targetName });
            GameLogger.Network.Info("CallInvite {From} {To}", callerId, targetId);
        }

        private static void OnCallAccept(NetworkConnectionToClient conn, C2S_CallAccept msg) {
            if (conn?.identity == null) return;
            PlayerController responder = conn.identity.GetComponent<PlayerController>();
            if (responder?.CharacterData == null) return;

            string responderId = responder.CharacterData.Id;
            string callerId = msg.callerCharacterId;
            if (!_calls.TryGetValue(responderId, out CallSession rs) || rs.PeerCharacterId != callerId) return;

            NetworkConnectionToClient callerConn = FindConnectionByCharacter(callerId);
            if (callerConn?.identity == null) {
                // Caller vanished between ring and accept — clean up and tell the responder.
                EndSessions(responderId, callerId);
                conn.Send(new S2C_CallEnded { reason = (byte)CallEndReason.PeerHangup });
                return;
            }
            PlayerController caller = callerConn.identity.GetComponent<PlayerController>();
            if (caller?.CharacterData == null) return;

            MarkAccepted(callerId);
            MarkAccepted(responderId);

            callerConn.Send(new S2C_CallAccepted {
                peerCharacterId = responderId, peerName = responder.CharacterData.Identity.FullName, peerNetId = responder.netId,
            });
            conn.Send(new S2C_CallAccepted {
                peerCharacterId = callerId, peerName = caller.CharacterData.Identity.FullName, peerNetId = caller.netId,
            });
            GameLogger.Network.Info("CallAccepted {Caller} {Responder}", callerId, responderId);
        }

        private static void OnCallDecline(NetworkConnectionToClient conn, C2S_CallDecline msg) {
            if (conn?.identity == null) return;
            PlayerController responder = conn.identity.GetComponent<PlayerController>();
            if (responder?.CharacterData == null) return;

            string responderId = responder.CharacterData.Id;
            string callerId = msg.callerCharacterId;
            if (!_calls.TryGetValue(responderId, out CallSession rs) || rs.PeerCharacterId != callerId) return;

            EndSessions(responderId, callerId);
            FindConnectionByCharacter(callerId)?.Send(new S2C_CallEnded { reason = (byte)CallEndReason.Declined });
            GameLogger.Network.Info("CallDeclined {Caller} {Responder}", callerId, responderId);
        }

        private static void OnCallHangup(NetworkConnectionToClient conn, C2S_CallHangup msg) {
            if (conn?.identity == null) return;
            PlayerController player = conn.identity.GetComponent<PlayerController>();
            if (player?.CharacterData == null) return;
            EndCallFor(player.CharacterData.Id);
        }

        /// <summary>Called from the server disconnect path so a dropped participant
        /// frees their peer cleanly.</summary>
        public static void OnPlayerGone(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return;
            EndCallFor(characterId);
        }

        // End the call the given character is part of, notifying the peer.
        private static void EndCallFor(string characterId) {
            if (!_calls.TryGetValue(characterId, out CallSession session)) return;
            string peerId = session.PeerCharacterId;
            CallEndReason reason = session.Accepted ? CallEndReason.PeerHangup : CallEndReason.Cancelled;
            EndSessions(characterId, peerId);
            FindConnectionByCharacter(peerId)?.Send(new S2C_CallEnded { reason = (byte)reason });
            GameLogger.Network.Info("CallHangup {Who} {Peer} {Reason}", characterId, peerId, reason);
        }

        private static void MarkAccepted(string characterId) {
            if (_calls.TryGetValue(characterId, out CallSession s)) {
                s.Accepted = true;
                _calls[characterId] = s;
            }
        }

        private static void EndSessions(string a, string b) {
            _calls.Remove(a);
            _calls.Remove(b);
        }

        private static IEnumerator TimeoutLoop() {
            WaitForSeconds wait = new WaitForSeconds(1f);
            while (NetworkServer.active) {
                yield return wait;
                if (_calls.Count == 0) continue;

                List<string> expiredCallers = null;
                foreach (KeyValuePair<string, CallSession> kv in _calls) {
                    CallSession s = kv.Value;
                    if (!s.Accepted && s.IsCaller && NetworkTime.time >= s.RingingDeadline) {
                        (expiredCallers ??= new List<string>()).Add(kv.Key);
                    }
                }
                if (expiredCallers == null) continue;

                foreach (string callerId in expiredCallers) {
                    if (!_calls.TryGetValue(callerId, out CallSession s)) continue;
                    string calleeId = s.PeerCharacterId;
                    EndSessions(callerId, calleeId);
                    FindConnectionByCharacter(callerId)?.Send(new S2C_CallEnded { reason = (byte)CallEndReason.Timeout });
                    FindConnectionByCharacter(calleeId)?.Send(new S2C_CallEnded { reason = (byte)CallEndReason.Timeout });
                    GameLogger.Network.Info("CallTimeout {Caller} {Callee}", callerId, calleeId);
                }
            }
        }

        private static NetworkConnectionToClient FindConnectionByCharacter(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return null;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                PlayerController player = conn.identity.GetComponent<PlayerController>();
                if (player?.CharacterData != null && player.CharacterData.Id == characterId) return conn;
            }
            return null;
        }

        // ── Client ──────────────────────────────────────────────────────────
        public static void OnClientStart() {
            NetworkClient.RegisterHandler<S2C_IncomingCall>(OnIncomingCall);
            NetworkClient.RegisterHandler<S2C_CallRinging>(OnCallRinging);
            NetworkClient.RegisterHandler<S2C_CallAccepted>(OnCallAccepted);
            NetworkClient.RegisterHandler<S2C_CallEnded>(OnCallEnded);
        }

        public static void OnClientStop() {
            NetworkClient.UnregisterHandler<S2C_IncomingCall>();
            NetworkClient.UnregisterHandler<S2C_CallRinging>();
            NetworkClient.UnregisterHandler<S2C_CallAccepted>();
            NetworkClient.UnregisterHandler<S2C_CallEnded>();
            CallVoiceSession.Close();
        }

        private static void OnIncomingCall(S2C_IncomingCall msg) {
            ContactsUI contacts = ContactsUI.Instance;
            if (contacts == null) return;
            if (PhoneControllerUI.Instance != null) PhoneControllerUI.Instance.ForceOpenApp(contacts);
            contacts.ShowIncomingCall(msg.callerCharacterId, msg.callerName, msg.callerNetId);
        }

        private static void OnCallRinging(S2C_CallRinging msg) {
            ContactsUI.Instance?.ShowOutgoingCall(msg.calleeCharacterId, msg.calleeName);
        }

        private static void OnCallAccepted(S2C_CallAccepted msg) {
            ContactsUI.Instance?.ShowActiveCall();
            CallVoiceSession.Open(msg.peerNetId);
        }

        private static void OnCallEnded(S2C_CallEnded msg) {
            CallVoiceSession.Close();
            ContactsUI.Instance?.EndCall((CallEndReason)msg.reason);
        }
    }
}
