# SCENE CONTRACT & DEPENDENCY GRAPH

Phase 1.5. What the runtime actually needs from scene assets, and what it builds for itself.

---

## 1. Dependency graph

```
  PROCESS START (any scene open — editor or player)
        │
        ├─ [RuntimeInitializeOnLoadMethod(SubsystemRegistration)]
        │     AppBootstrap.ResetStatics       clears _booted (domain-reload-off safety)
        │     UiInstaller.Arm                 subscribes to AppBootstrap.Booted
        │
        └─ [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]
              AppBootstrap.BootAfterFirstScene
                    │
                    ├─ new GameObject("[Vardholm]")  ← CREATED AT RUNTIME, not authored
                    ├─ DontDestroyOnLoad
                    └─ AppBootstrap.Build()
                          │
                          ├─ AppCompositionRoot.Build(this) ──────────────┐
                          ├─ RaiseBooted()  ──→ UiInstaller.Install()     │
                          ├─ AddComponent<Ticker>()                       │
                          ├─ AddComponent<DevOverlay>()   (dev only)      │
                          ├─ SceneLoader.Loaded += OnSceneLoaded          │
                          ├─ States → MainMenu                            │
                          └─ SelfHealIntoOpenScene()                      │
                                                                          │
  COMPOSITION ROOT (hand-wired, no container — ADR-0012)                  │
        UnityCoreLog · SignalBus · IslandClock · StringTableLocalization ◄─┘
        GameStateMachine · CommandDispatcher · SessionService
        SaveFileStore · SaveCodec · SaveSlotService
        SceneLoader · ZoneRegistry · InputRouter · VardholmControls
        5 command handlers · 2 save participants
                          │
  UI INSTALL (ForgottenIsle.UI — Game never references UI; it announces)
        UiInstaller.Install(context)
              ├─ AddComponent<UIDocument>()          ← CREATED AT RUNTIME
              ├─ UIService.CreateFallbackPanelSettings()  ← CREATED AT RUNTIME
              ├─ new UIService(document, loc, log)
              ├─ MainMenuController · PauseController
              └─ subscribe GameStateChangedSignal → screen stack
```

## 2. Answers to the ten inspection questions

| # | Question | Answer |
|---|---|---|
| 1 | Required scenes | **4**: `Bootstrap`, `MainMenu`, `ZoneRibcage`, `ZoneFernmaw` (`SceneKeys.All`) |
| 2 | Required GameObjects **authored in a scene** | **None.** Every object is created at runtime. |
| 3 | MonoBehaviours that must exist in a scene | **None.** `AppBootstrap`, `Ticker`, `DevOverlay`, `UiInstaller` are all added to the runtime host. `PlayerRig` and `ZoneEntryAnchor` are *optional* — furnished at runtime when absent. |
| 4 | Required component references | One: `PlayerRig._cameraPivot` (`[SerializeField]`). **Now also settable at runtime**, so no Inspector work is needed. |
| 5 | UIDocument / PanelSettings | Both created at runtime by `UiInstaller`. No `.asset` required. |
| 6 | ScriptableObject / Resources deps | One: `Assets/Resources/Localization/en.csv` (a `TextAsset`), loaded by `LocalizationLoader`. Must stay byte-identical to the authoring copy. |
| 7 | Scenes expected by `SceneLoader` | Only what `ZoneRegistry` asks for: the two zone scenes. |
| 8 | Scenes expected in Build Settings | All 4, **in `SceneKeys.All` order** — `Bootstrap` must be index 0. |
| 9 | Runtime-created dependencies | Host GameObject, `Ticker`, `DevOverlay`, `UIDocument`, `PanelSettings`, the whole service graph, and (new) the zone furnishing: player rig, camera, ground, light, entry anchor. |
| 10 | Requires Inspector assignment | **Nothing, after Phase 1.5.** `_cameraPivot` was the only one and is now runtime-attachable. |

## 3. The scene contract

Every scene is **empty and valid**. Nothing is authored inside one; the runtime furnishes what it
needs. This is deliberate: an empty scene has no serialized component references, so it cannot be
malformed, cannot drift from code, and can be regenerated at any time without losing work.

| Scene | Build index | Contract | Why it exists |
|---|---:|---|---|
| `Bootstrap` | 0 | Empty. | The entry scene. Index 0 is what a player build loads first, and it must be cheap. The boot hook fires regardless of which scene opened, so this holds nothing. |
| `MainMenu` | 1 | Empty. | The menu is UI Toolkit on the persistent `UIDocument`, so this scene carries no menu objects. It is the state the game rests in when no run is loaded, and it is **the scene that gets unloaded when a run starts** — which is what the PlayMode test asserts. Future home of the cinematic backdrop (open item O-9). |
| `ZoneRibcage` | 2 | Empty; furnished at runtime. | Act 1 opening zone. The first playable space. |
| `ZoneFernmaw` | 3 | Empty; furnished at runtime. | Act 1 second zone. **Documented reason:** a second zone is the only way to exercise zone-to-zone travel and prove `ZoneRegistry`'s two-resident cap (acceptance criteria 11 and 12). Without it, streaming is untested. |

**No dead scene references. No documented-but-unused scene. `SceneKeys.All` is the single source
of truth and a test compares it against the build list.**

## 4. Runtime zone furnishing

When a zone scene loads, `ZoneFurnisher` inspects it and creates whatever is missing:

| Object | Created when absent | Notes |
|---|---|---|
| Ground plane | no collider under the anchor | 60×60 m, so the player cannot walk off the world |
| Directional light | no light in the scene | Only if the scene has none, so an authored lighting pass wins |
| `ZoneEntryAnchor` | none in scene | Placed at origin + 1 m up |
| `PlayerRig` (capsule) | none in scene | Placed on the anchor |
| `Camera` + pivot | rig has no camera pivot | Parented to the rig at eye height, wired via `AttachCameraPivot` |

**Authored content always wins.** The furnisher only fills gaps, so hand-building a real Ribcage
later requires no code change — it just stops furnishing what the scene now provides.

## 5. What still needs a human

Creating the four `.unity` files themselves. Unity scene assets are editor-serialized YAML with
GUID/fileID cross-references; hand-authoring them outside the editor risks a corrupt asset that is
worse than a missing one.

**This is automated, not manual:** `Vardholm → Setup Project` creates them through
`EditorSceneManager`, which is Unity's own API doing its own serialization. One menu click, no
GameObject work, nothing to follow from a README.
