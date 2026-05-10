# NPC_SYSTEM.md — NPC Architecture

The NPC system is a complete, functional implementation as of the latest commits. It follows the same room-scoped, custom-message architecture as the prop and item systems — no `NetworkIdentity`, no `SyncVar`, Mirror used as transport only.

---

## NPC Data Model

### `NpcServerState` (`Assets/Scripts/NPC/Core/NpcServerState.cs`)
Mutable server-side record for one live NPC. Stored in `NpcServerManager._npcs[npcId]`.

| Field | Type | Meaning |
|---|---|---|
| `NpcId` | `int` | Auto-assigned integer ID (starts at 1, increments per Register call) |
| `PrefabId` | `string` | String key referencing the prefab in `NpcPrefabDatabase` |
| `RoomId` | `string` | Room the NPC is assigned to (currently always `"city"`) |
| `StyleJson` | `string` | JSON of `Style` (same format as `PlayerController.rawCharacterData`), empty = default prefab appearance |
| `Identity` | `NpcIdentity` | `FirstName`, `LastName`, `Mood` (MoodEnum) |
| `Position` | `Vector3` | Current position (pushed by `NpcAIController.Update()`) |
| `Rotation` | `Quaternion` | Current rotation |
| `Velocity` | `Vector3` | Current velocity (from NavMeshAgent) |
| `State` | `NpcStateType` | Current AI state (Idle, Walking, Sitting, BackToHome) |
| `LastSentPosition` | `Vector3` | Last broadcast position (for delta filtering) |
| `LastSentRotation` | `Quaternion` | Last broadcast rotation |
| `LastSentState` | `NpcStateType` | Last broadcast state |
| `EverSent` | `bool` | False until the first broadcast — ensures first update always sends |

### `NpcIdentity` (`Assets/Scripts/NPC/Core/NpcIdentity.cs`)
Plain struct: `FirstName: string`, `LastName: string`, `Mood: MoodEnum`. Constant for the NPC's lifetime.

### `NpcStateType` (`Assets/Scripts/NPC/Core/NpcStateType.cs`)
Enum used in both server state and network messages:
- `Idle` — standing still
- `Walking` — moving to a destination (derived from velocity in `NpcAIController.Update()`)
- `Sitting` — occupying a seat prop
- `BackToHome` — returning to spawn point before despawn

---

## Server-Side NPC Management

### `NpcServerManager` (Plain C# singleton, `Assets/Scripts/NPC/Server/NpcServerManager.cs`)
The authoritative server store. No MonoBehaviour.

**Key methods:**
- `Register(roomId, prefabId, position, rotation, styleJson, identity)` — assigns `npcId`, stores state, broadcasts `S2C_SpawnNpc` to room
- `PushTransform(npcId, position, rotation, velocity, stateType)` — updates in-memory state only (no broadcast — throttled by Tick)
- `NotifyStateChanged(npcId, newState)` — forces immediate broadcast (state transitions must be immediate for animation correctness)
- `Unregister(npcId)` — removes state, broadcasts `S2C_DestroyNpc`
- `SendRoomSnapshot(conn, roomId)` — sends `S2C_SpawnNpc` + `S2C_UpdateNpcTransform` for each NPC in the room
- `Tick(deltaTime)` — called by `NpcServerTicker.Update()`; accumulates time, calls `FlushUpdates()` at `1/UpdatesPerSecond` interval

**Broadcast throttle configuration:**
- `UpdatesPerSecond = 10f` (10 Hz default)
- `PositionThreshold = 0.05f` metres — minimum position delta to trigger a broadcast
- `RotationThresholdDeg = 2f` degrees — minimum rotation delta
- `SyncRotation = true` — can be disabled to save bandwidth

### `NpcAIController` (MonoBehaviour, `Assets/Scripts/NPC/Server/NpcAIController.cs`)
Server-side AI for one NPC. Requires `NavMeshAgent`. Lives only on the server (guarded by `NetworkServer.active` in `OnEnable`). Implements `ICharacterEntity`.

**Lifecycle (pool-based):**
1. `NpcPool.Get(prefab, spawnPoint, identity, roomId, prefabId, position, rotation)`
2. Pool calls `ConfigureForSpawn(home, identity, roomId, prefabId)` — injects data
3. Pool calls `ResetForPool()` — clears transient state
4. Pool calls `SetActive(true)` → `OnEnable` fires:
   - Style randomization (if `randomizeStyleOnSpawn`)
   - `NpcServerManager.Register()` → get `_npcId`
   - `BuildStateMachine()` — creates and wires state machine
