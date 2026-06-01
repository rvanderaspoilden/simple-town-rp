using System;
using System.Collections.Generic;

namespace Sim.Entities.Persistence {

    // ── Rent tick (POST /homes/rent/tick) ───────────────────────────────────
    //
    // Driven by the Unity server on a timer. The backend charges rent on every
    // due home (catch-up cumulative, even for offline tenants), reverses the
    // amount to the owner (character money or city treasury), and evicts any
    // tenant who cannot pay — returning the evictions so the server can react
    // (teleport + notify online players).

    [Serializable]
    public class RentTickBody {
        public long nowInGameSeconds;
    }

    [Serializable]
    public class RentEvictionDto {
        public string homeId;
        public string characterId;
        public string transitPlaceId;
    }

    [Serializable]
    public class RentChargeDto {
        public string homeId;
        public string tenantId;
        public int tenantBalance;     // tenant money after debit (authoritative)
        public int amount;
        public string ownerId;        // player-owner credited, empty/null when paid to city
        public int ownerBalance;      // owner money after credit (when ownerId set)
    }

    [Serializable]
    public class RentTickResponse {
        public int paid;
        public List<RentEvictionDto> evictions = new List<RentEvictionDto>();
        public List<RentChargeDto> charges = new List<RentChargeDto>();
    }
}
