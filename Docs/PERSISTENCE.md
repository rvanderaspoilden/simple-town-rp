# PERSISTENCE.md — Relational Migration & Target Architecture

> Last updated: 2026-05-12 — Phase 2 (read switch) shipped.

This doc is the single source of truth for **how gameplay state is persisted**.
It covers the legacy JSONB blob, the target relational schema, the migration
roadmap, and the runtime bridge between Mirror's int prop IDs and DB UUIDs.

Cross-refs: `PROP_SYSTEM.md` (runtime prop registry), `ARCHITECTURE.md`
(system map), `NETWORK_FLOW.md` (Mirror messages).

---

## 1. Legacy state (what we're leaving behind)

Each apartment was persisted as a **single JSONB blob** `homes.scene_data`:

```
homes
├── id           UUID
├── address      JSONB
├── owner        TEXT
├── tenant       TEXT
├── preset       TEXT
└── scene_data   JSONB  ← every prop position, cover paint, door lock, light pos
                          serialized as one big "SceneData" object
```

Pain points:
- A single PUT `/homes/:id` rewrote the whole blob on **every** prop move.
- No identity per prop — props were addressable only by `(roomId, intPropId)`,
  and intPropId was regenerated on every server restart (`_nextAutoId` 10000+).
- Storage / nesting / cross-place transfer impossible without parsing the blob.
- No optimistic locking — last-writer-wins.

---

## 2. Target schema (relational)

Three foundation tables, one stable bridge to `homes`:

```
                              ┌────────────────┐
                              │  homes (kept)  │
                              │  id, address   │
                              │  tenant, …     │
                              └───────┬────────┘
                                      │ 1:1 (places.id = homes.id)
                                      ▼
   ┌──────────────────┐   1:N  ┌────────────────┐  N:1  ┌──────────────────┐
   │  covers          │◄───────│  places         │──────▶│ preset_id (FK,   │
   │  place_id (FK)   │        │  id  UUID PK    │       │ optional)        │
   │  surface_kind    │        │  place_key  TEXT│       └──────────────────┘
   │  surface_index   │        │  type  TEXT     │
   │  paint_config_id │        │  owner_id       │
   │  color JSONB     │        │  tenant_id      │
   │  PK(place,kind,  │        │  properties JSON│
   │     index)       │        │  created_at     │
   └──────────────────┘        │  updated_at     │
                               └───────┬────────┘
                                       │ 1:N
                                       ▼
                              ┌────────────────────┐
                              │  props             │
                              │  id  UUID PK       │
                              │  place_id (FK)     │
                              │  container_prop_id │──┐ self-FK (nesting)
                              │  config_id  INT    │  │
                              │  position  JSONB?  │◄─┘
                              │  rotation  JSONB?  │
                              │  is_built  BOOL    │
                              │  preset_index INT  │
                              │  state_data JSONB  │  ← kind-discriminated
                              │  version  INT      │  ← optimistic locking
                              │  owned_by  TEXT?   │
                              │  created_at        │
                              │  updated_at        │
                              └────────────────────┘

CHECK constraint: container_prop_id IS NULL OR position IS NULL
                  (a prop is either placed in the world OR nested inside another)
```

### Place types

| type | example | usage |
|---|---|---|
| `apartment` | `apt:SALMON HOTEL:3` | a player's home (id = homes.id) |
| `hall` | `hall:SALMON HOTEL:2` | the corridor on floor 2 |
| `city` | `city` | the outdoor city |
| `shop` | `shop:tools` | a vendor inventory |
| `transit` | `transit:global` | props bought but not yet placed (delivery) |
| `warehouse` | future | shared storage |
| `inventory` | future | per-player carry slot |

`place_key` is the human-readable unique key. `id` is the UUID. Both
must be unique. New apartments pin `places.id = homes.id` for a stable
cross-table lookup.

### `props.state_data` JSONB shape

