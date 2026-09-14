# VARDHOLM — TECHNICAL ARCHITECTURE
## v1.0 · Unity 6 LTS · C# · URP · New Input System · Portrait Mobile (iOS + Android)

**Owner:** Technical Director
**Status:** Production baseline. Changes require a written ADR in `/docs/adr/` and TD sign-off.
**Canon dependency:** `STORY_BIBLE.md` v1.0. Every identifier in this document that names fiction (`Fernmaw`, `ZoneId.Sweatways`, `item.water_vine_coil`) is canon-derived.

---

### 0. VERIFICATION LEDGER (read this first)

This document names Unity packages and features. Everything below is either (a) stated as our design decision, which needs no external verification, or (b) a claim about Unity, which is tagged. **Anything tagged `⚠ VERIFY` has not been confirmed by me against a running Unity 6 LTS install and must be spiked by the owning engineer before it enters the build.**

| Claim | Confidence | Action |
|---|---|---|
| `com.unity.inputsystem`, `com.unity.render-pipelines.universal`, `com.unity.addressables`, `com.unity.localization`, `com.unity.test-framework`, `com.unity.memoryprofiler`, `com.unity.burst`, `com.unity.collections` exist and are the correct package names | High | Pin exact versions in `Packages/manifest.json` at project init; record them in an ADR |
| URP 17 (Unity 6) requires the Render Graph API for `ScriptableRenderPass`, with a deprecated Compatibility Mode | Medium-high | `⚠ VERIFY` — write our two renderer features against Render Graph from day one regardless |
| Unity 6 GPU Resident Drawer + GPU Occlusion Culling exist and require compute-capable graphics APIs | Medium | `⚠ VERIFY on iPhone 12 / Adreno 6xx / Mali-G76 specifically.` Assume **off** for mobile in the budget until a spike proves otherwise |
| Adaptive Probe Volumes (APV) are available in URP on Unity 6 | Medium | `⚠ VERIFY` mobile support + memory cost before committing the lighting plan |
| `UnityEngine.Awaitable` exists in Unity 6 and is pooled/main-thread-aware | Medium-high | `⚠ VERIFY` its allocation profile with the Profiler before we claim "zero-alloc async" |
| `ProfilerRecorder` (`Unity.Profiling`) can sample `"Draw Calls Count"`, `"SetPass Calls Count"`, `"GC Allocated In Frame"`, `"Total Reserved Memory"` | Medium-high | `⚠ VERIFY exact counter name strings per platform` — they differ; the HUD must degrade gracefully when a recorder reports `Valid == false` |
| asmdef JSON field `"noEngineReferences": true` exists and removes the implicit UnityEngine reference | High | Confirmed by our own asmdef compile gate on day 1 — it either compiles or it doesn't |
| iPhone 12 per-app memory ceiling before jetsam | **Unverified** — community figures cluster near 2 GB on 4 GB devices | `⚠ VERIFY` by shipping a memory-ramp TestFlight build that allocates until termination. Our budget below assumes a **1.35 GB working-set target**, which is a design decision, not a measurement |
| VContainer (hadashiA) is Unity 6-compatible and allocation-light at resolve time | Medium | `⚠ VERIFY` version compatibility and license (MIT, per its repo — confirm) before adoption |
| TextMeshPro performs Arabic shaping / bidi natively | **I believe it does not** | `⚠ VERIFY.` RTL plan below assumes we need a shaping step |
| `com.unity.addressables.android` (Play Asset Delivery integration) | Low-medium — I am not certain of this package's name/status in Unity 6 | `⚠ VERIFY` before planning Android delivery around it; fall back to plain AAB + Addressables remote groups |

**Rule:** no engineer may write "Unity does X" in a design doc, a PR description or a code comment without either a link to the docs page or a `⚠ VERIFY` tag. This ledger is amended, not deleted, as items are cleared.

---

## 1. ASSEMBLY DEFINITION LAYOUT

### 1.1 The five assemblies

```
ForgottenIsle.Core          (no UnityEngine)     ← the game, as a deterministic library
      ▲
      │
ForgottenIsle.Game          (UnityEngine)        ← the game, as a Unity application
      ▲
      │
ForgottenIsle.UI            (UnityEngine + UGUI) ← the game, as pixels and touches
      ▲
      │                     ForgottenIsle.Editor (UnityEditor)
      │                           ▲
ForgottenIsle.Tests.EditMode ─────┘
ForgottenIsle.Tests.PlayMode
```

**Reference direction is strictly downward.** There are no upward references, no sibling references between `UI` and `Editor`, and no cycles. If you need to go up, you need an interface defined below and injected from above.

---

#### `ForgottenIsle.Core` — the deterministic domain layer

| | |
|---|---|
| **Purpose** | The entire rules-of-the-game: world state, inventory, crafting graph, survival simulation, puzzle state machines, story/act gating, the Field Slate evidence model, command validation and execution, save serialization. Given a save file and an ordered command log, it produces a bit-identical world state on any machine. |
| **References UnityEngine?** | **No.** `"noEngineReferences": true`. |
| **MAY reference** | Nothing in the project. Externally: BCL (`System.*` on .NET Standard 2.1), and **one** precompiled managed dependency — `Newtonsoft.Json` via `com.unity.nuget.newtonsoft-json` — used only for save/catalog (de)serialization. `⚠ VERIFY` that this assembly is itself engine-free; if it is not, we hand-roll a writer. |
| **MUST NOT reference** | `UnityEngine`, `UnityEditor`, `ForgottenIsle.Game`, `ForgottenIsle.UI`, `Unity.Collections`, `Unity.Mathematics` (see §1.2), TextMeshPro, Addressables, the Localization package, or any `System.IO` path touching `Application.persistentDataPath`. |
| **Contains no** | MonoBehaviours, ScriptableObjects, coroutines, `Debug.Log`, `Time.*`, `Random.*`, `float` time deltas sourced from the frame, GameObjects, or display strings. |

#### `ForgottenIsle.Game` — the Unity application layer

| | |
|---|---|
| **Purpose** | Everything that makes Core visible, audible and interactive: MonoBehaviour adapters, the single ticker, scene/zone streaming, Addressables, audio director, the hydrophone spectrogram renderer, save file I/O, platform services, input controllers, the composition root, and the authored-content importers that bake ScriptableObjects into Core's engine-free data tables. |
| **References UnityEngine?** | Yes. |
| **MAY reference** | `ForgottenIsle.Core`, `UnityEngine`, `Unity.InputSystem`, `Unity.RenderPipelines.Universal.Runtime`, `Unity.Addressables`, `Unity.Localization`, `Unity.TextMeshPro`, `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`. |
| **MUST NOT reference** | `ForgottenIsle.UI`, `ForgottenIsle.Editor`, `UnityEditor` (outside `#if UNITY_EDITOR`, which is permitted only in `Runtime/Debug/` and is scanned for in CI). |

#### `ForgottenIsle.UI` — presentation and input surface

| | |
|---|---|
| **Purpose** | Views, view-models, screen stack, UGUI + TextMeshPro, localized text binding, safe-area handling, the touch/gesture layer on top of the New Input System, transitions, the performance HUD's rendering half. |
| **References UnityEngine?** | Yes. |
| **MAY reference** | `ForgottenIsle.Game` (for controller interfaces and event channels only), `ForgottenIsle.Core` (for **read-only value types**: `ItemId`, `ResultCode`, `LocKey`, DTOs), `UnityEngine.UI`, `Unity.TextMeshPro`, `Unity.InputSystem`, `Unity.Localization`. |
| **MUST NOT reference** | `ForgottenIsle.Editor`, `UnityEditor`. **MUST NOT call any mutating API.** See §2.3 for how that is enforced at the type level, not just by convention. |

#### `ForgottenIsle.Editor` — tooling

| | |
|---|---|
| **Purpose** | Custom inspectors, the content bakery (ScriptableObject → Core data tables), the Register/notch-and-crescent authoring tool, the damper-console tuning window, Addressables group automation, build scripts, the localization key extractor, the asmdef graph validator. |
| **References UnityEngine?** | Yes, plus `UnityEditor`. |
| **MAY reference** | `ForgottenIsle.Core`, `ForgottenIsle.Game`, `ForgottenIsle.UI`, `UnityEditor`. |
| **MUST NOT** | Be referenced by any runtime assembly. `"includePlatforms": ["Editor"]` makes this structural. |

#### `ForgottenIsle.Tests.EditMode` / `ForgottenIsle.Tests.PlayMode`

