# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> The parent `../CLAUDE.md` already documents the monorepo layout, the backend (`simple-town-ws`), the high-level Unity architecture (managers, networking, entities, building, sub-games), and the full game design context. **Read it first.** This file only adds Unity-specific operational details that are not in the parent.

---

## Editor & Build

- **Unity version:** `6000.4.4f1` (Unity 6). Do not downgrade — package versions in `Packages/manifest.json` resolve against this editor.
- **Render pipeline:** URP.
- **No project `asmdef`.** All gameplay code in `Assets/Scripts/` compiles into the default `Assembly-CSharp`. Only third-party packages (Mirror, Dissonance, ParrelSync, etc.) have their own asmdefs. When adding a new script, do **not** introduce an asmdef without checking — it will silently break references from gameplay code that lives in the default assembly.
- **Tests:** the project has no test assemblies. Don't write `[Test]`/`[UnityTest]` files unless the user explicitly asks — they won't run anywhere.

## Scene Flow

Scenes live in `Assets/Scenes/`. The runtime flow is:

```
Launcher.unity  →  Main Menu.unity  →  City.unity (multiplayer gameplay)
                                    ↘  SubGames/Cook.unity, SubGames/Dream.unity
```

- **`Launcher`** — bootstraps singletons (`ApiManager`, `DatabaseManager`, `LoadingManager`) and loads the Main Menu.
- **`Main Menu`** — REST login → character/home selection → calls `NetworkManager.singleton.StartClient()`.
- **`City`** — the online scene that Mirror loads after a successful connection.
- **`Test.unity`, `Prototype.unity`** — scratch scenes; do not assume gameplay flows through them.

When `OnStopClient` fires, `SimpleTownNetwork` hard-loads `"Main Menu"` via `SceneManager.LoadScene` — preserve that name if you rename the scene.

## Namespaces

Gameplay code uses `Sim` and sub-namespaces (`Sim.Building`, `Sim.Entities`, `Sim.Enums`, `Sim.Constants`, `Sim.Communication`). **Network messages and `SimpleTownNetwork` itself sit in the global namespace** — keep them there, otherwise Mirror's reflection-based message registration in `OnStartServer`/`OnStartClient` won't match handlers across files that mix namespaces.

## Mirror Connection Handshake

Authentication is **not** done via a `NetworkAuthenticator`. The flow is:

1. Client authenticates with the backend over REST (`ApiManager.Authenticate` → JWT stored on `ApiManager.accessToken`).
2. Client selects a character; `SimpleTownNetwork.CharacterData` is populated.
3. `MainMenuManager.Play()` calls `StartClient()`.
4. After Mirror's TCP handshake, **`SimpleTownNetwork.OnClientConnect()` (no params, override)** runs on the client and sends `CreateCharacterMessage { userId, characterId }` via `NetworkClient.Send(...)`.
5. The server's handler `OnCreateCharacter` (registered in `OnStartServer`) refetches the character via REST, instantiates `playerPrefab`, calls `NetworkServer.AddPlayerForConnection`, and sends `UpdateCityDataMessage` back.

Pitfalls:
- The wrong signature `OnClientConnect(NetworkConnectionToClient conn)` does **not** override anything in current Mirror — the message will silently never be sent. Always use `public override void OnClientConnect()` and `NetworkClient.Send(...)` (the `NetworkConnectionToClient` API is server-side only).
- If a `BasicAuthenticator` (or any `NetworkAuthenticator`) is attached to the `NetworkManager` GameObject in the scene, `OnClientConnect` only fires after auth completes. Since auth is handled at the REST layer, **no `NetworkAuthenticator` should be attached** unless you also wire up matching credentials on the client.
- The `useSpectusAccount` / `useElbloodyAccount` toggles on `SimpleTownNetwork` are dev shortcuts that bypass the login flow with hardcoded user/character IDs. Leave them off in any committed scene.

## Backend URI Configuration

`ApiManager.uri` (serialized) defaults to `http://localhost:3000`. The `local` checkbox on the same component **always overrides** `uri` back to localhost in `Awake()` — un-tick it before pointing at a remote backend, or the override will silently win.

The backend wraps array responses in a root key (e.g. `{ "Characters": [...] }`, `{ "Homes": [...] }`) specifically because Unity's `JsonUtility.FromJson` cannot deserialize bare JSON arrays. Keep response DTOs (in `Assets/Scripts/Entities/Responses/`) aligned with the backend's `*Response` shapes — a missing wrapper field results in a silent empty deserialization, not an exception.

## ScriptableObject Loading

`DatabaseManager` calls `Resources.LoadAll<T>(...)` at startup against subfolders of `Assets/Resources/Configurations/` (Actions, Covers, Items, Props, Shops, SubGames, etc.). Anything that needs to be discoverable at runtime **must** live under `Assets/Resources/`. Moving a config out of `Resources/` is silent — `DatabaseManager` just returns an empty list.

## ApiManager Event Pattern

All REST responses surface as **static C# events** on `ApiManager` (e.g. `OnCharacterRetrieved`, `OnHomesRetrieved`, `OnAuthenticationSucceeded`). Consumers subscribe in `OnEnable` and unsubscribe in `OnDisable` — follow that pattern; subscribing in `Start` without a matching unsubscribe leaks across scene reloads because `ApiManager` is `DontDestroyOnLoad`.

## Multiplayer Testing Locally

`Assets/ParrelSync/` is in the project. Use **ParrelSync → Clones Manager** in the editor menu to spin up a second editor instance pointing at the same project — that's the standard way to test host + client locally without rebuilding. Each clone shares the project files but has its own `Library/`, so first open of a clone takes a long re-import.

## Voice Chat (Dissonance)

Dissonance is wired through the `MirrorIgnorance` integration in `Assets/Dissonance/Integrations/MirrorIgnorance/`. The `DissonanceSetup.prefab` must be present in any scene where voice should work. If you're touching the player prefab and chat stops working, check that the `MirrorIgnorancePlayer` component is still on it.

## Conventions to Preserve

- **Coroutines for HTTP**, not `async/await`. `ApiManager` returns `UnityWebRequest` from `*Request` methods and runs `*Coroutine` methods that yield `SendWebRequest()`. Stay consistent — mixing in async/await complicates lifecycle on scene unload.
- **Plain C# data classes in `Entities/`** mirror MongoDB documents and are (de)serialized with `JsonUtility`. They must remain `[Serializable]` with public fields matching backend casing (often `snake_case` for `_id`, `user_id`, `last_timestamp`, etc.). Do not switch to `Newtonsoft.Json` for these — the round-trip with `SyncVar`-broadcast JSON strings (e.g. `PlayerController.rawCharacterData`) depends on `JsonUtility`.
- **`SyncVar` hooks for character data:** server sets `rawCharacterData` (a JSON string), the hook deserializes on each client. Don't try to `SyncVar` the `CharacterData` object itself — Mirror can't serialize arbitrary nested classes without manual writers.