Discriminated by `kind`:

```jsonc
// Door
{ "kind": "door", "isOpen": false, "lockState": 1, "doorNumber": 3 }

// PaintBucket
{ "kind": "bucket", "paintConfigId": 5, "color": [0.8, 0.2, 0.2] }

// Light (and generic) — header only, no extra state
{ "kind": "light" }
```

`StateDataMapper.BuildPayload(stateData, header)` (Unity-side) translates a row
back into the byte[] payload that `ServerPropManager` consumes.

### `props.version` — optimistic locking

Every PATCH `/props/:id` sends `expectedVersion`. The backend `UPDATE … WHERE
id = $1 AND version = $2 RETURNING …` fails with 409 if another writer raced.
The Unity server is the single writer for gameplay, so 409s should be rare —
they indicate a bug, not a race we have to handle gracefully.

After a successful PATCH, the backend returns the new version. The dispatcher
updates `PropDbBridge.Version` so the next PATCH stays in sync.

---

## 3. Runtime bridge: int propId ↔ UUID

Mirror's prop messages carry `int PropId` (auto-incrementing from 10000).
The DB stores UUIDs. The bridge lives in `ServerPropManager`:

```csharp
class PropDbBridge {
    string Uuid;
    int    Version;
}
Dictionary<int, PropDbBridge> _runtimeToDb;
```

Helpers:
- `AssociateUuid(propId, uuid, version=1)` — set up the bridge.
- `GetUuid(propId)`, `GetBridge(propId)` — lookups.
- `UpdateVersion(propId, version)` — refresh after PATCH.
- `FindPropIdByUuid(uuid)` — reverse lookup (used after buy→build).
- `ClearBridge(propId)` — on remove.

A bridge is created in two places:
1. **At apartment load** — `ApartmentController.InstantiateLevelFromPlaceState`
   spawns each prop with its `propUuid` + `propVersion` from the DB row.
2. **At build** — `PropInteractionDispatcher.BuildPropCoroutine` reads the
   UUID from the deleted delivery row and bridges it on the freshly-spawned prop.

Props without a bridge (legacy scene-spawned props, fixtures like inner doors,
delivery box) silently skip the PATCH side. The dual-write legacy path
(`apt.Save()`) covers them until Phase 3.

---

## 4. Phased rollout

| Phase | What | Status |
|---|---|---|
| **0** | Schema + indexes + backfill from `homes.scene_data` | ✅ shipped |
| **1** | New CRUD endpoints + dual-write (legacy `scene_data` AND new tables) | ✅ shipped |
| **2** | Read switch — Unity loads from `/places/:id/state`, legacy fallback if empty | ✅ shipped 2026-05-12 |
| **3** | Drop legacy writes (`apt.Save()`), drop `homes.scene_data` column, drop fallback | ⏳ pending validation |

### Phase 0 — migrations

- `migrations/03_places_props_covers.sql` — tables, indexes, `updated_at` trigger.
- `migrations/04_backfill_from_homes.sql` — idempotent reader that derives
  rows from each `homes.scene_data` (buckets, props, lights, walls, grounds,
  saved door states). Inner doors are **not** backfilled — they're preset
  fixtures, see §6.
- `migrations/05_deliveries_prop_id.sql` — `ALTER TABLE deliveries ADD COLUMN
  prop_id UUID REFERENCES props(id)` (the buy→build identity link).

### Phase 1 — endpoints + DTOs

Backend (NestJS, RxJS Observables, class-validator):

| Route | Body | Purpose |
|---|---|---|
| `POST /places` | `CreatePlaceDto` (idempotent via `placeKey`) | Ensure place exists |
| `GET /places/:id/state` | — | Return place + props + covers in one round-trip |
| `POST /props` | `CreatePropDto` | Create a prop (used at buy time) |
| `PATCH /props/:id` | `UpdatePropDto` (`expectedVersion` required) | Move, modify, or change isBuilt |
| `DELETE /props/:id` | — | Drop a prop |
| `PUT /covers/:placeId` | `UpsertCoversDto` | Bulk upsert covers for a place |
| `DELETE /deliveries/:id` | — (returns deleted row) | Consume a delivery (echoes `prop_id` for bridge) |

