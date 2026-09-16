# PHASE 1 IMPLEMENTATION PLAN

**Scope.** Unity project, assemblies, bootstrap, main menu, core game state, scene navigation, a
placeholder player capsule — **plus the save spine and the localization facade**, pulled forward
per the ordering critique in `02-mvp-scope-and-roadmap.md` §2.18.

**Not in scope.** Any gameplay system. See §10.

**Blocked on.** **Task 0** — the ADR reconciliation in `../architecture/00-decisions.md`.
Phase 1 does not open while three architecture documents disagree about assembly names.

---

# 0. TASK 0 — RECONCILIATION (blocking, ~1 day)

Apply ADR-0001 … ADR-0010 to the documents they govern. Mechanical, but it must be done by a
human who reads the diffs, because ADR-0008 (locomotion) invalidates a prologue teaching beat and
ADR-0007 (inventory) deletes a type that other sections reference.

**Exit:** `rg "Isle\.Domain|Isle\.Core|ResourceDefinition|SurvivalStat.*Morale"` returns nothing
in `docs/`, and one engineer other than the author has signed off on the diff.

---

# 1. PRECONDITIONS

## 1.1 Unity and packages

Pin an exact Unity 6 LTS patch version in `ProjectSettings/ProjectVersion.txt` and record it in
an ADR. "Unity 6 LTS" is not a version; a patch difference is a merge conflict in every
`.unity` file.

| Package | Why Phase 1 needs it |
|---|---|
| `com.unity.inputsystem` | The only input path. Legacy input is disabled in Task 1. |
| `com.unity.render-pipelines.universal` | URP asset + renderer, created in Task 1. |
| `com.unity.addressables` | The definition catalog ships as an Addressable (ADR-0005). Needed at boot. |
| `com.unity.localization` | The localization facade. No user-facing string is ever hardcoded. |
| `com.unity.test-framework` | EditMode + PlayMode suites. |
| `com.unity.nuget.newtonsoft-json` | Save codec — **pending R1**, see §9. |
| `com.unity.memoryprofiler` | Not shipped; used for the Task 14 memory pass. |

`⚠ VERIFY` every package name and version against the pinned editor on day one. The verification
ledger in `../architecture/01-technical-architecture.md` §0 is the authority; amend it, never
delete from it.

## 1.2 Project settings (Task 1 checklist)

- Colour space **Linear**; graphics APIs **Metal** (iOS) / **Vulkan** then GLES3 (Android).
- Orientation **Portrait** only; auto-rotation off.
- Scripting backend **IL2CPP**, **ARM64** only; managed stripping **Low** for Phase 1
  (raise later, with the link.xml that the save codec will need).
- Active Input Handling → **Input System Package (New)**.
- Three quality levels named `Low` / `Mid` / `High` matching the tiers in Technical
  Architecture §8.1. Do not use Unity's defaults.
- `Application.targetFrameRate = 60`; vSync off.
- Accelerometer frequency **Disabled** (it costs a main-thread poll and we never read it).

## 1.3 Repository setup

`.gitignore` covering `Library/`, `Temp/`, `Obj/`, `Build/`, `Logs/`, `UserSettings/`,
`*.csproj`, `*.sln`. `.gitattributes` enabling **Git LFS** for `*.psd *.png *.tga *.wav *.mp3
*.fbx *.mp4` and forcing `*.unity *.prefab *.asset` to `merge=unityyamlmerge -text`. Force Text
serialization and Visible Meta Files in Editor settings — **before the first asset is created**,
because changing it later rewrites every file in the project.

`CODEOWNERS` names one owner per `.unity` file from day one (see R4).

---

# 2. FILE MANIFEST

47 files. Enumerated, not sampled.

```
ForgottenIsle/
├─ .gitignore  .gitattributes  CODEOWNERS
├─ ci/
│  ├─ check-layering.sh
│  └─ CoreTests.csproj
├─ Assets/
│  ├─ Scenes/           Bootstrap.unity  MainMenu.unity  Zone_Ribcage.unity  Zone_Fernmaw.unity
│  ├─ Settings/         URP_Low.asset  URP_Mid.asset  URP_High.asset  InputActions.inputactions
│  ├─ Localization/     en.csv
│  └─ Scripts/
│     ├─ Core/      (ForgottenIsle.Core.asmdef — noEngineReferences)
│     ├─ Game/      (ForgottenIsle.Game.asmdef)
│     ├─ UI/        (ForgottenIsle.UI.asmdef)
│     ├─ Editor/    (ForgottenIsle.Editor.asmdef)
│     └─ Tests/     (EditMode + PlayMode asmdefs)
```

### `ForgottenIsle.Core` — engine-free (15 files)

