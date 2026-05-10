# CURRENT_PROBLEMS.md — Known Technical Debt and Bugs

---

## Bugs Fixed in Recent Sessions

The following issues were investigated and fixed as of the commit history available (`develop` branch, latest commit `d30a1e29d`):

### 1. `BuildingBehavior` on Inactive GameObject
**Root cause:** The City scene ships with `"Behaviour Manager"` as an inactive GameObject. `FindObjectsOfType<BuildingBehavior>()` does not find components on inactive objects. The `_buildingRegistry` dictionary was empty because `Awake()` never fired.

**Fix:** All lookups now use `FindObjectsByType<BuildingBehavior>(FindObjectsInactive.Include, FindObjectsSortMode.None)`. A static fallback `DiscoverInactiveBuildings()` is called once on first lookup miss. `SimpleTownNetwork.OnStartServer` iterates buildings with the inclusive scan.

### 2. Room Authority Violated by `GeographicArea` Triggers
**Root cause:** `GeographicArea` trigger volumes exist inside apartments (sub-zones within the hall room). Code was calling `ClientPropManager.EnterRoom()` from `OnTriggerEnter/Exit`, which called `ClearProps()` and destroyed all runtime-spawned props on the floor.

**Fix:** `GeographicArea` triggers update only the HUD location text. `EnterRoom()` is called exclusively from `SimpleTownNetwork.OnTeleportPlayer()` (via `TeleportMessage.NewRoomId`). This is documented in `PlayerController.cs` with an explicit comment.

### 3. Wrong `OnClientConnect` Override Signature
**Root cause:** Using `OnClientConnect(NetworkConnectionToClient conn)` does not override anything in current Mirror — the message was silently never sent.

**Fix:** The correct signature `public override void OnClientConnect()` is used in `SimpleTownNetwork`. `NetworkClient.Send(CreateCharacterMessage)` is called inside.

### 4. `ApartmentController.RetrieveData()` Race with Hall Despawn
**Root cause:** If a player disconnected during floor loading, `HallController` was destroyed but `ApartmentController.RetrieveData()` coroutine continued and called `this.associatedHallController.CheckGenerationState()` on a null reference.

**Partial fix:** `HallController.RemoveDisconnectedPlayer` cleans up `playersToMove` so the player is not teleported after disconnect. The coroutine race condition (apartment coroutine outliving hall) is still theoretically possible — see "Remaining Known Issues" below.

### 5. `_generationBroadcasted` Double-Broadcast Guard
**Root cause:** Multiple `ApartmentController.RetrieveData()` coroutines completing in the same frame could each call `CheckGenerationState()`, which would broadcast `S2C_ApartmentSpawn` multiple times to all clients.

**Fix:** `_generationBroadcasted = true` guard in `CheckGenerationState()` prevents duplicate broadcasts. `ApartmentController.Regenerate()` calls `OnApartmentRegenerating()` to reset the guard for re-generation cycles.

### 6. `PropInteractionRouter` Room Mismatch Validation
**Root cause:** A malicious or stale C2S message could claim a `RoomId` that the player is not actually in, allowing prop interactions across rooms.

**Fix:** Every handler in `PropSystemBootstrap` validates `PlayerRoomTracker.Instance.GetRoom(conn) == msg.RoomId` before dispatching. Mismatches are logged and the message is dropped.

### 7. NPC Seat Slots Not Released on Disconnect
**Root cause:** When a player disconnected while sitting on a seat prop, the seat slot remained occupied (netId != 0), making that seat permanently unavailable.

**Fix:** `PropSystemBootstrap.Server_OnDisconnect` calls `PropInteractionRouter.ReleaseSeatsByPlayer(roomId, netId)` before `PlayerRoomTracker.OnDisconnect(conn)`. This iterates all seat props in the player's room and clears their occupant slot.

---

## Remaining Known Issues

### Critical

