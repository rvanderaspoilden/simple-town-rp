# ARCHITECTURE.md — High-Level System Map

## System Diagram (ASCII)

```
┌─────────────────────────────────────────────────────────────────────┐
│                        UNITY CLIENT                                  │
│                                                                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────────┐   │
│  │ Launcher │→ │MainMenu  │→ │ City.unity│  │ SubGames/        │   │
│  │          │  │ (REST    │  │(Mirror    │  │ Cook, Dream      │   │
│  │Singleton │  │ login)   │  │ online   │  │                  │   │
│  │Bootstrap │  │          │  │ scene)   │  │                  │   │
│  └──────────┘  └──────────┘  └──────┬───┘  └──────────────────┘   │
│                                      │                               │
│            ┌─────────────────────────┼────────────────────┐         │
│            │      NETWORK LAYER      │                    │         │
│            │  SimpleTownNetwork      │                    │         │
│            │  (NetworkManager sub)   │                    │         │
│            │  • OnStartServer        │                    │         │
│            │  • OnStartClient        │                    │         │
│            │  • OnClientConnect      │                    │         │
│            │  • message registration │                    │         │
│            └──────────┬──────────────┘                    │         │
│                       │                                   │         │
│     ┌─────────────────┼─────────────────┐                │         │
│     │         ROOM SYSTEM               │                │         │
│     │  BuildingBehavior                 │                │         │
│     │  └── HallController (per floor)  │                │         │
│     │       └── ApartmentController    │                │         │
│     │                                  │                │         │
│     │  PlayerRoomTracker (server)      │                │         │
│     │  ClientPropManager (client)      │                │         │
│     └─────────────────┬───────────────-┘                │         │
│                       │                                   │         │
│     ┌─────────────────┼──────────────────────────────────┘         │
│     │         PROP SYSTEM                                           │
│     │  ServerPropManager  ←→  ServerPropSource                     │
│     │  ClientPropManager  ←→  PropBehaviourBase / IPropBehaviour   │
│     │  PropInteractionRouter  →  PropInteractionDispatcher         │
│     └──────────────────────────────────────────────────────────────│
│                                                                      │
│     ┌────────────────────────────────────────────────────────────┐  │
│     │  NPC SYSTEM                                                 │  │
│     │  NpcServerManager  →  NpcAIController (NavMesh state mach) │  │
│     │  NpcSpawnManager + NpcPool                                  │  │
│     │  ClientNpcManager  →  ClientNpcView (interpolated)         │  │
│     └────────────────────────────────────────────────────────────┘  │
│                                                                      │
│     ┌────────────────────────────────────────────────────────────┐  │
│     │  ITEM SYSTEM                                                │  │
│     │  ServerItemManager  →  ClientItemManager                   │  │
│     └────────────────────────────────────────────────────────────┘  │
│                                                                      │
│     ┌────────────────────────────────────────────────────────────┐  │
│     │  JOBS / CAREER SYSTEM                                       │  │
│     │  JobServerManager  ←→  JobBoardServer (per-category gate)   │  │
│     │  JobAutoPublisher (random arrivals + cap on Available)     │  │
│     │  RewardSystem  ←→  MoneyReward / SocialCreditReward /       │  │
│     │                    JobXpReward                              │  │
│     │  JobClientManager  →  JobActiveHUD, JobBoardUI, CareerUI    │  │
│     │  Persisted: characters.current_job + character_jobs table  │  │
│     └────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
             │ TCP/IP (Mirror KCP or TCP)
             │ (Mirror network messages — gameplay)
             │
             │ HTTPS (only the Unity SERVER hits these for in-game state;
             │        clients hit auth/character endpoints at main menu)
┌─────────────────────────────────────────────────────────────────────┐
│                      NestJS BACKEND (simple-town-ws)                 │
│                                                                      │
│  Auth / catalog       /auth/login, /characters, /homes, /city       │
│  Gameplay state       /places, /props, /covers, /deliveries         │
│                       (Phase 2: read switch live — see PERSISTENCE) │
│                                                                      │
│  Supabase (PostgreSQL) — JSONB where natural, relational elsewhere  │
└─────────────────────────────────────────────────────────────────────┘
```

> **Persistence model:** see `PERSISTENCE.md` for the full schema, the
> migration phases (0 → 3), and the runtime int-propId ↔ DB-UUID bridge.
>
> **Jobs / Career model:** see `JOBS_SYSTEM.md` for the full mission framework,
> board layer, career persistence, network catalog, and end-to-end flows.

---

## Layer Descriptions

