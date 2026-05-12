# PROP_SYSTEM.md — Prop Lifecycle

> **Persistence:** runtime state lives in `ServerPropManager`; durable state
> lives in the `props` / `covers` tables (see `PERSISTENCE.md`). A runtime
> `int propId` is bridged to a DB `UUID` via `ServerPropManager.PropDbBridge`.

---

## Two Prop Origins

### Scene Props (City.unity)
Props placed by the designer directly in the `City.unity` scene. Every client has the GameObject already via the scene load — no instantiation message is needed.

**Registration:** `CityRoomInitializer.OnStartServer()` (a `NetworkBehaviour` in City.unity) calls `ServerPropManager.RegisterSceneProp(source)` for every `ServerPropSource` found with `RoomId == "city"`.

**Snapshot delivery:** On `SendRoomSnapshot`, scene props send only `S2C_PropUpdate` (state only — no position, no prefab ID, because the GO already exists on the client). The client `OnPropUpdate` handler applies the state via `IPropBehaviour.ApplyState()`.

**PropId:** Hardcoded by the designer in the Inspector on the `PropIdentity` component. Must be in the range 1–9999 to avoid collision with runtime IDs.

**Characteristics:**
- `ServerPropState.IsScene = true`
- Cannot be moved (`UpdatePropTransform` rejects scene props with a warning)
- Not tracked in `_spawnedGOs` on the server
- Not destroyed by `ClearRoom()` (the GOs persist in the scene)

### Runtime Props (Apartment Furniture, Doors, Delivery Boxes)
Props spawned at runtime via `ServerPropManager.SpawnProp()`. These are instantiated server-side, assigned an auto-ID, and broadcast to all room members via `S2C_PropSpawn`.

**Registration:** Happens inside `SpawnProp()` — the prefab is instantiated, `PropIdentity.Assign(propId, roomId)` is called, and the state is stored in `_rooms[roomId][propId]`.

**Snapshot delivery:** On `SendRoomSnapshot`, runtime props send `S2C_PropSpawn` (full instantiation message including position, rotation, prefab ID, and initial payload). The client instantiates the prefab locally.

**PropId:** Auto-assigned by `ServerPropManager._nextAutoId`, starting at 10000.

**Characteristics:**
- `ServerPropState.IsScene = false`
- Can be moved (`UpdatePropTransform` updates position in `ServerPropState` and moves the server-side GO)
- Tracked in `_spawnedGOs` on the server for GameObject lifecycle management
- Destroyed by `RemoveProp()` / `ClearRoom()` via `UnityEngine.Object.Destroy(go)`

---

## ServerPropState Fields

Defined in `Assets/Scripts/Props/Server/ServerPropState.cs`:

| Field | Type | Meaning |
|---|---|---|
| `PropId` | `int` | Unique identifier. 1–9999 = scene prop, 10000+ = runtime prop |
| `PrefabId` | `int` | `PropsConfig.GetId()`. Used by `S2C_PropSpawn` so the client can look up the prefab in `DatabaseManager.PropsDatabase` |
| `RoomId` | `string` | Room the prop belongs to (e.g. `"hall:Rue_de_la_Paix:2"`) |
| `Position` | `Vector3` | World-space position. Updated by `UpdatePropTransform()` |
| `Rotation` | `Quaternion` | World-space rotation. Updated by `UpdatePropTransform()` |
| `Type` | `PropType` | Routes deserialization and interaction handling |
| `Payload` | `byte[]` | Serialized typed state (see PropPayloads section below) |
| `IsScene` | `bool` | True = scene prop (S2C_PropUpdate in snapshot), False = runtime (S2C_PropSpawn) |

---

## ServerPropSource vs PropBehaviourBase vs IPropBehaviour

All three live on the **same prefab GameObject**.

```
GameObject (prop prefab)
├── PropIdentity              — shared identity (propId, roomId)
├── ServerPropSource          — server-only logic (abstract base)
│     └── e.g. DoorPropSource, SeatPropSource, TeleporterPropSource, etc.
├── PropBehaviourBase         — client-side base (abstract)
│     └── e.g. DoorBehaviour, SeatBehaviour, TeleporterBehaviour, etc.
│     implements IPropBehaviour
├── PropsRenderer             — visual state (built/unbuilt, presets)
└── (other visual/audio components)
```

### `ServerPropSource` (`Assets/Scripts/Props/Server/ServerPropSource.cs`)
- Abstract base, `RequireComponent(typeof(PropIdentity))`
- Provides `PropId` and `RoomId` from the sibling `PropIdentity`
- Subclasses implement `Type` (returns the `PropType` enum value) and `GetInitialState()` (returns the initial binary payload)
- Server-only guards must be inside subclass methods (e.g. `OnTriggerEnter` guarded by `NetworkServer.active`)
- Concrete subclasses: `DoorPropSource`, `SeatPropSource`, `TeleporterPropSource`, `DeliveryBoxPropSource`, `DispenserPropSource`, `PaintBucketPropSource`, `PackagePropSource`, `GenericPropSource`

