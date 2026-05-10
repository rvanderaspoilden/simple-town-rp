# NETWORK_FLOW.md — All Network Flows

---

## C2S Message Catalog (Client → Server)

All C2S messages are plain C# structs implementing `NetworkMessage`. All are in the global namespace.

### Connection & Character

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `CreateCharacterMessage` | `userId: string`, `characterId: string` | `SimpleTownNetwork.OnClientConnect()` on the local client | `SetupCharacterCoroutine`: REST fetch character + home, instantiate player GO, route to `BuildingBehavior.TeleportToApartment` or `FinalizePlayerSpawn` |

### Shop / Delivery

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `CreateDeliveryRequest` | `recipientId: string`, `type: DeliveryType`, `paintConfigId: int`, `propsConfigId: int`, `color: float[]`, `propsPresetId: int` | `ShopUI` (client) | `CreateDeliveryCoroutine`: POST to backend, sends `ShopResponseMessage` back; if success and recipient has delivery box, refreshes delivery count |

### Teleport (legacy C2S path)

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `TeleportMessage` (C2S) | `destination: Vector3`, `NewRoomId: string` | Client sending a teleport request | Server echoes back the same `TeleportMessage` to the client. `OnPlayerTeleportTo` just calls `conn.Send(request)` — the server does not mutate the message. |

> Note: This C2S path is unusual. Most teleports are server-initiated (hall/apartment teleports send `TeleportMessage` directly from server to client).

### Room Management

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `C2S_EnterRoom` | `RoomId: string` | `ClientPropManager.EnterRoom()` | `Server_OnEnterRoom`: `PlayerRoomTracker.EnterRoom(conn, roomId)` then `ServerPropManager.SendRoomSnapshot(conn, roomId)` |
| `C2S_LeaveRoom` | `RoomId: string` | `ClientPropManager.EnterRoom()` (sent before switching rooms) | `Server_OnLeaveRoom`: `PlayerRoomTracker.LeaveRoom(conn)` |

### Prop Interactions

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `C2S_PropInteraction` | `PropId: int`, `RoomId: string`, `Type: PropType`, `Payload: byte[]` | `ClientPropManager.RequestInteraction()` | `Server_OnInteraction`: validates room match, routes to `PropInteractionRouter.Route()` |
| `C2S_BuildProp` | `RoomId: string`, `DeliveryBoxPropId: int`, `DeliveryId: string`, `PropConfigId: int`, `PresetId: int`, `Position: Vector3`, `Rotation: Quaternion`, `PaintConfigId: int`, `ColorR/G/B: float` | `BuildManager` (client) | `Server_OnBuildProp`: validates ownership, calls `PropInteractionDispatcher.BuildProp()` |
| `C2S_EditProp` | `RoomId: string`, `PropId: int`, `Position: Vector3`, `Rotation: Quaternion` | `BuildManager` (client) | `Server_OnEditProp`: validates ownership, calls `ServerPropManager.UpdatePropTransform()` + saves apartment |
| `C2S_RemoveProp` | `RoomId: string`, `PropId: int` | `PlayerInteraction` (via `PropBehaviourBase.OnSellRequest`) | `Server_OnRemoveProp`: validates ownership, calls `ServerPropManager.RemoveProp()` + saves apartment |
| `C2S_TeleporterUse` | `FloorDestination: int` | `TeleporterBehaviour` (client) | `Server_OnTeleporterUse`: routes via `PlayerRoomTracker.GetRoom(conn)` → `TeleporterBehaviour.TryGetByRoom` → `ServerHandleUse()` |
| `C2S_SaveApartment` | `RoomId: string` | `ApartmentController` (client) | `PropInteractionDispatcher.HandleSaveApartment()` → `apt.Save()` coroutine |
| `C2S_ApplyWallCovers` | `RoomId: string`, `CoversJson: byte[]` (UTF-8 JSON of `CoverDataWrapper`) | `ApartmentController.ApplyWallSettings()` | `PropInteractionDispatcher.HandleApplyWallCovers()` → `apt.ServerApplyWallCovers()` + save |
| `C2S_ApplyGroundCovers` | `RoomId: string`, `CoversJson: byte[]` | `ApartmentController.ApplyGroundSettings()` | `PropInteractionDispatcher.HandleApplyGroundCovers()` → `apt.ServerApplyGroundCovers()` + save |

