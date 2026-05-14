# JOBS_SYSTEM.md — Jobs, Missions, Career & Board Architecture

The Jobs system is a data-driven mission framework with a persisted **career**
layer on top. A player chooses one job category at a time (Delivery, Cleaning,
…), earns XP on that career, and only sees the missions of the job they're
actually employed in. Mirror is used as a pure transport — there is no
`NetworkBehaviour` or `SyncVar` on any job / mission / board object.

Cross-refs: `ARCHITECTURE.md` (system map), `NETWORK_FLOW.md` (Mirror
messages), `PERSISTENCE.md` (Supabase schema).

---

## High-Level Map

```
                          ┌────────────────────────────┐
                          │   JobDefinition (SO)        │  Recipe: steps,
                          │  + JobStepDefinition[]      │  rewards, expiration,
                          │  + RewardDefinition[]       │  category, maxConc.
                          └─────────────┬──────────────┘
                                        │ Publish / Offer
                                        ▼
┌──────────────────────┐   broadcast    ┌──────────────────────┐
│ JobAutoPublisher (MB) │──────────────▶│ JobServerManager      │
│  random spawns        │               │  byInstanceId         │
│  cap on Available     │               │  byOwner              │
└──────────────────────┘                │  Tick(dt) → expire    │
                                        │  Active / Available   │
                                        └────┬─────────────┬────┘
                                             │             │
                              ┌──────────────┘             └───────────────┐
                              ▼                                            ▼
                   ┌───────────────────┐                        ┌──────────────────┐
                   │ JobBoardServer    │  category subscribers   │ RewardSystem     │
                   │ category filter   │◀──────── events ──────▶│ MoneyReward,     │
                   │ + career gate     │                        │ JobXpReward, etc.│
                   └──────────┬────────┘                        └──────────────────┘
                              │ snapshots                                   │
                              ▼                                             │
                   ┌─────────────────────┐                                  ▼
                   │ JobBoardClient (UI) │                       ┌──────────────────┐
                   │  JobBoardUI panel   │                       │ CharacterJob row │
                   │  per-category rows  │                       │ xp += amount     │
                   └─────────────────────┘                       └──────────────────┘
                              ▲
                              │ owner-only messages
                              │
                   ┌──────────┴───────────┐
                   │  JobClientManager    │  Singleton client + UI events
                   │  JobActiveHUD        │  step / target / distance
                   └──────────────────────┘
```

---

## Data Model

### `JobDefinition` (ScriptableObject, `Sim.Jobs.JobDefinition`)
File: `Assets/Scripts/Jobs/Core/JobDefinition.cs`. Loaded at boot by
`JobDatabase` from `Resources/Configurations/Jobs/Definitions`.

| Field | Type | Meaning |
|---|---|---|
| `jobId` | `string` | Stable network/persistence identifier |
| `displayNameKey` | `string` | UI label (used for HUD + toast titles) |
| `icon` | `Sprite` | Optional UI sprite |
| `category` | `JobCategory` | Job family: `Delivery`, `Cleaning`, `Repair`, `Gardening`, `Concierge`, `Music`, `Custom` |
| `steps` | `JobStepDefinition[]` | Sequence of step SOs (`ReachTarget`, `DeliverToTarget`, `UseMachine`, …) |
| `rewards` | `RewardDefinition[]` | Additive on `JobCompleted` (`MoneyReward`, `SocialCreditReward`, `JobXpReward`) |
| `expirationSeconds` | `float` | Max duration once **Active**. 0 = no expiration |
| `boardExpirationSeconds` | `float` | Max display time on the board while **Available**. 0 = no expiration |
| `maxConcurrentPerPlayer` | `int` | Anti-spam cap (kept at 1 — one mission at a time) |
| `salaryAmount` | `int` | Periodic salary paid to every player whose `CurrentJobCategory == category`. Resolved via `JobDatabase.GetSalaryForCategory` (first matching definition wins). The interval comes from `cities.salary_period_seconds` |

### `JobInstance` (runtime, server only)
File: `Assets/Scripts/Jobs/Core/JobInstance.cs`. Pure C#, lives in
`JobServerManager._byInstanceId`. Status machine:

```
   Publish ──▶ Available ──Take──▶ Active ──Complete──▶ Completed
                  │                  │   │
                  │                  │   └─ Fail / Abandon / Expire ─▶ Failed/Abandoned/Expired
                  │                  ▼
                  ▼                  StepInstance.Tick
                  Tick (board)
                  ElapsedSeconds → if > BoardExpirationSeconds → Expired
```

Key methods: `CreateOffer`, `CreatePublished`, `Accept`, `Take(netId)`,
`Tick(dt)`, `AdvanceStep`, `Fail`, `Abandon`, `OwnerDisconnected`.
`Take`/`Accept` reset `ElapsedSeconds` so the offer-expiration timer and the
active-mission-expiration timer don't conflate.

### `JobContext` (server only)
Blackboard shared between steps. Holds `Dictionary<string, IJobTarget>` (keys
match the `JobTargetKey` enum: `Pickup`, `Delivery`, `City`, `Trash`, `Cart`)
plus a generic `Dictionary<string, object>`.

### `JobStepDefinition` / `JobStepInstance`
Each step SO creates a per-mission `JobStepInstance` (POCO with `Tick`,
`OnEnter`, `OnExit`, `MarkRunning`, `Succeed`, `Fail`). Step types shipped:

| Step | File | Behaviour |
|---|---|---|
| `ReachTarget` | `Steps/ReachTarget/` | Player must come within proximity of `targetKey` target |
| `DeliverToTarget` | `Steps/DeliverToTarget/` | Proximity + N-second handover + verify owner still holds payload |
| `PickupPackage` | `Steps/PickupPackage/` | Legacy — spawn auto au sol (preserved, rarely used) |
| `UseMachine` | `Steps/UseMachine/` | Wait for interaction with a `PackagingMachineBehaviour`, spawn item directly in hand |

### `JobPoint` (scene MonoBehaviour, `Sim.Jobs.JobPoint`)
File: `Assets/Scripts/Jobs/Targets/JobPoint.cs`. A static map target. Each
instance has `pointId`, `category`, `role` (enum `PointRole`: Pickup, Delivery,
Trash, Cart, City, Any), `displayName`, and an `indicator` GameObject. Filters
auto-publisher draws via `MatchesRole`.

### `JobCategoryLabels` (`Sim.Jobs.JobCategoryLabels`)
File: `Assets/Scripts/Jobs/Core/JobCategoryLabels.cs`. Centralised FR labels
(`Display`, `DisplayUpper`) — used by HUD, phone Career app, toast strings.

---

## Career Layer (player-side persistence)

### `CharacterData` extensions
File: `Assets/Scripts/Entities/CharacterData.cs`.

| New field | Type | Meaning |
|---|---|---|
| `currentJob` | `int` (`-1` = unemployed) | Active job; mirrors backend `characters.current_job` (NULL on DB side maps to `-1` client-side) |
| `jobs` | `List<CharacterJobData>` | One row per career the player has ever applied to |

Helpers: `JobCategory? CurrentJobCategory`, `CharacterJobData GetJob(JobCategory)`.
The whole CharacterData is broadcast to all clients via the existing
`PlayerController.rawCharacterData` SyncVar (hook `ParseCharacterData` →
event `OnCharacterDataChanged`). No new SyncVar.

### `CharacterJobData` (`Sim.Entities.CharacterJobData`)
File: `Assets/Scripts/Entities/CharacterJobData.cs`. Mirror of the backend
`character_jobs` row.

| Field | Type |
|---|---|
| `_id` | UUID |
| `character_id` | UUID |
| `category` | `int` (JobCategory) |
| `xp` | `int` |
| `started_at` | ISO date string (first application date — preserved across resign/re-apply) |

Future columns (`level`, `last_promoted_at`, `missions_completed`, …) land here
without touching the rest of the system.

### Career change & XP gain (server-side)
Implemented on `PlayerController` (`Assets/Scripts/Player/PlayerController.cs`):

```csharp
[Server] StartCareerChange(int newJob)
  └─ CareerChangeCoroutine
        1. JobServerManager.Instance.OnPlayerDisconnected(netId)  // abandons active mission
        2. if newJob >= 0:
              POST /character-jobs/start  → CharacterJob row (idempotent upsert)
              MergeJob(row)               // merge into CharacterData.jobs
        3. PUT /characters/:id/update-current-job   { currentJob: newJob }
        4. characterData.CurrentJobRaw = newJob
        5. SetRawCharacterData(JsonUtility.ToJson(characterData))  // SyncVar rebroadcast

[Server] AddJobXp(int category, int delta)
  └─ Local merge (creates row if missing) + xp += delta
     SetRawCharacterData(...)
     PUT /character-jobs/add-xp { characterId, category, delta }
```