| | |
|---|---|
| **EditMode purpose** | ~85% of all test code. Pure NUnit against `ForgottenIsle.Core`: command validation, crafting graph, survival curves, puzzle solvers, save round-trip, determinism (same seed + same command log → same state hash), and the asmdef graph validator itself. Runs in seconds. No scene, no play mode, no Unity API. |
| **PlayMode purpose** | Integration only: bootstrap boots, a zone streams in and out without leaking, the command→event→UI round trip fires, input actions map to controller calls, save/load survives a scene transition. Slow, few, and each one is justified in its XML doc comment. |
| **MAY reference** | EditMode: Core, Game, UI, Editor. PlayMode: Core, Game, UI (**not** Editor). Both: `UnityEngine.TestRunner`, `nunit.framework.dll`, and NSubstitute (`⚠ VERIFY` Unity 6 compatibility; if it is troublesome, hand-written fakes — Core's interfaces are tiny and hand-faking them is cheap). |

---

### 1.2 How engine-free is Core, exactly — and where the line is drawn

"Engine-free" is not a purity ritual. It buys us four concrete things: **(1)** an EditMode test suite that runs the entire game's rules in under 10 seconds with no domain reload, **(2)** determinism we can assert on, **(3)** the ability to fuzz 10,000 command sequences in CI, and **(4)** a hard structural guarantee that UI cannot mutate state, because the mutation APIs live in an assembly UI can only see through read-only types.

It costs us four things, and here is exactly how each is paid:

**Vector math.** `UnityEngine.Vector3` is unavailable. `Unity.Mathematics` is *probably* engine-free but I have not confirmed it (`⚠ VERIFY`), and adopting it drags in Burst compile-time constraints we do not want in a test-first assembly. **Decision:** Core defines its own `readonly struct Vec3 { public readonly float X, Y, Z; }` plus a `Vec3Math` static class. Conversion extensions (`Vec3 ⇄ Vector3`) live in `ForgottenIsle.Game/Interop/CoreInterop.cs`. This is ~200 lines we write once. Core's use of position is shallow anyway — zone id, node id, and a coarse position for "which side of the Combs are you on"; it never does physics.

**Time.** Core has no `Time.deltaTime`. All simulation is driven by `Tick(float dt)` where `dt` is a **compile-time constant** (`SimConstants.SimDt = 0.1f`) passed in by the ticker (§7). Core also exposes `IClock` for in-fiction wall-clock (the tide table, Lorvik's 11-minute pressure cycle) — implemented in Game, faked in tests.

**Randomness.** Banned: `UnityEngine.Random`, `System.Random`. Core ships `PcgRandom`, a seeded PCG32 struct stored **inside `WorldState`** so it serializes with the save. Reloading a save and repeating an action gives the same result. There is exactly one RNG stream per subsystem (`WorldState.Rng.Weather`, `.Loot`, `.Ambient`) so adding a new consumer cannot shift an existing one's sequence.

**Logging.** Banned: `Debug.Log`. Core takes `ICoreLog` with `Warn(LogCode code, in ResultArg a0)` — *codes, not strings*, so logging allocates nothing and needs no `string.Format`. Game implements it over `Debug.Log` with a lookup table; tests implement it as an assertion sink that fails the test on any `Warn`.

**Where the line genuinely bends:** Core does **not** own file I/O. `SaveGameCommand` produces a `SaveDocument` (a POCO) and a `byte[]`; `ForgottenIsle.Game/Saves/SaveFileStore.cs` writes it, handles `Application.persistentDataPath`, atomic rename, and iOS backup exclusion. Core is pure; the filesystem is not deterministic and does not belong there.

**Coverage target:** `ForgottenIsle.Core` ≥ 80% line coverage, and **100% of `ICommandHandler.Validate` branches**. Enforced in CI (§10).

---

### 1.3 asmdef JSON skeletons

`Assets/Scripts/Core/ForgottenIsle.Core.asmdef`
```json
{
  "name": "ForgottenIsle.Core",
  "rootNamespace": "ForgottenIsle.Core",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [ "Newtonsoft.Json.dll" ],
  "autoReferenced": false,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true
}
```
> `noEngineReferences: true` is the load-bearing line. `overrideReferences: true` + an explicit `precompiledReferences` list means no stray DLL in `Plugins/` can silently become a Core dependency. `autoReferenced: false` prevents Unity's default `Assembly-CSharp` from picking it up if someone drops a loose script outside an asmdef.

`Assets/Scripts/Game/ForgottenIsle.Game.asmdef`
```json
{
  "name": "ForgottenIsle.Game",
  "rootNamespace": "ForgottenIsle.Game",
  "references": [
    "ForgottenIsle.Core",
    "Unity.InputSystem",
    "Unity.RenderPipelines.Universal.Runtime",
    "Unity.Addressables",
    "Unity.ResourceManager",
    "Unity.Localization",
    "Unity.TextMeshPro",
    "Unity.Burst",
    "Unity.Collections",
    "Unity.Mathematics",
    "VContainer"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": false,
  "noEngineReferences": false
}
```
> `⚠ VERIFY` every one of these assembly *names* against the installed packages — package display names and assembly names differ (e.g. TextMeshPro ships inside `com.unity.ugui` in recent versions). Fix them at project init; do not guess.

`Assets/Scripts/UI/ForgottenIsle.UI.asmdef`
```json
{
  "name": "ForgottenIsle.UI",
  "rootNamespace": "ForgottenIsle.UI",
  "references": [
    "ForgottenIsle.Core",
    "ForgottenIsle.Game",
    "Unity.InputSystem",
    "Unity.Localization",
    "Unity.TextMeshPro",
    "UnityEngine.UI",
    "VContainer"
  ],
  "autoReferenced": false,
  "noEngineReferences": false
}
```

`Assets/Scripts/Editor/ForgottenIsle.Editor.asmdef`
```json
{
  "name": "ForgottenIsle.Editor",
  "rootNamespace": "ForgottenIsle.Editor",
  "references": [ "ForgottenIsle.Core", "ForgottenIsle.Game", "ForgottenIsle.UI", "Unity.Addressables.Editor", "Unity.Localization.Editor" ],
  "includePlatforms": [ "Editor" ],
  "autoReferenced": false,
  "noEngineReferences": false
}
```

`Assets/Tests/EditMode/ForgottenIsle.Tests.EditMode.asmdef`
```json
{
  "name": "ForgottenIsle.Tests.EditMode",
  "rootNamespace": "ForgottenIsle.Tests.EditMode",
  "references": [
    "UnityEngine.TestRunner", "UnityEditor.TestRunner",
    "ForgottenIsle.Core", "ForgottenIsle.Game", "ForgottenIsle.UI", "ForgottenIsle.Editor"
  ],
  "includePlatforms": [ "Editor" ],
  "overrideReferences": true,
  "precompiledReferences": [ "nunit.framework.dll", "NSubstitute.dll" ],
  "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
  "autoReferenced": false
}
```

`Assets/Tests/PlayMode/ForgottenIsle.Tests.PlayMode.asmdef`
```json
{
  "name": "ForgottenIsle.Tests.PlayMode",
  "rootNamespace": "ForgottenIsle.Tests.PlayMode",
  "references": [
    "UnityEngine.TestRunner",
    "ForgottenIsle.Core", "ForgottenIsle.Game", "ForgottenIsle.UI"
  ],
  "includePlatforms": [],
  "overrideReferences": true,
  "precompiledReferences": [ "nunit.framework.dll" ],
  "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
  "autoReferenced": false
}
```

---

### 1.4 Enforcing the dependency rules in CI

Three independent gates, because each catches something the others miss.

**Gate A — the compiler.** `noEngineReferences` on Core means any `using UnityEngine;` is a compile error. This is free and cannot be argued with. It runs on every PR as part of the build step.

**Gate B — the asmdef graph test.** An EditMode test that reads the asmdef JSON on disk and asserts the *exact* edge set. This catches the case where someone adds a reference that compiles fine but violates the layering (e.g. `Game → UI`).

```csharp
// Assets/Tests/EditMode/Architecture/AssemblyGraphTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace ForgottenIsle.Tests.EditMode.Architecture
{
    public sealed class AssemblyGraphTests
    {
        // The ONLY project-internal references each assembly is permitted.
        static readonly Dictionary<string, string[]> Allowed = new()
        {
            ["ForgottenIsle.Core"]             = new string[0],
            ["ForgottenIsle.Game"]             = new[] { "ForgottenIsle.Core" },
            ["ForgottenIsle.UI"]               = new[] { "ForgottenIsle.Core", "ForgottenIsle.Game" },
            ["ForgottenIsle.Editor"]           = new[] { "ForgottenIsle.Core", "ForgottenIsle.Game", "ForgottenIsle.UI" },
            ["ForgottenIsle.Tests.EditMode"]   = new[] { "ForgottenIsle.Core", "ForgottenIsle.Game", "ForgottenIsle.UI", "ForgottenIsle.Editor" },
            ["ForgottenIsle.Tests.PlayMode"]   = new[] { "ForgottenIsle.Core", "ForgottenIsle.Game", "ForgottenIsle.UI" },
        };

        [Test]
        public void Every_project_assembly_references_only_what_the_layer_map_permits()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def  = AsmDef.Parse(File.ReadAllText(path));      // tiny JSON reader in Tests
                if (!Allowed.TryGetValue(def.Name, out var allowed)) continue;   // 3rd-party asmdefs

                var internalRefs = def.References
                    .Select(AsmDef.StripGuidPrefix)                   // handles "GUID:xxxx" form
                    .Where(r => r.StartsWith("ForgottenIsle."))
                    .ToArray();

                var illegal = internalRefs.Except(allowed).ToArray();
                Assert.IsEmpty(illegal,
                    $"{def.Name} ({path}) illegally references: {string.Join(", ", illegal)}.\n" +
                    "Layering is UI -> Game -> Core. Add an interface in the lower layer and inject it.");
            }
        }

        [Test]
        public void Core_declares_noEngineReferences()
        {
            var def = AsmDef.Parse(File.ReadAllText("Assets/Scripts/Core/ForgottenIsle.Core.asmdef"));
            Assert.IsTrue(def.NoEngineReferences,
                "ForgottenIsle.Core must stay engine-free. See ARCHITECTURE.md §1.2.");
        }
    }
}
```

> **Note on asmdef references using GUIDs.** Unity may store references as `"GUID:9f3..."` instead of assembly names depending on project settings. `AsmDef.StripGuidPrefix` resolves GUID form back to a name via `AssetDatabase.GUIDToAssetPath`. `⚠ VERIFY` which form our project writes and make the parser handle both — do not assume.

**Gate C — the banned-symbol scan.** A shell step in CI, because it also catches things inside `#if` blocks and string-based reflection that the compiler and asmdef graph both miss:

```bash
# ci/check-layering.sh — exits nonzero on violation
set -euo pipefail
fail=0

grep -rn --include='*.cs' -E '^\s*using\s+(UnityEngine|UnityEditor)' Assets/Scripts/Core \
  && { echo "::error::UnityEngine/UnityEditor used in Core"; fail=1; }

# UI may not touch mutation entry points.
grep -rn --include='*.cs' -E '\b(WorldState|IWorldStateMutator|SceneManager\.Load|Addressables\.LoadScene)\b' Assets/Scripts/UI \
  && { echo "::error::UI reached past its layer (state mutation or scene loading)"; fail=1; }

# UNITY_EDITOR in runtime code is allowed only under Runtime/Debug/.
grep -rln --include='*.cs' 'UNITY_EDITOR' Assets/Scripts/Game Assets/Scripts/UI \
  | grep -v '/Runtime/Debug/' \
  && { echo "::error::UNITY_EDITOR outside Runtime/Debug/"; fail=1; }

exit $fail
```

---

## 2. LAYERED ARCHITECTURE

```
┌──────────────────────────────────────────────────────────────┐
│ UI            Views, ViewModels, screen stack, touch gestures │  ForgottenIsle.UI
│               Reads: event channels + read-only DTOs          │
│               Writes: NOTHING                                 │
├──────────────────────────────────────────────────────────────┤
│ Controllers   Input interpretation, gating, command assembly  │  ForgottenIsle.Game
│               "the player tapped X" -> "dispatch CommandY"    │
├──────────────────────────────────────────────────────────────┤
│ Services      Engine-facing capabilities behind Core-defined  │  ForgottenIsle.Game
│               interfaces: audio, streaming, save I/O, haptics │
├──────────────────────────────────────────────────────────────┤
│ Commands      Validate + Execute. The ONLY mutation entry     │  ForgottenIsle.Core
│               point into world state. Returns CommandResult.  │
├──────────────────────────────────────────────────────────────┤
│ Domain        InventorySystem, CraftingSystem, SurvivalSystem,│  ForgottenIsle.Core
│ Systems       PuzzleSystem, StorySystem, DiscoverySystem...   │
│               Stateless static/instance logic over WorldState │
├──────────────────────────────────────────────────────────────┤
│ World State   WorldState: plain data. Versioned. Serializable.│  ForgottenIsle.Core
└──────────────────────────────────────────────────────────────┘
```

### 2.1 Layer contracts

**World State.** `WorldState` is a single object graph of POCOs and structs: `Player`, `Inventory`, `Survival`, `Zones[]`, `Interactables`, `Puzzles`, `Story`, `FieldSlate`, `Rng`, `Clock`. It has **no methods that contain rules** — only `Commit()`/`BeginTransaction()` and a `StateVersion` counter. It does not know it is a game. It is fully serializable and its serialized form is the save file. Nothing outside `ForgottenIsle.Core` holds a reference to it; §2.3 explains how that is guaranteed.

**Domain Systems.** Rule implementations. Signature shape is always `static Result Do(SubState state, params..., IEventSink events)` — they take the narrowest slice of state they need. `InventorySystem` takes `Inventory`, not `WorldState`. This is what makes them testable in three lines. Systems may read across slices via a passed-in `in WorldQuery` read struct, but may never mutate a slice they were not handed.

**Commands.** The mutation boundary. Every change to `WorldState` in the entire product happens inside an `ICommandHandler<T>.Execute`. There are no exceptions and no back doors; `WorldState`'s mutable fields are `internal` to `ForgottenIsle.Core`, so the compiler enforces it for everything outside the assembly, and the handler-only rule inside Core is enforced by a Roslyn analyzer (§11, rule 3).

**Services.** Engine capability behind a Core-declared interface. `IAudioDirector`, `IZoneStreamer`, `ISaveFileStore`, `IHaptics`, `IPlatformClock`, `ITelemetry`. Core declares the interface; Game implements it; the composition root wires it. Services **may not** dispatch commands — they are effectors, not deciders. (One deliberate exception, called out and reviewed: `ZoneStreamDirector` dispatches `ZoneLoadedCommand` on completion. It is documented as a *completion notification*, not a decision, and it is the only one.)

**Controllers.** They turn intent into commands. `InteractionController`, `CraftingController`, `DamperConsoleController`, `TravelController`, `FieldSlateController`. A controller owns: input action subscriptions, debounce/gating, translation from screen-space to `WorldObjectId`, command assembly, and routing the `CommandResult` to feedback. A controller holds **no game state** — only transient input state (is a drag in progress, when was the last tap).

**UI.** Views render; ViewModels hold the last-known projection of domain data; Presenters subscribe to event channels. A View's only outbound call is to a controller interface, and every controller interface method returns `void` or `CommandResult` — never state.

### 2.2 The enforcement rule: UI NEVER mutates state

Three mechanisms, in increasing order of strength:

1. **Convention** (weakest): documented here, reviewed in PR.
2. **Visibility**: `WorldState`'s setters and all `List<T>` fields are `internal`. `ForgottenIsle.UI` is a different assembly with no `InternalsVisibleTo`. UI *cannot* write to world state even if it obtains the object.
3. **Reachability**: UI never obtains the object. `IWorldReader` — the only read interface exposed upward — returns immutable `readonly struct` DTOs (`InventorySlotView`, `SurvivalReadout`, `PuzzleFaceView`), not live references. The DI container registers `IWorldReader` in the UI scope; `WorldState` itself is registered only in the Core scope, which UI's scope cannot resolve from.

`ForgottenIsle.UI` has exactly one write-shaped dependency: `ICommandDispatcher`. And it is *deliberately not injected into Views* — only Controllers get it. Views talk to controllers. This is checked by Gate C's grep and by rule 2 in §11.

### 2.3 End-to-end call path: player taps COLLECT

**Canon note.** The brief's "coconut" has no place on Vardholm — Fernmaw is tree-fern canopy inside a collapsed aqueduct trench. The canonical equivalent, and the actual Act 1 object, is a **water-vine coil**: a cut length of liana in Fernmaw that yields ~150 ml of drinkable water and a strand of raw cordage. Same mechanical shape — a one-shot collectable with limited uses. `ItemId item.fernmaw.water_vine_coil`, world node `node.fernmaw.vine_cluster_03`.

Player state: standing in Fernmaw, the proximity prompt is up, the COLLECT button is visible in the thumb zone of a 390×844 portrait screen.

| # | Class (assembly) | Call | Data passed |
|---|---|---|---|
| 1 | `CollectPromptView` (UI) | `Button.onClick` → `OnCollectTapped()` | none |
| 2 | `CollectPromptView` (UI) | `_presenter.RequestCollect(_boundTarget)` | `WorldObjectId(0x4E2A_0003)` — an opaque 64-bit handle the presenter gave it. The View has never seen a `GameObject` reference to the vine, nor an `ItemId`. |
| 3 | `InteractionPresenter` (UI) | `_interaction.Collect(in request)` | `CollectRequest { Target = 0x4E2A_0003, Source = InputSource.TouchButton }` |
| 4 | `InteractionController` (Game/Controllers) | gate checks: `_screenStack.IsModalOpen == false`, `_tapDebounce.TryConsume(0.25f)`, `_streamer.IsTransitioning == false`. Then builds the command. | — |
| 5 | `InteractionController` (Game) | `_dispatcher.Dispatch(in cmd)` | `CollectItemCommand { Actor = ActorId.Player, Target = WorldObjectId(0x4E2A_0003), RequestedCount = 1 }` — a `readonly struct`, passed by `in`. **Zero allocations.** |
| 6 | `CommandDispatcher` (Core) | resolves `ICommandHandler<CollectItemCommand>` from its type map (one dictionary lookup, one interface cast, no boxing); opens `_state.BeginTransaction()` | — |
| 7 | `CollectItemHandler.Validate` (Core) | `WorldQuery.TryGetInteractable(state, target, out node)`; `node.ZoneId == state.Player.ZoneId`; `node.Harvest.UsesRemaining > 0`; `InventorySystem.CanAdd(state.Inventory, node.Harvest.Yield, 1, out var why)` | returns `CommandResult.Fail(ResultCode.InventoryFull, ResultArg.Item(yield))` on rejection — nothing has been mutated |
| 8 | `CollectItemHandler.Execute` (Core) | `InventorySystem.Add(state.Inventory, item.fernmaw.water_vine_coil, 1, events)` | raises `ItemAddedEvent { Item, Count = 1, SlotIndex = 4, NewTotal = 1 }` |
| 9 | " | `HarvestSystem.ConsumeUse(node, events)` — `UsesRemaining` 3→2 | raises `InteractableStateChangedEvent { Target, NewVisualState = Harvested, RegrowsAtMinutes = 0 }` |
| 10 | " | `DiscoverySystem.TryRecordFirstSight(state.FieldSlate, discovery.water_vine, state.Clock.Minutes, events)` — first collection only | raises `FieldSlateEntryAddedEvent { EntryId, Category = Flora, IsFirstSight = true }` |
| 11 | " | returns `CommandResult.Ok(ResultCode.ItemCollected, ResultArg.Item(yield), ResultArg.Int(1))` | — |
| 12 | `CommandDispatcher` (Core) | `_state.Commit()` → `StateVersion++`; on any thrown exception, `Rollback()` and return `ResultCode.InternalError` | — |
| 13 | `InteractionController` (Game) | inspects the result. On `Ok` it does **nothing visible** — the events drive presentation. On failure: `_feedback.Report(in result)` | `CommandResult` (a 24-byte struct, returned by value) |
| 14 | `GameLoop.LateUpdate` (Game) | `_eventQueue.Flush()` — once per frame, in emission order, after all commands for the frame have run | — |
| 15 | `InventoryHudPresenter` (UI) | `OnItemAdded(in ItemAddedEvent e)` → `_slots[4].SetItem(e.Item, e.NewTotal)`; slot pulse tween | struct, by `in`, no boxing |
| 16 | `WorldObjectBinder` (Game) | `OnInteractableStateChanged(in e)` → looks up `WorldBindingRegistry[0x4E2A_0003]` → the vine `GameObject` → plays `Harvest` state on its `Animator`, swaps the LOD0 mesh for the cut variant | — |
| 17 | `AudioDirector` (Game) | `OnItemAdded` → one-shot `sfx.collect.vine_wet` from the pooled 8-voice SFX bus, positioned at the binder's transform | — |
| 18 | `FieldSlateBadgePresenter` (UI) | `OnFieldSlateEntryAdded` → badge count +1, 900 ms glow, no modal — Nadia's notebook fills silently | — |

**What is notable about this path.** The View never learned what item it collected. The controller never learned whether the inventory had room. Core never learned that a `GameObject` exists. And the only object that changed is inside an assembly the UI cannot write to. Nine classes, one allocation-free command struct, one `CommandResult` returned by value, and three struct events flushed once at end of frame.

---

## 3. COMMAND PATTERN

### 3.1 Shape

```csharp
// ForgottenIsle.Core/Commands/ICommand.cs
namespace ForgottenIsle.Core.Commands
{
    /// <summary>Marker. All commands are readonly structs — no allocation per player action.</summary>
    public interface ICommand { }

    public interface ICommandHandler<TCommand> where TCommand : struct, ICommand
    {
        /// <summary>Pure. MUST NOT mutate anything. Called before Execute, and also called
        /// on its own by UI affordance queries (grey out a button without performing the act).</summary>
        CommandResult Validate(in TCommand command, in WorldQuery query);

        /// <summary>Mutates. Only called if Validate returned Ok. MUST NOT re-validate
        /// defensively — if Execute can fail, that check belongs in Validate.</summary>
        CommandResult Execute(in TCommand command, WorldState state, IEventSink events);
    }
}
```

**Validation vs execution is a hard split, and it is not ceremony — it pays for three features:**

1. **Predictive UI.** The COLLECT button greys out because `InteractionController` calls `_dispatcher.CanExecute(in cmd)` on the frame the prompt appears — the same `Validate` code path, zero risk of divergence between "the button says I can" and "the game says I can't."
2. **Failure messaging without a second rules implementation.** The reason code that greys the button is the reason code in the toast.
3. **Atomicity.** If `Execute` throws, we have a bug, not a gameplay outcome. The transaction rolls back and CI treats any `InternalError` in an automated playthrough as a failing test.

### 3.2 The result type

```csharp
// ForgottenIsle.Core/Commands/CommandResult.cs
namespace ForgottenIsle.Core.Commands
{
    public enum ResultCode : ushort
    {
        // 0–99 success
        Ok = 0, ItemCollected = 1, ItemsCombined = 2, ItemCrafted = 3, ItemConsumed = 4,
        PuzzleAdvanced = 5, PuzzleSolved = 6, DiscoveryLogged = 7, TravelStarted = 8, SaveWritten = 9,

        // 100+ failure
        InventoryFull = 100, ItemNotHeld = 101, RecipeUnknown = 102, MissingIngredient = 103,
        ToolRequired = 104, OutOfReach = 105, AlreadyHarvested = 106, NotYetRegrown = 107,
        WrongZone = 108, ActGateNotMet = 109, PuzzleNotStarted = 110, PuzzleInputRejected = 111,
        HandsFull = 112, NoBattery = 113, SaveInProgress = 114, StorageFull = 115,
        InternalError = 999,
    }

    public enum ArgKind : byte { None, Item, Count, Recipe, Zone, WorldObject, Minutes, Percent }

    /// <summary>12 bytes. A localizable substitution slot with no string and no allocation.</summary>
    public readonly struct ResultArg
    {
        public readonly ArgKind Kind;
        public readonly int     Value;   // ItemId/RecipeId/ZoneId are all int-backed ids
        ResultArg(ArgKind k, int v) { Kind = k; Value = v; }
        public static ResultArg Item(ItemId id) => new(ArgKind.Item, id.Raw);
        public static ResultArg Count(int n)    => new(ArgKind.Count, n);
        public static ResultArg None            => default;
    }

    public readonly struct CommandResult
    {
        public readonly bool       Ok;
        public readonly ResultCode Code;
        public readonly ResultArg  Arg0;
        public readonly ResultArg  Arg1;

        CommandResult(bool ok, ResultCode c, ResultArg a0, ResultArg a1)
        { Ok = ok; Code = c; Arg0 = a0; Arg1 = a1; }

        public static CommandResult Success(ResultCode c = ResultCode.Ok,
            ResultArg a0 = default, ResultArg a1 = default) => new(true, c, a0, a1);

        public static CommandResult Fail(ResultCode c,
            ResultArg a0 = default, ResultArg a1 = default) => new(false, c, a0, a1);
    }
}
```

**Localization contract.** `ResultCode` maps to a loc key by convention: `ResultCode.InventoryFull` → `result.inventory_full`. The mapping is generated, not hand-written (§9), so adding an enum member without a string fails CI. `ResultArg` slots fill `{0}` and `{1}`: `"The dry bag is full. Drop something to make room for {0}."` with `{0}` resolved via `ItemId → item.{id}.name`. **Core never sees a display string.** This is what makes Core engine- and locale-free while still producing a fully localized error.

### 3.3 Are commands undoable?

**No. There is no `Undo()` on `ICommandHandler`, and none will be added.** The rationale is both practical and thematic:

- Practically, a general undo requires every handler to author and maintain an inverse, which doubles the surface area and is the classic source of "undo left a dangling reference in the puzzle state machine" bugs. Our failure mode with an inverse-based undo is a corrupted save, which is the worst bug this game can have.
- Instead we get the two things undo is usually wanted for, more cheaply:
  - **Atomic failure** — `BeginTransaction`/`Rollback` via a shadow copy of the touched sub-state. A failed command leaves *no* trace. This covers 95% of the real need.
  - **Rewind for QA** — `WorldStateSnapshotStore` keeps a ring of the last 32 serialized states (the save serializer already exists, so this is ~free). Dev builds can rewind to any of them from the debug menu. This never ships to players.
- Thematically, this is a game about a woman whose sin was deleting a record. **Rule 7 of the fiction contract — "the record is never destroyed" — is also an architecture rule here.** The Field Slate supports *revision* (an entry gets a superseding entry, both visible), never erasure. `RevisSlateEntryCommand` appends; it does not delete. That is a domain invariant with a test: `FieldSlate_entries_are_append_only`.

### 3.4 The command roster

| Command | Mutates | Notable validation | Emits |
|---|---|---|---|
| `CollectItemCommand` | Inventory, Interactables, FieldSlate | reach, uses remaining, capacity | `ItemAdded`, `InteractableStateChanged`, `FieldSlateEntryAdded` |
| `CombineItemsCommand` | Inventory, FieldSlate | both items held, pair is a known or discoverable combination, tool durability | `ItemsCombined`, `ItemRemoved`, `ItemAdded`, `RecipeDiscovered` |
| `InteractWithObjectCommand` | varies by node | act gate, zone, required tool in bag | `InteractableStateChanged`, `AudioCueRequested`, possibly `StoryFlagSet` |
| `CraftItemCommand` | Inventory, CampState | full ingredient set, station present and powered (the Reel Deck needs the Fold Camp genset) | `ItemCrafted`, `ItemRemoved` ×n |
| `ConsumeItemCommand` | Inventory, Survival | item is consumable, survival meter has headroom | `ItemConsumed`, `SurvivalChanged` |
| `StartPuzzleCommand` | Puzzles | puzzle not already solved, prerequisites (e.g. the tide table is in the Slate before the damper console will accept input) | `PuzzleStarted` |
| `SubmitPuzzleInputCommand` | Puzzles | puzzle active, input within the puzzle's legal domain | `PuzzleInputAccepted`/`Rejected`, `PuzzleSolved` |
| `CompletePuzzleCommand` | Puzzles, Story, World | solution state reached (dispatched *by the puzzle system*, never by UI) | `PuzzleSolved`, `WorldGateOpened` |
| `DiscoverCommand` | FieldSlate | not already logged, or logged and now revisable | `FieldSlateEntryAdded`, `FieldSlateEntryRevised` |
| `AdvanceStoryCommand` | Story | all beat prerequisites satisfied; act transitions are one-way | `StoryBeatEntered`, `ActChanged` |
| `TravelToLocationCommand` | Player, Streaming intent | destination unlocked, no modal open, survival allows the traverse (Rime Shoulder refuses without cold layers) | `TravelBegan` → (streamer) → `ZoneEntered` |
| `BuildCampUpgradeCommand` | CampState, Inventory | materials, site clear, act gate | `CampUpgradeBuilt`, `ItemRemoved` ×n |
| `SaveGameCommand` | SaveMeta only | no save already in flight, no command mid-transaction | `SaveRequested` → (service) → `SaveCompleted`/`SaveFailed` |

### 3.5 Dispatcher and two handlers

```csharp
// ForgottenIsle.Core/Commands/CommandDispatcher.cs
using System;
using System.Collections.Generic;

namespace ForgottenIsle.Core.Commands
{
    public interface ICommandDispatcher
    {
        CommandResult Dispatch<TCommand>(in TCommand command) where TCommand : struct, ICommand;
        CommandResult CanExecute<TCommand>(in TCommand command) where TCommand : struct, ICommand;
    }

    public sealed class CommandDispatcher : ICommandDispatcher
    {
        readonly WorldState _state;
        readonly IEventSink _events;
        readonly ICoreLog   _log;
        // Type -> ICommandHandler<T> boxed as object. One lookup + one cast per dispatch.
        // Deliberately an instance field, not a static generic cache: static state would make
        // parallel EditMode fixtures share handlers and is untestable.
        readonly Dictionary<Type, object> _handlers = new(32);

        public CommandDispatcher(WorldState state, IEventSink events, ICoreLog log)
        { _state = state; _events = events; _log = log; }

        public void Register<TCommand>(ICommandHandler<TCommand> handler)
            where TCommand : struct, ICommand
            => _handlers[typeof(TCommand)] = handler;

        public CommandResult CanExecute<TCommand>(in TCommand command)
            where TCommand : struct, ICommand
        {
            if (!TryGet<TCommand>(out var h)) return CommandResult.Fail(ResultCode.InternalError);
            return h.Validate(in command, WorldQuery.Over(_state));
        }

        public CommandResult Dispatch<TCommand>(in TCommand command)
            where TCommand : struct, ICommand
        {
            if (!TryGet<TCommand>(out var handler))
            {
                _log.Warn(LogCode.NoHandlerRegistered, ResultArg.Count(0));
                return CommandResult.Fail(ResultCode.InternalError);
            }

            var validation = handler.Validate(in command, WorldQuery.Over(_state));
            if (!validation.Ok) return validation;          // nothing mutated, nothing emitted

            _state.BeginTransaction();
            try
            {
                var result = handler.Execute(in command, _state, _events);
                if (result.Ok) _state.Commit();
                else           _state.Rollback();           // handler chose to abort mid-flight
                return result;
            }
            catch (Exception ex)
            {
                _state.Rollback();
                _events.DiscardSinceTransactionStart();     // no half-emitted events reach the UI
                _log.Exception(LogCode.CommandThrew, ex);
                return CommandResult.Fail(ResultCode.InternalError);
            }
        }

        bool TryGet<TCommand>(out ICommandHandler<TCommand> handler) where TCommand : struct, ICommand
        {
            if (_handlers.TryGetValue(typeof(TCommand), out var o))
            { handler = (ICommandHandler<TCommand>)o; return true; }   // interface cast: no boxing
            handler = null; return false;
        }
    }
}
```

> **Allocation note.** `Dictionary<Type, object>.TryGetValue` with a `Type` key does not allocate. The `(ICommandHandler<TCommand>)o` cast is a reference cast, not boxing. `in TCommand` avoids a defensive copy only if the struct is `readonly` — **all command structs must be declared `readonly struct`**, and a Roslyn analyzer enforces it (§11, rule 4).

```csharp
// ForgottenIsle.Core/Commands/Handlers/CollectItemHandler.cs
namespace ForgottenIsle.Core.Commands.Handlers
{
    public readonly struct CollectItemCommand : ICommand
    {
        public readonly ActorId       Actor;
        public readonly WorldObjectId Target;
        public readonly int           RequestedCount;
        public CollectItemCommand(ActorId actor, WorldObjectId target, int count = 1)
        { Actor = actor; Target = target; RequestedCount = count; }
    }

    public sealed class CollectItemHandler : ICommandHandler<CollectItemCommand>
    {
        public CommandResult Validate(in CollectItemCommand cmd, in WorldQuery q)
        {
            if (!q.TryGetInteractable(cmd.Target, out var node))
                return CommandResult.Fail(ResultCode.InternalError);

            if (node.ZoneId != q.PlayerZone)
                return CommandResult.Fail(ResultCode.WrongZone, ResultArg.Zone(node.ZoneId));

            if (!node.Harvest.IsHarvestable)
                return CommandResult.Fail(ResultCode.AlreadyHarvested);

            if (node.Harvest.UsesRemaining <= 0)
            {
                return node.Harvest.RegrowsAtMinutes > q.ClockMinutes
                    ? CommandResult.Fail(ResultCode.NotYetRegrown,
                        ResultArg.Minutes(node.Harvest.RegrowsAtMinutes - q.ClockMinutes))
                    : CommandResult.Fail(ResultCode.AlreadyHarvested);
            }

            if (node.Harvest.RequiredTool != ItemId.None && !q.InventoryHas(node.Harvest.RequiredTool))
                return CommandResult.Fail(ResultCode.ToolRequired, ResultArg.Item(node.Harvest.RequiredTool));

            var count = cmd.RequestedCount <= 0 ? 1 : cmd.RequestedCount;
            if (!InventorySystem.CanAdd(q.Inventory, node.Harvest.Yield, count))
                return CommandResult.Fail(ResultCode.InventoryFull, ResultArg.Item(node.Harvest.Yield));

            return CommandResult.Success();
        }

        public CommandResult Execute(in CollectItemCommand cmd, WorldState state, IEventSink events)
        {
            var node  = state.Interactables.Get(cmd.Target);
            var item  = node.Harvest.Yield;
            var count = cmd.RequestedCount <= 0 ? 1 : cmd.RequestedCount;

            InventorySystem.Add(state.Inventory, item, count, events);
            HarvestSystem.ConsumeUse(state, cmd.Target, state.Clock.Minutes, events);

            if (node.Harvest.RequiredTool != ItemId.None)
                InventorySystem.WearTool(state.Inventory, node.Harvest.RequiredTool, node.Harvest.ToolWear, events);

            DiscoverySystem.TryRecordFirstSight(
                state.FieldSlate, DiscoveryCatalog.ForItem(item), state.Clock.Minutes, events);

            return CommandResult.Success(ResultCode.ItemCollected,
                ResultArg.Item(item), ResultArg.Count(count));
        }
    }
}
```

```csharp
// ForgottenIsle.Core/Commands/Handlers/SubmitPuzzleInputHandler.cs
// Representative of the Act 4 damper console: four gates, each with ballast / vent / tension,
// tuned live against a tide table. Input is one axis change on one gate.
namespace ForgottenIsle.Core.Commands.Handlers
{
    public readonly struct SubmitPuzzleInputCommand : ICommand
    {
        public readonly PuzzleId  Puzzle;
        public readonly byte      Channel;   // damper gate index 0..3
        public readonly PuzzleAxis Axis;     // Ballast | Vent | Tension
        public readonly short     Delta;     // signed detents; the console has 24 per axis
        public SubmitPuzzleInputCommand(PuzzleId p, byte channel, PuzzleAxis axis, short delta)
        { Puzzle = p; Channel = channel; Axis = axis; Delta = delta; }
    }

    public sealed class SubmitPuzzleInputHandler : ICommandHandler<SubmitPuzzleInputCommand>
    {
        public CommandResult Validate(in SubmitPuzzleInputCommand cmd, in WorldQuery q)
        {
            if (!q.TryGetPuzzle(cmd.Puzzle, out var p))     return CommandResult.Fail(ResultCode.InternalError);
            if (p.Phase != PuzzlePhase.Active)              return CommandResult.Fail(ResultCode.PuzzleNotStarted);
            if (cmd.Channel >= p.ChannelCount)              return CommandResult.Fail(ResultCode.InternalError);
            if (cmd.Delta == 0)                             return CommandResult.Fail(ResultCode.PuzzleInputRejected);

            var current = p.Channels[cmd.Channel].Get(cmd.Axis);
            var next    = current + cmd.Delta;
            if (next < p.AxisMin || next > p.AxisMax)
                return CommandResult.Fail(ResultCode.PuzzleInputRejected, ResultArg.Count(current));

            return CommandResult.Success();
        }

        public CommandResult Execute(in SubmitPuzzleInputCommand cmd, WorldState state, IEventSink events)
        {
            ref var p  = ref state.Puzzles.GetRef(cmd.Puzzle);
            ref var ch = ref p.Channels[cmd.Channel];
            ch.Set(cmd.Axis, (short)(ch.Get(cmd.Axis) + cmd.Delta));
            p.InputCount++;

            // Residual is what the player HEARS: the hydrophone overlay is driven off this number.
            var residual = DamperSolver.Residual(in p, state.Tide.CurrentPhase);
            events.Raise(new PuzzleChannelChangedEvent(cmd.Puzzle, cmd.Channel, cmd.Axis,
                                                       ch.Get(cmd.Axis), residual));

            if (residual <= p.SolveThreshold)
            {
                p.Phase = PuzzlePhase.Solved;
                p.SolvedAtMinutes = state.Clock.Minutes;
                events.Raise(new PuzzleSolvedEvent(cmd.Puzzle, p.InputCount,
                                                   state.Clock.Minutes - p.StartedAtMinutes));
                StorySystem.OnPuzzleSolved(state, cmd.Puzzle, events);
                return CommandResult.Success(ResultCode.PuzzleSolved);
            }

            return CommandResult.Success(ResultCode.PuzzleAdvanced, ResultArg.Percent(
                DamperSolver.ProgressPercent(residual, p.StartResidual, p.SolveThreshold)));
        }
    }
}
```

### 3.6 How the result reaches UI without UI reaching into state

Two distinct return paths, and keeping them distinct is the whole point:

**Path A — the immediate, synchronous, *per-action* answer.** `Dispatch` returns `CommandResult` to the **controller**. The controller hands it to `ICommandFeedback` (Game), whose UI-side implementation is `ResultToastPresenter`. That presenter converts `ResultCode` → `LocKey` → a localized string via the Localization package, substitutes `ResultArg`s, and shows a 2.4 s toast in the bottom safe area. The View never sees a `ResultCode`; it sees a finished string. Failures are always Path A.

**Path B — the ambient, asynchronous, *state-changed* answer.** Successful mutations reach UI only through domain events (§4). This is deliberate: if we let the controller push success state into UI directly, we would have two code paths that update the inventory HUD — one for "you just collected it" and one for "a save was loaded / a craft consumed it / the tide destroyed it." That divergence is where inventory desync bugs live. **There is exactly one way the inventory HUD updates: `ItemAddedEvent` / `ItemRemovedEvent`.**

The consequence to hold the line on: **a successful command's `CommandResult` is almost always ignored by the controller.** If you find yourself reading `result.Arg0` on success to update a view, you have written Path B in Path A's place. Raise an event instead.

---

## 4. EVENT / NOTIFICATION FLOW

### 4.1 The three options, judged against this game

| | Typed event bus | Observable state (`ReactiveProperty`/`INotifyPropertyChanged`) | Polling (`if (version != _lastVersion) Rebuild()`) |
|---|---|---|---|
| Allocation profile | Zero per event if payloads are structs and delegates are cached at subscribe | One closure + often one boxed value per property change; `ReactiveProperty<T>` with `T : struct` boxes on many implementations | Zero |
| Expresses *what happened* | Yes — `PuzzleSolvedEvent` carries input count and elapsed time | No — only *that a value differs* | No |
| Expresses transient facts (a sound cue, a first-sight flash) | Yes | Awkward — you need a value to change, so people invent fake counters | No |
| Cost of a new consumer | One subscribe line | One binding | Rebuild cost every frame, always |
| Failure mode | Spaghetti: cascades, ordering bugs, leaks | Diamond updates, `PropertyChanged` storms | Missed transients; wasted frames; 60 Hz string rebuilds (murder on mobile GC) |
| Debuggability | Good — a single flush point you can log | Poor — the stack is inside the reactive library | Excellent |

**Recommendation: a typed, struct-payload, frame-batched event bus.** The deciding factor is that most of this game's UI reacts to *transients*, not to values. "Nadia logged a first sight," "the reel deck reached 4:12 and Sabo says the word *gates*," "the residual crossed the threshold and the room went quiet" — none of those are a property changing. Polling cannot see them. Observable state has to fake them.

Where a value genuinely *is* the model — the four survival meters, the recorder battery percentage — we use the same bus but the presenter caches and renders on change only, which is observable-state behavior built out of events. We do not add a second mechanism for it.

### 4.2 The implementation

```csharp
// ForgottenIsle.Core/Events/IEventSink.cs
namespace ForgottenIsle.Core.Events
{
    public interface IDomainEvent { }

    public interface IEventSink
    {
        void Raise<T>(in T evt) where T : struct, IDomainEvent;
        void DiscardSinceTransactionStart();
    }

    public interface IEventChannel<T> where T : struct, IDomainEvent
    {
        /// <summary>Returns a token; dispose it or call Unsubscribe. Subscribing allocates once.</summary>
        SubscriptionToken Subscribe(RefAction<T> handler);
        void Unsubscribe(in SubscriptionToken token);
    }

    public delegate void RefAction<T>(in T evt) where T : struct, IDomainEvent;
}
```

```csharp
// ForgottenIsle.Core/Events/DomainEventQueue.cs
// Payloads live in typed pooled buffers (no boxing). Global emission order is preserved
// by a parallel envelope list, so the UI sees ItemRemoved before ItemAdded on a craft.
public sealed class DomainEventQueue : IEventSink
{
    readonly struct Envelope { public readonly int ChannelId, Index;
        public Envelope(int c, int i) { ChannelId = c; Index = i; } }

    readonly List<Envelope> _order   = new(256);
    readonly List<IChannel> _channels = new(64);              // index == ChannelId
    readonly Dictionary<Type, IChannel> _byType = new(64);
    int _transactionMark;
    bool _flushing;

    public void Raise<T>(in T evt) where T : struct, IDomainEvent
    {
        if (_flushing) throw new InvalidOperationException(
            "Domain events may not be raised from an event handler. See ARCHITECTURE.md §4.3.");
        var ch = Channel<T>();
        _order.Add(new Envelope(ch.Id, ch.Enqueue(in evt)));
    }

    public void MarkTransactionStart() => _transactionMark = _order.Count;

    public void DiscardSinceTransactionStart()
    {
        for (int i = _order.Count - 1; i >= _transactionMark; i--)
            _channels[_order[i].ChannelId].Truncate(_order[i].Index);
        _order.RemoveRange(_transactionMark, _order.Count - _transactionMark);
    }

    /// <summary>Called exactly once per frame by GameLoop.LateUpdate. Zero allocations.</summary>
    public void Flush()
    {
        _flushing = true;
        try
        {
            for (int i = 0; i < _order.Count; i++)
            {
                var e = _order[i];
                _channels[e.ChannelId].Dispatch(e.Index);
            }
        }
        finally
        {
            _flushing = false;
            for (int i = 0; i < _channels.Count; i++) _channels[i].Clear();  // keeps capacity
            _order.Clear();
            _transactionMark = 0;
        }
    }
}
```

`Channel<T>` holds a `T[]` grown to a high-water mark and a `RefAction<T>[]` of subscribers. Dispatch is `for (int i = 0; i < _subCount; i++) _subs[i](in _buffer[index]);` — **a delegate invocation with an `in struct` parameter does not box.**

### 4.3 How we avoid event spaghetti

Six rules. These are not style preferences; each one kills a specific class of bug we have all shipped before.

1. **Events are facts in the past tense, never requests.** `ItemAddedEvent`, not `AddItemEvent`. If a name reads as an imperative, it wanted to be a command.
2. **Handlers may not raise events.** Enforced at runtime by the `_flushing` guard above, which throws in development builds. This makes cascade depth exactly 1 and makes the event log linear and readable.
3. **Handlers may not dispatch commands.** Chained state change hides causality. If a domain rule says "collecting the last vine coil advances the beat," that rule belongs in `StorySystem`, called from inside `CollectItemHandler.Execute`, emitting both events in one transaction. The one sanctioned exception is `ZoneStreamDirector` (§2.1), documented in code with a `// ARCH-EXCEPTION-001` comment that CI greps for and counts — the count is pinned at 1 and a PR that raises it fails.
4. **One flush point.** `GameLoop.LateUpdate` calls `Flush()` once. Nothing else may call it. A dev-build `EventTraceRecorder` subscribes to every channel and writes the frame's event list to a ring buffer; the debug menu shows the last 240 frames of events, which is how we debug "why did the badge pulse twice."
5. **Every event has exactly one owning system, declared in `Events/OWNERS.md`** and asserted by a test that walks `IDomainEvent` implementors and checks each is raised from exactly one assembly-internal type. Two systems raising `SurvivalChangedEvent` is how meters start double-counting.
6. **Subscriptions are lifetime-bound.** UI presenters subscribe in `OnEnable` and unsubscribe in `OnDisable`, or take an `IScopedSubscriber` from the DI container that auto-disposes with the scene scope. A PlayMode test loads and unloads the Island scene 20 times and asserts `channel.SubscriberCount` returns to its baseline — leaked subscriptions are the #1 cause of "the HUD updated a destroyed GameObject."

### 4.4 Zero allocations per frame — the guarantee and how it is proven

- Event payloads: `readonly struct`, stored in pre-grown arrays, cleared not freed.
- Delegates: allocated once at subscribe. **No lambdas at raise or dispatch time.** A Roslyn analyzer flags any lambda or method group conversion inside a method named `Tick`, `Update`, `LateUpdate`, `FixedUpdate`, or `Flush`.
- The envelope list: `List<Envelope>` of a 8-byte struct, capacity 256, reused.
- Boxing: impossible in the dispatch path because every generic parameter is constrained `where T : struct` and every call site passes `in`.

**Proof, not assertion:** an EditMode test runs 10,000 representative frames of simulation and asserts via `GC.GetAllocatedBytesForCurrentThread()` that the delta after warm-up is **0 bytes**. A PlayMode test runs the Fernmaw traversal loop for 600 frames and asserts `ProfilerRecorder` for `"GC Allocated In Frame"` has a **max** under 1 KB (some allocation from Unity's own systems is unavoidable; the number is a pinned budget, and raising it requires TD sign-off in the PR).

---

## 5. DEPENDENCY INJECTION / COMPOSITION

### 5.1 The options

| | Service Locator | Constructor injection, hand-wired | DI container (VContainer / Zenject) |
|---|---|---|---|
| Dependencies visible in the type signature | No — hidden, discovered at runtime | Yes | Yes |
| Testability | Poor: global state, order-dependent test failures | Excellent | Excellent |
| MonoBehaviour story | Easy (that's the appeal) | Painful — MonoBehaviours can't have constructors | Handled (`[Inject]` method/field on MonoBehaviours) |
| Team-of-6 ramp-up | Immediate | Immediate | ~half a day, plus one senior owning the installers |
| Failure mode | Null at runtime, in a build, on a device | Compile error | Resolution exception at scope build — **at startup, loudly** |
| Runtime cost | Dictionary lookup per get | None | Container build cost at scope creation; VContainer is designed to minimise reflection — `⚠ VERIFY` its actual resolve cost on device before we lean on per-zone scopes |

### 5.2 Recommendation

**Constructor injection everywhere it is possible (all of `ForgottenIsle.Core`, and every non-MonoBehaviour class in `Game`), wired by VContainer at three scopes.** `⚠ VERIFY` VContainer's Unity 6 compatibility and license before adoption; if the spike fails, fall back to **hand-wired constructor injection with a plain `GameInstaller` class** — the composition root below barely changes, because it is already written as explicit construction. That fallback is genuinely viable for a team this size; the container buys us scoped lifetime management for zone-level services, and that is the only thing we would miss.

**Service locator is banned** except for one documented case: `PerfHud` and the dev-only debug menu, which are `#if DEVELOPMENT_BUILD || UNITY_EDITOR` and must not perturb production wiring.

**Three scopes:**
- **App scope** (created in Bootstrap, never destroyed): `WorldState`, `DomainEventQueue`, `CommandDispatcher`, all command handlers, `ISaveFileStore`, `ILocalizationService`, `IAudioDirector`, `IPlatformClock`, `ITelemetry`, `GameLoop`.
- **Session scope** (created on New Game / Load, destroyed on return to menu): controllers, `ZoneStreamDirector`, `WorldBindingRegistry`, the UI screen stack.
- **Zone scope** (one per additively loaded zone scene): zone-local presenters, the zone's binder registry slice, zone ambience. Destroyed with the scene, which disposes its subscriptions — this is what makes rule 6 of §4.3 cheap to keep.

### 5.3 Composition root sketch

```csharp
// ForgottenIsle.Game/Bootstrap/AppScope.cs
using VContainer;
using VContainer.Unity;
using UnityEngine;
using ForgottenIsle.Core;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Commands.Handlers;
using ForgottenIsle.Core.Events;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>Lives on the single root object in Bootstrap.unity. Never unloaded.</summary>
    public sealed class AppScope : LifetimeScope
    {
        [SerializeField] ContentCatalogAsset _catalog;   // baked by ForgottenIsle.Editor
        [SerializeField] GameLoop            _gameLoop;  // scene reference, not Instantiate

        protected override void Configure(IContainerBuilder b)
        {
            // ---- Core: pure, no Unity ------------------------------------------------
            b.Register<ContentDatabase>(_ => _catalog.ToCoreDatabase(), Lifetime.Singleton);
            b.Register<WorldState>(Lifetime.Singleton);
            b.Register<DomainEventQueue>(Lifetime.Singleton)
                 .AsImplementedInterfaces().AsSelf();     // IEventSink + channel provider
            b.Register<ICoreLog, UnityCoreLog>(Lifetime.Singleton);
            b.Register<CommandDispatcher>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();

            // ---- Services: Unity-facing, Core-declared interfaces ---------------------
            b.Register<ISaveFileStore,   SaveFileStore>(Lifetime.Singleton);
            b.Register<IPlatformClock,   PlatformClock>(Lifetime.Singleton);
            b.Register<IAudioDirector,   AudioDirector>(Lifetime.Singleton);
            b.Register<IHaptics,         Haptics>(Lifetime.Singleton);
            b.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
            b.Register<ITelemetry,       NullTelemetry>(Lifetime.Singleton);   // opt-in only

            // ---- The ticker ----------------------------------------------------------
            b.RegisterComponent(_gameLoop);
            b.RegisterEntryPoint<AppFlowController>();    // Bootstrap -> MainMenu -> Session

            b.RegisterBuildCallback(RegisterCommandHandlers);
        }

        static void RegisterCommandHandlers(IObjectResolver r)
        {
            var d = r.Resolve<CommandDispatcher>();
            d.Register(new CollectItemHandler());
            d.Register(new CombineItemsHandler(r.Resolve<ContentDatabase>()));
            d.Register(new InteractWithObjectHandler());
            d.Register(new CraftItemHandler(r.Resolve<ContentDatabase>()));
            d.Register(new ConsumeItemHandler(r.Resolve<ContentDatabase>()));
            d.Register(new StartPuzzleHandler());
            d.Register(new SubmitPuzzleInputHandler());
            d.Register(new CompletePuzzleHandler());
            d.Register(new DiscoverHandler(r.Resolve<ContentDatabase>()));
            d.Register(new AdvanceStoryHandler(r.Resolve<ContentDatabase>()));
            d.Register(new TravelToLocationHandler());
            d.Register(new BuildCampUpgradeHandler(r.Resolve<ContentDatabase>()));
            d.Register(new SaveGameHandler());
            // A test asserts this list covers every ICommand implementor in Core.
        }
    }
}
```

**The test that makes this safe:**

```csharp
[Test]
public void Every_ICommand_has_exactly_one_registered_handler()
{
    var commandTypes = typeof(ICommand).Assembly.GetTypes()
        .Where(t => t.IsValueType && typeof(ICommand).IsAssignableFrom(t)).ToArray();
    var dispatcher = TestWorld.New().Dispatcher;       // uses the same registration function
    foreach (var t in commandTypes)
        Assert.IsTrue(dispatcher.HasHandlerFor(t), $"No handler registered for {t.Name}.");
}
```

`TestWorld.New()` builds a fully wired Core — `WorldState`, dispatcher, queue, fake clock, seeded RNG — **with zero Unity involvement**, in about 40 µs. This is the single most valuable object in the codebase, and it exists only because Core is engine-free.

---

## 6. SCENE + WORLD STREAMING

### 6.1 Scene inventory

| Scene | Load | Contains | Lifetime |
|---|---|---|---|
| `Bootstrap` | Build index 0, loaded single | `AppScope`, `GameLoop`, `LoadingCurtain` canvas, `AudioDirector` listener rig, `PerfHud` (dev) | Never unloaded |
| `MainMenu` | Additive | Menu UI, a single pre-lit cinematic prop set (the Ribcage at dusk), title audio | Unloaded on session start |
| `Island_Session` | Additive | `SessionScope`, player rig, camera rig, global volume, tide/weather drivers, persistent audio buses | Whole session |
| `Zone_Ribcage`, `Zone_Fernmaw`, `Zone_FoldCamp`, `Zone_Combs`, `Zone_Sweatways`, `Zone_RimeShoulder`, `Zone_AshThroat`, `Zone_Oleander`, `Zone_QuietRoom` | Additive, via Addressables | Geometry, lightmaps, zone props, zone binders, zone scope | 1–2 resident at a time |

**Nine zones, and we never hold more than two.** Adjacency is authored in `ZoneGraph` (a Core data table): Fernmaw borders Ribcage, Combs and Sweatways. A traverse is always through a **Threshold** — a cut gully, a ladder, a lava-tube mouth — which is a short, geometrically enclosed connector that belongs to *both* zones' bounds and hides the load. This is a level-design constraint, not just a tech one, and it is in the level designer's brief.

### 6.2 Who owns loading

**`ZoneStreamDirector` (Game/Streaming) owns every scene load and unload in the product.** Nothing else calls `SceneManager` or `Addressables.LoadSceneAsync`. Gate C's grep enforces this for UI; a PR checklist item enforces it for Game.

The direction of control is: **command → state → director → engine → command.** UI cannot load a scene because UI cannot dispatch `TravelToLocationCommand` directly — it asks `TravelController`, which validates and dispatches; the handler sets `state.Streaming.Intent`; the director observes the intent through `TravelBeganEvent` and performs the work; on completion it dispatches `ZoneLoadedCommand`, which is the one sanctioned service→command call (`ARCH-EXCEPTION-001`).

### 6.3 A zone transition, frame by frame

Player walks into the Fernmaw→Sweatways threshold (the cut lava-tube mouth behind the drip-comb).

| Frame / time | What happens |
|---|---|
| **F0** | `ThresholdTrigger` (Game) reports entry. `TravelController` calls `_dispatcher.CanExecute(new TravelToLocationCommand(ZoneId.Sweatways))`. If it fails (no lamp, act gate), the prompt greys with a localized reason and nothing else happens. |
| **F0** | On Ok: `Dispatch`. `TravelToLocationHandler` writes `state.Player.PendingZone = Sweatways`, `state.Streaming.Phase = Beginning`, and raises `TravelBeganEvent { From, To, ThresholdId }`. |
| **F0 (LateUpdate)** | `Flush()`. `ZoneStreamDirector.OnTravelBegan` starts its `Awaitable` transition routine. `LoadingCurtainPresenter` starts the curtain fade. |
| **F0 → F0+0.35 s** | **Curtain in.** Not a black screen — a 350 ms fade to a full-bleed still of Nadia's Field Slate page for the destination, with the hydrophone's low hiss ducking over the zone ambience. Input is blocked by `ScreenStack.PushBlocking()`. Player movement is frozen by `state.Player.ControlLocked = true` (set by the handler, so the *domain* knows control is locked, not just the UI). |
| **F0+0.35 s** | **Safe point.** `GameLoop` sets `_simPaused = true` — the domain stops ticking. This is the only place in the game the simulation pauses, and it pauses *at a tick boundary*, never mid-tick. |
| **F0+0.35 s** | `Addressables.LoadSceneAsync("Zone_Sweatways", LoadSceneMode.Additive, activateOnLoad: false)`. `Application.backgroundLoadingPriority = ThreadPriority.High` for the duration (restored after). `⚠ VERIFY` the exact Addressables 2.x API signature for deferred activation in Unity 6 before writing this. |
| **loading (typ. 0.9–2.5 s)** | Curtain holds. The Field Slate still is content, so a slow load reads as a deliberate beat rather than a stall. If the load exceeds 4 s, a subtle progress rule appears under the slate. |
| **F_ready** | Scene activation. `ZoneBinderRegistry` for Sweatways registers its `WorldObjectId → Transform` map. Zone scope is built by VContainer. |
| **F_ready+1** | **State handoff.** The director dispatches `ZoneLoadedCommand { Zone = Sweatways, EntryThreshold = th.sweatways_from_fernmaw }`. The handler: sets `state.Player.ZoneId = Sweatways`, clears `PendingZone`, sets `Streaming.Phase = Resident`, applies **entry-side survival deltas** (the Sweatways are warm and wet: wet-clothing timer resets, thirst rate +12%), raises `ZoneEnteredEvent`. |
| **F_ready+1** | `PlayerRigBinder.OnZoneEntered` teleports the player rig to the destination threshold's `EntryAnchor` (a `Transform` in the new scene), matching yaw to the threshold's authored facing so the player walks out the way they walked in. The camera rig snaps, then blends over 150 ms. |
| **F_ready+2** | `AudioDirector.OnZoneEntered` crossfades the ambience bed over 800 ms. In the Sweatways, this is also where the 11-minute pressure cycle's phase is re-derived from `state.Clock.Minutes` — **it is a function of the clock, not a looping timer**, so it is correct after any load, save or resume. |
| **F_ready+2** | **Unload.** `SceneManager.UnloadSceneAsync("Zone_Fernmaw")` — but only if Fernmaw is not adjacent-pinned (see below). `Resources.UnloadUnusedAssets()` is called **once, here, during the curtain**, never during gameplay. |
| **F_ready+2 → +0.4 s** | Curtain out over 400 ms; `_simPaused = false`; `state.Player.ControlLocked = false` via `ZoneReadyCommand`. Input unblocked. |

**Adjacency pinning.** The Ribcage↔Fernmaw and Fernmaw↔Sweatways pairs are traversed constantly in Acts 1–2. `ZoneGraph` marks pairs as `KeepResident` when the *sum* of their measured memory footprints is under 260 MB; for those, we skip the unload and the curtain is 350 ms of pure fade with no load at all. The footprint numbers come from a device measurement pass, not from a guess, and the flag is data, not code.

**Position/state survival.** The only thing that "survives" is `WorldState`, which was never in a scene. Player position is *not* stored as a Unity `Transform` across the boundary — the domain stores `ZoneId` + `ThresholdId`, and the rig is placed from the destination scene's anchor. This means a save written mid-session always reloads to a well-defined, authored, non-clipping position. Precise intra-zone position (for save/reload *within* a zone) is stored as `Vec3` in `state.Player.LocalPosition`, written by `PlayerRigBinder` once per second and on every save, snapped to the navmesh on restore.

### 6.4 Memory budget — iPhone 12

`⚠ VERIFY` the ceiling (§0). Budget assumes a **1.35 GB working-set target**, chosen to leave headroom under the community-reported ~2 GB figure. These are **budgets we hold ourselves to**, tracked per-build in CI, not measurements.

| Bucket | Budget | Notes |
|---|---|---|
| Textures (resident) | 480 MB | ASTC 6×6 for albedo, 8×8 for masks/detail. Two zones max. Streaming mip limit dropped one level on the low-end tier. |
| Meshes | 130 MB | Vertex compression on; 16-bit indices where possible; read/write **off** on every mesh (a checked import rule). |
| Audio | 110 MB | Ambience beds streamed from disk (Vorbis, streaming load type). Reel-to-reel voice assets — the longest single-asset class in the game — are streamed, never decompress-on-load. SFX: ADPCM, decompress-on-load, ~14 MB. |
| Managed heap | 120 MB | `WorldState` at the end of Act 5 with an 11,000-entry logbook read in is well under 8 MB; the rest is Unity + our pools. GC is generational-incremental (`⚠ VERIFY` incremental GC default state in Unity 6) and we never need to collect mid-gameplay. |
| Shaders + variants | 45 MB | Variant stripping is mandatory (§8). |
| Unity native/system | 230 MB | Engine, URP resources, render targets, physics, font atlases. |
| Localization (CJK atlases) | 30 MB | §9. Only the active locale's atlas is resident. |
| Headroom | 205 MB | Absorbs load spikes (the moment both zones are resident during a non-pinned transition) and OS pressure. |
| **Total** | **1350 MB** | |

**The spike that matters** is the overlap window during a transition, where the outgoing zone is still resident. The curtain makes it safe in time, but not in memory. Mitigation: the director unloads the outgoing zone's *streaming texture residency* before starting the new load (drop its mip limit to minimum), which reclaims the majority of the outgoing zone's footprint before the peak. `⚠ VERIFY` the API for per-texture streaming budget control in Unity 6 and measure the actual reclaim before relying on it; if it does not work, the fallback is a hard unload-then-load with a slightly longer curtain, which we accept.

---

## 7. UPDATE STRATEGY

### 7.1 One ticker

**There is exactly one `Update()` in `ForgottenIsle.Game` and `ForgottenIsle.UI` combined**, on `GameLoop`. Everything else implements `ITickable` / `ILateTickable` and is registered with it. MonoBehaviours that need per-frame work register in `OnEnable` and deregister in `OnDisable`.

Why: Unity's per-MonoBehaviour `Update` has a native→managed transition per call, and with ~400 props in a jungle zone that is measurable on an A14. More importantly, a single ticker gives us **deterministic ordering** without script execution order settings, which are unreviewable and invisible in diffs.

```csharp
// ForgottenIsle.Game/Loop/GameLoop.cs
using UnityEngine;
using ForgottenIsle.Core;
using ForgottenIsle.Core.Events;

namespace ForgottenIsle.Game.Loop
{
    public sealed class GameLoop : MonoBehaviour
    {
        public const float SimDt   = 0.1f;      // 10 Hz domain tick. A compile-time constant.
        const int   MaxStepsPerFrame = 5;       // 0.5 s of catch-up max
        const float MaxFrameDelta    = 0.25f;   // never feed more than this to the accumulator

        SimulationRunner   _sim;        // injected
        DomainEventQueue   _events;     // injected
        TickerRegistry     _presentation = new(256);
        float  _accumulator;
        bool   _simPaused;
        double _simSeconds;             // authoritative sim time; never reads Time.time

        [Inject] public void Construct(SimulationRunner sim, DomainEventQueue events)
        { _sim = sim; _events = events; }

        void Awake()
        {
            Application.targetFrameRate = QualityTier.Current.TargetFps;   // 60 or 30
            QualitySettings.vSyncCount  = 0;                               // targetFrameRate governs
        }

        void Update()
        {
            float delta = Mathf.Min(Time.unscaledDeltaTime, MaxFrameDelta);

            if (!_simPaused)
            {
                _accumulator += delta * _sim.TimeScale;   // 1.0 normally; >1 only when resting
                int steps = 0;
                while (_accumulator >= SimDt && steps < MaxStepsPerFrame)
                {
                    _events.MarkTransactionStart();
                    _sim.Tick(SimDt);                     // <- the ONLY entry into Core per frame
                    _simSeconds  += SimDt;
                    _accumulator -= SimDt;
                    steps++;
                }
                // Resumed from background / long stall: drop the backlog rather than fast-forward.
                if (steps == MaxStepsPerFrame) _accumulator = 0f;
            }

            _presentation.Tick(delta);                    // cameras, binders, tweens, VFX drivers
        }

        void LateUpdate()
        {
            _events.Flush();                              // exactly one flush point, §4.3 rule 4
            _presentation.LateTick(Time.unscaledDeltaTime);
        }

        public void SetSimPaused(bool paused) { _simPaused = paused; if (paused) _accumulator = 0f; }
    }
}
```

### 7.2 Fixed vs variable

| System | Timestep | Why |
|---|---|---|
| Survival (thirst, core temperature, wet/dry, fatigue) | **Fixed, 10 Hz** | Determinism. A save reloaded on a 30 fps device must produce the same thirst curve as on a 120 Hz device. Variable dt integration over hours accumulates float drift; fixed dt does not. |
| In-fiction clock, tide phase, the Sweatways 11-minute pressure cycle | **Fixed, 10 Hz, derived not accumulated** | The tide is `TideTable.PhaseAt(state.Clock.Minutes)` — a pure function. Nothing integrates. This is why it is correct across saves, loads, transitions and backgrounding. |
| Weather (cloud lid density, rain state, Rime Shoulder wind) | **Fixed, 1 Hz** (a divider on the 10 Hz tick) | It changes on the scale of minutes; 10 Hz is waste. Sub-tick presentation interpolates. |
| Battery drain on the field recorder | **Fixed, 10 Hz** | The 9% starting battery is a *designed* Act 1 pressure. It must be frame-rate independent to the millisecond or the tuning is meaningless. |
| Puzzle state machines | **Event-driven only** — no tick | They advance on `SubmitPuzzleInputCommand`. The damper console's residual is recomputed on input, not per frame. |
| Camera, character controller, animation, tweens, VFX | **Variable, `Time.deltaTime`** | Presentation. Smoothness beats determinism. |
| Physics | **Unity's `FixedUpdate`, 50 Hz, and we barely use it** | No combat, no ragdolls, no vehicles. Physics is a character controller, a handful of rigidbody props, and raycasts. Consider raising `fixedDeltaTime` to 0.02→0.0333 on the low-end tier — `⚠ VERIFY` the character controller's behaviour at 30 Hz before doing it. |
| Audio | Its own thread | `AudioDirector` only issues start/stop/parameter calls from the presentation tick. |

### 7.3 Avoiding per-object Update

- **Binders are event-driven.** `WorldObjectBinder` has no `Update`. It changes when an `InteractableStateChangedEvent` says so.
- **Tick groups with dividers.** `TickerRegistry` supports `TickGroup.EveryFrame`, `.Every4Frames`, `.Every16Frames`, with objects distributed across phase buckets so the work per frame is flat. Zone ambience emitters, distant LOD prop swaps and foliage wind phase updates are all `Every16Frames` — that is 400 objects becoming 25 calls per frame.
- **Distance culling of logic.** `ProximityTicker` maintains a spatial hash of zone objects and only ticks those within 35 m of the camera; the rest are suspended and resumed on entry. Rebuild is amortised over 8 frames.
- **Animators off-screen.** `Animator.cullingMode = CullCompletely` on every non-story prop.
- **Coroutines are banned in gameplay code.** They allocate, they are invisible to the profiler by name, and their lifetime is tied to a GameObject's active state in ways that surprise people. Use `ITickable` for repeating work and `Awaitable` for one-shot async (`⚠ VERIFY` its allocation profile, §0). Coroutines remain permitted in `ForgottenIsle.Editor`.

---

## 8. PERFORMANCE ARCHITECTURE

### 8.1 Targets and tiers

| Tier | Devices (indicative) | Target | Resolution | Notes |
|---|---|---|---|---|
| **High** | iPhone 12 and newer; Snapdragon 8xx-class Android | 60 fps | Native, capped at 1170×2532-equivalent | Full foliage density, soft shadows on the key light |
| **Mid** | iPhone XR–11; Snapdragon 7xx / Dimensity 900-class | 60 fps | 0.85× render scale | Reduced foliage instance count, hard shadows |
| **Low** | Everything else that meets min spec | **30 fps** | 0.7× render scale | No real-time shadows except the lantern's blob; post-process stack reduced to tonemap + vignette |

Tier is selected at first boot by a device-model lookup table (shipped data, updatable) with a fallback heuristic on `SystemInfo.graphicsDeviceName`/`systemMemorySize`, then **confirmed by a 6-second in-game measurement** during the first Ribcage walk: if the 95th-percentile frame time misses the target, drop a tier and say nothing. The player may override in Settings.

### 8.2 Object pooling

Pooled, mandatory: all UI toasts and list rows, all SFX voices, all decal/VFX instances, all `WorldObjectBinder` prefab instances for harvestable props, all Field Slate entry views, all spectrogram bin quads in the hydrophone overlay.

We use a single generic pool with an explicit prewarm manifest per zone (`ZonePoolManifest`, a ScriptableObject baked into a Core table), prewarmed during the loading curtain. **Never grow a pool during gameplay** — a pool that exhausts logs a dev-build error and recycles its oldest instance. Exhaustion is a content bug to be fixed, not a runtime condition to be absorbed silently.

`⚠ VERIFY` whether Unity 6's built-in `UnityEngine.Pool.ObjectPool<T>` meets our needs (I believe it exists since 2021); if it does, use it rather than writing our own, wrapped in a thin `IPrewarmablePool` we control.

### 8.3 LOD and culling

- **LOD groups on every mesh over 400 triangles.** Three levels plus cull. Crossfade **off** (it costs a second draw during the blend, and on mobile that is worse than the pop it prevents). LOD bias is per-tier.
- **Foliage** (Fernmaw is the worst case) uses GPU instancing with a per-zone instance budget: 1,800 instanced tree-fern fronds at High, 900 at Low, distributed by a baked density map. Foliage never casts shadows; the canopy light is baked.
- **Occlusion culling:** Unity's baked occlusion (umbra) is baked per zone scene. The Sweatways and Oleander — enclosed, corridor-shaped, high-occlusion — are where it pays. Fernmaw and Rime Shoulder are open and get almost nothing from it; there we rely on LOD, distance culling per layer (`Camera.layerCullDistances`) and the cloud lid as a natural far-plane fog wall at 140 m.
- **Unity 6 GPU Occlusion Culling / GPU Resident Drawer:** `⚠ VERIFY` mobile support and measure. **Budget assumes it is OFF on mobile.** If a spike shows it works and wins on our target devices, that is an ADR and a re-budget, not a silent adoption.

### 8.4 Lighting — the jungle at night

This is the hardest lighting problem in the game: Fernmaw under canopy, at night, lit by a hand lantern, and it must read as *grounded and cinematic*, not as a black screen with a cone in it.

**Everything static is baked.** Lightmaps at 12 texels/unit for hero areas, 6 for the rest; non-directional mode (directional lightmaps double the memory for a benefit we cannot see through a canopy). Bakes are checked in as part of the zone scene and CI fails if a zone's lightmap data is missing or stale relative to its scene hash.

**The lantern is the only dynamic light that casts shadows.** One point light, shadow resolution 512, shadow distance 12 m, on High only. On Mid it keeps shadows at 256/8 m; on Low it casts none and we compensate with a baked ambient occlusion boost and a screen-space vignette that tracks lantern intensity.

**Bounce and ambience** come from light probes — a hand-placed probe volume through the aqueduct trench at 2 m spacing, denser at the thresholds. `⚠ VERIFY` Adaptive Probe Volumes in URP on Unity 6 and, critically, their **memory and streaming cost on mobile**; APV would save us weeks of probe placement across nine zones, so it is worth a real spike, but the budget above assumes classic light probes.

**Moonlight above the canopy** is a baked directional contribution plus a single non-shadowing realtime directional at 4% intensity to keep specular alive on wet leaves. Rain wetness is a material parameter driven from the weather tick, not a separate light.

**Rime Shoulder is the inverse problem** — above the cloud lid, blinding, and the only place you see sky. It gets a real directional light with cascaded shadows (2 cascades, 60 m), which it can afford because it is geometrically sparse.

### 8.5 URP renderer feature budget

**Maximum two custom `ScriptableRendererFeature`s in the shipping renderer, and both must be justified in an ADR.** The two we have budgeted:

1. **`HydrophoneOverlayFeature`** — the signature verb. A single full-screen pass that composites the directional spectrogram: a chromatic band-energy visualization driven by a 256-bin float buffer the audio system writes once per 100 ms, warped by the aim cone. One blit, one custom shader, no extra render target beyond a half-resolution intermediate.
2. **`WetSurfaceMaskFeature`** — draws a low-res (quarter) mask of rain-exposed surfaces used by the water-sheeting shader in Fernmaw, the Sweatways and the Combs. `⚠ VERIFY` whether this can be folded into the existing material pass with a baked exposure mask instead; if so, we drop to one feature and take the win.

Hard limits:
- **No camera stacking beyond one overlay camera**, and the UI uses Screen Space – Overlay so it does not need one at all. The only stacked camera in the game is the Reel Deck's VU-meter render texture, and it renders at 15 Hz on a manual `Camera.Render()` call, not every frame.
- **Post-processing stack:** tonemapping (ACES-approximate, custom LUT), bloom (low quality, half-res), vignette, film grain, colour adjustments. **No depth of field, no motion blur, no SSAO at runtime** (AO is baked). Chromatic aberration only during the hydrophone overlay, at low intensity.
- **Render Graph:** both features are authored against the Render Graph API from the start (`⚠ VERIFY` §0). No Compatibility Mode code enters the repo.
- **Forward+ vs Forward:** default to **Forward**. Forward+ earns its cost only with many lights, and we have one. `⚠ VERIFY` by profiling both on an iPhone 12 in Fernmaw at night before finalising.

### 8.6 Texture and shader budgets

| Rule | Value |
|---|---|
| Max texture dimension | 2048 (hero props and the Field Slate pages: 2048; world props: 1024; small props and decals: 512) |
| Compression | **ASTC** on both platforms. 6×6 for albedo, 8×8 for masks/roughness/metallic packs, 5×5 only for the six hero surfaces (Nadia's hands, the hydrophone, the Field Slate, the damper console face, Lorvik's logbook, the Quiet Room fins). `⚠ VERIFY` ASTC support across the Android min-spec list; fall back to ETC2 on any device that lacks it, as a separate texture variant, not a runtime decision. |
| Channel packing | Mandatory. Metallic/Roughness/AO/Height packed into one RGBA. No single-channel textures in the shipping set. |
| Mipmaps | On for everything in world space. Off for UI. |
| Read/Write enabled | **Off**, always. A CI asset-rule check fails the build on any texture or mesh with it enabled. |
| Materials per zone | ≤ 40 unique. Enforced by an editor validator. |
| Shader variants | Stripped aggressively via `IPreprocessShaders`. Target: **< 3,000 variants total**, measured in the build log. A build that exceeds it fails. Every URP keyword we do not use (lightmap directional, additional light shadows on Low, decals, forward+ clustering when unused) is stripped by an explicit strip list, not by hope. |
| Shader complexity | Fragment shaders in the opaque pass: ≤ 60 ALU on the mobile compiler's report for the common material; the wet-surface shader gets ≤ 90. Measured per-PR on the six hero materials. |

**Budgets per frame (High tier, worst-case Fernmaw at night):** ≤ 90 SetPass calls, ≤ 320 batches, ≤ 350k triangles, ≤ 2.0 ms CPU in `GameLoop`, ≤ 11 ms GPU. These are **our targets**, asserted by an automated benchmark scene in the nightly device run, not claims about what the hardware can do.

### 8.7 GC allocation policy

**Banned in any code reachable from `GameLoop.Update` / `LateTick` / `Flush` / any `ITickable.Tick`:**

| Banned | Use instead |
|---|---|
| `new` on any reference type | A pool, or a struct |
| `string` concatenation, interpolation, `ToString()` on a number | `TextMeshProUGUI.SetText(format, arg)` (takes numeric args without boxing — `⚠ VERIFY` the exact overload set in the TMP version we ship), or a cached `StringBuilder` in the presenter |
| LINQ, anywhere | Hand-written loops. LINQ is permitted in `ForgottenIsle.Editor` and in tests only. |
| `foreach` over an interface-typed collection (`IEnumerable<T>`, `IList<T>`) | `for` over the concrete `List<T>`/array. `foreach` over a concrete `List<T>` is permitted — its enumerator is a struct. |
| Lambdas and closures capturing locals | Cached delegate fields, or a struct with an explicit method |
| Boxing: passing a struct as `object`, `Enum.ToString()`, `enum` as a `Dictionary` key without a comparer | `IEqualityComparer<TEnum>` supplied explicitly; codes not strings |
| `params` arrays | Explicit overloads up to 3 args |
| `Camera.main` | A cached reference (it is a `FindGameObjectWithTag` under the hood — `⚠ VERIFY` whether Unity 6 caches it; assume not) |
| `GetComponent` in a tick | Cached in `Awake`/`OnEnable` |
| `string`-keyed animator/shader lookups in a tick | `Animator.StringToHash` / `Shader.PropertyToID`, cached in a static readonly field |

Enforced by: (a) a Roslyn analyzer package in the repo with these as rules FI0001–FI0012, elevated to **errors** on the CI build; (b) the zero-allocation tests in §4.4; (c) the nightly device run failing on a GC-alloc regression above the pinned budget.

**Incremental GC** is enabled (`⚠ VERIFY` its default and its per-platform availability in Unity 6). But the policy is that incremental GC is a safety net, not a plan: our allocation target during gameplay is zero, so the collector should have nothing to do.

### 8.8 Performance overlay spec

```
┌───────────────────────────────┐   toggle: four-finger tap held 800 ms,
│ 59.4 fps   16.8 ms  ▁▂▃▂▁▂▅▂  │   or the ` key in the Editor.
│ CPU  9.2   GPU  11.4  (main)  │   Available in Development builds only
│ GC   0 B/f   heap 84.1 MB     │   (#if DEVELOPMENT_BUILD || UNITY_EDITOR).
│ Mem  1.09 GB reserved         │   Renders on its own Screen Space Overlay
│ Draw 71  SetPass 63  Tri 284k │   canvas, above everything, with a
│ Zone Fernmaw   Sim 10.0 Hz    │   CanvasGroup so it never eats touches.
│ Ev   14/f   Cmd 0/f           │
└───────────────────────────────┘
```

| Row | Source | Notes |
|---|---|---|
| fps / frame ms / sparkline | `ProfilerRecorder("Main Thread")`, 120-sample ring | Sparkline is 64 samples of frame time normalised to 2× target. **Shows the 95th percentile as a red rule**, because average fps hides the stutters that actually ruin a game. |
| CPU / GPU | `ProfilerRecorder` on the main-thread and render-thread markers | `⚠ VERIFY` exact marker names per platform; if a recorder is `!Valid`, the row renders `—` rather than a wrong number |
| GC bytes/frame, managed heap | `"GC Allocated In Frame"`, `"GC Reserved Memory"` | **Turns red at 1 byte.** Non-negotiable. |
| Total reserved memory | `"Total Reserved Memory"` | Against the 1.35 GB budget, as a bar |
| Draw calls / SetPass / triangles | `"Draw Calls Count"`, `"SetPass Calls Count"`, `"Triangles Count"` | Against the §8.6 budgets, coloured |
| Zone / sim rate | Our own instrumentation | Sim rate shows actual steps/sec — if it reads 9.2 Hz we are dropping simulation steps, which is a bug |
| Events/frame, commands/frame | `DomainEventQueue`, `CommandDispatcher` counters | A frame with 300 events is a cascade bug; this is how we see it |

All values are rendered with `TMP_Text.SetText` using numeric overloads and a fixed-width font asset, updated at **4 Hz, not every frame** — a perf HUD that costs 0.4 ms is a liar. It allocates zero bytes per frame, and there is a test that asserts it.

---

## 9. LOCALIZATION ARCHITECTURE

**Package:** `com.unity.localization`. Ship locales at launch: `en`, `fr`, `de`, `es-419`, `pt-BR`, `ja`, `ko`, `zh-Hans`, `zh-Hant`, `ru`. RTL (`ar`) is **readiness only** at launch, not a shipping locale — see §9.5.

### 9.1 Key naming convention

```
<domain>.<subject>.<field>[.<variant>]
```
Lowercase, `snake_case` segments, no spaces, ASCII only, max 80 chars.

| Domain | Example | Table |
|---|---|---|
| `ui` | `ui.inventory.title`, `ui.settings.audio.master`, `ui.button.collect` | `UI` |
| `item` | `item.water_vine_coil.name`, `item.water_vine_coil.desc`, `item.field_recorder.desc` | `Items` |
| `result` | `result.inventory_full`, `result.tool_required`, `result.not_yet_regrown` | `Results` |
| `zone` | `zone.fernmaw.name`, `zone.sweatways.entry_note` | `World` |
| `doc` | `doc.poulter.1887_11_04.body`, `doc.sabo.reel_14.transcript`, `doc.lorvik.day_11204` | `Documents` |
| `slate` | `slate.flora.water_vine.first`, `slate.flora.water_vine.revised_01` | `FieldSlate` |
| `puzzle` | `puzzle.damper_console.hint_01`, `puzzle.register.axis_ballast` | `Puzzles` |
| `story` | `story.act2.beat_summit.title` | `Story` |

**Hard rule:** the key is derived from the **content id**, never from the English text. `result.inventory_full`, never `result.the_dry_bag_is_full`. When copy changes, the key does not, and the translation memory keeps working.

### 9.2 Table organisation

Nine String Table Collections, split by *load lifetime*, not by feature:

| Collection | Resident | Size (en, est.) | Why split |
|---|---|---|---|
| `UI` | Always | ~12 KB | Needed in the menu before anything else loads |
| `Results` | Always | ~6 KB | Every failure toast |
| `World` | Always | ~8 KB | Zone names appear in the map and travel UI |
| `Items` | Always | ~90 KB | The Field Slate can reference any item at any time |
| `Story` | Always | ~40 KB | |
| `Documents` | **Per-act, Addressable** | ~600 KB total | The single largest text asset in the game. Poulter's journal, Ferrier's notes, Sabo's reel transcripts, Solheim's variance reports, Castellar's calcs, Obuya's letters, and 11,000 logbook entries of Lorvik's. Acts 3–5 alone are most of it. Loading Act 5's documents during Act 1 wastes ~400 KB × locale. |
| `FieldSlate` | Per-act, Addressable | ~180 KB | |
| `Puzzles` | Per-act, Addressable | ~30 KB | |
| `Credits` | On demand | ~15 KB | |

Lorvik's logbook deserves a note: **11,000 entries are not 11,000 loc keys.** They are a structured data table (`day`, `lantern_id`, `bridge_id`, `vessel_seen`) plus ~40 localized sentence templates. The wording never changes across thirty-one years — that is the point of the character — so it is a template with substitutions. The handwriting degradation is a rendering concern (font weight/jitter driven by entry year), not a text concern.

### 9.3 Pluralisation, numbers, dates

**Pluralisation** uses the Localization package's Smart Strings plural formatter (`⚠ VERIFY` its exact syntax and whether it implements full CLDR plural categories — Russian needs `one/few/many/other`, Polish likewise, Arabic needs six). If it does not cover CLDR properly, we do not hand-roll it: we add a plural rule table driven by the CLDR data and route through a `IPluralSelector` service. This is a known trap and we budget two days for it.

```
result.collected_n = {count:plural:one{Collected {0}.}|other{Collected {count} × {0}.}}
```

**Numbers.** All numeric formatting goes through `ILocalizedFormatter`, which uses the active `CultureInfo` from the Localization package's selected locale — **not** `CultureInfo.CurrentCulture`, which on some devices does not match the chosen game language. Depth, temperature and frequency units are locale-defaulted but user-overridable in Settings (a marine acoustician's audience will care, and metres/feet is a real preference split).

**Dates.** Every in-fiction date in this game is *diegetic and fixed*: `1887-11-04`, `Day 11,204`, `Log Fourteen`. These are **content, not formatted values** — Poulter's journal header is written the way Poulter wrote it, and a French player sees the same date form he used. Only two things get culture-formatted: the save-slot timestamp and the playtime duration. This is a deliberate call and it is in the localization brief so translators do not "fix" the diegetic dates.

**Culture-invariant everywhere else.** All save data, all logs, all telemetry, all parsing: `CultureInfo.InvariantCulture`, explicitly. A Roslyn rule (FI0007) flags any `ToString()`/`Parse` on a numeric or date type without an explicit `IFormatProvider`. This is how you avoid the Turkish-`i` and comma-decimal-separator bugs that corrupt saves in Europe.

### 9.4 Font and atlas strategy — the CJK memory problem

This is the real one. Here is the arithmetic, with every assumption stated so it can be checked.

**Assumptions (each `⚠ VERIFY` against our final font choice):** SDF atlas, single-channel (R8), 8-bit. Sampling point size 48 px, padding 9 px → an effective glyph cell of roughly 66 × 66 px. A 2048 × 2048 atlas therefore fits about ⌊2048/66⌋² = 31² = **961 glyphs**, and costs 2048 × 2048 × 1 byte = **4.19 MB** uncompressed in memory.

| Locale | Distinct glyphs needed | Atlases (2048², 961 ea.) | Resident memory |
|---|---|---|---|
| Latin + Cyrillic (`en/fr/de/es/pt/ru`) | ~700 incl. accents and punctuation | 1 | 4.2 MB |
| `ja` | ~2,600 (kana + jōyō kanji + the extras our documents actually use) | 3 | 12.6 MB |
| `ko` | ~2,800 (the Hangul syllables our text actually uses, not all 11,172) | 3 | 12.6 MB |
| `zh-Hans` | ~3,400 | 4 | 16.8 MB |
| `zh-Hant` | ~3,900 | 5 | 21.0 MB |

**If we shipped all of them resident, that is ~67 MB of font atlas.** Against a 1.35 GB budget that is survivable but stupid — it is 5% of the budget for glyphs nobody is looking at.

**The strategy, in five parts:**

1. **One font asset per locale group, delivered as an Addressable.** Only the active locale's font assets are loaded. Switching language unloads the old group. This alone takes the worst case from 67 MB to 21 MB.
2. **Static, pre-baked atlases — not dynamic — for the glyphs we know we need.** The Editor tool `Tools/Localization/Bake Font Atlases` walks every string in every table for a locale, computes the exact distinct glyph set, and bakes a static atlas containing precisely those glyphs. This is the single biggest win: the Chinese text in this game is a fixed, finite corpus of documents, so we never need a general-purpose CJK font. Expected real glyph counts after this pass are meaningfully lower than the table above, which assumes a generic common-set. The bake runs in CI and the resulting atlas count is reported in the build summary.
3. **A small dynamic fallback atlas** (1024², 1.05 MB, multi-atlas capped at 2) attached as a TMP fallback for anything the bake missed — a player's save name, an unexpected glyph after a late copy change. It must be *empty* in a shipping build's first hour; a dev-build warning fires on every dynamic glyph population so we catch bake misses during QA rather than shipping a hitch.
4. **Two sampling sizes, not one.** Body text (document pages, logbook) bakes at 48 px; the HUD and titles use a separate, much smaller atlas at 80 px for the ~120 glyphs the UI actually uses. Scaling one 48 px atlas up for titles looks soft, and baking everything at 80 px triples the memory.
5. **Diegetic handwriting is not a font problem for CJK.** Poulter's copperplate and Lorvik's degrading hand are *Latin* display faces used for English-language facsimile presentation. In CJK locales the facsimile page renders the original Latin handwriting as an **image** (part of the document art, already in the texture budget) with the translated text set below it in the body face. This is both a memory decision and an authenticity one: a 1887 English journal does not become handwritten Japanese.

**Font atlas memory budget: 30 MB resident, worst case (`zh-Hant` + UI atlas + fallback).** That is the number in §6.4.

### 9.5 RTL readiness

We are not shipping Arabic or Hebrew at launch, but we are not painting ourselves into a corner either.

- **Layout:** every UI prefab uses anchored, mirror-capable layout. A `LayoutDirection` value on the localization service flips `HorizontalLayoutGroup` reverse-arrangement, anchor sides and icon-text ordering. A dev-menu toggle forces RTL with pseudo-localized text so we can inspect every screen for mirroring bugs **now**, during development, not in a panic during an Arabic port.
- **Text shaping:** `⚠ VERIFY` — I do not believe TextMeshPro performs Arabic contextual shaping or bidi reordering natively. The plan of record is to evaluate a third-party shaping layer (there are established Unity Arabic/RTL text packages; license and Unity 6 compatibility must be verified before adoption) **or** to pre-shape strings in the localization pipeline at bake time. The pre-shaping route is more attractive for us because our text is a fixed corpus with almost no runtime string composition.
- **What we commit to now:** no hardcoded left alignment on body text, no hardcoded `+x` offsets that assume LTR, no baked-in "→" glyphs in UI art (they are separate sprites that mirror), and no string concatenation to build sentences — which we have banned anyway (§9.6).

### 9.6 The CI check that fails the build on a hardcoded string

Two complementary checks, because either alone is defeatable.

**Check 1 — Roslyn analyzer `FI0009: user-facing string literal`.** Error severity in CI. It flags a string literal (or interpolated string) that flows into any of: `TMP_Text.text`, `TMP_Text.SetText`, `UnityEngine.UI.Text.text`, or any parameter annotated `[UserFacing]`. Allowed exits: a `LocKey` constant, a `LocalizedString`, or the literal being marked `[NotLocalized("reason")]` — which requires a written reason, shows up in a report, and is reviewed. Strings in `ForgottenIsle.Editor`, tests, and log messages are exempt.

**Check 2 — key integrity, as an EditMode test suite:**

```csharp
// Assets/Tests/EditMode/Localization/LocalizationIntegrityTests.cs
[Test] public void Every_LocKey_constant_resolves_in_the_source_locale() { /* ... */ }
[Test] public void Every_ResultCode_has_a_result_table_entry()
{
    foreach (ResultCode code in Enum.GetValues(typeof(ResultCode)))
    {
        if ((ushort)code < 100) continue;                  // successes need no message
        var key = LocKeys.ForResult(code);                 // "result." + snake_case(code)
        Assert.IsTrue(LocTables.Results.ContainsKey(key),
            $"ResultCode.{code} has no string. Add '{key}' to the Results table.");
    }
}
[Test] public void No_table_entry_is_orphaned() { /* key present in table but referenced nowhere */ }
[Test] public void Every_shipping_locale_covers_every_key_in_always_resident_tables() { /* ... */ }
[Test] public void Smart_string_placeholders_match_between_source_and_each_translation() { /* ... */ }
```

That last one catches the most expensive localization bug there is: a translator drops `{0}` and a string that worked in English throws or renders wrong in German, three weeks after the translation drop, in a build that already went to cert.

The **orphan** test and the **placeholder** test run on every PR. The **full locale coverage** test runs on `main` and nightly, and is allowed to *warn* rather than fail until content lock, because translations legitimately lag authoring. At content lock it becomes an error.

---

## 10. BUILD + CI

### 10.1 Branch strategy

**Trunk-based with short-lived branches.** `main` is always shippable to TestFlight/Internal Testing.

- `main` — protected. No direct pushes. Squash merge only. Every commit on `main` has a green CI.
- `feat/<initials>-<short-desc>` — lifetime target **under 3 days**. A branch older than 5 days gets flagged by a bot; older than 10 gets a conversation.
- `release/v<major>.<minor>` — cut at feature freeze, cherry-picks only, deleted after ship.
- `fix/<ticket>` — branched from the release branch during cert, merged back to both.

**Feature flags over long branches.** Anything that takes more than three days lands behind a flag in `FeatureFlags` (a Core enum + a ScriptableObject override in Editor), off by default. Act 4's Register mechanic and Act 5's HUD-stripped mode both ship behind flags until they are done.

Unity-specific rules that avoid pain: **Force Text serialization** and **Visible Meta Files** on, `.gitattributes` marking `.unity`, `.prefab`, `.asset` with the Unity YAML merge tool, Git LFS for everything binary (`.psd`, `.fbx`, `.wav`, `.png`, `.tga`, lightmap `.exr`). **Scene ownership is exclusive** — one named owner per scene file, enforced socially and by a CODEOWNERS entry, because YAML merges on scenes are a lie that costs a day.

### 10.2 What runs on PR

Runner: **GameCI** (`game-ci/unity-test-runner`, `game-ci/unity-builder`) on GitHub Actions, self-hosted macOS runner for iOS. `⚠ VERIFY` current action versions and Unity 6 LTS image availability before committing; the fallback is Unity Build Automation for device builds with GitHub Actions handling the fast checks. Licence activation uses a repository secret (ULF/serial) — never committed.

| Stage | Runs on | Gate | Budget |
|---|---|---|---|
| 1. Lint + layering | Every PR, ubuntu | `ci/check-layering.sh`, `dotnet format --verify-no-changes`, banned-symbol grep | 40 s |
| 2. Compile all targets | Every PR | Editor compile + Player script-only compile for iOS and Android | 4 min |
| 3. Roslyn analyzers as errors | Stage 2 | FI0001–FI0012 (GC policy, `readonly struct` commands, culture-invariant formatting, FI0009 hardcoded strings) | included |
| 4. EditMode tests | Every PR | Full Core suite + architecture tests + localization integrity. **Coverage gate: Core ≥ 80%, Validate branches 100%.** | 3 min |
| 5. Asset rules | Every PR | Texture max size, read/write off, compression format, materials-per-zone, missing-lightmap check, Addressables group membership | 2 min |
| 6. Addressables catalog build | Every PR | Fails on a duplicate-asset-across-groups report over threshold, or an unresolved reference | 5 min |
| 7. PlayMode tests | Every PR (headless) | Bootstrap boot, one full zone transition round trip, save/load round trip, subscription-leak test | 6 min |
| 8. Development build, Android | Every PR | Produces an installable APK artifact for the PR | 12 min |
| 9. Device smoke | **Nightly on `main`** | Automated 8-minute playthrough on a physical iPhone 12 and a mid Android; asserts fps 95th percentile, GC/frame, peak memory, draw calls against the §8 budgets; uploads a perf JSON that is diffed against the previous night | 25 min |
| 10. iOS + Android release builds | Nightly + on `release/*` | IL2CPP, signed, uploaded to TestFlight / Internal Testing | 45 min |

**PR gate total: under 20 minutes.** If it creeps past 25, we move stages 6–8 to a merge queue rather than letting people learn to ignore CI.

### 10.3 Asset pipeline / Addressables

| Group | Content | Delivery | Compression |
|---|---|---|---|
| `Local_Boot` | Bootstrap, MainMenu, UI atlases, always-resident loc tables, the UI font atlas | In-build, local | LZ4 |
| `Local_Act1` | `Zone_Ribcage`, `Zone_Fernmaw`, their audio, Act 1 documents | In-build, local | LZ4 |
| `Zone_<name>` × 7 | One group per remaining zone, scene + its exclusive assets | Local by default; **candidates for on-demand delivery** if the store package exceeds 4 GB | LZ4 |
| `Shared_Materials`, `Shared_Props` | Assets referenced by 3+ zones | Local | LZ4 |
| `Locale_<code>` | Per-locale font assets and per-act document tables | Local | LZ4 |
| `Audio_Voice_Reels` | The Sabo/Ferrier/Solheim/Obuya reel recordings — the largest single bucket | Local, streaming load type | Vorbis q0.4 |

**LZ4 over LZMA everywhere**, deliberately: LZMA's smaller download is not worth its decompression cost and memory spike on a mid-range Android during a loading curtain we are trying to keep under 2.5 seconds.

**Duplicate analysis runs on every PR** (Addressables' "Check Duplicate Bundle Dependencies"). A shared material pulled into four zone bundles is four copies in memory and on disk; the report threshold is 8 MB of duplication and exceeding it fails the PR.

**On-demand delivery** (iOS On-Demand Resources / Android Play Asset Delivery) is designed for but **not enabled at start**. `⚠ VERIFY` the Unity 6 integration path for Play Asset Delivery — I am not confident of the current package name or its maturity. Decision point is at first full-content build: if the local package is under the store's install-size comfort zone, we ship everything local and skip a whole class of bugs.

### 10.4 Platform settings that matter

| Setting | iOS | Android | Why |
|---|---|---|---|
| Scripting backend | IL2CPP | IL2CPP | Mandatory on iOS; on Android it is the performance decision. Mono is not shipped. |
| Architecture | ARM64 | **ARM64 only** | 32-bit ARMv7 is dropped. It doubles build time, halves our memory headroom, and the min-spec devices we target are 64-bit. This is a product decision recorded in an ADR. |
| Graphics API | **Metal only** (remove OpenGL ES) | **Vulkan first, GLES3 fallback** | `⚠ VERIFY` whether our URP feature set and the wet-surface shader behave identically on GLES3; if there is divergence, the fallback gets a reduced variant, not a broken one. |
| Managed stripping level | **Medium**, with an explicit `link.xml` | Same | High is tempting and it will strip something reflective — Newtonsoft's converters and the Localization package's type resolution are the usual casualties. Medium plus a curated `link.xml` is the defensible position. Any `link.xml` addition requires a comment saying what broke without it. |
| IL2CPP code generation | **Faster runtime** | Faster runtime | Build size cost accepted; this is a 60 fps target. |
| Incremental GC | On | On | `⚠ VERIFY` default and availability |
| Target API / OS | iOS 15+ (`⚠ VERIFY` against Unity 6's own minimum) | minSdk 26+, targetSdk per current Play requirement (`⚠ VERIFY` the requirement at submission time — it changes annually) | |
| Orientation | **Portrait only**, portrait-upside-down disabled | Same | Portrait-only is a design commitment. The safe-area handler covers notch, Dynamic Island, punch-hole and gesture bar; there is a device-matrix screenshot test for it. |
| Accelerometer frequency | **Disabled** | Disabled | Free frame time; we do not use it. |
| Multithreaded rendering | On | On | |
| Auto graphics API | **Off** | Off | We pick the list explicitly. Auto has shipped surprises. |
| Splash | Disabled (licence permitting) / minimal | Same | The first thing on screen should be black, then water. |

---

## 11. THE 12 ARCHITECTURE RULES

Each rule is stated so that a reviewer can answer "is this violated?" with yes or no, and each names its enforcement. A PR that violates one does not get a debate; it gets a link.

**1. `ForgottenIsle.Core` never references `UnityEngine` or `UnityEditor.`**
*Enforced by:* `noEngineReferences: true` (compiler) + `ci/check-layering.sh` + `AssemblyGraphTests.Core_declares_noEngineReferences`.

**2. UI never mutates state and never loads scenes.**
`ForgottenIsle.UI` may hold `IWorldReader` and controller interfaces. It may not hold `ICommandDispatcher`, `WorldState`, `SceneManager` or `Addressables`.
*Enforced by:* `internal` visibility on `WorldState` members + grep gate C + PR review.

**3. Every mutation of `WorldState` happens inside an `ICommandHandler.Execute`.**
No system, service, binder or presenter writes to world state directly.
*Enforced by:* Roslyn analyzer FI0003 (flags assignment to a `WorldState`-rooted member outside a type implementing `ICommandHandler<>`) + `internal` visibility.

**4. Every command is a `readonly struct`; every domain event is a `readonly struct`; both are passed by `in`.**
*Enforced by:* analyzer FI0004 + an EditMode reflection test over all `ICommand`/`IDomainEvent` implementors.

**5. `Validate` is pure. `Execute` never re-validates.**
If `Validate` returns Ok, `Execute` must succeed or throw (which is a bug, not an outcome).
*Enforced by:* analyzer FI0005 (no writes to state-rooted members inside `Validate`) + a fuzz test that dispatches 10,000 random commands against random states and asserts `InternalError` count is zero.

**6. Zero heap allocations per frame in gameplay code.**
*Enforced by:* analyzers FI0001–FI0012, the `GC.GetAllocatedBytesForCurrentThread()` assertion test, and the nightly device GC budget.

**7. One `Update()`. One event flush. One place that loads scenes.**
`GameLoop.Update`, `GameLoop.LateUpdate → Flush()`, `ZoneStreamDirector`.
*Enforced by:* grep gate counting `void Update(` in `Game`+`UI` (pinned at 1), counting `Flush()` call sites (pinned at 1), counting `SceneManager.Load|Addressables.LoadScene` call sites (pinned at 1 each) + the `ARCH-EXCEPTION-001` counter pinned at 1.

**8. Domain simulation runs at a fixed 10 Hz on a constant `dt`. Presentation runs on `Time.deltaTime`. They never mix.**
No `Time.*` call may appear in a call path reachable from `SimulationRunner.Tick`.
*Enforced by:* rule 1 makes it structurally impossible in Core; analyzer FI0008 flags `Time.` in any `Game` type registered as a domain tickable.

**9. No user-facing string literals in code. No `ResultCode` without a string. No orphan keys.**
*Enforced by:* analyzer FI0009 + the four localization integrity tests.

**10. Event handlers may not raise events or dispatch commands.**
Cascade depth is exactly 1.
*Enforced by:* the `_flushing` runtime guard (throws in dev builds) + analyzer FI0010 flagging `IEventSink.Raise` / `ICommandDispatcher.Dispatch` inside a method bound as an event handler.

**11. Every asset obeys the import budget: ≤ 2048 px, ASTC, channel-packed, read/write off, no uncompressed audio, no missing lightmap data.**
*Enforced by:* the CI asset-rules stage, which fails the PR and names the offending asset path.

**12. Nothing is asserted about Unity that has not been verified.**
Any statement about a Unity API, package or platform behaviour in a design doc, ADR, code comment or PR description carries either a docs link or a `⚠ VERIFY` tag, and every `⚠ VERIFY` in this repo has a named owner and a ticket.
*Enforced by:* a grep for `⚠ VERIFY` across `/docs` producing a report that is reviewed at each milestone; an unowned `⚠ VERIFY` fails the milestone checklist, not the build.

---

## 12. FIRST FOUR WEEKS — WHAT THIS DOCUMENT MEANS IN PRACTICE

| Week | Deliverable | Proves |
|---|---|---|
| 1 | Six asmdefs, all three CI layering gates green, `TestWorld.New()` builds a Core world with no Unity, `CollectItemCommand` end-to-end with a cube and a debug HUD | The layering is real, not aspirational |
| 2 | `GameLoop` + fixed tick + event queue + the zero-allocation test passing; the perf HUD on a device | The performance policy is measurable from day 8, not retrofitted |
| 3 | Bootstrap → Session → two grey-box zones with a working curtain transition, save/load across it, memory measured on an iPhone 12 | The streaming design survives contact with hardware, and the jetsam ceiling in §0 gets verified |
| 4 | Localization pipeline: key convention in use, FI0009 failing a deliberate bad PR, one CJK atlas baked and measured | The single most expensive thing to retrofit is done before there is content to retrofit it into |

Everything else — the hydrophone overlay, the damper console, the Register, the Reel Deck — is built on top of a spine that is already proven under CI and on a device. That is the point of doing this in this order.