### Item System

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `C2S_RequestPickupItem` | `EntityId: int` | `PlayerInteraction` (client) | `ServerItemManager.HandlePickup()`: distance check, hand assignment, broadcast `S2C_ItemAttachedToHand` |
| `C2S_RequestDropItem` | `Hand: HandType` | `PlayerHands` (client) | `ServerItemManager.HandleDrop()`: drop at player position, broadcast `S2C_ItemDetachedFromHand` |
| `C2S_RequestSwapHands` | *(empty)* | `PlayerHands` (client) | `ServerItemManager.HandleSwap()`: swap left/right, broadcast `S2C_ItemAttachedToHand` twice |
| `C2S_AdminSpawnItem` | `ItemConfigId: int`, `Position: Vector3` | Debug/admin UI | `ServerItemManager.HandleAdminSpawn()`: `SpawnItem()` in player's current room |

### Player Commands (Mirror Commands — not custom messages)

| Command | Server Action |
|---|---|
| `CmdSetTalk(bool)` | Sets `isTalking` SyncVar (drives `BubbleUI`) |
| `CmdConsumeItem(int entityId)` | Validates item, applies `ConsumableConfig` health effects, calls `ServerItemManager.DespawnItem()`, fires `RpcConsume()` |

---

## S2C Message Catalog (Server → Client)

### Connection & City

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `UpdateCityDataMessage` | `City: City`, `ShouldHideLoading: bool` | One connection (new player) | `OnCityDataUpdatedResponse`: store city data, update `TimeManager.StartTimestamp`; if `ShouldHideLoading` then hide loading screen |
| `ShopResponseMessage` | `isSuccess: bool` | One connection (shop buyer) | `ShopUI.Instance.OnBuyResponse()` |
| `NotificationMessage` | `code: NotificationCode` (e.g. `ITEM_DESTROYED`) | One connection | Triggers inventory UI update |

### Teleport

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `TeleportMessage` | `destination: Vector3`, `NewRoomId: string` | One connection | `OnTeleportPlayer`: if `NewRoomId` non-empty → `ClientPropManager.EnterRoom(NewRoomId)`; then `TeleportCoroutine(destination)` (show loading, wait 1s, teleport, wait 2s, hide loading) |

### Hall / Apartment Spawn

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `S2C_HallSpawn` | `Street: string`, `FloorNumber: int`, `Position: Vector3` | All clients | `OnHallSpawn`: `BuildingBehavior.OnClientHallSpawn()` — instantiates `HallController` prefab (client-only) or registers existing (host mode) |
| `S2C_HallDespawn` | `Street: string`, `FloorNumber: int` | All clients | `OnHallDespawn`: destroys client hall + removes from registry |
| `S2C_ApartmentSpawn` | `Street: string`, `FloorNumber: int`, `DoorNumber: int`, `PresetName: string`, `Position: Vector3`, `Rotation: Quaternion` | All clients | `OnApartmentSpawn`: `HallController.ClientSpawnApartment()` — instantiates apartment or applies `ClientSetup` in host mode |