### `JobXpReward` SO (`Sim.Jobs.JobXpReward`)
File: `Assets/Scripts/Jobs/Rewards/JobXpReward.cs`. Drag into a
`JobDefinition.rewards` list. On `Apply(job)`:

1. Resolves owner via `NetworkServer.spawned[OwnerNetId]`.
2. Calls `player.AddJobXp((int)job.Definition.Category, amount)`.
3. Sends a `JobNotificationMessage` to the owner: `"+{amount} XP {Label}"`.

### Periodic income (`PlayerCareerSalaryTicker`)
File: `Assets/Scripts/Jobs/Runtime/PlayerCareerSalaryTicker.cs`. Server-only
MonoBehaviour, owned by `JobSystemBootstrap` (created on `OnServerStart` as a
second component on the `JobServerTicker` GameObject, destroyed on
`OnServerStop`).

**Model**
- Per-job amount: `JobDefinition.salaryAmount` (SO field). Resolved through
  `JobDatabase.GetSalaryForCategory(JobCategory)` — first definition matching
  the category wins. Returns 0 if no definition exists for that category.
- Unemployment fallback: `cities.unemployed_income` (DB column, pre-existing,
  default 0). Paid to any online player whose `CurrentJobCategory` is null.
- Global period: `cities.salary_period_seconds` (DB column, default 600s),
  read via `SimpleTownNetwork.singleton.CityData.SalaryPeriodSeconds`. Cached
  inside the ticker to survive transient zero reads while the city is being
  re-fetched.

**Behaviour (POC)**
- One global accumulator. Every `period` seconds, iterate
  `NetworkServer.connections.Values`. For each `PlayerController` :
  - If `CurrentJobCategory.HasValue` → `amount = JobDatabase.GetSalaryForCategory(category)`,
    toast label `"Salaire {Métier}"`.
  - Otherwise → `amount = CityData.UnemployedIncome`, toast label
    `"Allocation chômage"`.
  - If `amount > 0` → `PlayerBankAccount.GiveMoney(amount)` (round-trips
    through `PUT /characters/:id/update-money`) + `ToastNotificationMessage`
    (type `BANK`) `"{label} : +{amount} €"`.
- **Online-only**: a disconnected player accrues nothing while offline.
  Reconnecting starts a fresh cycle from the next global tick.
- Money is **created** — no debit from `cities.money`. Future hook : add a
  city budget step before paying.

**Future variant** — per-player horloge : add `character_jobs.last_payout_at`
TIMESTAMPTZ, compute `floor((now - last_payout_at) / period)` outstanding
payments at login + on each tick. Strictly fairer (the player's clock starts
at first employment), and supports offline accrual. Roughly 30 min of code on
top of the POC.

---

## Board Layer

### `JobBoard` (scene MonoBehaviour, `Sim.Jobs.JobBoard`)
File: `Assets/Scripts/Jobs/Board/JobBoard.cs`. Posté en scène avec un collider.
`IInteractable` exposing action `OPEN`. Fields: `category`, `boardTitle`.

**Client-side career gate** (`Open()` lines 57-71): if
`PlayerController.Local.CharacterData.CurrentJobCategory != this.category`,
plays a local toast "Tu n'es pas employé pour ce métier." and short-circuits
before sending `JobBoardOpenMessage`. The server enforces the same check (see
below) — the client pre-check is purely a UX shortcut.

### `JobBoardServer` (plain C# singleton)
File: `Assets/Scripts/Jobs/Runtime/JobBoardServer.cs`. Maintains a per-category
set of subscribers. Subscribes to `JobEvents.JobPublished/JobTaken/StepAdvanced/
JobCompleted/JobFailed` and rebroadcasts a snapshot to relevant connections on
every change.

**Server-side career gate** (`OpenBoard`): resolves the
`PlayerController.CharacterData.CurrentJobCategory` from the sender's
`NetworkConnectionToClient.identity`. If it doesn't match the requested
category → reject + `JobNotificationMessage` toast. Log:
`JobBoardOpenDenied_WrongJob`.

