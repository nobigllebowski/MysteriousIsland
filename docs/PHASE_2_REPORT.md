# PHASE 2 REPORT — Vertical Slice

**Date:** 2026-09-14 · **Branch:** `claude/keen-darwin-frw656`
**Status:** implemented and pushed. **Never compiled, never run, never play-tested** — there is no
Unity, no .NET SDK and no Mono in this environment. Everything below distinguishes what was
*written* from what was *verified*, and by what.

Phase 3 has not been started.

---

## PHASE 2 STATUS

| | |
|---|---|
| Implementation | Complete for the 20 priorities, except where §10 says otherwise |
| Static gates | `ci/validate-structure.py` **exit 0** · `ci/check-layering.sh` **exit 0** |
| Compilation | **UNCONFIRMED** — no compiler in this environment |
| Tests executed | **none** — the Unity Test Runner has never been run against this work |
| Play-tested | **no** |

The honest summary: the slice is *written* end to end and every invariant a script can check still
holds. Whether it *runs* is the first thing to find out in the editor.

---

## 1. WHAT ACTUALLY WORKS

"Works" here means *implemented and statically consistent*, not *observed*. The distinction is the
whole point of §7 and §9.

- **Movement.** `PlayerRig` drives a real `CharacterController` (height 2, radius 0.34, step 0.45,
  slope 52°, skin 0.04) with acceleration 18 / deceleration 26 and gravity −22, grounded by a −2
  bias. Pitch lives on the camera pivot only, clamped ±78°, so the body never tips.
- **Touch controls.** A floating joystick on the left half of the screen and a look pad on the
  right, built in UI Toolkit with pointer capture. Look is consumed on read; move is a held state.
  Hiding the controls releases the stick, so a pause can never leave the player walking.
- **Interaction.** A registry-based proximity scan (`InteractionSystem.Tick`) picks the nearest
  eligible `Interactable` each `LateUpdate` and publishes a prompt signal. Pressing interact builds
  an `ICommand` and dispatches it. Nothing interacts by touching a collider.
- **Three interactable kinds.** `AncientMarker` (re-readable), `DiscoveryPickup` (once, then gone),
  `ZoneGate` (travels, or narrates why it will not).
- **Progression.** `WorldProgress` holds inspected / collected / unlocked as ordinal string sets.
  Every mutator is idempotent and returns whether anything changed.
- **Objectives.** Derived, never stored — `Objectives.Current(progress, zoneId)` is a pure
  function, so any order of play still yields a line that is true.
- **Two zones, built at runtime.** `ZoneBuilder` constructs terrain, rocks, flora, a landmark, fog
  and lighting from a seed. The scene assets stay empty; they are the contract, not the content.
- **HUD.** Objective block, pause button (48 dp minimum), interaction prompt card, narration card
  with a 6.4 s dwell. Every string comes from `en.csv` through `LocKey`.
- **Save.** Progression is the third `ISaveParticipant`. Save → quit → continue restores the zone,
  the player pose and everything found.
- **Audio.** Wired, convention-based, and silent: no clips ship, and none were fabricated.
- **Dev shortcuts.** F3 toggles the overlay; F5 travels to the other zone, F6 takes the brass tag,
  F7 resets progression — all through the same commands the game uses, all compiled out of release.

### What explicitly does NOT exist

No inventory grid. No crafting. No skill tree. No combat, weapons, monsters or chases. No
multiplayer. No procedural infinite world. No survival meters yet (Phase 5). No story beyond the
six narration lines below. This was deliberate: Phase 2 was scoped to a playable loop, not to
systems that would need a game to sit in.

---

## 2. FILES CREATED

| File | Lines | What it is |
|---|---:|---|
| `Core/Progress/ContentIds.cs` | 53 | Stable string ids for zones, markers, discoveries, gates |
| `Core/Progress/WorldProgress.cs` | 147 | The progression sets; idempotent mutators |
| `Core/Progress/Objectives.cs` | 85 | Pure objective derivation |
| `Game/Progress/ProgressService.cs` | 296 | Owns progress; third save participant |
| `Game/Interaction/Interactable.cs` | 97 | Abstract base: id, keys, range, command |
| `Game/Interaction/InteractionSystem.cs` | 285 | Registry, proximity scan, prompt, dispatch |
| `Game/Interaction/AncientMarker.cs` | 51 | Re-readable marker |
| `Game/Interaction/DiscoveryPickup.cs` | 76 | Collect-once discovery |
| `Game/Interaction/ZoneGate.cs` | 84 | Travel, or narrate the refusal |
| `Game/World/ZoneMeshes.cs` | 326 | Procedural ground, rock and rib meshes |
| `Game/World/ZoneBuilder.cs` | 498 | The two zone recipes |
| `Game/Audio/AudioDirector.cs` | 185 | Convention-based clip lookup; silent by design |
| `UI/Hud/HudScreen.cs` | 220 | The HUD view |
| `UI/Hud/TouchControls.cs` | 270 | Joystick and look pad |
| `UI/Controllers/HudController.cs` | 150 | Signals → HUD text |
| `Tests/EditMode/ProgressionTests.cs` | 211 | 14 cases |
| `Tests/EditMode/InteractionTests.cs` | 152 | 9 cases |
| `Tests/PlayMode/GameplayLoopTests.cs` | 300 | 5 `[UnityTest]` cases |