1. **`ApartmentController.RetrieveData()` coroutine outliving `HallController` destruction.** If the hall is destroyed (all players disconnected) while `RetrieveData()` is still running its REST calls, the coroutine will eventually call `associatedHallController.CheckGenerationState()` on a destroyed (null) MonoBehaviour reference. This will produce a `MissingReferenceException`. Mitigation requires either a cancellation token or checking `associatedHallController != null` before the call.

2. **`S2C_HallSpawn` / `S2C_ApartmentSpawn` not re-sent on late join.** `NetworkServer.SendToAll(S2C_HallSpawn)` is called only at floor creation time. A client connecting after a floor is already loaded will not receive these messages and will never see the hall/apartment scene graph unless they are physically teleported to that floor. There is no snapshot mechanism for hall/apartment scene graph reconstruction.

3. **`ServerApartmentRegistry` is never `Clear()`ed on server stop.** It is a static singleton with no `Reset()` method. If the Unity editor runs multiple play sessions without restarting, stale `ApartmentController` references from the previous session could remain in the registry. However, since `ApartmentController` calls `Unregister(this.ApartmentKey)` in `OnDestroy()`, this is mitigated in practice.

### Moderate

4. **`PropsManager` is a separate MonoBehaviour that duplicates `DatabaseManager.PropsDatabase` access.** `PropsManager` (`Assets/Scripts/Managers/PropsManager.cs`) is a scene-level singleton that caches props by ID. `ServerPropManager.SpawnProp()` and `ClientPropManager.OnPropSpawn()` both use `DatabaseManager.PropsDatabase.GetPropsById()` directly. These two access patterns are inconsistent and `PropsManager` appears to be a legacy component.

5. **`NetworkEntity.cs` uses `SyncVar` + retry coroutine for parent assignment.** This is a `NetworkBehaviour` with a `parentId: uint` SyncVar. It was the base class for networked objects in the old architecture. It is still compiled and may be on scene prefabs, but is inconsistent with the new custom-message architecture. Should be audited to confirm it is truly unused.

6. **`PlayerController` mixes `NetworkBehaviour`/`SyncVar` with the custom-message architecture.** `rawCharacterData`, `rawCharacterHome`, `isTalking`, `_playerState` are SyncVars on the player prefab. This is intentional (player state needs to replicate to all clients) but creates a hybrid approach. Specifically, `CmdConsumeItem` is a Mirror `[Command]` rather than a custom C2S message, unlike all other client→server flows.

7. **`TeleportCoroutine` hardcoded delays.** `WaitForSeconds(1f)` and `WaitForSeconds(2f)` are not configurable. On slow connections or heavy servers, the 2-second hide delay may fire before the room snapshot is fully received, causing a visual pop.

8. **`DispenserPropSource`** resolution depends on `PropsConfig` being castable to `DispenserConfiguration`. The `PropInteractionRouter.HandleDispenser()` casts `propsConfig as DispenserConfiguration`. If a dispenser prop has a non-`DispenserConfiguration` PropsConfig assigned, the purchase silently fails with a warning. This is a type-safety gap.

9. **`ClientNpcManager` does not filter by room.** The comment in `ClientNpcManager.cs` notes that room filtering is intended but defers to server-side scoping: "the server ne broadcast déjà qu'aux conns de la room; ce filtrage côté client est une ceinture+bretelles utile pendant les transitions." No room-ID filter is actually implemented in `OnSpawnNpc` or `OnUpdateTransform`. If a stale broadcast arrives during a room transition, it will instantiate a NPC that the player shouldn't see.

### Minor

10. **`SaveLocal()` in `ApartmentController`** writes JSON to `Application.dataPath`. This is a development tool that must never run in production. There is no `#if UNITY_EDITOR` guard.

11. **`PlayerController.ParseCharacterHome` contains `Debug.Log("TOTO")`** — development artifact that was committed.

12. **103 `Debug.Log()` calls across production scripts** (non-`#if` guarded). The project has a `GameLogger`/`ClientLogger` structured logging system but raw `Debug.Log`s are still present in many files, including `ApartmentController`, `BuildingBehavior`, `HallController`, `PropInteractionRouter`, `PropInteractionDispatcher`, and `SimpleTownNetwork`. This makes log filtering and production log management difficult.