5. `Update()` — ticks state machine, pushes transform to `NpcServerManager`
6. `NpcPool.Release()` → `SetActive(false)` → `OnDisable`:
   - `SeatService.ReleaseAllSeats(this, roomId)`
   - `NpcServerManager.Unregister(_npcId)`
   - NavMeshAgent path reset

**State machine transitions:**
```
Idle ──(idle complete + should return home)──> BackToHome
Idle ──(idle complete + not return home + decide sit)──> Sit
Idle ──(idle complete + not return home)──> GoToInterestArea
GoToInterestArea ──(arrived)──> Idle
Sit ──(finished sitting)──> Idle
BackToHome ──(arrived at home)──> RequestDespawn → NpcSpawnManager.Despawn
```

**AI suspension:** `Update()` returns early if `!RoomActivityController.Instance.IsRoomActive(roomId)`. NPCs do not move or broadcast when no player is in their room.

### `NpcSpawnManager` (Plain C# singleton, `Assets/Scripts/NPC/Server/NpcSpawnManager.cs`)
Manages the lifecycle of spawn points and active NPC count.

**Configuration:**
- `MaxActiveNpcs = 10`
- `RespawnDelaySeconds = 20f`
- `RoomId = "city"` — currently all NPCs are in the city room

**Tick behavior:** One spawn per tick maximum (to amortize load). Skips if room is inactive. Decrements cooldowns on spawn points.

**Despawn:** `RequestDespawn(npc)` called by `NpcBackToHomeState` when the NPC reaches its home position. Calls `NpcPool.Release(npc)`.

### `NpcPool` (Plain C# singleton, `Assets/Scripts/NPC/Server/NpcPool.cs`)
Object pool for `NpcAIController` GameObjects. Avoids `Instantiate`/`Destroy` per NPC spawn cycle.

**`Get(prefab, spawnPoint, identity, roomId, prefabId, position, rotation)`:**
1. Looks up the queue for this prefab
2. Dequeues or `Instantiate`s a new GO
3. Calls `ConfigureForSpawn()` then `ResetForPool()` on the `NpcAIController`
4. Sets position/rotation, calls `SetActive(true)`

**`Release(npc)`:** Calls `SetActive(false)`, enqueues back. The GO is never destroyed during normal operation.

**`Dispose()`:** Destroys all pooled GOs. Called on server stop.

### `InterestPointRegistry` + `InterestPoint` + `NpcSpawnPoint`
- `InterestPoint` (`Assets/Scripts/NPC/Server/InterestPoint.cs`) — MonoBehaviour marking a location NPCs can wander to. Registers itself with `InterestPointRegistry` on `Awake`.
- `InterestPointRegistry` — plain C# singleton storing all registered `InterestPoint`s; provides random/nearest-point queries.
- `NpcSpawnPoint` (`Assets/Scripts/NPC/Server/NpcSpawnPoint.cs`) — MonoBehaviour marking an NPC home/spawn position. Has `IsOccupied` flag, `NpcPrefab` override, `PrefabId` string.

### `SeatService` (`Assets/Scripts/NPC/Server/SeatService.cs`)
Server-side service for NPCs sitting on `Seat` props. Shares the same `ServerPropManager` seat slot mechanism used for players. Called by `NpcSitState` to reserve/release seats. Also called on `OnDisable` to release any held seats.

### `RoomActivityController` (`Assets/Scripts/NPC/Server/RoomActivityController.cs`)
Plain C# singleton tracking player count per room. Wired to `PlayerRoomTracker.OnPlayerEnterRoom` / `OnPlayerLeaveRoom` by `NpcSystemBootstrap`. Provides `IsRoomActive(roomId)` — used by `NpcAIController.Update()` and `NpcSpawnManager.Tick()`.

---

## Client-Side NPC Display

### `ClientNpcManager` (MonoBehaviour, DontDestroyOnLoad, `Assets/Scripts/NPC/Client/ClientNpcManager.cs`)
Receives NPC network messages and manages `ClientNpcView` instances.