### Transport Layer
Mirror handles the underlying TCP connection. All custom messages are plain C# structs implementing `NetworkMessage`. No `NetworkBehaviour`/`SyncVar` is used for prop, NPC, or item state — Mirror is transport only for these systems. `PlayerController` is the exception: it is a `NetworkBehaviour` with a small set of `SyncVar`s for player-specific data (`rawCharacterData`, `rawCharacterHome`, `isTalking`, `_playerState`).

### Network Messages Layer
Two families of messages (see `NETWORK_FLOW.md` for full catalog):
- **C2S_*** — client sends to server (room entry, prop interactions, build/edit/remove, teleporter use, save)
- **S2C_*** — server sends to client (snapshots, prop state, room state, hall/apartment spawn, teleport, NPC, items)

Handler registration happens in `SimpleTownNetwork.OnStartServer`/`OnStartClient` and the four bootstrap classes (`PropSystemBootstrap`, `NpcSystemBootstrap`, `ItemSystemBootstrap`, `JobSystemBootstrap`).

### Room System Layer
Rooms are logical groupings identified by a string ID (see `ROOM_SYSTEM.md`). The server tracks which connection is in which room via `PlayerRoomTracker`. All prop broadcasts, NPC broadcasts, and item broadcasts are scoped to room members only. The room entry handshake (`C2S_EnterRoom` → snapshot) is the foundation for all late-join correctness.

### Prop System Layer
Two registries: `ServerPropManager` (server authority) and `ClientPropManager` (client view). Prop state is serialized as typed byte payloads (`PropPayloads.cs`). Every prop prefab carries both a `ServerPropSource` (server logic) and a `PropBehaviourBase`/`IPropBehaviour` (client visual). `PropIdentity` is the shared identity component.