### Prop System

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `S2C_RoomSnapshot` | `RoomId: string`, `PropCount: int` | One connection | `OnRoomSnapshot`: log only; signals start of prop snapshot sequence |
| `S2C_PropSpawn` | `PropId: int`, `PrefabId: int`, `RoomId: string`, `Position: Vector3`, `Rotation: Quaternion`, `Type: PropType`, `Payload: byte[]` | One or all in room | `OnPropSpawn`: instantiate prefab, assign `PropIdentity`, call `IPropBehaviour.ApplyState()`, index in `_props` |
| `S2C_PropUpdate` | `PropId: int`, `RoomId: string`, `Type: PropType`, `Payload: byte[]` | One or all in room | `OnPropUpdate`: call `IPropBehaviour.ApplyState()` on existing prop |
| `S2C_PropTransform` | `PropId: int`, `RoomId: string`, `Position: Vector3`, `Rotation: Quaternion` | All in room | `OnPropTransform`: move the spawned GO to new position/rotation |
| `S2C_PropRemove` | `PropId: int`, `RoomId: string` | All in room | `OnPropRemove`: destroy local GO (if runtime-spawned), remove from index |
| `S2C_RoomState` | `RoomId: string`, `Payload: byte[]` | One or all in room | `OnRoomState`: fires `ClientPropManager.OnRoomStateReceived` event; `ApartmentController` subscribes to apply `ApartmentRoomState` (covers, tenantId, preset) |
| `S2C_BuildAck` | `Success: bool` | One connection (builder) | Fires `ClientPropManager.OnBuildAckReceived` event; `BuildManager` exits build mode |
| `S2C_DeliveryBoxOpened` | `PropId: int`, `RoomId: string`, `Deliveries: Delivery[]` | One connection | `DeliveryBoxBehaviour.OnDeliveryBoxOpened()` — shows delivery contents UI |
| `S2C_DispenserPurchaseResult` | `PropId: int`, `Success: bool`, `ItemId: int` | One connection | `DispenserBehaviour.HandlePurchaseResult()` |

### NPC System

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `S2C_SpawnNpc` | `NpcId: int`, `PrefabId: string`, `RoomId: string`, `Position: Vector3`, `Rotation: Quaternion`, `StyleJson: string`, `FirstName: string`, `LastName: string`, `Mood: byte` | All in room | `ClientNpcManager.OnSpawnNpc()`: instantiate prefab, `ClientNpcView.Init()` |
| `S2C_UpdateNpcTransform` | `NpcId: int`, `RoomId: string`, `Position: Vector3`, `Rotation: Quaternion`, `Velocity: Vector3`, `State: NpcStateType` | All in room | `ClientNpcManager.OnUpdateTransform()`: `ClientNpcView.PushSnapshot()` (interpolated) |
| `S2C_DestroyNpc` | `NpcId: int`, `RoomId: string` | All in room | `ClientNpcManager.OnDestroyNpc()`: destroy local NPC GO |

### Item System

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `S2C_SpawnItem` | `EntityId: int`, `RoomId: string`, `ItemConfigId: int`, `Position: Vector3`, `Rotation: Quaternion`, `IsHeld: bool`, `HolderNetId: uint`, `HolderHand: HandType`, `LocalPosition: Vector3`, `LocalRotation: Quaternion` | All in room | `ClientItemManager`: instantiate item prefab; if `IsHeld` attach to holder's hand |
| `S2C_DestroyItem` | `EntityId: int`, `RoomId: string` | All in room | `ClientItemManager`: destroy item GO |
| `S2C_PickupResult` | `Success: bool`, `EntityId: int`, `ErrorMessage: string` | One connection (requester) | `ClientItemManager`: confirm/reject pickup |
| `S2C_ItemAttachedToHand` | `EntityId: int`, `PlayerNetId: uint`, `HandType: HandType`, `LocalPosition: Vector3`, `LocalRotation: Quaternion` | All in room | Attach item GO to specified player's hand |
| `S2C_ItemDetachedFromHand` | `EntityId: int`, `WorldPosition: Vector3`, `WorldRotation: Quaternion` | All in room | Detach item from hand, drop at world position |
| `S2C_DropResult` | `Success: bool`, `Hand: HandType`, `ErrorMessage: string` | One connection | Confirm/reject drop |

---

## Connection Handshake Sequence (Step by Step)