| Path | Type | Responsibility | Depends on |
|---|---|---|---|
| `Core/ForgottenIsle.Core.asmdef` | asmdef | `noEngineReferences: true`. The structural guarantee. | — |
| `Core/Compat/IsExternalInit.cs` | POCO | Polyfill unlocking `record`/`init` (ADR-0003). | — |
| `Core/Primitives/Vec3.cs` | struct | Engine-free position. | — |
| `Core/Primitives/LocKey.cs` | struct | A localization key. Makes a raw string un-renderable by construction. | — |
| `Core/Primitives/ResultCode.cs` | enum | Every failure reason, localizable, allocation-free. | — |
| `Core/Primitives/PcgRandom.cs` | struct | Seeded PCG32, serialized inside world state. Determinism. | — |
| `Core/Time/IClock.cs` | interface | In-fiction clock. Faked in tests. | — |
| `Core/Commands/ICommand.cs` | interface | Marker + `ResultCode Validate()`. | ResultCode |
| `Core/Commands/ICommandHandler.cs` | interface | `Validate` / `Execute` split. | ICommand |
| `Core/Commands/CommandDispatcher.cs` | class | Routes a command to its handler; never throws on invalid input. | handlers |
| `Core/Commands/CommandResult.cs` | struct | Success/failure + `ResultCode` + args. No strings. | ResultCode |
| `Core/State/GameState.cs` | record | The aggregate. 14 participants (ADR-0009). Phase 1 registers 2. | — |
| `Core/State/SessionState.cs` | record | Act, zone, playtime, recorded-percentage. | — |
| `Core/Save/ISaveParticipant.cs` | interface | `Capture()` / `Restore()`. Every system implements it from the day it is written. | — |
| `Core/Save/SaveDocument.cs` | record | Versioned envelope: schema version, build, timestamp, hash, metadata. | SessionState |
| `Core/Save/SaveMigrator.cs` | class | Chain-of-migrations, v(n) → v(n+1). Empty chain in Phase 1, but the seam exists. | SaveDocument |
| `Core/Logging/ICoreLog.cs` | interface | Codes, not strings. Tests fail on any `Warn`. | — |

### `ForgottenIsle.Game` (16 files)

| Path | Type | Responsibility | Depends on |
|---|---|---|---|
| `Game/ForgottenIsle.Game.asmdef` | asmdef | References Core + Unity packages. | Core |
| `Game/Bootstrap/AppBootstrap.cs` | MonoBehaviour | The single entry point. `RuntimeInitializeOnLoadMethod` guard so any scene can be played directly in-editor. | CompositionRoot |
| `Game/Bootstrap/AppCompositionRoot.cs` | class | Hand-wired composition (ADR-0012). One method, one line per object. | everything |
| `Game/Bootstrap/GameStateMachine.cs` | class | `Boot → MainMenu → Loading → InGame → Paused`. Legal-transition table. Rejects illegal transitions loudly. | — |
| `Game/Bootstrap/Ticker.cs` | MonoBehaviour | **The only `Update` in the project.** Drives domain ticks at a fixed 10 Hz with a catch-up clamp. | Core |
| `Game/Scenes/SceneKeys.cs` | static | Scene name constants. Validated against the build settings list by a test. | — |
| `Game/Scenes/SceneLoader.cs` | class | Async additive load/unload, progress reporting, activation gate, 20 s watchdog → `LoadFailed`. | SceneKeys |
| `Game/Scenes/ZoneRegistry.cs` | class | Which zone scenes are resident. Enforces ADR-0004's max-two rule. | SceneLoader |
| `Game/Session/SessionService.cs` | class | Owns the live `GameState`. The only object permitted to mutate it. | Core |
| `Game/Saves/SaveFileStore.cs` | class | `persistentDataPath`, atomic temp→rename, `.bak` rotation, iOS backup exclusion. | — |
| `Game/Saves/SaveSlotService.cs` | class | Three slots + autosave. Enumerates metadata without deserializing the body. Skips unreadable slots. | SaveFileStore |
| `Game/Saves/SaveCodec.cs` | class | `SaveDocument` ⇄ bytes. Newtonsoft, or the hand-rolled fallback (R1). | Core |
| `Game/Localization/ILocalizedText.cs` | interface | The facade UI binds to. | Core |
| `Game/Localization/LocalizationService.cs` | class | CSV-backed lookup. **A missing key renders `#key#`, never empty, never a throw.** | ILocalizedText |
| `Game/Input/InputRouter.cs` | class | Input actions → controller calls. Blocked entirely while `Paused`. | InputActions |
| `Game/Player/PlayerCapsule.cs` | MonoBehaviour | A grey capsule that moves. Deliberately not a character controller. | InputRouter |
| `Game/Debug/DevOverlay.cs` | MonoBehaviour | State, zone, resident scene count, fps, frame time. Dev builds only. | GameStateMachine |