### Player Layer
`PlayerController` is a `NetworkBehaviour`. The local player triggers room transitions, interactions, and build mode. Remote players are rendered with NavMesh disabled (position is synced via Mirror's default NetworkTransform on the player prefab — this is the one place NetworkTransform is legitimately used).

### UI Layer
`HUDManager` is the central UI hub. The phone is the main UI entry point for missions, shop, covers, etc. `BuildManager` owns the build/edit mode UI. All UI is non-blocking overlay.

### Jobs / Career Layer
Data-driven mission framework (`JobDefinition` SOs + step/reward sub-SOs). Server authority via `JobServerManager` (plain C# singleton, ticked by `JobServerTicker` MonoBehaviour). Per-category `JobBoardServer` gates board access by the player's `CharacterData.CurrentJobCategory`. `JobAutoPublisher` (scene MonoBehaviour) spawns offers at random intervals up to a cap on simultaneous `Available` offers. Persistence is split: `characters.current_job` (active category pointer) + dedicated `character_jobs` table (one row per career, `xp` + `started_at`). The phone Career app (`CareerUI`) sends `JobChangeCareerMessage` to apply/resign; `PlayerController.StartCareerChange` does the REST upsert + rebroadcast. See `JOBS_SYSTEM.md`.

---

## Singleton Registry with Responsibility

| Singleton | Lifecycle | Responsibility |
|---|---|---|
| `ApiManager` | DontDestroyOnLoad | All REST calls; JWT storage |
| `DatabaseManager` | DontDestroyOnLoad | ScriptableObject config loading |
| `TimeManager` | Scene-local | In-game clock |
| `HUDManager` | Scene-local (City) | UI panel management |
| `LoadingManager` | DontDestroyOnLoad | Loading screen |
| `BuildManager` | Scene-local (City) | Build/edit mode state |
| `ServerPropManager` | Plain C# (reset on server stop) | Authoritative prop store |
| `ClientPropManager` | DontDestroyOnLoad MonoBehaviour | Client prop registry + S2C handlers |
| `PlayerRoomTracker` | Plain C# (reset on server stop) | conn → roomId mapping |
| `PropInteractionDispatcher` | MonoBehaviour (City scene) | Async HTTP-requiring prop interactions |
| `ServerApartmentRegistry` | Plain C# (static) | ApartmentKey → ApartmentController |
| `NpcServerManager` | Plain C# (reset on server stop) | NPC state store + throttled broadcast |
| `NpcSpawnManager` | Plain C# (reset on server stop) | NPC pool/spawn lifecycle |
| `RoomActivityController` | Plain C# (reset on server stop) | Room player count; NPC AI gating |
| `ServerItemManager` | Plain C# (reset on server stop) | World item state + hand tracking |
| `JobServerManager` | Plain C# (reset on server stop) | Authoritative mission store + tick (board/active expirations) |
| `JobBoardServer` | Plain C# (reset on server stop) | Per-category board subscribers + career gate on OpenBoard |
| `JobClientManager` | Plain C# (cleared on client stop) | Client mission state + events for HUD |
| `JobBoardClient` | Plain C# (cleared on client stop) | Client-side board snapshot consumption |
| `JobDatabase` | Plain C# static | Loads all `JobDefinition` SOs at boot |
| `RewardSystem` | Plain C# static (subscribed via JobEvents) | Applies each `RewardDefinition` on JobCompleted |
| `JobItemCleanup` | Plain C# static (subscribed via JobEvents) | Despawns mission items on JobFailed |
| `ClientNpcManager` | DontDestroyOnLoad MonoBehaviour | Client NPC view management |
| `NpcPool` | Plain C# (disposed on server stop) | GameObject pool for NpcAIController |
| `InterestPointRegistry` | Plain C# (reset on server stop) | NPC wander targets |

---

## Scene Flow

```
Launcher.unity
│  bootstraps: ApiManager, DatabaseManager, LoadingManager (DontDestroyOnLoad)
│  loads: Main Menu.unity
│
Main Menu.unity
│  POST /auth/login → JWT stored in ApiManager.accessToken
│  GET /characters, GET /homes → character + home selection
│  SimpleTownNetwork.CharacterData populated
│  MainMenuManager.Play() → NetworkManager.singleton.StartClient()
│
City.unity (online scene, Mirror-managed)
   │  OnStartServer: handler registration, PropSystemBootstrap, NpcSystemBootstrap, ItemSystemBootstrap
   │  OnStartClient: handler registration (same bootstraps)
   │  OnClientConnect: client sends CreateCharacterMessage
   │  Server: SetupCharacterCoroutine → REST fetch → playerGo instantiated → BuildingBehavior.TeleportToApartment
   │       or → FinalizePlayerSpawn (city spawn)
   │  Client receives: UpdateCityDataMessage, then TeleportMessage (if apartment player)
   │
   ├── SubGames/Cook.unity (additive load by SubGameController)
   └── SubGames/Dream.unity (additive load by SubGameController)

On client disconnect: SceneManager.LoadScene("Main Menu")
```

---

## Key Invariants

1. `ServerPropManager._nextAutoId` starts at 10000. Scene prop IDs must be 1–9999.
2. `ApartmentController.RoomId` returns `associatedHallController.RoomId` (i.e. `hall:{street}:{floor}`), not `apt:{street}:{door}`. The per-apartment identifier for registry/state purposes is `ApartmentKey` = `apt:{street}:{door}`.
3. `PropInteractionRouter` validates that the sender's tracked room matches the claimed `RoomId` in the message before dispatching.
4. Build/edit/remove operations require the connection to own an apartment (`ServerApartmentRegistry.TryGetByConn`). Tenant identity is resolved from the player's `CharacterData.Id`.
5. `HallController.CheckGenerationState` broadcasts `S2C_ApartmentSpawn` exactly once per floor-load cycle (guarded by `_generationBroadcasted`). `ApartmentController.Regenerate()` resets this guard.
6. A player is added to Mirror's server via `NetworkServer.AddPlayerForConnection` only after the floor is fully loaded (`FinalizePlayerSpawn`). This ensures `TeleportMessage` is sent after `AddPlayerForConnection`.

---

## Technical Debt Notes

- **`Props.cs` and legacy subclasses** in `Assets/Scripts/Building/Props/` are dead code. They are compiled but no new code should reference them.
- **`SpawnItemMessage`** is marked `[Obsolete]` but kept to avoid breaking serialized scene references. It is never registered as a server handler.
- **`CharacterUpdateMoneyRequest`** is a plain `[Serializable]` struct with no Mirror handler — it cannot be sent as a network message despite its name.
- **`CreateBuildingMessage`** lives in `Network.Messages` namespace — an exception to the global-namespace rule. It has no registered handler visible in `SimpleTownNetwork.cs`.
- **`PlayerController`** still uses `SyncVar` for `rawCharacterData`, `rawCharacterHome`, `isTalking`, `_playerState`. These are intentional (player visibility), but sit uncomfortably alongside the custom-message architecture.
- **`TeleportCoroutine`** has a hardcoded `WaitForSeconds(1f)` and `WaitForSeconds(2f)` — non-configurable.
- **`Debug.Log` calls** are scattered throughout production code alongside the structured `GameLogger`/`ClientLogger` system — inconsistent logging strategy.
- **`SaveLocal()`** in `ApartmentController` writes to `Application.dataPath` — development artifact that must not run in production builds.
