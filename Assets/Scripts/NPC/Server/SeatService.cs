using Sim.Logging;
using UnityEngine;

/// <summary>
/// Service serveur unifié pour la réservation des sièges. Source de vérité unique
/// utilisée à la fois par :
///   - <see cref="PropInteractionRouter.HandleSeat"/> (chemin joueur, déclenché par
///     un C2S_PropInteraction depuis le client),
///   - <see cref="NpcSitState"/> (chemin NPC, appelé directement côté serveur).
///
/// Évite toute duplication de la logique d'occupation : la même méthode est utilisée
/// pour les joueurs et les NPC. Le SeatState reste typé uint[] — les NPC encodent
/// leur id via <see cref="CharacterEntityIds.EncodeNpc"/> (bit 31 mis).
/// </summary>
public static class SeatService {

    /// <summary>
    /// Tente d'attribuer un slot Seat libre à <paramref name="entity"/> sur le prop indiqué.
    /// Renvoie true et remplit <paramref name="seatTransform"/> en cas de succès.
    /// Idempotent : si l'entité occupe déjà un slot Seat, renvoie son transform actuel.
    /// </summary>
    public static bool TryReserveSeat(ICharacterEntity entity, string roomId, int propId,
                                       out Transform seatTransform) {
        seatTransform = null;
        if (entity == null) return false;

        if (!ServerPropManager.Instance.TryGetPropState(roomId, propId, out var state)) {
            GameLogger.Network.Warning("SeatReserveNotFound {PropId} {RoomId}", propId, roomId);
            return false;
        }
        if (state.Type != PropType.Seat) return false;
        if (!SeatBehaviour.TryGet(propId, out SeatBehaviour seat) || seat.SeatTransforms == null) {
            GameLogger.Network.Warning("SeatReserveNoBehaviour {PropId}", propId);
            return false;
        }

        SeatState current = SeatState.Deserialize(state.Payload);
        if (current.SeatOccupants == null || current.SeatOccupants.Length == 0) return false;

        uint occupant = entity.OccupantId;

        // Déjà assis → idempotent.
        for (int i = 0; i < current.SeatOccupants.Length; i++) {
            if (current.SeatOccupants[i] == occupant) {
                if (i < seat.SeatTransforms.Length) seatTransform = seat.SeatTransforms[i];
                return seatTransform != null;
            }
        }

        // Slot libre ?
        int idx = System.Array.IndexOf(current.SeatOccupants, 0u);
        if (idx < 0 || idx >= seat.SeatTransforms.Length) return false;

        current.SeatOccupants[idx] = occupant;
        ServerPropManager.Instance.UpdatePropState(roomId, propId, current.Serialize());
        seatTransform = seat.SeatTransforms[idx];
        return seatTransform != null;
    }

    /// <summary>Libère tous les slots détenus par l'entité sur ce prop (Seat + Couch).</summary>
    public static void ReleaseSeat(ICharacterEntity entity, string roomId, int propId) {
        if (entity == null) return;
        if (!ServerPropManager.Instance.TryGetPropState(roomId, propId, out var state)) return;
        if (state.Type != PropType.Seat) return;

        SeatState current = SeatState.Deserialize(state.Payload);
        bool changed = ClearOccupant(current.SeatOccupants,  entity.OccupantId)
                     | ClearOccupant(current.CouchOccupants, entity.OccupantId);
        if (changed)
            ServerPropManager.Instance.UpdatePropState(roomId, propId, current.Serialize());
    }

    /// <summary>Libère tous les slots détenus par l'entité dans toute la room.</summary>
    public static void ReleaseAllSeats(ICharacterEntity entity, string roomId) {
        if (entity == null) return;
        foreach (ServerPropState state in ServerPropManager.Instance.GetRoomStates(roomId)) {
            if (state.Type != PropType.Seat) continue;
            SeatState current = SeatState.Deserialize(state.Payload);
            bool changed = ClearOccupant(current.SeatOccupants,  entity.OccupantId)
                         | ClearOccupant(current.CouchOccupants, entity.OccupantId);
            if (changed)
                ServerPropManager.Instance.UpdatePropState(roomId, state.PropId, current.Serialize());
        }
    }

    /// <summary>
    /// Cherche le premier siège libre dans la room. Renvoie le propId et le transform
    /// d'approche (position du seat slot 0 — le NPC s'approchera de cette position avant
    /// de tenter une vraie réservation, qui ré-évaluera la disponibilité).
    /// </summary>
    public static bool TryFindFreeSeatInRoom(string roomId, out int propId, out Vector3 approachPosition) {
        propId = -1;
        approachPosition = Vector3.zero;

        foreach (ServerPropState state in ServerPropManager.Instance.GetRoomStates(roomId)) {
            if (state.Type != PropType.Seat) continue;
            SeatState seat = SeatState.Deserialize(state.Payload);
            if (seat.SeatOccupants == null) continue;
            if (System.Array.IndexOf(seat.SeatOccupants, 0u) < 0) continue;

            if (!SeatBehaviour.TryGet(state.PropId, out var behaviour)) continue;
            if (behaviour.SeatTransforms == null || behaviour.SeatTransforms.Length == 0) continue;

            propId = state.PropId;
            approachPosition = behaviour.SeatTransforms[0].position;
            return true;
        }
        return false;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static bool ClearOccupant(uint[] slots, uint id) {
        if (slots == null) return false;
        bool changed = false;
        for (int i = 0; i < slots.Length; i++) {
            if (slots[i] == id) { slots[i] = 0u; changed = true; }
        }
        return changed;
    }
}