### `ForgottenIsle.UI` (9 files)

| Path | Type | Responsibility |
|---|---|---|
| `UI/ForgottenIsle.UI.asmdef` | asmdef | May reference Game + Core **read-only types only**. |
| `UI/Core/ScreenStack.cs` | class | Push/pop screens. One screen visible at a time. |
| `UI/Core/ScreenBase.cs` | abstract | Lifecycle: `Show` / `Hide` / `OnBack`. |
| `UI/Core/SafeAreaDriver.cs` | MonoBehaviour | Reads `Screen.safeArea`. **Nothing hardcodes an inset.** |
| `UI/Core/LocalizedLabel.cs` | MonoBehaviour | Binds a `LocKey` to a TMP label. The only way text reaches the screen. |
| `UI/Screens/MainMenuScreen.cs` | ScreenBase | CONTINUE / NEW GAME / SETTINGS. Calls controllers; mutates nothing. |
| `UI/Screens/PauseScreen.cs` | ScreenBase | RESUME / SAVE / QUIT TO MENU. |
| `UI/Screens/LoadingCurtain.cs` | ScreenBase | The curtain. Owns the minimum-display-time rule so a fast load doesn't flash. |
| `UI/Controllers/MainMenuController.cs` | class | Turns taps into commands. The UI→controller→service boundary, demonstrated. |

### `ForgottenIsle.Editor` (3 files)

`ForgottenIsle.Editor.asmdef` · `Build/BuildPipeline.cs` (CI entry points) ·
`Validation/AssemblyGraphWindow.cs` (visualises the reference graph; makes a violation obvious).

### Tests (4 files + 2 asmdefs)

`Tests/EditMode/StateMachineTests.cs` · `SaveRoundTripTests.cs` · `LocalizationTests.cs` ·
`CommandDispatchTests.cs` · `AssemblyGraphTests.cs` — and `Tests/PlayMode/BootFlowTests.cs`.

---

# 3. KEY IMPLEMENTATION SKETCHES

## 3.1 The state machine (the legal-transition table is the design)

Ten edges, and the one worth reading twice is `Loading -> MainMenu`: quit to menu is not a direct
hop from `InGame` or `Paused` but the two-step route `InGame|Paused -> Loading -> MainMenu`, so that
the loading curtain is already down while the session's zone scenes are unloaded — a direct edge
would put the game in menu mode with zones still resident and the player watching their world come
apart behind the menu.

```csharp
public enum GameStateId : byte { Boot, MainMenu, Loading, InGame, Paused, LoadFailed }

public sealed class GameStateMachine
{
    // Explicit. A transition not listed here is a bug, and it is loud.
    private static readonly (GameStateId From, GameStateId To)[] Legal =
    {
        (GameStateId.Boot,       GameStateId.MainMenu),
        (GameStateId.MainMenu,   GameStateId.Loading),
        (GameStateId.Loading,    GameStateId.InGame),
        (GameStateId.Loading,    GameStateId.MainMenu),  // quit, step 2: unload done, curtain up
        (GameStateId.Loading,    GameStateId.LoadFailed),
        (GameStateId.LoadFailed, GameStateId.MainMenu),
        (GameStateId.InGame,     GameStateId.Paused),
        (GameStateId.Paused,     GameStateId.InGame),
        (GameStateId.Paused,     GameStateId.Loading),   // quit, step 1: curtain in
        (GameStateId.InGame,     GameStateId.Loading),   // zone transition, or quit without pausing
    };

    public GameStateId Current { get; private set; } = GameStateId.Boot;
    public event Action<GameStateId, GameStateId> Changed;

    public bool CanTransition(GameStateId to)
    {
        for (var i = 0; i < Legal.Length; i++)
            if (Legal[i].From == Current && Legal[i].To == to) return true;
        return false;
    }

    public CommandResult TryTransition(GameStateId to)
    {
        if (!CanTransition(to)) return CommandResult.Fail(ResultCode.IllegalStateTransition);
        var from = Current;
        Current = to;
        Changed?.Invoke(from, to);
        return CommandResult.Ok;
    }
}
```

## 3.2 The localization facade — the missing-key rule is the whole point

```csharp
public sealed class LocalizationService : ILocalizedText
{
    private readonly Dictionary<string, string> _table;
    private readonly ICoreLog _log;

    public string Get(in LocKey key)
    {
        if (_table.TryGetValue(key.Value, out var s)) return s;
        _log.Warn(LogCode.MissingLocKey);
        return $"#{key.Value}#";   // Visible. Never empty, never a throw, never the raw key alone.
    }
}
```

A blank button in a shipped build is unfindable; `#ui.menu.continue#` is findable from a
screenshot. That is the entire design rationale, and acceptance item 30 tests it.

## 3.3 Atomic save (the bug we most want not to ship)

