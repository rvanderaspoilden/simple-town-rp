# ROOM_SYSTEM.md — Room Architecture

## Room ID Naming Convention

Rooms are identified by plain strings. Factory helpers in `RoomId.cs` (`Assets/Scripts/Props/Core/RoomId.cs`):

| Factory Method | Result format | Example |
|---|---|---|
| `RoomId.City()` | `"city"` | `"city"` |
| `RoomId.Apartment(street, doorNumber)` | `"apartment:{street}:{doorNumber}"` | `"apartment:Rue_de_la_Paix:3"` |
| `RoomId.Custom(id)` | `"{id}"` | `"hall:Rue_de_la_Paix:2"` |

In practice the codebase also uses these patterns (not via the factory but as literal strings):
- **`"city"`** — the outdoor city scene, all players and scene props
- **`"hall:{street}:{floor}"`** — a building floor. Produced by `HallController.RoomId` property: `$"hall:{street}:{floorNumber}"`. This is the ONLY room ID for all props on that floor.
- **`"apt:{street}:{doorNumber}"`** — used as `ApartmentKey` (per-apartment identity) but NOT as a room ID. `ApartmentController.ApartmentKey` uses this format.

> Note: The `RoomId.Apartment()` factory produces `"apartment:..."` but `ApartmentController` uses `"apt:..."` for its own key. These are different formats. The room ID is always the hall ID (`hall:...`), never `apartment:...` or `apt:...`.

---

## One-Room-Per-Floor Rule

**Rule:** Every apartment on floor N and the hall corridor itself all share the single room ID `hall:{street}:{floor}`.

**Why:** The prop system scopes all broadcasts to room members. If each apartment had its own room, a player in the hall corridor would not receive prop updates for any apartment door or shared-floor element. Using the hall room as the common scope means:
- Front doors (spawned per apartment) are visible to all players on the floor.
- Delivery boxes, inner doors, and furniture are broadcast to all players on the floor simultaneously.
- `PlayerRoomTracker` has a single entry per player, so a player in apartment 2B and a player in the hallway of floor 2 are in the same room from the network perspective.

**Consequences:**
- `ApartmentController.RoomId` returns `associatedHallController.RoomId`, not an apartment-specific string.
- When the server dispatches `C2S_BuildProp` / `C2S_EditProp` / `C2S_RemoveProp`, the room validation passes because the player is in the hall room — but **ownership** is enforced via `ServerApartmentRegistry.TryGetByConn()` which resolves the tenant's apartment. The room match alone is not sufficient authorization.
- `S2C_RoomState` messages for apartments all carry the hall's `RoomId`. Multiple `S2C_RoomState` messages with the same `RoomId` are expected — each contains a different apartment's `ApartmentRoomState`. The client disambiguates by deserializing the payload and checking `state.street == this.street && state.doorNumber == this.doorNumber`.
- `ServerPropManager` stores apartment states in `_apartmentStatesByRoom[roomId][apartmentKey]` — one dict entry per apartment, keyed by `ApartmentKey`, grouped under the shared hall room ID.

---

## Room Entry Sequence