**Handlers:**
- `OnSpawnNpc(S2C_SpawnNpc)` — instantiate prefab from `NpcPrefabDatabase`, call `ClientNpcView.Init()`
- `OnUpdateTransform(S2C_UpdateNpcTransform)` — call `ClientNpcView.PushSnapshot()`
- `OnDestroyNpc(S2C_DestroyNpc)` — destroy GO, remove from `_views`

**`ClearAll()`:** Called by `NpcSystemBootstrap.OnClientStop()` — destroys all NPC views.

### `ClientNpcView` (`Assets/Scripts/NPC/Client/ClientNpcView.cs`)
MonoBehaviour on each client-side NPC GO. Receives position snapshots from `ClientNpcManager` and interpolates between them. Drives local animation state from `NpcStateType`.

### `NpcPrefabDatabase` (ScriptableObject, `Assets/Scripts/NPC/Scriptables/NpcPrefabDatabase.cs`)
Maps `prefabId: string` → `GameObject`. Loaded from `Resources/Configurations/Databases/NPC Database`. Used by `ClientNpcManager` to look up instantiation prefabs.

### `NpcNameDatabase` (ScriptableObject, `Assets/Scripts/NPC/Scriptables/NpcNameDatabase.cs`)
List of first and last names for procedural NPC identity generation. Loaded from `Resources/Configurations/Databases/NPC Name Database` by `NpcSystemBootstrap.OnServerStart()`. If not found, a fallback `NPC{random}` name is used.

---

## NPC ↔ Room Relationship

All NPCs currently spawn in `roomId = "city"` (set by `NpcSpawnManager.RoomId = "city"`). The system architecture supports multiple rooms (the `NpcServerManager` stores NPCs per room and broadcasts per room), but the spawn manager only spawns into a single configurable room.

When a player enters a room, `NpcSystemBootstrap.Server_OnPlayerEnterRoom` is triggered via `PlayerRoomTracker.OnPlayerEnterRoom`. This calls `NpcServerManager.SendRoomSnapshot(conn, roomId)` which sends `S2C_SpawnNpc` + `S2C_UpdateNpcTransform` for each NPC currently in that room.

NPCs in empty rooms (no players) have their AI suspended (`RoomActivityController.IsRoomActive()` returns false) but remain alive in memory and the pool.

---

## Network Messages for NPCs

See `Assets/Scripts/NPC/Network/NpcNetworkMessages.cs`:

| Message | Direction | Fields | Purpose |
|---|---|---|---|
| `S2C_SpawnNpc` | Server → room clients | `NpcId, PrefabId, RoomId, Position, Rotation, StyleJson, FirstName, LastName, Mood` | Instantiate NPC on client |
| `S2C_UpdateNpcTransform` | Server → room clients | `NpcId, RoomId, Position, Rotation, Velocity, State` | Move/animate NPC (throttled 10 Hz, immediate on state change) |
| `S2C_DestroyNpc` | Server → room clients | `NpcId, RoomId` | Destroy NPC on client |

There are no C2S NPC messages — players cannot interact with NPCs via the network (no delivery to NPC, no NPC interaction in current implementation).

---

## Known Gaps / Incomplete Features

1. **All NPCs spawn in `"city"` only.** `NpcSpawnManager.RoomId = "city"` is hardcoded. Hall/apartment NPCs are not implemented.

2. **No C2S NPC interaction messages.** Players cannot deliver to, talk to, or otherwise interact with NPCs via the network. The context document mentions NPC delivery as optional for the POC.

3. **`NpcPrefabDatabase` resource path is hardcoded** (`"Configurations/Databases/NPC Database"`). If not found, a warning is logged but the system silently fails to spawn NPC visuals on the client.

4. **Interpolation implementation in `ClientNpcView`** — the file exists but its implementation details were not read in this audit. The NPC position interpolation quality is unconfirmed.

5. **Seat interaction for NPCs shares the same `SeatState` payload** as player seats. The NPC occupant ID is encoded via `CharacterEntityIds.EncodeNpc(_npcId)` — this encoding scheme sets bit 31. There is no verified guarantee that NPC occupant IDs never collide with player `netId` values in the lower 31 bits.

6. **No NPC persistence.** NPCs are entirely ephemeral — spawned on server start, despawned on server stop. There is no backend record of NPC state.

7. **`NpcServerTicker` is created as a `new GameObject("NpcServerTicker")`** with `DontDestroyOnLoad`. This GO persists across scene reloads but is properly destroyed in `NpcSystemBootstrap.OnServerStop()`.