Dual-write: `PropInteractionRouter` (`HandleEditProp`, `HandleRemoveProp`,
`HandleDoor`, etc.) call **both** the dispatcher sync helpers (new path) and
the legacy `apt.Save()` (old path). This protects the rollout — Phase 2
can read from either side.

### Phase 2 — read switch

`ApartmentController.RetrieveData` calls `LoadPlaceStateOrFallback(homeResponse)`:

```
GET /places/:homeId/state
  ├── 200 + PlaceStateIsHydrated(state)    → InstantiateLevelFromPlaceState(state)
  └── otherwise                              → InstantiateLevel(homeResponse.SceneData)  [legacy]
```

`PlaceStateIsHydrated` returns true iff `state.covers.length > 0 || state.props.length > 0`.
This filters out freshly-assigned apartments whose preset hasn't been
materialized into the new tables yet (see §6 "Pending").

### Phase 3 — drop legacy (pending)

1. Remove every `apt.Save()` call in `PropInteractionRouter` / `PropInteractionDispatcher`.
2. Remove `LoadPlaceStateOrFallback` legacy branch — `InstantiateLevelFromPlaceState` is the sole path.
3. Backend: `ALTER TABLE homes DROP COLUMN scene_data;` + remove `HomeService.updateSceneData`.
4. Remove `SaveUtils.SpawnPropFromSave`, `SceneData`, `DoorData`, `DefaultData` types from Unity.

---

## 5. Critical flows

### 5.1 Buy a prop (in shop)

```
Client                       Unity Server                        Backend
──────                       ────────────                        ───────
[click "buy"]
     │                       OnStartServer:
     │                         EnsureTransitPlace ────► POST /places { type:"transit" }
     │                                          ◄──── TransitPlaceId cached
     │
     │  C2S_BuyProp (UnityServer-relayed) ─────►
     │                       SimpleTownNetwork.CreateDeliveryCoroutine:
     │                         POST /props
     │                           { place_id: TransitPlaceId,    ─► props row created
     │                             config_id, preset_index, … }    (UUID assigned)
     │                                                            ◄── { id: propUuid }
     │
     │                         POST /deliveries
     │                           { recipientId, propId: propUuid }
     │                                                            ─► delivery row
     │                                                              with prop_id FK
     │  S2C_ShopResponse ◄──────
```

The prop UUID is created **at buy time**, not build time. This is the
identity that survives shop → mailbox → apartment.

### 5.2 Validate a package (unpackage → build)

```
Client                       Unity Server                              Backend
──────                       ────────────                              ───────
[validate placement]
     │
     │  C2S_BuildProp ──────►
     │                       PropInteractionRouter.HandleBuildProp
     │                         → PropInteractionDispatcher.BuildPropCoroutine:
     │
     │                         DELETE /deliveries/:id ─────────────► row deleted
     │                                                              ◄─ { _id, propId, … }  (echo)
     │
     │                         Extract propUuid from response
     │                         ServerPropManager.SpawnProp(...) → newPropId (int)
     │                         AssociateUuid(newPropId, propUuid, v=1)
     │
     │                         PATCH /props/:propUuid ────────────► UPDATE props
     │                           { expectedVersion: 1,                  SET place_id=apt,
     │                             place_id: aptId,                     position=msg.Position,
     │                             position, rotation,                  rotation=…,
     │                             isBuilt }                            is_built=…,
     │                                                                  version = version+1
     │                                                              ◄─ { …, version: 2 }
     │                         UpdateVersion(newPropId, 2)
     │
     │  S2C_BuildAck ◄────────
     │  S2C_PropSpawn ◄───── (broadcast to room)
```