Plus 24 `.meta` files, generated to match the repository's existing minimal
(`fileFormatVersion` + `guid`) format so a fresh clone does not churn GUIDs.

## 3. FILES MODIFIED

| File | Change |
|---|---|
| `Core/Commands/GameCommands.cs` | `InspectCommand`, `CollectCommand` |
| `Core/Signals/GameSignals.cs` | `ProgressChangedSignal` (+ kind enum), `ObjectiveChangedSignal`, `InteractionTargetChangedSignal`, `NarrationSignal` |
| `Core/Save/SaveSections.cs` | `Progress = "progress"`, so no participant holds its section id as a literal |
| `Game/Bootstrap/CommandHandlers.cs` | `InspectHandler`, `CollectHandler`; travel now refuses a locked zone; new-game resets progression |
| `Game/Bootstrap/AppCompositionRoot.cs` | Builds `ProgressService` + `InteractionSystem`; 3 participants; 2 new handlers |
| `Game/Bootstrap/GameContext.cs` | `Progress`, `Interactions` |
| `Game/Bootstrap/ZoneFurnisher.cs` | Calls `ZoneBuilder`; adds the `CharacterController` |
| `Game/Bootstrap/AppBootstrap.cs` | Attaches the `AudioDirector` to the persistent host |
| `Game/Player/PlayerRig.cs` | Character-controller movement, teleport, interact input, interaction tick |
| `Game/Input/InputRouter.cs` | Virtual move/look from touch, merged with device input |
| `Game/Diagnostics/DevOverlay.cs` | Position / found / near / objective readouts; F5–F7 shortcuts |
| `UI/Bootstrap/UiInstaller.cs` | HUD replaces the idle screen in-world; input pump on the UI scheduler |
| `Assets/Localization/en.csv` (+ `Resources` mirror) | 28 new keys |
| `ci/validate-structure.py` | Whitelist extended for the Unity types Phase 2 introduced |

---

## 4. GAMEPLAY FLOW

```
Boot → Main Menu → NEW GAME
  → ZoneRibcage builds: terrain, rib arch, rocks, flora, fog, player, camera, HUD
  → objective: "Explore the shore."
  → walk to the Standing Stone, INSPECT
      → narration: the stone is warm from underneath
      → objective: "Something here was tagged. Find it."
  → find the Brass Tag, TAKE
      → narration: stamped 11.04.97, two initials — someone serviced this
      → ZoneFernmaw unlocks
      → objective: "Follow the gully inland."
  → the Gully Mouth gate, TRAVEL
      → Ribcage unloads, Fernmaw loads, player spawns at its anchor
      → objective: "Explore the channel."
  → the Cut Channel Wall, INSPECT   → cut at a constant fall, by someone counting
  → the Waterlogged Reel, TAKE      → quarter-inch tape; something recorded down here
      → objective: "Return to the shore."
  → the Channel Mouth gate, TRAVEL back
      → the Brass Tag is still gone; the Standing Stone can be read again
      → objective: "The record is current."
```

Before the tag is taken, dispatching `TravelToZoneCommand(ZoneFernmaw)` is **refused** at the
command layer with `NotAllowedInState` — the lock is a rule, not a hidden button.

---

## 5. SAVE DATA

Progression is section `"progress"`, written by `ProgressService` as three newline-separated
`key=value` lines whose values are id lists joined by `U+001F` (the ASCII unit separator — never
valid inside an id, so no escaping is needed and no id can forge a delimiter). Writing the
separator as `<US>` for legibility:

```
inspected=marker.rib_stone<US>marker.aqueduct_cut
collected=discovery.brass_tag
unlocked=ZoneRibcage<US>ZoneFernmaw
```

Three participants now register: `session`, `player`, `progress`.

Two deliberate robustness rules, both covered by tests:

- **A save with no `progress` section starts clean.** That is a pre-Phase-2 run, not corruption.
- **A save that records the Ribcage as locked reopens it.** No legitimate progression locks the
  opening zone, so a save claiming otherwise is damaged, and honouring it would strand the player.

## 6. ZONE CONTENT

| | ZoneRibcage | ZoneFernmaw |
|---|---|---|
| Seed | 20260914 | 71104 |
| Terrain amplitude | 3.2 | 6.4 |
| Fog density | 0.012 | 0.045 |
| Rocks / flora | 26 / 10 | 18 / 46 |
| Landmark | 6-rib arch | 14 × 3 dressed-block aqueduct wall |
| Marker | Standing Stone | Cut Channel Wall |
| Discovery | Brass Tag (unlocks Fernmaw) | Waterlogged Reel |
| Gate | The Gully Mouth → Fernmaw | The Channel Mouth → Ribcage |

Ground is a 33 × 33 grid from two Perlin octaves with the spawn apron faded flat, plus vertex
colours. `SampleHeight` re-evaluates the same function rather than raycasting, so objects can be
placed before physics has ticked once.

---

