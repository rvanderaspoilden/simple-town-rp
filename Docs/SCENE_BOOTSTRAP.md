# SCENE_BOOTSTRAP.md — Managers, DDOL & garde-fous anti-suppression

> Dernière mise à jour : 2026-05-25.

Ce doc explique **quels objets « configuration » vivent dans les scènes**, lesquels sont
`DontDestroyOnLoad` (DDOL), et les **deux garde-fous** mis en place pour éviter de casser la
scène City en supprimant un GameObject par erreur.

Réfs : `ARCHITECTURE.md` (carte des systèmes), `PERSISTENCE.md` (état relationnel),
`NETWORK_FLOW.md` (Mirror).

---

## 1. Flux de boot

```
Launcher.unity ──► Main Menu.unity ──► City.unity
   │                                      ▲
   └─ bootstrap les singletons DDOL ──────┘ (ils persistent jusqu'à City)
```

La scène **Launcher** instancie et marque `DontDestroyOnLoad` les managers de base
(**Api Manager, Network Manager, DatabaseManager, PropSystem**, GameLogger…). Ils
**survivent** aux changements de scène : quand City se charge, ils sont déjà là.

---

## 2. Classification des objets « configuration » de City

| Objet (City) | Composant | DDOL ? | Profil |
|---|---|---|---|
| ~~Api Manager~~ | `ApiManager` | ✅ | **Retiré de City** — bootstrappé par Launcher / auto-heal |
| ~~DatabaseManager~~ | `DatabaseManager` | ✅ | **Retiré de City** — idem |
| ~~Network Manager~~ | `SimpleTownNetwork` (Mirror) | ✅ | **Retiré de City** — idem |
| ~~PropSystem~~ | `ClientPropManager` + `PropInteractionDispatcher` | ✅ | **Retiré de City** — auto-créé par `PropSystemBootstrap` |
| HUD Manager | `HUDManager` | ✅ | Manager DDOL gameplay (origine City) |
| Camera Manager | `CameraManager` | ✅ | idem |
| CursorManager | `CursorManager` | ✅ | idem |
| LoadingCanvas | `LoadingManager` | ✅ | idem |
| Marker Canvas | `MarkerController` | ✅ | idem |
| MissionHighlightManager | `MissionHighlightManager` | ✅ | idem |
| **Time Manager** | `TimeManager` + `NetworkIdentity` | ❌ | ⚠️ **Objet réseau de scène** — non recréable par bootstrap |
| **Sub Game Controller** | `SubGameController` + `NetworkIdentity` | ❌ | ⚠️ idem |
| **CityRoomInit** | `CityRoomInitializer` + `NetworkIdentity` | ❌ | ⚠️ idem (enregistre les props de scène) |
| **Behaviour Manager** | `BuildingBehavior` (inactif) | ❌ | ⚠️ spécifique City |
| Job Provider | `JobsDebugProvider` + `JobAutoPublisher` | ❌ | scene-local (debug) |
| ServerAudioListener | `ServerAudioListener` | ❌ | scene-local |
| EventSystem | `EventSystem` | ❌ | scene-local (input UI) |

⚠️ = **critique & non auto-réparable** → protégé par le validateur (§4).

---

## 3. Auto-heal des managers DDOL (`CitySceneBootstrap`)

`Assets/Scripts/Managers/CitySceneBootstrap.cs` — un GameObject `CitySceneBootstrap` dans
City porte ce composant. À l'`Awake` (très tôt, `[DefaultExecutionOrder(-10000)]`), il appelle
`ManagerEnsurer.EnsureCoreManagers()` qui **recrée Api/Database/Network s'ils sont absents**,
depuis les prefabs canoniques de `Resources/Prefabs/Managers/` (`Api Manager`, `DatabaseManager`,
`Network Manager`). **Check-before-instantiate** (on n'instancie que si `Instance`/`singleton`
est null — sinon `DatabaseManager` double-`RegisterPrefabs`).

