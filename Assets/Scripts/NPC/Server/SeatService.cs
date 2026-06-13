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
    /// Hauteur (mètres) à laquelle on lance le linecast de ligne de vue, pour éviter
    /// de raser le sol (origin/siège sont au niveau des pieds).
    /// </summary>
    private const float LineOfSightHeight = 1.0f;

    private static int _obstructionMask = -1;
    private static int ObstructionMask {
        get {
            if (_obstructionMask == -1) _obstructionMask = LayerMask.GetMask("Wall", "Door");
            return _obstructionMask;
        }
    }

    /// <summary>
    /// Vrai si un mur (layer "Wall") ou une porte (layer "Door") se trouve entre
    /// <paramref name="from"/> et <paramref name="to"/>. Le linecast ignore les triggers
    /// et est lancé à hauteur de buste pour ne pas heurter le sol. Évite qu'un NPC
    /// cible/réserve un siège situé derrière un mur ou une porte (même room, distance
    /// à vol d'oiseau courte).
    /// </summary>
    private static bool IsBlockedByWall(Vector3 from, Vector3 to) {
        if (ObstructionMask == 0) return false; // layers absents du projet → pas de blocage
        Vector3 a = from + Vector3.up * LineOfSightHeight;
        Vector3 b = to   + Vector3.up * LineOfSightHeight;
        return Physics.Linecast(a, b, ObstructionMask, QueryTriggerInteraction.Ignore);
    }

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
        GameLogger.Network.Info("SeatReserved {PropId} {Slot} {Occupant} {IsNpc} {RoomId}",
            propId, idx, occupant, entity.IsNpc, roomId);
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
        if (changed) {
            ServerPropManager.Instance.UpdatePropState(roomId, propId, current.Serialize());
            GameLogger.Network.Info("SeatReleased {PropId} {Occupant} {IsNpc} {RoomId}",
                propId, entity.OccupantId, entity.IsNpc, roomId);
        }
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
    /// Cherche le siège libre LE PLUS PROCHE de <paramref name="origin"/> dans la room.
    /// Si <paramref name="maxDistance"/> &gt; 0, ignore les sièges au-delà.
    /// Renvoie false si aucun siège valide n'est trouvé.
    ///
    /// Le NPC s'approchera de la position retournée puis ré-évaluera la disponibilité
    /// via <see cref="TryReserveSeat"/> (le slot peut avoir été pris entre-temps).
    /// </summary>
    public static bool TryFindFreeSeatInRoom(string roomId, Vector3 origin, float maxDistance,
                                              out int propId, out Vector3 approachPosition) {
        propId = -1;
        approachPosition = Vector3.zero;

        float bestSqr = maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue;
        bool  found   = false;

        foreach (ServerPropState state in ServerPropManager.Instance.GetRoomStates(roomId)) {
            if (state.Type != PropType.Seat) continue;
            SeatState seat = SeatState.Deserialize(state.Payload);
            if (seat.SeatOccupants == null) continue;
            if (System.Array.IndexOf(seat.SeatOccupants, 0u) < 0) continue;

            if (!SeatBehaviour.TryGet(state.PropId, out var behaviour)) continue;
            if (behaviour.SeatTransforms == null || behaviour.SeatTransforms.Length == 0) continue;

            Vector3 pos = behaviour.SeatTransforms[0].position;
            float sqr = (pos - origin).sqrMagnitude;
            if (sqr > bestSqr) continue;

            // Ligne de vue : un mur entre le NPC et le siège l'exclut (pas
            // d'interaction « à travers les murs », cf. CanInteractWith côté joueur).
            if (IsBlockedByWall(origin, pos)) continue;

            bestSqr          = sqr;
            propId           = state.PropId;
            approachPosition = pos;
            found            = true;
        }
        return found;
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