---

## Architectural Risks

1. **Prop payload versioning.** `PropPayloads.cs` defines fixed binary formats. If the payload format changes (e.g. adding a field to `DoorState`), old saves loaded from MongoDB will deserialize with incorrect data. There is no version byte in any payload. The deserialization code uses bounds checks (`data.Length >= N`) as a soft migration, but this is fragile.

2. **`_nextAutoId` is in-memory only.** If the server restarts while an apartment is saved with propId 10045, on the next server start `_nextAutoId` resets to 10000. Props spawned from saves (`SaveUtils.SpawnPropFromSave`) are assigned new propIds, which is correct. But if a player reconnects mid-session and props are re-spawned, IDs will overlap with any still-active runtime props from the previous server session that are not yet cleaned up.

3. **`ServerApartmentRegistry` linear scan in `FindOwnerOfProp` and `TryGetByTenant`.** Both methods iterate the entire registry. As the number of loaded apartments grows (multiple floors, multiple players), these become O(N) on every prop interaction and delivery. This is currently acceptable at small scale.

4. **Room ID is a plain string with no validation.** A malformed `C2S_EnterRoom` with an arbitrary string could register a connection in a non-existent room, occupying a slot in `_roomToConns`. `PlayerRoomTracker.EnterRoom` checks `string.IsNullOrEmpty` but not format validity.

5. **NPC `_nextId` resets to 1 on each `NpcServerManager.Reset()`**. If NPC IDs overlap with a previous session's IDs that are still cached on a client (e.g. slow connection), the client could apply updates from the new NPC to the old NPC's `ClientNpcView`. `ClearAll()` in `OnClientStop()` and re-registration on room entry mitigate this.

---

## TODOs Left in Code

| File | TODO |
|---|---|
| `Assets/Scripts/Player/PlayerController.cs:127` | `// TODO: to remove and use state` — `OnLocalPlayerMoveStarted` static event flagged for removal in favor of state machine observation |
| `Assets/Scripts/UI/Main Menu/ApartmentCreationManager.cs:43` | `// TODO: use to show animation` — animation on apartment creation not yet implemented |

---

## Dead Code Indicators

1. **`SpawnItemMessage`** (`Assets/Scripts/Network/Messages/SpawnItemMessage.cs`) — marked `[System.Obsolete]`, never registered as a server handler. Kept to avoid breaking serialized scene references. Safe to delete once confirmed unused in all scenes.

2. **`CharacterUpdateMoneyRequest`** (`Assets/Scripts/Network/Messages/CharacterUpdateMoneyRequest.cs`) — plain `[Serializable]` struct, not a `NetworkMessage`. No handler registered. Appears to be a dead stub.

3. **`CreateBuildingMessage`** (`Assets/Scripts/Network/Messages/CreateBuildingMessage.cs`) — a `NetworkMessage` in `Network.Messages` namespace. No server handler registered in `SimpleTownNetwork.OnStartServer`. Likely a future/unused feature for building customization.

4. **`NetworkEntity.cs`** — `NetworkBehaviour` base class from the old architecture. Still compiled. Should be audited to confirm no prefab in the project still has this component.

5. **`PropsManager.cs`** — scene-level MonoBehaviour that duplicates `DatabaseManager.PropsDatabase` caching. Its `InstantiateProps()` methods instantiate prefabs directly (not via `ServerPropManager.SpawnProp()`), bypassing the entire prop sync architecture. If used anywhere in the current codebase, those callers are unsynced.

6. **`Assets/Scripts/Building/Props/`** — legacy prop classes (`Props.cs`, `StaticProp.cs`, `Action.cs`, `Interactables/`, etc.) from before the prop system rewrite. Still compiled but should not be referenced by new code. Project-wide audit recommended to confirm no `PropBehaviourBase`-adjacent code still inherits from `Props`.
