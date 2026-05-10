# AI_CONTEXT.md — One-Page Cheat-Sheet for AI Assistants

## Project in One Sentence

**The Broz** is a multiplayer social RPG Unity client (Unity 6, URP) where players live in shared apartments, receive deliveries, and interact socially — all networked through Mirror with a custom room-scoped prop architecture and a NestJS backend.

---

## Tech Stack

| Component | Version / Package |
|---|---|
| Unity | 6000.4.4f1 (Unity 6) |
| Render Pipeline | URP |
| Networking | Mirror (custom TCP transport, no SyncVar on gameplay objects) |
| Voice | Dissonance + MirrorIgnorance integration |
| Camera | Cinemachine |
| NavMesh | Unity built-in NavMeshAgent |
| Animation tweening | DOTween |
| Backend | NestJS (TypeScript + MongoDB) at `http://localhost:3000` |
| Assembly | All gameplay code compiles into default `Assembly-CSharp` (no asmdef) |

---

## Golden Rules (Never Violate)

1. **No SyncVar / NetworkTransform / NetworkIdentity on gameplay prop objects.** All prop state travels through custom `NetworkMessage` structs (C2S_* and S2C_*). Mirror is transport only.
2. **Room authority via TeleportMessage.NewRoomId only.** Never call `ClientPropManager.EnterRoom()` from a `GeographicArea` trigger or any code path other than `SimpleTownNetwork.OnTeleportPlayer`.
3. **One room per floor.** The hall room `hall:{street}:{floor}` contains the hall corridor AND all apartments on that floor. `ApartmentController.RoomId` returns `hallController.RoomId`, not an apartment-specific ID.
4. **PropBehaviourBase only, never Props.cs.** The legacy `Props.cs` and its subclasses are deprecated. All new prop code uses `PropBehaviourBase` / `ServerPropSource`.
5. **Global namespace for all NetworkMessage structs.** Mirror's reflection-based handler registration breaks if messages are in a non-global namespace. Keep all `NetworkMessage` structs in the global namespace.
6. **OnClientConnect() signature must be `public override void OnClientConnect()`.** The `NetworkConnectionToClient` parameter overload does not override anything in current Mirror.
7. **No NetworkAuthenticator on the NetworkManager GameObject.** REST JWT handles auth before Mirror connects.
8. **Coroutines for HTTP, never async/await.** Mixing async/await with `UnityWebRequest` complicates lifecycle on scene unload.
9. **BuildingBehavior may be on an inactive GameObject.** Always use `FindObjectsByType<T>(FindObjectsInactive.Include, ...)` or `BuildingBehavior.TryGetBuilding()` — never `FindObjectsOfType<T>()`.
10. **Runtime prop IDs start at 10000.** Scene props use designer-assigned IDs 1–9999. Overlap is a bug.

---

## Key Singleton Map

| Class | Location | Role |
|---|---|---|
| `ApiManager` | `Assets/Scripts/Managers/ApiManager.cs` | All HTTP calls via `UnityWebRequest`; stores JWT; fires static events on responses |
| `DatabaseManager` | `Assets/Scripts/Managers/DatabaseManager.cs` | Loads ScriptableObject configs from `Resources/Configurations/` |
| `TimeManager` | `Assets/Scripts/Managers/TimeManager.cs` | In-game clock persisted via `City.last_timestamp` |
| `HUDManager` | `Assets/Scripts/Managers/HUDManager.cs` | Central access to all HUD UI panels |
| `LoadingManager` | `Assets/Scripts/Managers/LoadingManager.cs` | Loading screen during scene transitions and teleports |
| `BuildManager` | `Assets/Scripts/Managers/BuildManager.cs` | Furniture placement (build mode) in apartments |
| `SimpleTownNetwork` | `Assets/Scripts/Network/SimpleTownNetwork.cs` | NetworkManager subclass; drives handshake, teleport, building lifecycle |
| `ServerPropManager` | `Assets/Scripts/Props/Server/ServerPropManager.cs` | Server-side prop state store, snapshots, broadcasts (plain C# singleton) |
| `ClientPropManager` | `Assets/Scripts/Props/Client/ClientPropManager.cs` | Client-side prop registry, S2C message handlers (MonoBehaviour, DontDestroyOnLoad) |
| `PlayerRoomTracker` | `Assets/Scripts/Props/Server/PlayerRoomTracker.cs` | Maps server connections to current roomId (plain C# singleton) |
| `PropInteractionDispatcher` | `Assets/Scripts/Props/Server/PropInteractionDispatcher.cs` | MonoBehaviour for async prop interactions that require HTTP coroutines |
| `ServerApartmentRegistry` | `Assets/Scripts/Managers/ServerApartmentRegistry.cs` | Maps apartment key → ApartmentController; enables tenant-scoped auth |
| `NpcServerManager` | `Assets/Scripts/NPC/Server/NpcServerManager.cs` | Server-side NPC state; throttled broadcast at 10 Hz |
| `NpcSpawnManager` | `Assets/Scripts/NPC/Server/NpcSpawnManager.cs` | NPC spawn-point lifecycle, pool management |
| `RoomActivityController` | `Assets/Scripts/NPC/Server/RoomActivityController.cs` | Tracks player count per room; suspends NPC AI in empty rooms |
| `ServerItemManager` | `Assets/Scripts/Items/Server/ServerItemManager.cs` | Server-side world item state; pickup/drop/swap |

---

## Namespace Map

| Namespace | Scope |
|---|---|
| `Sim` | Core gameplay (`PlayerController`, `ApartmentController`, `ApiManager`, etc.) |
| `Sim.Building` | Building geometry components (`Wall`, `Ground`, `GeographicArea`) |
| `Sim.Entities` | Plain C# data classes mirroring MongoDB docs (`CharacterData`, `Home`, `City`, etc.) |
| `Sim.Enums` | All game enums (`PropType` is global, others are in `Sim.Enums`) |
| `Sim.Constants` | `CommonConstants`, `SceneConstants` |
| `Sim.Communication` | `BubbleUI` |
| `Sim.NPC` | NPC identity and scriptables |
| `Sim.Logging` | `GameLogger`, `ClientLogger` |
| `Sim.Utils` | `SaveUtils` |
| `Network.Messages` | Only `CreateBuildingMessage` (legacy, to be moved to global) |
| *(global)* | All `NetworkMessage` structs (C2S_*, S2C_*, etc.), `SimpleTownNetwork`, `BuildingBehavior`, `HallController`, `ApartmentController` |

---

## Where NOT to Look

- `Assets/Scenes/Test.unity`, `Assets/Scenes/Prototype.unity` — scratch scenes, no production flow
- `Assets/Scripts/Building/Props/` — legacy props (`Props.cs`, `StaticProp.cs`, `Action.cs`) — deprecated, do not extend
- `Assets/Scripts/Network/Messages/SpawnItemMessage.cs` — marked `[Obsolete]`, replaced by `C2S_AdminSpawnItem`
- `CharacterUpdateMoneyRequest` — plain C# struct with no Mirror handler registration; not a live message
- `useSpectusAccount`, `useElbloodyAccount` toggles on `SimpleTownNetwork` — dev shortcuts, never committed to scenes
- The `local` checkbox on `ApiManager` — always overrides `uri` to localhost in `Awake()`; must be un-ticked for remote