`BuildSnapshot(category)` filters `JobServerManager.Active` to entries whose
status is `Available` or `Active`. Expired/Completed/Failed jobs drop out
automatically.

### `JobAutoPublisher` (scene MonoBehaviour)
File: `Assets/Scripts/Jobs/Providers/JobAutoPublisher.cs`. Server-only,
spawns offers on the board at random intervals.

Inspector fields:

| Field | Default | Meaning |
|---|---|---|
| `jobDefinitions` | — | Pool of `JobDefinition` SOs (uniform random pick) |
| `minInterval` | 30s | Min delay between arrivals |
| `maxInterval` | 90s | Max delay between arrivals |
| `maxAvailableOffers` | 5 | Hard cap on `Available` offers (all categories) |
| `startDelay` | 10s | Initial offset before first spawn |
| `retryInterval` | 5s | Re-check delay when cap reached |

Tirage : un pickup point (Role=Pickup) + un delivery point (Role=Delivery)
parmi les `JobPoint` matching the definition's category.

### `JobsDebugProvider` (scene MonoBehaviour)
File: `Assets/Scripts/Jobs/Providers/JobsDebugProvider.cs`. `F10` →
publish-on-board (manual debug). The legacy F9 binding (direct Offer) has been
removed — only Publish remains.

---

## Network Messages

All `NetworkMessage` structs live in the global namespace per the
Mirror reflection requirement.

### C2S (Client → Server)

| Message | Fields | Sender | Server Action |
|---|---|---|---|
| `JobAcceptedMessage` | `instanceId: string` | `JobClientManager` (HUD Accept button) | `JobServerManager.Accept(...)` |
| `JobAbandonRequestMessage` | `instanceId: string` | HUD Abandon button | `JobServerManager.Abandon(...)` |
| `JobBoardOpenMessage` | `category: JobCategory` | `JobBoardClient` (UI open) | `JobBoardServer.OpenBoard` (with career gate) |
| `JobBoardCloseMessage` | `category: JobCategory` | UI close | `JobBoardServer.CloseBoard` |
| `JobBoardTakeMessage` | `instanceId: string` | Board entry "Prendre" click | `JobServerManager.TakeFromBoard(...)` (with career gate) |
| `JobUseMachineMessage` | `machineId: string` | `PackagingMachineBehaviour` (USE action) | `UseMachineStepInstance.TryUseMachineFor` |
| `JobChangeCareerMessage` | `newJob: int` (-1 = resign) | Phone Career app (Postuler / Démissionner) | `PlayerController.StartCareerChange(newJob)` |

### S2C (Server → Client)

| Message | Fields | Recipient | Client Action |
|---|---|---|---|
| `JobOfferedMessage` | `instanceId, jobId, statusByte, currentStepIndex, currentPromptKey, currentTargetId, currentTargetName, primaryTarget*, secondaryTarget*, payloadItemId` | One owner | `JobClientManager.HandleOffered` → updates HUD + fires `OnJobOffered` |
| `JobStepAdvancedMessage` | `instanceId, newStepIndex, promptKey, currentTargetId, currentTargetName` | One owner | `JobClientManager.HandleStepAdvanced` |
| `JobFinishedMessage` | `instanceId, terminalStatus, failureReason` | One owner | `JobClientManager.HandleFinished` |
| `JobBoardSnapshotMessage` | `categoryByte, entries: JobBoardEntry[]` | Per-category subscribers | `JobBoardClient.OnSnapshot` → rebuilds the board UI |
| `JobRewardNotificationMessage` | `amount, label` | One owner | `NotificationManager` BANK toast (e.g. "+25 €") |
| `JobNotificationMessage` | `text: string` | One owner / broadcast | `NotificationManager` JOB toast |
| `ToastNotificationMessage` | `text, typeByte` | One owner | Generic toast (used by gating + shop refusal flows) |

---

## Bootstrap & Lifecycle

`JobSystemBootstrap` (`Assets/Scripts/Jobs/Runtime/JobSystemBootstrap.cs`) is
called from `SimpleTownNetwork.OnStartServer/OnStopServer` and
`OnStartClient/OnStopClient`.

**Server start**:
1. `JobDatabase.Load()` — loads all `JobDefinition` SOs from Resources.
2. `JobServerManager.Subscribe()`, `JobBoardServer.Subscribe()`,
   `RewardSystem.Subscribe()`, `JobItemCleanup.Subscribe()`.