### `PropBehaviourBase` (`Assets/Scripts/Props/Behaviours/PropBehaviourBase.cs`)
- Abstract MonoBehaviour, `RequireComponent(typeof(PropIdentity))`
- Implements both `IPropBehaviour` and `IInteractable`
- Reads `PropsConfig` for actions, range, presets
- Implements base actions: LOOK (client-local), BUILD (sends `C2S_PropInteraction`), SELL (fires `OnSellRequest` event), MOVE (fires `OnMoveRequest` event)
- Delegates type-specific actions to the `Execute(Action)` virtual method
- `ApplyState()` reads `PropStateHeader`, applies built/unbuilt state and preset to `PropsRenderer`

### `IPropBehaviour` (`Assets/Scripts/Props/Behaviours/IPropBehaviour.cs`)
- Interface: `void ApplyState(PropType type, byte[] payload)`
- `ClientPropManager` stores `Dictionary<int, IPropBehaviour>` — one entry per prop, keyed by propId
- Concrete implementations: `GenericPropBehaviour`, `DoorBehaviour`, `SeatBehaviour`, `TeleporterBehaviour`, `DeliveryBoxBehaviour`, `DispenserBehaviour`, `PaintBucketBehaviour`, `PackageBehaviour`

---

## PropIdentity Role

`PropIdentity` (`Assets/Scripts/Props/Core/PropIdentity.cs`) is a MonoBehaviour that acts as the shared ID anchor on a prop prefab.

- For scene props: `propId` and `roomId` are set in the Inspector by the designer.
- For runtime props: `PropIdentity.Assign(propId, roomId)` is called immediately after instantiation by both `ServerPropManager.SpawnProp()` (server) and `ClientPropManager.OnPropSpawn()` (client).
- `PropBehaviourBase` reads `_identity.PropId` when sending interactions.
- `ClientPropManager.IndexSceneProps()` iterates `FindObjectsByType<PropIdentity>()` to build the initial index.

---

## PropType Enum

Defined in `Assets/Scripts/Props/Core/PropType.cs`:

| Value | Byte | Description |
|---|---|---|
| `Generic` | 0 | Standard furniture. Payload = header only (5 bytes). Interactions: BUILD |
| `Door` | 1 | Door prop. Payload = header(5) + isOpen(1) + lockState(1) + doorNumber(4). Server-driven via triggers (not C2S) |
| `Seat` | 2 | Seat/couch. Payload = header + seat occupant netIds + couch occupant netIds. Interactions: SIT, COUCH, REVOKE |
| `Dispenser` | 3 | Item vending machine. Payload = header only. Catalog from `DispenserConfiguration` ScriptableObject. Purchase via C2S |
| `PaintBucket` | 4 | Paint bucket. Payload = header + paintConfigId(4) + R(4) + G(4) + B(4). Open is client-local (shows UI) |
| `DeliveryBox` | 5 | Mailbox. Payload = header + deliveryCount(4). Open triggers REST fetch then S2C_DeliveryBoxOpened |
| `Package` | 6 | Parcel. Payload = header + propsConfigId(4). Open is client-local (triggers build mode with contained prop) |
| `Teleporter` | 7 | Elevator. No C2S_PropInteraction — uses C2S_TeleporterUse instead. Server routes via PlayerRoomTracker |

---

## Snapshot Delivery

### Scene Prop (`IsScene=true`) Snapshot
```
ServerPropManager.SendRoomSnapshot(conn, roomId)
  For each prop where s.IsScene == true:
    conn.Send(new S2C_PropUpdate {
        PropId  = s.PropId,
        RoomId  = s.RoomId,
        Type    = s.Type,
        Payload = s.Payload
    });
```
Client already has the GO (from scene load). `OnPropUpdate` finds the behaviour in `_props[PropId]` (indexed by `IndexSceneProps`) and calls `ApplyState()`.

### Runtime Prop (`IsScene=false`) Snapshot
```
ServerPropManager.SendRoomSnapshot(conn, roomId)
  For each prop where s.IsScene == false:
    conn.Send(new S2C_PropSpawn {
        PropId   = s.PropId,
        PrefabId = s.PrefabId,
        RoomId   = s.RoomId,
        Position = s.Position,
        Rotation = s.Rotation,
        Type     = s.Type,
        Payload  = s.Payload
    });
```
Client instantiates the prefab via `DatabaseManager.PropsDatabase.GetPropsById(msg.PrefabId)`, assigns `PropIdentity`, indexes in `_props`, and calls `ApplyState()`.