```
Player teleports to hall:
  TeleportMessage.NewRoomId = "hall:Rue_de_la_Paix:2"

Client:
  SimpleTownNetwork.OnTeleportPlayer()
  └─ ClientPropManager.EnterRoom("hall:Rue_de_la_Paix:2")
       ├─ C2S_LeaveRoom { old room } (if any)
       ├─ ClearProps() — destroy all runtime-spawned GOs, clear index
       ├─ IndexSceneProps("hall:Rue_de_la_Paix:2") — scan scene for PropIdentity with this roomId
       └─ C2S_EnterRoom { "hall:Rue_de_la_Paix:2" }

Server (PropSystemBootstrap.Server_OnEnterRoom):
  ├─ PlayerRoomTracker.EnterRoom(conn, "hall:Rue_de_la_Paix:2")
  │    └─ fires OnPlayerEnterRoom event
  │         ├─ NpcServerManager.SendRoomSnapshot(conn, roomId)
  │         └─ ServerItemManager.OnPlayerEnterRoom(conn, roomId)
  └─ ServerPropManager.SendRoomSnapshot(conn, "hall:Rue_de_la_Paix:2")
       ├─ S2C_RoomSnapshot { PropCount=N }
       ├─ For each prop (isScene=true):  S2C_PropUpdate
       ├─ For each prop (isScene=false): S2C_PropSpawn
       ├─ S2C_RoomState { hall-level room state, if any }
       └─ For each apartment: S2C_RoomState { apartment payload }

Client receives snapshot:
  ├─ S2C_RoomSnapshot: logs prop count
  ├─ S2C_PropUpdate (scene props): IPropBehaviour.ApplyState() on indexed props
  ├─ S2C_PropSpawn (runtime): instantiate prefab, index, ApplyState()
  └─ S2C_RoomState (per apartment): fires OnRoomStateReceived event
       └─ ApartmentController.OnRoomStateReceived():
            ├─ Deserialize ApartmentRoomState
            ├─ Check state.street == this.street && state.doorNumber == this.doorNumber
            └─ ApplyRoomState(): set preset, apply wall/ground covers
```

---

## ClientPropManager State Machine

`ClientPropManager` is a simple CRUD registry — it has no explicit state machine with named states. The effective states are:

| State | Condition | Behavior |
|---|---|---|
| **No room** | `_currentRoomId == null or ""` | Ignores all incoming S2C messages; `RequestInteraction()` drops the request with a warning |
| **In room** | `_currentRoomId` set | Processes S2C messages matching the current room ID; ignores mismatched room IDs |
| **Transitioning** | During `EnterRoom()` call | Clears props, sends `C2S_LeaveRoom` + `C2S_EnterRoom`, then re-indexes scene props |

Key invariant: `OnRoomSnapshot` / `OnPropSpawn` / `OnPropUpdate` / `OnPropTransform` / `OnPropRemove` all silently ignore messages with `msg.RoomId != _currentRoomId`. This is a belt-and-suspenders filter since the server only broadcasts to room members.

---

## PlayerRoomTracker Role

`PlayerRoomTracker` (`Assets/Scripts/Props/Server/PlayerRoomTracker.cs`) is a plain C# singleton (no MonoBehaviour) that maintains two bidirectional dictionaries:
- `_connToRoom: Dictionary<NetworkConnectionToClient, string>` — which room each connection is in
- `_roomToConns: Dictionary<string, HashSet<NetworkConnectionToClient>>` — which connections are in each room

It fires two events:
- `OnPlayerEnterRoom(conn, roomId)` — fired after `EnterRoom()` updates the maps. Subscribed by `NpcSystemBootstrap` (to trigger NPC snapshot) and `RoomActivityController` (to track active rooms).
- `OnPlayerLeaveRoom(conn, roomId)` — fired before removal from maps. Subscribed by `RoomActivityController`.

On disconnect, `PlayerRoomTracker.OnDisconnect(conn)` is called by `PropSystemBootstrap.Server_OnDisconnect`, which also first calls `PropInteractionRouter.ReleaseSeatsByPlayer()` to free any held seats.

`ServerPropManager.BroadcastToRoom()` and `NpcServerManager.BroadcastToRoom()` both call `PlayerRoomTracker.Instance.GetConnectionsInRoom(roomId)` to get the send targets.

---

## How Apartment State Travels to Client

Apartment state (tenant identity, preset name, wall covers, ground covers) is encoded as `ApartmentRoomState` (defined in `RoomStatePayloads.cs`) and serialized to UTF-8 JSON via `JsonUtility`. It travels via `S2C_RoomState.Payload`.

**Server side:**
1. `ApartmentController.UpdateRoomState()` creates an `ApartmentRoomState` and calls `ServerPropManager.SetApartmentState(roomId, apartmentKey, payload)`.
2. `ServerPropManager._apartmentStatesByRoom[roomId][apartmentKey] = payload` persists it.
3. `S2C_RoomState { RoomId=hall:..., Payload=payload }` is broadcast to all connections in the hall room.
4. On `SendRoomSnapshot`, every entry in `_apartmentStatesByRoom[roomId]` is re-sent so late joiners receive all apartments.

