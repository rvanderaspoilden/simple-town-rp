using System;
using System.Collections.Generic;

namespace Sim.Entities.Persistence {

    // ── Land rent tick (POST /places/land-rent/tick) ────────────────────────
    //
    // Land counterpart of RentApi. Driven by the Unity server on the same timer
    // as home rent. The backend charges rent on every rented-and-due land parcel
    // (catch-up cumulative, even for offline tenants), credits the city treasury
    // (the Land Office has no player owner), and evicts any tenant who cannot pay
    // — returning the evictions so the server can react (vacate parcel + notify).

    [Serializable]
    public class LandRentTickBody {
        public long nowInGameSeconds;
    }

    [Serializable]
    public class LandRentEvictionDto {
        public string placeId;
        public string characterId;
        public string transitPlaceId;
    }

    // One successful land-rent charge. Rent always flows to the city treasury, so
    // only the tenant balance is carried back — the server refreshes that online
    // player's money SyncVar (collect_land_rent mutates characters.money directly).
    [Serializable]
    public class LandRentChargeDto {
        public string placeId;
        public string tenantId;
        public int tenantBalance;     // tenant money after debit (authoritative)
        public int amount;
    }

    [Serializable]
    public class LandRentTickResponse {
        public int paid;
        public List<LandRentEvictionDto> evictions = new List<LandRentEvictionDto>();
        public List<LandRentChargeDto> charges = new List<LandRentChargeDto>();
    }
}