```
CLIENT                                   SERVER
  │                                         │
  │── TCP connect ──────────────────────>   │  Mirror TCP handshake
  │                                         │
  │  [OnClientConnect() fires]              │
  │── CreateCharacterMessage ──────────>    │
  │   { userId, characterId }               │  OnCreateCharacter handler
  │                                         │  SetupCharacterCoroutine starts:
  │                                         │  1. REST GET /characters?userId=...
  │                                         │  2. REST GET /homes?characterId=...
  │                                         │  3. Instantiate playerPrefab (NOT yet spawned)
  │                                         │  4. player.SetRawCharacterData(json)
  │                                         │
  │                          if home found  │
  │                                         │  BuildingBehavior.TeleportToApartment()
  │                                         │  HallController.Init() (if floor not loaded)
  │                                         │  ApartmentController.Init() per door
  │                                         │  ApartmentController.RetrieveData() (REST)
  │                                         │
  │<── S2C_HallSpawn ───────────────────   │  All clients
  │<── S2C_ApartmentSpawn (per apt) ────   │  All clients (once floor ready)
  │                                         │
  │                                         │  FinalizePlayerSpawn():
  │                                         │  NetworkServer.AddPlayerForConnection()
  │<── UpdateCityDataMessage ───────────   │  { City, ShouldHideLoading=false }
  │<── TeleportMessage ─────────────────   │  { destination=apt.SpawnPosition, NewRoomId=hall:street:floor }
  │                                         │
  │  [OnTeleportPlayer fires]               │
  │── C2S_EnterRoom ───────────────────>   │  { RoomId="hall:street:floor" }
  │                                         │  PlayerRoomTracker.EnterRoom()
  │                                         │  ServerPropManager.SendRoomSnapshot()
  │<── S2C_RoomSnapshot ────────────────   │  { RoomId, PropCount }
  │<── S2C_PropSpawn/Update (×N) ───────   │  per prop in room
  │<── S2C_RoomState (per apt) ─────────   │  ApartmentRoomState payloads
  │<── S2C_SpawnNpc (×M) ───────────────   │  NPC snapshot from NpcServerManager
  │<── S2C_SpawnItem (×K) ──────────────   │  Item snapshot from ServerItemManager
  │                                         │
  │  [TeleportCoroutine: hide loading]      │
  │                                         │
  │             if no home found:           │
  │                                         │  FinalizePlayerSpawn(spawnInCity=true)
  │<── UpdateCityDataMessage ───────────   │  { ShouldHideLoading=true }
  │  [OnStartLocalPlayer fires]             │
  │── C2S_EnterRoom { "city" } ────────>   │
```

---

## Teleport Flow Sequence (City → Hall)

```
CLIENT                                   SERVER
  │                                         │
  │  [Player uses main elevator]            │
  │── C2S_TeleporterUse ───────────────>   │  { FloorDestination: int }
  │                                         │  PropInteractionDispatcher.HandleTeleporterUse()
  │                                         │  TeleporterBehaviour.ServerHandleUse()
  │                                         │  BuildingBehavior.TeleportToFloor()
  │                                         │  HallController created if needed
  │<── S2C_HallSpawn ───────────────────   │  All clients
  │<── S2C_ApartmentSpawn (per apt) ────   │  All clients (once floor generated)
  │                                         │
  │                                         │  HallController.MoveToSpawn(conn)
  │<── TeleportMessage ─────────────────   │  { destination=elevator spawn, NewRoomId="hall:street:floor" }
  │                                         │
  │  [OnTeleportPlayer fires]               │
  │── C2S_LeaveRoom { "city" } ────────>   │  (sent inside EnterRoom before switching)
  │── C2S_EnterRoom { "hall:..." } ─────>  │
  │<── snapshot (props + NPC + items) ──   │
```

---

## Teleport Flow Sequence (Hall → City, elevator back)

```
CLIENT                                   SERVER
  │                                         │
  │  [Player uses floor elevator, dest=0]   │
  │── C2S_TeleporterUse ───────────────>   │  { FloorDestination: 0 }
  │                                         │  BuildingBehavior.TeleportToFloor()
  │                                         │  targetFloor == 0 → city spawn
  │                                         │  HallController.RemovePlayer(conn.identity)
  │                                         │  TryToCleanHall() → if empty:
  │<── S2C_HallDespawn ─────────────────   │  All clients
  │<── TeleportMessage ─────────────────   │  { destination=mainElevator.SpawnTransform, NewRoomId="city" }
  │── C2S_LeaveRoom { "hall:..." } ─────>  │
  │── C2S_EnterRoom { "city" } ────────>   │
  │<── city snapshot ───────────────────   │
```

---

## Prop Build Flow Sequence