```csharp
public void Write(int slot, byte[] payload)
{
    var final = PathFor(slot);            // slot_1.isle
    var temp  = final + ".tmp";
    var bak   = final + ".bak";

    File.WriteAllBytes(temp, payload);
    // Flush to physical storage before the rename, or the rename can win the race
    // against the write and we publish a valid name over an empty file.
    using (var fs = new FileStream(temp, FileMode.Open, FileAccess.Write)) fs.Flush(true);

    if (File.Exists(final)) File.Replace(temp, final, bak, true);
    else                    File.Move(temp, final);

    MarkNoBackup(final);                  // iOS: exclude from iCloud backup
}
```

If `File.Replace` proves non-atomic on Android scoped storage (R6), the `.bak` still holds the
previous good save and `SaveSlotService` skips unreadable slots — the failure mode is "you lose
the last autosave", never "your file is gone".

## 3.4 The composition root — hand-wired on purpose

```csharp
public static class AppCompositionRoot
{
    public static AppContext Build(MonoBehaviour host)
    {
        var log        = new UnityCoreLog();
        var clock      = new UnityClock();
        var fsm        = new GameStateMachine();
        var loc        = new LocalizationService(CsvLocaleLoader.Load("en"), log);
        var fileStore  = new SaveFileStore(Application.persistentDataPath);
        var codec      = new SaveCodec();
        var slots      = new SaveSlotService(fileStore, codec, log);
        var session    = new SessionService(clock, log);
        var sceneLoader= new SceneLoader(host, log);
        var zones      = new ZoneRegistry(sceneLoader, maxResident: 2);
        var dispatcher = new CommandDispatcher(log);

        dispatcher.Register(new QuitToMenuHandler(fsm, zones, session));
        dispatcher.Register(new SaveGameHandler(slots, session));

        return new AppContext(fsm, loc, slots, session, zones, dispatcher, log);
    }
}
```

One method, one line per object, no magic. ADR-0012 says we revisit VContainer in Phase 2 with a
written go/no-go; because every class already takes its dependencies through a constructor,
adopting a container later changes *this file* and nothing else.

---

# 4. UI TECHNOLOGY DECISION

> **SUPERSEDED by ADR-0014.** The UI layer is **UI Toolkit**, built in code, ported from the
> NATION: WORLD ORDER project's framework; that is what ships (`ForgottenIsle.UI`). The choice
> below assumed a greenfield start, and it was not one. The table is kept as the record of the
> argument, not as the decision.

> ~~**uGUI + TextMeshPro for Phase 1. Not UI Toolkit.**~~

| | uGUI | UI Toolkit |
|---|---|---|
| Safe-area handling | `RectTransform` anchors; `SafeAreaDriver` is ~30 lines and a solved problem. | Doable, but the idiom is less settled and more of our team has not done it. |
| Animation | Mature tooling, and our HUD spec is animation-heavy (fades, bottom-sheet detents). | Transitions are improving but the bottom-sheet drag is more custom work. |
| Team familiarity | High. | Mixed. |
| Runtime cost | Known. Canvas rebuilds are the trap, and we know how to avoid them. | Often better at scale, but Phase 1 has ~3 screens. |

**Decision rationale.** Phase 1 has three screens and a curtain. The deciding factor is not
which technology is better in the abstract — it is which one lets us prove the *architecture*
(UI→controller→service, localized text, safe areas) without also learning a UI framework.

**Migration risk if we are wrong.** Contained by design: screens talk to the world only through
`ScreenBase` and a controller. Swapping the rendering layer means rewriting the screen classes,
not the architecture. Re-evaluate at Phase 12, when the real HUD lands.

---

# 5. WORK BREAKDOWN — DEPENDENCY ORDER

Each task compiles independently and is sized under a day.

| # | Task | Depends on |
|---|---|---|
| 0 | ADR reconciliation | — |
| 1 | Unity project, settings, gitignore/LFS, CODEOWNERS | 0 |
| 2 | CI skeleton: version / format / layering stages (no licence needed) | 1 |
| 3 | Five asmdefs + `AssemblyGraphTests` + `check-layering.sh` | 1 |
| 4 | Core primitives: `Vec3`, `LocKey`, `ResultCode`, `PcgRandom`, `IsExternalInit` | 3 |
| 5 | `ICommand` / `ICommandHandler` / `CommandDispatcher` / `CommandResult` | 4 |
| 6 | `GameStateMachine` + transition tests | 5 |
| 7 | `LocalizationService` + `en.csv` + `LocalizedLabel` + tests | 4 |
| 8 | Save spine: `SaveDocument`, `SaveCodec`, `SaveFileStore`, `SaveSlotService`, `SaveMigrator` | 5 |
| 9 | `SessionService` + `GameState` with 2 participants | 8 |
| 10 | `SceneLoader` + `ZoneRegistry` + `LoadingCurtain` + watchdog | 6 |
| 11 | `AppBootstrap` + `AppCompositionRoot` + `Ticker` | 6,7,9,10 |
| 12 | `InputRouter` + `PlayerCapsule` | 11 |
| 13 | `MainMenuScreen`, `PauseScreen`, `MainMenuController`, `SafeAreaDriver` | 11 |
| 14 | `DevOverlay` + device pass + demo rehearsal | 12,13 |