---

## Build Flow

Full sequence in `PropInteractionDispatcher.BuildPropCoroutine()` (Phase 2):

1. REST `DELETE /deliveries/{DeliveryId}` — backend echoes the deleted row so
   we can read its `propId` (the UUID assigned at buy time, see `PERSISTENCE.md`
   §5.1). Without a `downloadHandler` on the request this NREs — `ApiManager.
   DeleteDeliveryRequest` attaches a `DownloadHandlerBuffer`.
2. Extract `propUuid` from the response body.
3. Look up `PropsConfig` by `msg.PropConfigId`.
4. Determine `isBuilt` from `config.MustBeBuilt()` — if the prop requires the
   BUILD action before becoming interactive, `IsBuilt=false`.
5. Build initial payload:
   - If `msg.PaintConfigId >= 0`: `PaintBucketState { Header, PaintConfigId, R, G, B }.Serialize()`
   - Otherwise: let `ServerPropSource` emit its default body (header override only).
6. `ServerPropManager.SpawnProp(apt.RoomId, PropConfigId, Position, Rotation, …)` → returns `newPropId`.
7. `apt.TrackProp(newPropId)` — adds to `_ownedPropIds`.
8. Reparent server-side GO under `apt.PropsContainer`.
9. **Bridge:** `ServerPropManager.AssociateUuid(newPropId, propUuid)` ties the
   runtime int ID to the DB UUID. Subsequent sync calls (`SyncPropTransform`,
   `SyncPropState`, `SyncPropBuilt`, `SyncPropRemove`) look up the UUID via
   this bridge.
10. **PATCH** `/props/{propUuid}` (`PatchBuiltPropLocation`) — moves the prop from
    the transit place to the apartment place, sets position/rotation/isBuilt,
    returns the new `version`. The bridge's `Version` is updated.
11. `RefreshDeliveryBoxCount()` — REST GET delivery count, updates `DeliveryBoxState.DeliveryCount`, broadcasts `S2C_PropUpdate`.
12. `apt.Save()` — legacy dual-write to `homes.scene_data` (removed in Phase 3).
13. `conn.Send(new S2C_BuildAck { Success = true })`.

---

## Edit Flow

`PropInteractionRouter.HandleEditProp()`:
1. `ServerApartmentRegistry.TryGetByConn(conn)` — get tenant's apartment
2. `apt.OwnsProp(msg.PropId)` — verify ownership (prop in `_ownedPropIds`)
3. `ServerPropManager.UpdatePropTransform(apt.RoomId, msg.PropId, msg.Position, msg.Rotation)`:
   - Updates `ServerPropState.Position`/`Rotation`
   - Moves server-side GO if present in `_spawnedGOs`
   - Broadcasts `S2C_PropTransform { PropId, RoomId, Position, Rotation }` to room
4. `PropInteractionDispatcher.SyncPropTransform(propId, pos, rot)` — PATCHes
   `/props/{uuid}` with the new position+rotation (no-op if no bridge).
5. `apt.StartCoroutine(apt.Save())` — legacy dual-write (removed in Phase 3).

---

## Remove Flow

`PropInteractionRouter.HandleRemoveProp()`:
1. Ownership verification (same as edit)
2. `ServerPropManager.RemoveProp(apt.RoomId, msg.PropId)`:
   - Removes from `_rooms[roomId]`
   - If in `_spawnedGOs`: `UnityEngine.Object.Destroy(go)`, removes from `_spawnedGOs`
   - Broadcasts `S2C_PropRemove { PropId, RoomId }` to room
3. `PropInteractionDispatcher.SyncPropRemove(propId)` — DELETE `/props/{uuid}` (no-op if no bridge).
4. `apt.StartCoroutine(apt.Save())` — legacy dual-write (removed in Phase 3).

Client `OnPropRemove`:
- Removes from `_props`
- If in `_spawnedGOs`: `Destroy(go)`, removes from `_spawnedGOs`

---

## ApartmentController Ownership Tracking (`_ownedPropIds`)

`ApartmentController` maintains `private readonly HashSet<int> _ownedPropIds`. This serves two purposes:

1. **Authorization:** `apt.OwnsProp(propId)` is checked by `PropInteractionRouter` before allowing edit/remove.
2. **Cleanup:** On `OnDestroy()` (hall is torn down) or `Regenerate()`, all owned props are removed from `ServerPropManager` to prevent orphaned state.

`TrackProp(propId)` is called:
- In `Init()` when the front door is spawned
- In `InstantiateLevel()` for inner doors, delivery box, and furniture loaded from save
- In `PropInteractionDispatcher.BuildPropCoroutine()` for newly built props