- **Flux normal (Launcher → City)** : les singletons existent déjà → **no-op** (zéro effet).
- **Flux standalone** (Play directement sur City) : ils sont absents → recréés depuis le prefab.

Conséquence : **City ne contient plus de copie posée à la main** de ces managers → on ne peut
plus les supprimer par erreur. `PropSystem` n'a pas besoin du bootstrap (auto-créé par
`PropSystemBootstrap.OnClientStart/OnServerStart`).

> Source de vérité = les prefabs `Resources/Prefabs/Managers/`. Édite-les pour changer la config.

---

## 4. Validateur anti-suppression (`CitySceneValidator`)

`Assets/Scripts/Editor/CitySceneValidator.cs` (éditeur uniquement).

- Hook sur `EditorSceneManager.sceneSaving` : **à chaque sauvegarde de City**, vérifie que
  tous les composants requis (liste `RequiredComponents`) sont présents.
- Menu **Tools ▸ Simple Town ▸ Validate City Scene** pour valider à la demande.
- S'il manque quelque chose → **erreur rouge** listant précisément les composants absents.

La liste des requis = les objets **critiques/non auto-réparables** (Time Manager, Sub Game
Controller, CityRoomInit, Behaviour Manager, EventSystem, les managers DDOL gameplay) **+
`CitySceneBootstrap`** lui-même. Les 4 retirés (Api/DB/Network/PropSystem) n'y sont **pas**
(ils s'auto-réparent).

### Ajouter un nouvel objet requis
Ajoute le nom de classe du composant dans `RequiredComponents` (matché par `Type.Name`).

---

## 5. En cas de suppression accidentelle — récupération

1. **Undo** (Ctrl+Z) si c'est immédiat.
2. Sinon, le validateur te dit quel composant manque à la sauvegarde.
3. Pour un manager DDOL (Api/DB/Network) : re-glisse le prefab depuis
   `Resources/Prefabs/Managers/` — ou ne fais rien, le bootstrap le recrée au runtime.
4. Pour un objet réseau de scène (Time Manager, Sub Game Controller, CityRoomInit) ou
   Behaviour Manager : **Undo obligatoire** (pas de prefab de secours / non auto-réparable).
   Pense à en faire des prefabs si tu veux une récupération facile.

---

## 6. Quick-Play : tester depuis n'importe quelle scène (`DevQuickPlay`)

`Assets/Scripts/Managers/DevQuickPlay.cs` — **éditeur uniquement** (`#if UNITY_EDITOR`, strippé
des builds, aucun GameObject à supprimer).

But : presser **Play** depuis City (ou toute scène hors Launcher) lance le jeu + auto-connecte,
sans repasser par Launcher + Host du HUD Mirror.

- **Activation** : menu **Tools ▸ Simple Town ▸ Quick Play (auto-host depuis toute scène)**
  (coché = actif, stocké en `EditorPrefs`). OFF par défaut → comportement normal préservé.
- **Choix du compte** : sous-menu **Tools ▸ Simple Town ▸ Quick Play Account** → `Spectus`
  (défaut) ou `Elbloody` (radio, EditorPrefs `SimpleTown.QuickPlaySpectus`).
- Quand actif, à l'entrée en Play (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`, une fois) :
  1. ignore si scène = Launcher/Main Menu, ou si le réseau tourne déjà ;
  2. `ManagerEnsurer.EnsureCoreManagers()` ;
  3. `SimpleTownNetwork.EditorSetDevAccount(spectus)` (compte choisi, en dur) ;
  4. **`StartHost`** — ou **`StartClient`** si on est un clone **ParrelSync** (test 2 joueurs en
     un clic ; détection par réflexion, fallback Host).
- Comme `onlineScene = City`, Mirror charge/recharge **City** automatiquement.

Pour tester le **vrai flux Launcher**, décoche le menu.