Tasks 7 and 8 are the natural second-engineer split after task 3 lands.

---

# 6. TESTS

**EditMode** (fast, no scene, no Unity API — the bulk of the suite):

```
StateMachineTests
  Boot_To_MainMenu_IsLegal
  MainMenu_To_InGame_IsRejected_BecauseLoadingIsMandatory
  Paused_To_Paused_IsRejected
  IllegalTransition_ReturnsIllegalStateTransition_AndDoesNotChangeState
  Changed_FiresOnce_WithFromAndTo

SaveRoundTripTests
  Capture_Then_Restore_ProducesIdenticalGameState
  Envelope_CarriesSchemaVersion_BuildVersion_And_Checksum
  Metadata_ReadsWithoutDeserializingBody
  CorruptedBody_IsRejected_AndSlotReportsUnreadable
  UnknownFutureSchemaVersion_FailsLoudly_NotSilently
  MigratorChain_IsEmpty_ButSeamIsExercised

LocalizationTests
  KnownKey_ReturnsTranslation
  MissingKey_ReturnsHashWrappedKey_NeverEmpty
  MissingKey_LogsExactlyOneWarning
  EveryKeyReferencedInCode_ExistsIn_en_csv        // the CI gate that bans hardcoded strings

CommandDispatchTests
  UnregisteredCommand_ReturnsNoHandler_DoesNotThrow
  InvalidCommand_FailsValidation_AndExecuteIsNeverCalled
  ValidCommand_Executes_Once

AssemblyGraphTests
  Core_HasNoEngineReferences
  UI_DoesNotReference_Editor
  NoAssembly_ReferencesUpward
  SceneKeys_AllExistIn_BuildSettings
```

**PlayMode** (few, slow, each justified in an XML doc comment):

```
BootFlowTests
  Bootstrap_Reaches_MainMenu_WithinTwoSeconds
  NewGame_Loads_Ribcage_And_UnloadsMenu
  ZoneTransition_NeverExceeds_TwoResidentZones
  QuitToMenu_UnloadsEverySessionScene_NoLeakedGameObjects
```

**CI.** Stages 1–3 (version, format, layering) run on `ubuntu-latest` with **no Unity licence**.
`ForgottenIsle.Core` additionally runs under plain `dotnet test` via `ci/CoreTests.csproj`
referencing the same `.cs` files — that is what the engine-free assembly buys us, and it is our
insurance against R3.

---

# 7. ACCEPTANCE CRITERIA

Objectively verifiable. A reviewer answers pass/fail with no judgement calls.

**Structure**
1. The project opens on the pinned Unity version with **zero** console errors and zero warnings from our own assemblies.
2. Five asmdefs exist with exactly the ADR-0001 names.
3. `ForgottenIsle.Core.asmdef` has `"noEngineReferences": true`, and adding `using UnityEngine;` to any Core file fails compilation.
4. `AssemblyGraphTests` passes, and manually adding a Core→Game reference makes it fail.
5. `ci/check-layering.sh` exits non-zero on an introduced violation.
6. `dotnet test ci/CoreTests.csproj` runs the Core suite green with no Unity installed.

**Flow**
7. Launching `Bootstrap.unity` reaches the main menu in under 2 s on device.
8. Any scene can be entered directly in-editor and self-heals to a valid state.
9. NEW GAME → curtain → `Zone_Ribcage`, capsule on the entry anchor.
10. The capsule moves under the on-screen stick and the camera under swipe.
11. Walking into the Fernmaw threshold transitions zones; exactly one zone unloads, one loads.
12. Resident zone count never exceeds 2 (asserted by `DevOverlay` and by a PlayMode test).
13. Pause blocks input: pushing the stick while paused moves nothing.
14. Domain simulation is paused while `Paused` (the clock does not advance).
15. QUIT TO MENU routes through `Loading` and unloads every session scene.
16. No `GameObject` leaks across a menu→zone→menu→zone cycle (asserted, not eyeballed).

**Save**
17. SAVE writes a slot; CONTINUE reads back act, zone, in-fiction time and recorded-percentage.
18. CONTINUE restores the **most recently recorded zone**, not the starting zone.
19. Slot metadata renders on the menu without deserializing the save body.
20. A hand-corrupted slot is reported unreadable and does not crash the menu.
21. A save written with a future schema version fails loudly with both version numbers in the message.
22. Three slots are independently writable and readable.