## 7. TESTS

**Written — 28 new cases.**

| Suite | Cases | Covers |
|---|---:|---|
| `ProgressionTests` (EditMode) | 14 | collect-once, unlock rules, the objective chain, save round trip, a pre-Phase-2 save, `ContentIds` ≡ `SceneKeys` |
| `InteractionTests` (EditMode) | 9 | inspect/collect legality, refusal outside gameplay, double-collect refused, markers re-readable |
| `GameplayLoopTests` (PlayMode) | 5 | the Ribcage is furnished; inspect→collect→unlock→travel; a collected discovery stays gone across a rebuild; save/quit/continue; the rig has a controller and a camera |

Repository totals after Phase 2: **229 EditMode cases** (168 `[Test]` + 61 `[TestCase]`) and
**14 PlayMode `[UnityTest]`** across 13 test files.

**Executed: none.** Not one of these has ever been run — not the 28 new ones and not the ones that
predate them. There is no Unity and no .NET runtime here.

**Not executed, and specifically worth knowing:** the PlayMode suite self-skips with
`Assert.Ignore` if the zone scenes are not in Build Settings, so a first run without
`Vardholm → Setup Project` reports *ignored*, not *failed*. Ignored is not passed.

## 8. STATIC VALIDATION

```
python3 ci/validate-structure.py   → exit 0   (105 C# files, 134 public types, 0 errors, 47 warnings)
bash ci/check-layering.sh          → exit 0   (all 5 invariants)
```

All 47 warnings are `LOCKEY` "key never used by a literal" — keys reserved for screens that are not
built yet, or composed at runtime. None is an error.

Additional audits run by hand for this phase, none of which a gate covers:

| Audit | Result |
|---|---|
| Namespace ↔ folder | matches for all new files |
| Symbol existence (every member the new tests call) | all present with matching signatures |
| asmdef dependencies | Core ← Game ← UI; tests reference all three; no upward edge |
| Duplicate types | none |
| Save participants | 3, ids distinct, all now from `SaveSections` |
| Localization coverage | every new `LocKey` has a row in `en.csv`, mirrored to `Resources` |
| Scene keys | `ContentIds.Zone*` ≡ `SceneKeys.Zone*` (also asserted by a test) |
| Runtime entry point | one `AppBootstrap` via `RuntimeInitializeOnLoadMethod`; still exactly one `Update()` |

## 9. UNITY VERIFICATION REQUIRED

In order, because each step gates the next:

1. **Open the project.** Compilation has never succeeded here. Expect the usual first-open triage.
2. **Run `Vardholm → Setup Project`.** Without it the scenes do not exist and PlayMode self-skips.
3. **Run EditMode tests.** 229 cases, none ever executed.
4. **Run PlayMode tests.** 14 cases. Confirm they *ran* rather than were ignored.
5. **Press Play and walk.** The input binding strings (`"2DVector(mode=2)"`, `<Keyboard>/w`, the
   processors) are parsed at runtime — only pressing a key proves them.
6. **Touch controls on a device.** Pointer capture behaviour under a real touchscreen, and whether
   the 74 px stick radius and 0.06 look sensitivity feel right, cannot be judged from a desk.
7. **The material fallback chain.** `ZoneBuilder.CreateMaterial` tries URP Lit → Standard →
   Unlit/Color → Sprites/Default. Which one it lands on decides whether the island looks lit or
   flat. Tagged `⚠ VERIFY` in the source.
8. **Frame time on a mid-range phone** with both zones' geometry resident during a transition.

## 10. KNOWN RISKS

| # | Risk | Why it matters |
|---|---|---|
| 1 | **Nothing has been compiled.** | Every claim in §1 is "written", not "works". This is the only risk that matters until it is retired. |
| 2 | **`ConvexHullTriangles` is star-shaped-only.** | It is documented as such and used only on point sets that satisfy it. Reused elsewhere, it produces wrong geometry silently. |
| 3 | **Material fallback chain.** | The look of the whole slice depends on which shader resolves. |
| 4 | **No audio exists.** | The wiring is real and the game is silent. Deliberate — fabricating binary clips would make "audio works" look true. |
| 5 | **Touch feel is unvalidated.** | Radius, sensitivity and dead zones are reasoned numbers, not measured ones. |
| 6 | **CONFLICT-6 is still open.** | World clock is 60× in code and 30× in docs, with survival tuned at 30×. Untouched by Phase 2; it blocks Phase 5. |
| 7 | **Phase 1 Task 0 was never run.** | Nine design documents predate the ADRs and were never reconciled. See `CURRENT_STATE.md`. |
| 8 | **PlayMode tests can pass by skipping.** | They `Assert.Ignore` without scenes. A green run means nothing until you check it was not ignored. |

## 11. NEXT PHASE

**Not started, and not to be started without being asked.**

The three things that should precede any Phase 3 work:

1. Open the editor and retire risk #1 — compile, then run both test suites, then play it.
2. Resolve CONFLICT-6 one way or the other; it will only get more expensive.
3. Run Phase 1 Task 0 — reconcile the nine pre-ADR documents, or retire them.

Phase 3's own scope is untouched by this report.