**Client side:**
1. `ClientPropManager.OnRoomStateReceived` fires `OnRoomStateReceived` event with `(roomId, payload)`.
2. Each `ApartmentController` subscribes to this event in `Awake()`, unsubscribes in `OnDestroy()`.
3. `ApartmentController.OnRoomStateReceived()` deserializes `ApartmentRoomState`, checks `state.street == this.street && state.doorNumber == this.doorNumber`, then calls `ApplyRoomState()`.
4. `ApplyRoomState()` applies preset, wall covers, and ground covers visually.

---

## Edge Cases

### Late Join (player connects after hall is already generated)
`CheckGenerationState` already fired and broadcast `S2C_ApartmentSpawn` to all connected clients. The new client receives:
1. `S2C_HallSpawn` — server sends this when any client enters the floor. Wait — actually, `S2C_HallSpawn` and `S2C_ApartmentSpawn` are only sent at floor creation time (via `NetworkServer.SendToAll`). A client that connects after the floor is generated does NOT automatically receive these.

> Assumption: There is no re-broadcast mechanism for `S2C_HallSpawn`/`S2C_ApartmentSpawn` on late join. A client that connects while a floor is already loaded may not see the hall/apartments unless it is teleported to that floor. The snapshot mechanism covers prop state, but the hall/apartment scene graph must be received during active floor creation or via a subsequent teleport.

### Hall Despawn Mid-Load (player disconnects while floor is generating)
`HallController.RemoveDisconnectedPlayer()` is registered to `SimpleTownNetwork.OnPlayerDisconnected`. It removes the connection from `playersInside` and `playersToMove`, then calls `associatedBuilding.TryToCleanHall()`. If `playersToMove` is empty and `playersInside` is empty, the hall is destroyed and `S2C_HallDespawn` is sent. Any apartment coroutines still running on a destroyed `HallController` will reference a null `associatedHallController`, which will produce a NullReferenceException in `ApartmentController.CheckGenerationState()`.

> Known risk: if `RetrieveData()` coroutine completes on an `ApartmentController` after the hall is destroyed, the `associatedHallController.CheckGenerationState()` call will throw or silently no-op depending on whether the hall GO is already null.

### Multiple Players on the Same Floor
All players in `hall:street:floor` receive all prop broadcasts. There is no per-player filtering within a floor. Apartment ownership checks are server-side only (build/edit/remove). Multiple players can observe each other's apartment furniture simultaneously.

### Room Transition Race Condition
If `TeleportMessage` arrives and the client calls `ClientPropManager.EnterRoom(newRoomId)` while still processing a snapshot from the old room, the `ClearProps()` call will destroy runtime GOs that are still referenced by in-flight `S2C_PropSpawn` messages. The room ID filter (`if msg.RoomId != _currentRoomId return`) mitigates this: after `EnterRoom()`, the old room ID is no longer `_currentRoomId`, so stale messages are dropped.

---

## Known Issues / Gotchas

1. **`GeographicArea` triggers must NOT call `ClientPropManager.EnterRoom()`**. These are spatial sub-zones within a room (e.g. apartment volume inside the hall floor room). Calling `EnterRoom` from a trigger would `ClearProps()` and destroy all floor-level props. The comment in `PlayerController.cs` explicitly documents this.

2. **`S2C_RoomState` is overloaded**. The same message type carries both per-room state and per-apartment state. The client must deserialize the payload to know which apartment it belongs to. A well-typed approach would use separate message types.

3. **`HallController.RoomId` is a computed property**. If `street` or `floorNumber` are zero/empty (before `Init()` or `ClientSetup()`), the room ID is malformed. Props registered before `Init()` completes will have incorrect room IDs.

4. **`ClientPropManager.IndexSceneProps()` uses `FindObjectsByType`**. This is O(N) over all scene objects every time a player enters a room. In the city room with many scene props, this is a notable allocation.

5. **`_generationBroadcasted` flag is never reset on server stop**. If the same `HallController` instance were reused across sessions (it is not — it is `Destroy()`ed), the flag would prevent re-broadcast. Currently safe because halls are always destroyed and re-instantiated.