### 5.3 Save: move / paint / lock / build action

Every interaction now calls **two** persistence paths in parallel:

| Action | Runtime update | New path (dispatcher) | Legacy path |
|---|---|---|---|
| Edit (move) | `UpdatePropTransform` + `S2C_PropTransform` | `SyncPropTransform(propId, pos, rot)` | `apt.Save()` |
| Paint / Door lock / State change | `UpdatePropState` + `S2C_PropUpdate` | `SyncPropState(propId, stateData)` | `apt.Save()` |
| Build (hammer interaction) | `UpdatePropState` (`IsBuilt=true`) | `SyncPropBuilt(propId, true)` | `apt.Save()` |
| Remove | `RemoveProp` + `S2C_PropRemove` | `SyncPropRemove(propId)` | `apt.Save()` |
| Paint cover (wall/ground) | `coverSettingsByFaces/Ground` + `S2C_CoverApply` | `SyncCovers(placeId, entries)` | `apt.Save()` |

Each sync helper looks up `GetBridge(propId)`. **No bridge → no-op** —
the prop is treated as a legacy/fixture prop and only the legacy path persists it.

### 5.4 Load: hydrate the apartment

```
RetrieveData (Server):
  GET /place/:homeId/state
    │
    ├── If state hydrated:
    │     InstantiateLevelFromPlaceState(state):
    │       1. Hydrate cover dictionaries from state.covers
    │       2. For each prop in state.props with position && place_id match:
    │            ├── If front door (kind=door, doorNumber matches apt):
    │            │     RestoreFrontDoorFromPlaceEntry → apply state to Init()'s door
    │            └── Else:
    │                  SpawnProp(... propUuid, propVersion) → auto-bridge
    │       3. SpawnInnerDoorsFromPreset()          ← fixtures, never persisted
    │       4. SpawnLightsFromPreset()              ← only if state.props had no lights
    │       5. SpawnDeliveryBoxFromPreset()         ← fixture
    │
    └── Else (place empty or HTTP error):
          InstantiateLevel(home.scene_data)         ← legacy fallback
```

Invariants applied at load:
- `LockState == LOCKED` ⇒ `IsOpen = false` (sanitize stale saves).
- `position == null` ⇒ prop is in a container or unset; not spawned in this apt.

---

## 6. Fixtures vs persisted props

Some props are **never** persisted — they're respawned from preset config
on every load. The legacy `sceneData` knew this implicitly; the new model
keeps the same rule:

| Prop | Source | Why |
|---|---|---|
| Front door | `Init()` from preset | Anchored to `frontDoorSpawn`; state restored on top |
| Inner doors | `currentConfiguration.doorSpawners` | Never user-movable in current design |
| Delivery box | `currentConfiguration.deliveryBoxSpawn` | Fixture; tenant-bound |
| Lights | `currentConfiguration.lightSpawns` | Only if state.props has no light entries |

**Pending decision**: lights and inner doors *could* be promoted to first-class
DB rows if we want per-instance customisation (different bulb types, replaceable
doors). For now they stay as fixtures.

---

## 7. What's left

### Phase 2 follow-ups (before Phase 3)

1. **Preset materialization at `assignApartment`** (backend).
   Today, a freshly-assigned apt has an empty `places` row → `PlaceStateIsHydrated`
   returns false → fallback to legacy. To remove the fallback, the backend must
   walk `preset.scene_data` and INSERT the corresponding props/covers rows at
   `HomeService.assignApartment` time.

2. **Inner doors promoted to rows** (optional).
   If we want them user-customisable, materialize them as `props` rows with
   `kind="door"` and a stable `preset_index`. Otherwise leave as fixtures.

3. **Lights persistence sanity check**.
   Verify that backfill 04 captured `sceneData.lights[]` correctly for
   apartments where the tenant had moved them.

### Phase 3 — drop legacy