**Device**
23. Screenshots on **three distinct cutout geometries** (notch, Dynamic Island, Android punch-hole) show no control or label under a cutout or the gesture bar.
24. 60 fps sustained on the reference iPhone 12 in an empty zone, measured by `DevOverlay`.
25. A release IL2CPP/ARM64 build installs and runs on a physical iOS **and** a physical Android device.
26. Backgrounding the app for 40 minutes and resuming does not corrupt state or spike the clock.
27. Managed stripping at the shipped level does not break save deserialization.
28. **Gating:** kill the app during a save write, ten consecutive runs; the previous good save is recoverable every time.

**Localization**
29. No user-facing string is a literal in code. The CI key-extraction gate passes.
30. Switching the device to a locale with no table renders `#key#` markers; **no label is blank and nothing throws.**

---

# 8. DEFINITION OF DONE

- All 30 acceptance criteria pass, with 23/25/28 verified on physical hardware.
- CI is green on `main` and the layering gate has been proven to fail on a deliberate violation.
- A second engineer has followed `docs/dev-setup.md` from a clean machine to a running build
  **without asking the author a question**, and has signed off. This is the real test of Phase 1.
- The ADRs introduced in this phase are committed.
- `CHANGELOG.md` is updated.
- The 60-second demo below has been rehearsed on device.

## The demo script (phase review, 60 seconds)

| Time | What the reviewer sees | What it proves |
|---|---|---|
| 0:00–0:06 | Cold launch → `Bootstrap` → main menu. CONTINUE greyed out; no save exists. | Boot path, state machine, slot enumeration on an empty slot set. |
| 0:06–0:14 | Tap NEW GAME. Curtain in, progress fills, curtain out. | Async additive load, progress reporting, the activation gate. |
| 0:14–0:20 | Curtain clears on `Zone_Ribcage`. The player capsule is standing exactly on the entry anchor. | Additive set reconciliation, progress reporting, anchor placement. |
| 0:20–0:28 | Drive the capsule forward with the on-screen stick into the Fernmaw threshold. Curtain in, curtain out, `Zone_Fernmaw` (different ambient colour). | Zone→zone traversal is real: one zone unloads, one loads, `Session` and `Bootstrap` persist. |
| 0:28–0:33 | Point at `DevOverlay`: `InGame · Fernmaw · scenes:3 · 59.6 fps`. | The state machine's current state and the resident scene count are both observable on device. |
| 0:33–0:38 | Tap the pause dot. Pause screen appears; push the stick — the capsule does not move. | `Paused` is a real state; sim is paused; input is blocked. |
| 0:38–0:42 | Tap SAVE. Tap QUIT TO MENU. Curtain in, curtain out, main menu. | Save writes atomically; `QuitToMenu` routes through `Loading`; every session scene unloads. |
| 0:42–0:48 | CONTINUE now reads `Act 1 · Fernmaw · Day 1, 19:21 · 0% recorded`. Tap it. | The header round-tripped the *new* zone, not the old one. |
| 0:48–0:54 | Curtain clears on `Zone_Fernmaw`, capsule on the Fernmaw anchor. | CONTINUE restores the recorded zone, end to end, on device. |
| 0:54–0:60 | Switch the device language to French in Settings, relaunch. Menu labels render `#ui.menu.continue#` markers — deliberately, because `fr.csv` does not exist yet — and **no label is blank and nothing throws**. | The localization facade is load-bearing from day one: a missing locale degrades visibly, never silently, never to an empty button. |

The reviewer is told before the run: "Nothing in this demo is a gameplay system. Everything in this demo is the spine every gameplay system will hang from."

---

# 9. RISKS