3. Register handlers: `JobAcceptedMessage`, `JobAbandonRequestMessage`,
   `JobBoardOpenMessage`, `JobBoardCloseMessage`, `JobBoardTakeMessage`,
   `JobUseMachineMessage`, **`JobChangeCareerMessage`**.
4. Create `JobServerTicker` GameObject (`DontDestroyOnLoad`) → ticks
   `JobServerManager.Tick(deltaTime)` at frame rate.

**Server stop**: symmetric unregistration + reset of all singletons.

**Client start/stop**: `JobClientManager` + `JobBoardClient` register/unregister
their S2C handlers and clear local state.

---

## Connection-Time Hydration

In `SimpleTownNetwork.SetupCharacterCoroutine` (server-only), after the
character REST fetch:

```
REST GET /characters/by-user-id/:userId    → CharacterResponse
REST GET /character-jobs/by-character/:id  → CharacterJobResponse  (NEW)
character.Jobs = jobsResponse.CharacterJobs
player.SetRawCharacterData(JsonUtility.ToJson(character))
```

The SyncVar fires `ParseCharacterData` on every client → `OnCharacterDataChanged`
→ `CharacterInfoPanelUI.Setup` redraws the HUD job label, `CareerUI.Refresh`
redraws the phone app.

---

## Persistence

See also `PERSISTENCE.md` for the full schema. Career-specific:

```sql
-- characters: pointer to active job
ALTER TABLE characters
    ADD COLUMN current_job SMALLINT;   -- NULL = unemployed

-- character_jobs: one row per (character × job) — XP store + history
CREATE TABLE character_jobs (
    id           UUID DEFAULT gen_random_uuid() PRIMARY KEY,
    character_id UUID NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    category     SMALLINT NOT NULL,
    xp           INTEGER  NOT NULL DEFAULT 0,
    started_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX idx_character_jobs_character_category
    ON character_jobs (character_id, category);
```

Migration file: `simple-town-ws/migrations/08_career.sql`.

**Backend routes** (NestJS, RxJS Observables):

| Route | Purpose |
|---|---|
| `GET /characters/:id` | Returns Character including `currentJob` |
| `PUT /characters/:id/update-current-job` body `{ currentJob: number \| null }` | Set / clear current job (`-1` and `null` both stored as SQL NULL) |
| `GET /character-jobs/by-character/:characterId` | List career rows for a character (`{ characterJobs: CharacterJob[] }`) |
| `POST /character-jobs/start` body `{ characterId, category }` | Idempotent upsert. Returns existing row (preserving `xp`/`started_at`) if present, else inserts fresh with `xp=0, started_at=now()` |
| `PUT /character-jobs/add-xp` body `{ characterId, category, delta }` | `startOrResume` then increment xp by delta |

Service: `simple-town-ws/src/shared/services/character-job.service.ts`. Wired in
`SharedModule` and `AppModule` (`CharacterJobModule`).

---

## UI Surfaces

### `JobActiveHUD`
File: `Assets/Scripts/Jobs/UI/JobActiveHUD.cs`. Singleton scene-scoped HUD that
shows title / step prompt / current target name / distance (4 Hz refresh,
NavMesh path with `Vector3.Distance` fallback). Accept (if `Offered`) and
Abandon (if `Active`) buttons. Subscribes to `JobClientManager` events.

### `JobActiveTargetIndicator`
Toggles `JobPoint.indicator` on the currently active target.

### `JobBoardUI` + `JobBoardEntryUI`
Panel + row prefab. Each row shows title, status, owner name, formatted reward
string (concatenation of `RewardDefinition.GetDisplayString()`).

### Phone Career App (`CareerUI`)
File: `Assets/Scripts/UI/Phone/CareerUI.cs`. `PhoneApplicationUI` subclass.

Two modes on the same prefab, switched in `Refresh()` based on
`character.CurrentJobCategory`:

- **noJobView** — "Pas de métier" + one Apply button per JobCategory.
  Each click sends `JobChangeCareerMessage { newJob: (int)category }`.
- **careerView** — job name (via `JobCategoryLabels.Display`), `XP : N`,
  "Depuis le {date}" formatted from `started_at`, "Démissionner" button →
  `JobChangeCareerMessage { newJob: -1 }`.