See §4 above.

### Long term

- Extend the model to halls, city, shop places (same schema, different
  `places.type`).
- Replace `deliveries` table: a prop with `place_id = TransitPlaceId` and
  `owned_by = recipientCharacterId` *is* a delivery in flight. The `deliveries`
  table becomes redundant.
- Per-player inventory: `places` with `type='inventory'` and a `properties.slot`
  field. Props move there on pickup, back to apt on drop.
- Container nesting: already supported via `props.container_prop_id`. A chest
  prop becomes a container; opening it reveals its child props.

---

## 8. File map

### Backend (`simple-town-ws/`)

| File | Role |
|---|---|
| `migrations/03_places_props_covers.sql` | Foundation schema |
| `migrations/04_backfill_from_homes.sql` | One-shot backfill from `homes.scene_data` |
| `migrations/05_deliveries_prop_id.sql` | Link deliveries → props |
| `src/shared/services/place.service.ts` | Place CRUD, idempotent findOrCreate |
| `src/shared/services/prop.service.ts` | Prop CRUD with optimistic locking |
| `src/shared/services/cover.service.ts` | Cover bulk upsert |
| `src/place/dto/*.ts` | DTOs with class-validator decorators |
| `src/shared/services/home.service.ts` | `assignApartment` ensures `places` row |
| `src/main.ts` | Global `ValidationPipe` (transform + whitelist) |

### Unity (`simple-town-rp/Assets/Scripts/`)

| File | Role |
|---|---|
| `Entities/Persistence/PlaceApi.cs` | Newtonsoft DTOs for `/places/*`, `/props/*`, `/covers/*` |
| `Entities/Persistence/StateDataMapper.cs` | `state_data` JSON → byte[] payload bridge |
| `Managers/ApiManager.cs` | `CreatePlaceRequest`, `GetPlaceStateRequest`, `CreatePropRequest`, `UpdatePropRequest`, `DeletePropRequest`, `UpsertCoversRequest`, `EnsureTransitPlace`, `TransitPlaceId` cache |
| `Network/SimpleTownNetwork.cs` | Boot: `EnsureTransitPlace`. Buy: `CreateDeliveryCoroutine` creates the prop in transit before the delivery |
| `Network/Messages/CreateDeliveryRequest.cs` | Carries `propId` injected by server |
| `Props/Server/ServerPropManager.cs` | `PropDbBridge` map + `AssociateUuid` / `GetBridge` / `UpdateVersion` / `FindPropIdByUuid` |
| `Props/Server/PropInteractionDispatcher.cs` | `SyncPropTransform`, `SyncPropState`, `SyncPropBuilt`, `SyncPropRemove`, `SyncCovers`, `BuildPropCoroutine` (UUID extraction + PATCH) |
| `Props/Server/PropInteractionRouter.cs` | Dual-write wiring (calls sync helpers + `apt.Save()`) |
| `Managers/ApartmentController.cs` | `LoadPlaceStateOrFallback`, `InstantiateLevelFromPlaceState`, `PlaceStateIsHydrated`, `RestoreFrontDoorFromPlaceEntry`, `SpawnInnerDoorsFromPreset`, `SpawnLightsFromPreset`, `SpawnDeliveryBoxFromPreset` |

---

## 9. Conventions to preserve

- **Server-only API calls for gameplay state.** Clients only hit the API at
  the main-menu stage (login, character/home selection). All in-game
  persistence goes through Unity-server-side coroutines.
- **Newtonsoft.Json for the new endpoints** (`NullValueHandling.Ignore` for
  optional position/rotation, dictionary `state_data` support). The legacy
  `Entities/` types stay on `JsonUtility` because of `SyncVar` round-trip
  constraints.
- **No mocks in tests.** If a test touches the DB, it must hit a real one
  (per project preference).
- **Class-validator on every DTO**, `ValidationPipe` with `transform: true`.