| # | Risk | Likelihood | Impact | What we do about it |
|---|---|---|---|---|
| **R1** | **Newtonsoft's Unity package is not engine-free**, so `ForgottenIsle.Core` cannot reference it and the save codec has no serializer. | Medium | High — blocks task 8 and the whole save path. | This is the **first sub-task of work item 8**, not a discovery mid-sprint: add the precompiled reference to Core's asmdef and compile. If it fails, fall back to a hand-rolled `SaveWriter`/`SaveReader` over `Span<byte>` — ~250 lines, and it is the shape ADR-0013 migrates to anyway. Schedule buffer: 1 day, already allowed for in the 1-day task estimate. |
| **R2** | **`AsyncOperation.progress` semantics with `allowSceneActivation = false`** differ from the widely-reported 0.9 cap, making the curtain's progress rule wrong or the activation gate never fire. | Medium | Medium — a stuck loading screen, which is the worst possible Phase 1 bug. | Isolated to one constant (`ActivationThreshold`) and one method (`AllOpsDone`). Verified on day 1 of task 10 with a 5-minute editor spike before the rest of the loader is written. A watchdog in `SceneLoader.Tick` fails the load after 20 s and routes to `LoadFailed` → `MainMenu`, so the failure mode is a recoverable bounce to the menu, never a hang. |
| **R3** | **GameCI actions have no Unity 6 LTS image**, or licence activation is unreliable in CI. | Medium | High — without CI the layering gates are advisory, and advisory gates decay within two weeks. | Task 2 is scheduled on day 2 precisely so this surfaces immediately. Fallback: Unity Build Automation for the player builds, with GitHub Actions running stages 1–3 (version, format, layering) on `ubuntu-latest` with no Unity at all. Those three stages need no licence and catch most layering violations. `ForgottenIsle.Core` tests additionally run in a plain `dotnet test` project (`ci/CoreTests.csproj` referencing the same `.cs` files) — that is the point of the engine-free assembly, and it is our CI insurance. |
| **R4** | **Two engineers edit the same scene**, producing an unmergeable YAML conflict. | High over the phase | Medium — a lost day. | `CODEOWNERS` names one owner per `.unity` file from task 1. `Bootstrap.unity` is owned by the engineer on tasks 10–14 and is the only contended scene; everyone else works in prefab-free C#. UnityYAMLMerge is configured per developer as part of `docs/dev-setup.md`, which the second engineer's sign-off (DoD) verifies. |
| **R5** | **Portrait safe-area assumptions in `MOBILE_UX.md` §0 are wrong on a real device**, and menu buttons land under the notch or the gesture bar. | Medium | Medium | `SafeAreaDriver` reads `Screen.safeArea` and nothing hardcodes an inset; the risk is not the code but the **layout authored against the 390×844 reference**. Acceptance item 23 requires screenshots on three distinct cutout geometries before Phase 1 closes. Budget: a half-day of layout adjustment inside task 13. |
| **R6** | **Rename-over-existing is not atomic** on iOS or Android scoped storage, so the `.tmp` → `.isle` step can produce a partial file. | Low-medium | Very High — a corrupt save is the worst bug this product can ship. | Acceptance item 28 (kill-during-write, ten runs) is a **gating** criterion, run on device, not a nice-to-have. If it fails, the mitigation is already designed: the `.bak` rotation means a failed rename leaves the previous good save recoverable, and `SaveSlotService` already skips unreadable slots. If rename proves non-atomic we add a write-marker sentinel file and treat its presence on launch as "the `.isle` is suspect, prefer `.bak`". Half a day. |
| **R7** | **The team scope-creeps Phase 1** — a survival meter, an inventory grid, a real hydrophone, a nicer menu. | High | High — Phase 1 is the spine, and a spine that ships late delays everything. | §10 is the answer and it is read aloud at phase kickoff. Any addition needs an ADR and the tech lead's sign-off. The reviewer at phase review is instructed to mark §10 violations as **failures**, not as bonus work. |
| **R8** | **Hand-wired composition (ADR-0012) becomes unwieldy** faster than expected as Phase 2 systems land. | Low in Phase 1 | Medium in Phase 2 | The composition root is deliberately one method with one call per object. The VContainer spike is a scheduled Phase 2 task with a written go/no-go; adopting it changes `AppCompositionRoot.cs` and nothing else, because every other class already takes its dependencies through a constructor. |
| **R9** | **`.csv` does not import as a `TextAsset`**, so the localization table cannot be referenced from `AppConfig`. | Low | Low | Five-minute check in task 7. Fallback is renaming to `.txt` and updating one serialized field. Listed here only so nobody spends an afternoon on it. |
| **R10** | **Domain-reload-off is enabled by someone chasing iteration speed**, and the composition root's static `_instance` guard produces phantom state across play sessions. | Medium | Medium | The setting is explicitly Reload Domain **ON** in §1.3 and is checked by acceptance item 4's sibling in the project-settings review. Turning it off requires a static-state audit and an ADR — it is a Phase 3 candidate, not a Phase 1 convenience. |

---

# 10. WHAT PHASE 1 DELIBERATELY DOES NOT DO

Read at phase kickoff and again at phase review. **Each line below appearing in the build is a review failure, not a bonus.**

**No gameplay systems.** No inventory, no item definitions, no crafting, no combination, no survival meters, no puzzles, no discoveries, no Field Slate, no camp, no story graph, no quests, no weather, no tide, no in-game clock beyond a stub `TimeState` field, no hydrophone, no acoustics, no Reel Deck. The nine domain systems in `CORE_SYSTEMS.md` are **not started**.

**No content.** Two grey-box zones with a floor and three boxes each. No art, no audio (not even ambience), no materials beyond an unlit grey, no lighting bake, no props, no VFX, no music. Seven of the nine zones exist only as `ZoneCatalog` rows.

