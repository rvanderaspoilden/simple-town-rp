using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Sim.Entities.Persistence {

    // ── Ledger (POST /characters/:id/ledger) ────────────────────────────────
    //
    // Single chokepoint for every money movement in the game. `amount` is signed
    // (+credit / -debit). System sources (shop, dispenser, job, bank) use
    // counterpartyType = "system" with a counterpartyId key; player-to-player
    // transfers use counterpartyType = "player" with the other character's id.

    /// <summary>Stable reason keys — must match the backend LedgerReason union.</summary>
    public static class LedgerReason {
        public const string ShopPurchase      = "shop_purchase";
        public const string DispenserPurchase = "dispenser_purchase";
        public const string JobReward         = "job_reward";
        public const string Salary            = "salary";
        public const string DeathPenalty      = "death_penalty";
        public const string P2pSale           = "p2p_sale";
        public const string P2pPurchase       = "p2p_purchase";
        public const string GiftSent          = "gift_sent";
        public const string GiftReceived      = "gift_received";
        public const string RentPaid          = "rent_paid";
        public const string RentReceived      = "rent_received";
        public const string RentEvicted       = "rent_evicted";
    }

    public static class LedgerCounterparty {
        public const string Player    = "player";
        public const string System    = "system";
        public const string Shop      = "SHOP";
        public const string Dispenser = "DISPENSER";
        public const string Job       = "JOB";
        public const string Bank      = "BANK";
    }

    [Serializable]
    public class PostLedgerBody {
        public int amount;               // signed: +credit / -debit
        public string reason;
        public string counterpartyType;  // "player" | "system"

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string counterpartyId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string propId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? configId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> metadata;
    }

    /// <summary>Response of POST /characters/:id/ledger — the new authoritative
    /// balance plus the created entry id.</summary>
    [Serializable]
    public class LedgerPostResponse {
        public int money;
        public string entryId;
    }
}