Inspector fields are explicit `JobChoice[]` pairs (`category` + `Button`) so
the UI is fully Inspector-wired. Subscribes to `PlayerController.OnCharacterDataChanged`
in `OnEnable` / unsubscribes in `OnDisable`.

### HUD job label (`CharacterInfoPanelUI`)
File: `Assets/Scripts/UI/CharacterInfoPanelUI.cs`. The existing `jobText` field
(used to be hardcoded to `"CHÔMEUR"`) is now driven by
`JobCategoryLabels.DisplayUpper(characterData.CurrentJobCategory)`. Already
subscribes to `OnCharacterDataChanged`, so it refreshes on apply / resign with
no extra wiring.

---

## End-to-End Delivery Flow (after the career layer)

```
1. JobAutoPublisher publishes a Delivery offer → toast broadcast
   "Nouvelle mission : Livreur".
2. Player walks to the Delivery JobBoard → OPEN action.
   - If not Livreur → local toast "Tu n'es pas employé pour ce métier."
   - If Livreur → JobBoardOpenMessage sent, server checks career, subscribes,
     pushes JobBoardSnapshotMessage with Available entries.
3. Click "Prendre" → JobBoardTakeMessage → JobServerManager.TakeFromBoard
   (career check, MaxConcurrentPerPlayer check) → JobOfferedMessage to owner.
4. HUD shows step 1 (Reach pickup), distance updates 4×/sec.
5. Reach pickup zone → step 1 succeed → step 2 (Use Machine, indicator stays
   on pickup point).
6. Click PackagingMachine → JobUseMachineMessage → UseMachineStep spawns the
   package directly in the player's hand (Persistent=false, AuthorizedNetId).
7. HUD switches to step 3 (Reach delivery). Indicator follows.
8. Reach delivery → step 4 (DeliverToTarget, ~1.2s hold).
9. JobCompleted → RewardSystem applies each reward in turn:
   - MoneyReward → bank.GiveMoney + REST persist + toast "+25 €" (BANK)
   - JobXpReward → player.AddJobXp(category, amount) + REST persist +
     toast "+10 XP Livreur" (JOB)
10. SetRawCharacterData rebroadcasts → CareerUI / CharacterInfoPanelUI redraw.
```

If the player resigns or switches careers mid-mission:

- `JobChangeCareerMessage` arrives → `PlayerController.StartCareerChange`.
- `JobServerManager.OnPlayerDisconnected(netId)` is called as the cleanup
  hook (reused for the career-change use case) → active mission goes through
  `FinalizeFail(OwnerDisconnected, JobStatus.Failed)`. `JobItemCleanup`
  (subscriber of `JobFailed`) despawns any mission item still in the player's
  hand.
- The previous career's XP row is **never deleted** — `started_at` and `xp`
  remain in `character_jobs` so resuming the same job later picks up where it
  left off.

---

## Files (cheat-sheet)