**No real player.** A capsule on a `CharacterController`. No animation, no first-person hands, no head bob, no comfort settings, no camera polish, no snap turn, no attention ring, no authored traversal, no crouch, no run curve, no auto-walk, no travel points, no Body Camera.

**No HUD.** No vitals glyphs, no bearing tape, no interaction prompt card, no contextual action button, no inventory sheet, no toasts, no discovery overlay, no subtitles, no haptics. The only on-screen elements are three menu screens and a development-build-only `DevOverlay`.

**No touch control scheme.** The placeholder player is driven by whatever the `Player` action map binds — a keyboard in the editor and a crude on-screen stick on device. The floating joystick, camera swipe zone, thumb-zone arbitration and `TouchRouter` in `MOBILE_UX.md` §2 are **Phase 2**.

**No data bake pipeline.** No ScriptableObject authoring types, no baker, no validator, no `isle.catalog`, no `IDefinitionRepository`, no content IDs beyond `ZoneId` and `SceneKey`. `DATA_SAVE.md` Parts 1–2 are **Phase 2**.

**No Addressables loading.** The package is installed; nothing loads through it. Zone streaming via Addressables, group layout, duplicate-dependency analysis and on-demand delivery are Phase 3.

**No localization package binding.** The locale set and one String Table Collection are created; runtime lookup goes through our CSV facade. One shipping locale (`en`). No plural rules, no CJK font atlases, no RTL readiness, no loc-key extractor. `ARCHITECTURE.md` §9 beyond the facade is Phase 2.

**No save migration.** `SaveSchema.Current = 1` and there is exactly one version, so there is no `ISaveMigrator`, no chain, no `retired_ids.json`, no captured-save fixtures. The migration framework lands the first time the schema goes to 2.

**No MessagePack, no compression, no thumbnails, no cloud save, no autosave triggers.** Saving is manual, from the pause menu, JSON, uncompressed. Three manual slots exist in the API; the menu uses one.

**No performance work.** No object pooling, no LOD, no occlusion culling, no shader variant stripping, no texture budgets, no quality-tier auto-detection, no zero-allocation assertions beyond the dispatcher's structural guarantee. `DevOverlay` reports fps; nothing optimises it. `ARCHITECTURE.md` §8 is Phase 4.

**No Roslyn analyzers.** The layering gates in Phase 1 are the compiler, the asmdef graph test and `check-layering.sh`. `FI0001`–`FI0012` are Phase 2, once there is gameplay code for them to police.

**No DI container.** Hand-wired, per ADR-0012.

~~**No UI Toolkit at runtime**, per ADR-0002.~~ **Superseded by ADR-0014:** UI Toolkit is the runtime UI. ADR-0002 (Core never references the engine) is untouched by that.

**No iOS CI.** Nightly only, on a self-hosted runner, from Phase 2.

---

## Appendix — the Phase 1 verification ledger seed

Every item below is created in `docs/verification-ledger.md` on day 1 with a named owner. Acceptance item 30 requires all of them closed or explicitly re-scoped.

| ID | Claim to verify | Owner | Blocks |
|---|---|---|---|
| V-01 | Exact Unity 6 LTS patch available; pinned in `ProjectVersion.txt` | Tech lead | Task 1 |
| V-02 | Package identifiers and versions for all ten Phase 1 packages | Tech lead | Task 1 |
| V-03 | Whether TextMeshPro ships inside `com.unity.ugui` or separately | UI engineer | Task 1 |
| V-04 | `com.unity.nuget.newtonsoft-json`'s shipped assembly is engine-free | Core engineer | Task 8 |
| V-05 | asmdef reference storage form (name vs `GUID:`) in this project | Core engineer | Task 2 |
| V-06 | `AsyncOperation.progress` cap with `allowSceneActivation = false` | Systems engineer | Task 10 |
| V-07 | `.csv` imports as a `TextAsset` in Unity 6 | Core engineer | Task 7 |
| V-08 | Input System "Generate C# Class" output path and whether it is committed | Systems engineer | Task 14 |
| V-09 | `FileStream.Flush(true)` + rename-over-existing atomicity on iOS and Android scoped storage | Platform engineer | Acceptance 28 |
| V-10 | `System.Security.Cryptography.SHA256` availability under IL2CPP on both platforms | Platform engineer | Task 8 |
| V-11 | UnityYAMLMerge binary path per developer OS | Tech lead | Task 1 |
| V-12 | GameCI action versions and Unity 6 LTS image availability | Build engineer | Task 2 |
| V-13 | `Screen.safeArea` values on the three reference cutout geometries | UI engineer | Acceptance 23 |
| V-14 | Incremental GC default state and per-platform availability in Unity 6 | Platform engineer | Phase 4 (re-scoped) |
| V-15 | Android target API level required at submission | Producer | Phase 5 (re-scoped) |