Exclusions in `GenerateSceneData()`: `frontDoorPropId` and `deliveryBoxPropId` are excluded from the save file — they are always re-spawned by `Init()`/`InstantiateLevel()` on load.

---

## SaveUtils.SpawnPropFromSave and Local→World Transform Conversion

`SaveUtils.SpawnPropFromSave(data, parent)` in `Assets/Scripts/Save/SaveUtils.cs`:

**Problem:** `SceneData` saves prop positions in local space relative to `apartment.PropsContainer`. `ServerPropManager` stores world-space positions. When loading, the container's world transform must be applied.

```csharp
Vector3    localPos = data.transform.position.ToVector3();
Quaternion localRot = Quaternion.Euler(data.transform.rotation.ToVector3());
Vector3    worldPos = container != null ? container.TransformPoint(localPos) : localPos;
Quaternion worldRot = container != null ? container.rotation * localRot     : localRot;
```

**On save** (`DefaultData(ServerPropState state, Transform parent)`):
```csharp
Vector3    localPos = parent != null ? parent.InverseTransformPoint(state.Position) : state.Position;
Quaternion localRot = parent != null ? Quaternion.Inverse(parent.rotation) * state.Rotation : state.Rotation;
```

This round-trip is safe as long as the `PropsContainer` transform is the same on load as on save. Since `ApartmentController` prefab positions are set by `HallController.Init()` at the spawn points, which are fixed, the container transform is deterministic.

---

## Payload Format Reference

All payloads begin with `PropStateHeader` (5 bytes):
- Byte 0: `IsBuilt` (0=false, 1=true)
- Bytes 1–4: `PresetId` (int32 LE, -1 = no preset)

Full payload sizes:

| PropType | Payload size | Extra fields |
|---|---|---|
| Generic | 5 bytes | header only |
| Door | 11 bytes | header(5) + isOpen(1) + lockState(1) + doorNumber(4) |
| Seat | variable | header(5) + seatCount(1) + seats(4×N) + couchCount(1) + couches(4×M) |
| PaintBucket | 21 bytes | header(5) + paintConfigId(4) + R(4) + G(4) + B(4) |
| DeliveryBox | 9 bytes | header(5) + deliveryCount(4) |
| Dispenser | 5 bytes | header only |
| Package | 9 bytes | header(5) + propsConfigId(4) |
| Teleporter | n/a | no C2S_PropInteraction payload — uses C2S_TeleporterUse |

---

## UUID Bridge & DB Sync (Phase 2 onward)

`ServerPropManager` keeps a `Dictionary<int, PropDbBridge>` mapping the
runtime `int propId` to the persistent UUID + version pair.

```
ServerPropManager
├── _rooms                     : Dictionary<string, Dictionary<int, ServerPropState>>
├── _spawnedGOs                : Dictionary<int, GameObject>
└── _runtimeToDb (new)         : Dictionary<int, PropDbBridge { Uuid, Version }>

API:
  AssociateUuid(propId, uuid, version=1)   — set up the bridge
  GetUuid(propId)                          — read the UUID
  GetBridge(propId)                        — read uuid + version (for PATCH)
  UpdateVersion(propId, version)           — refresh after a successful PATCH
  FindPropIdByUuid(uuid)                   — reverse lookup (used at build time)
  ClearBridge(propId)                      — on remove
```

A bridge is created in two places:
1. **Apartment load** — `ApartmentController.InstantiateLevelFromPlaceState`
   spawns each persisted prop with its `propUuid` + `propVersion`, and the
   `SpawnProp` overload auto-bridges.
2. **Build** — `PropInteractionDispatcher.BuildPropCoroutine` extracts the
   UUID from the deleted delivery row and calls `AssociateUuid`.

### Dispatcher sync helpers

Every gameplay write goes through one of these. They look up the bridge,
no-op if absent (legacy / fixture prop), otherwise PATCH/DELETE with
`expectedVersion`:

| Helper | Triggered by | DB effect |
|---|---|---|
| `SyncPropTransform(propId, pos, rot)` | `HandleEditProp` | PATCH `position`, `rotation` |
| `SyncPropState(propId, dict)` | door lock, paint, seat… | PATCH `state_data` |
| `SyncPropBuilt(propId, true)` | `HandleGeneric` build action | PATCH `is_built` |
| `SyncPropRemove(propId)` | `HandleRemoveProp` | DELETE `/props/{uuid}` |
| `SyncCovers(placeId, entries)` | wall/ground paint | PUT `/covers/{placeId}` |

After every successful PATCH, the dispatcher reads `version` from the
response and calls `UpdateVersion(propId, …)` so the next write doesn't
409. The Unity server is the single writer, so 409s should never happen
under normal operation — treat them as bugs.

See `PERSISTENCE.md` for the full flow (buy / build / load / dual-write).