```
CLIENT (tenant)                          SERVER
  │                                         │
  │  [Player opens delivery box]            │
  │── C2S_PropInteraction ─────────────>   │  { PropId, RoomId, Type=DeliveryBox, Payload=OpenRequest }
  │                                         │  PropInteractionDispatcher.OpenDeliveryBox()
  │                                         │  REST GET /deliveries?characterId=...
  │<── S2C_DeliveryBoxOpened ───────────   │  { PropId, RoomId, Deliveries[] }
  │                                         │
  │  [BuildManager enters build mode]       │
  │  [Player places prop preview]           │
  │── C2S_BuildProp ───────────────────>   │  { RoomId, DeliveryBoxPropId, DeliveryId,
  │                                         │    PropConfigId, PresetId, Position, Rotation,
  │                                         │    PaintConfigId, ColorR/G/B }
  │                                         │
  │                                         │  PropInteractionDispatcher.BuildPropCoroutine():
  │                                         │  1. REST DELETE /deliveries/{DeliveryId}
  │                                         │  2. Build initial payload (header + paint if bucket)
  │                                         │  3. ServerPropManager.SpawnProp() → assigns propId
  │<── S2C_PropSpawn ───────────────────   │  (broadcast to all in room)
  │                                         │  4. apt.TrackProp(newPropId)
  │                                         │  5. RefreshDeliveryBoxCount() (REST fetch + S2C_PropUpdate)
  │<── S2C_PropUpdate (delivery box) ──    │
  │                                         │  6. apt.Save() (REST PUT /homes/{id})
  │<── S2C_BuildAck { Success=true } ──    │  (to requesting connection only)
  │                                         │
  │  [BuildManager exits build mode]        │
```

---

## Prop Edit Flow Sequence

```
CLIENT (tenant)                          SERVER
  │                                         │
  │  [BuildManager enters MOVING_PROPS]     │
  │  [Player drags prop to new position]    │
  │── C2S_EditProp ────────────────────>   │  { RoomId, PropId, Position, Rotation }
  │                                         │
  │                                         │  PropInteractionRouter.HandleEditProp():
  │                                         │  1. TryGetByConn → verify tenant
  │                                         │  2. apt.OwnsProp(PropId) → verify ownership
  │                                         │  3. ServerPropManager.UpdatePropTransform()
  │<── S2C_PropTransform ───────────────   │  (broadcast to all in room)
  │                                         │  4. apt.Save()
```

---

## Room Enter/Leave Flow Sequence

```
CLIENT                                   SERVER
  │                                         │
  │  ClientPropManager.EnterRoom(newRoomId) │
  │  ├─ if currentRoom != null:             │
  │  │  └─ NetworkClient.Send(C2S_LeaveRoom)│
  │  │     PlayerRoomTracker.LeaveRoom(conn)│
  │  ├─ ClearProps() (destroy runtime GOs)  │
  │  ├─ IndexSceneProps(newRoomId)          │
  │  └─ NetworkClient.Send(C2S_EnterRoom)   │
  │── C2S_EnterRoom ───────────────────>   │
  │                                         │  PlayerRoomTracker.EnterRoom(conn, roomId)
  │                                         │  → fires OnPlayerEnterRoom event
  │                                         │    ├─ NpcServerManager.SendRoomSnapshot(conn, roomId)
  │                                         │    └─ ServerItemManager.OnPlayerEnterRoom(conn, roomId)
  │                                         │  ServerPropManager.SendRoomSnapshot(conn, roomId)
  │<── S2C_RoomSnapshot ────────────────   │  { PropCount }
  │<── S2C_PropUpdate (scene props) ────   │  ×N (isScene=true → S2C_PropUpdate)
  │<── S2C_PropSpawn (runtime props) ───   │  ×M (isScene=false → S2C_PropSpawn)
  │<── S2C_RoomState (room state) ──────   │  (if any stored)
  │<── S2C_RoomState (per apt state) ───   │  (for each apartment in room)
  │<── S2C_SpawnNpc ×K ─────────────────   │  NPC snapshot
  │<── S2C_UpdateNpcTransform ×K ───────   │  NPC initial transforms
  │<── S2C_SpawnItem ×J ────────────────   │  Item snapshot
```