```
Assets/Scripts/Jobs/
├── Core/
│   ├── JobDefinition.cs       (+ JobCategory enum)
│   ├── JobInstance.cs
│   ├── JobStepDefinition.cs / JobStepInstance.cs
│   ├── JobContext.cs
│   ├── JobEvents.cs
│   ├── JobStatus.cs / JobFailureReason.cs / StepStatus.cs
│   ├── JobTargetKey.cs
│   ├── PointRole.cs
│   ├── IJobTarget.cs
│   ├── RewardDefinition.cs
│   └── JobCategoryLabels.cs            (NEW — FR display labels)
├── Network/                            10 NetworkMessage structs
│   ├── JobOfferedMessage.cs
│   ├── JobAcceptedMessage.cs
│   ├── JobAbandonRequestMessage.cs
│   ├── JobStepAdvancedMessage.cs
│   ├── JobFinishedMessage.cs
│   ├── JobBoardOpenMessage.cs / CloseMessage / TakeMessage / SnapshotMessage
│   ├── JobBoardEntry.cs
│   ├── JobRewardNotificationMessage.cs
│   ├── JobNotificationMessage.cs
│   ├── JobUseMachineMessage.cs
│   └── JobChangeCareerMessage.cs        (NEW — career change C2S)
├── Runtime/
│   ├── JobServerManager.cs
│   ├── JobClientManager.cs
│   ├── JobBoardServer.cs                (+ career gate)
│   ├── JobBoardClient.cs
│   ├── JobDatabase.cs                   (+ GetSalaryForCategory)
│   ├── JobSystemBootstrap.cs            (+ handler for JobChangeCareerMessage,
│   │                                       + PlayerCareerSalaryTicker mount)
│   └── PlayerCareerSalaryTicker.cs      (NEW — periodic salary tick)
├── Targets/
│   ├── PlayerJobTarget.cs / NpcJobTarget.cs
│   ├── JobPoint.cs
│   └── JobTargetHooks.cs
├── Steps/
│   ├── ReachTarget/ DeliverToTarget/ PickupPackage/ UseMachine/
├── Rewards/
│   ├── MoneyReward.cs
│   ├── SocialCreditReward.cs
│   ├── JobXpReward.cs                   (NEW)
│   └── RewardSystem.cs
├── Providers/
│   ├── IJobProvider.cs
│   ├── JobsDebugProvider.cs             (F9 binding removed)
│   └── JobAutoPublisher.cs              (NEW — random arrivals + cap)
├── Items/
│   ├── JobItemCleanup.cs
│   └── PackagingMachineBehaviour.cs
├── UI/
│   ├── JobActiveHUD.cs
│   ├── JobActiveTargetIndicator.cs
│   ├── JobBoardUI.cs / JobBoardEntryUI.cs
│   └── JobDistanceUtil.cs
└── Board/
    └── JobBoard.cs                      (+ career gate)
```

Career-side files outside `Jobs/`:

```
Assets/Scripts/Entities/
├── CharacterData.cs                     (+ currentJob, jobs, helpers)
├── CharacterJobData.cs                  (NEW)
├── Responses/CharacterJobResponse.cs    (NEW)
├── Requests/CharacterUpdateCurrentJobRequest.cs   (NEW)
├── Requests/CharacterJobStartRequest.cs           (NEW)
└── Requests/CharacterJobAddXpRequest.cs           (NEW)

Assets/Scripts/Managers/ApiManager.cs    (+ 4 career endpoints)
Assets/Scripts/Network/SimpleTownNetwork.cs       (jobs hydrated at connect)
Assets/Scripts/Player/PlayerController.cs         (StartCareerChange + AddJobXp)
Assets/Scripts/UI/Phone/CareerUI.cs               (NEW phone app)
Assets/Scripts/UI/CharacterInfoPanelUI.cs         (job label dynamic)
```

Backend:

```
simple-town-ws/
├── migrations/08_career.sql
├── src/character/
│   ├── schemas/character.schema.ts      (+ currentJob)
│   ├── character.controller.ts          (+ update-current-job)
│   └── requests/character-update-current-job-request.ts
├── src/character-job/                   (NEW module)
│   ├── schemas/character-job.schema.ts
│   ├── responses/character-job-response.ts
│   ├── requests/character-job-start-request.ts
│   ├── requests/character-job-add-xp-request.ts
│   ├── character-job.controller.ts
│   └── character-job.module.ts
└── src/shared/services/
    ├── character.service.ts             (+ updateCurrentJob, null↔-1 sentinel)
    └── character-job.service.ts         (NEW)
```

---

## Editor Setup Checklist (POC test)

1. Run `migrations/08_career.sql` **and** `migrations/09_city_salary_period.sql`
   in Supabase SQL editor.
2. In Unity: create one `Career Variant.prefab` under
   `Assets/Resources/Prefabs/UI/Phone/Applications/` carrying a `CareerUI`,
   two view roots (`noJobView`, `careerView`) and wired TMP/Button refs.
3. Add a `PhoneApplicationButton` on the phone home in `City.unity` pointing
   to the new prefab.
4. In `City.unity`: add a `JobAutoPublisher` GameObject, drag the
   `Delivery_Test.asset` (and any other JobDefinitions) into `jobDefinitions`.
5. Drop a `JobBoard` per category on the right scene fixture (e.g. Delivery
   board in the warehouse).
6. Create `XpReward_Delivery.asset` (menu `Sim/Jobs/Rewards/JobXp`),
   `amount=10`, drag into `Delivery_Test.asset → rewards`.

See `verification` section of `plans/pour-des-raisons-de-wondrous-cat.md` for
the full end-to-end test plan.
