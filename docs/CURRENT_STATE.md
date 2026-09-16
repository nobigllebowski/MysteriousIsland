# CURRENT STATE

**Updated:** 2026-09-16 · **Branch:** `claude/keen-darwin-frw656`
**Phases 2 and 3 implemented; a graphics pass; the Phase 6 slice: the radio, the Slate, the
sightline, the hint ladders, the dry-fire problem, the audio core.** The project compiled and ran
once; the graphics pass then sank the island (fixed, not re-run). Everything after that was traced
by reading, not run: 422 tests written, none executed. Phase 5 (survival, camp, fuel) is blocked
on CONFLICT-6.
Accounts: [`PHASE_2_REPORT.md`](PHASE_2_REPORT.md) · [`PHASE_3_REPORT.md`](PHASE_3_REPORT.md)
(the latter is the invisible-world fix, not the items phase — the name predates the phase).

**The project now compiles and runs.** It reached Play Mode, loaded `ZoneRibcage`, furnished it and
drove the HUD and interaction prompt — the first time anything in this repository has executed.

This file records what is *actually true right now*, verified against the repository — not what
was planned. When it disagrees with a design document, this file and the shipped code win.

---

## 1. What exists

| | Files | Lines | State |
|---|---:|---:|---|
| `ForgottenIsle.Core` (engine-free) | 54 | 7,494 | Written, never compiled |
| `ForgottenIsle.Game` | 48 | 16,705 | Written, never compiled |
| `ForgottenIsle.UI` (UI Toolkit) | 23 | 6,636 | Written, never compiled |
| Tests (396 EditMode cases, 35 PlayMode) | 27 | 9457 | Written, **never executed** |
| `ForgottenIsle.Editor` | 2 | 443 | Written, never compiled |
| Documentation | 20 | — | 9 predate the ADRs, unreconciled |

**Implemented in Phase 1:** five assemblies; bootstrap and composition root; a 10-edge game state
machine; the single `Ticker`; additive scene loading with a 20 s watchdog; zone registry capped at
2 resident zones; session service; versioned save envelope with migration seam, atomic
write (flush→rename) and `.bak` rotation; 3 save slots + autosave; engine-free JSON/CSV;
localization with `#key#` fallback; New Input System routing; placeholder capsule player rig;
dev overlay; UI Toolkit framework, theme, and menu/pause/settings screens.

**Commands registered (14):** `StartNewGame`, `ResumeSavedRun`, `SaveGame`, `QuitToMenu`,
`TravelToZone`, `Inspect`, `Collect`, `TakeItem`, `CombineItems`, `UseItem`, `OpenRadio`,
`CloseRadio`, `TuneRadio`, `SqueezeMic`.
**Save participants registered (5):** session, player, progress, inventory, radio. ADR-0009
targets 14 at completion; each later phase adds its own.

**Added in Phase 1.5:** the `ForgottenIsle.Editor` assembly (ADR-0001's fifth, previously missing);
`Vardholm → Setup Project` / `Validate Project` / `Open Bootstrap Scene` menu commands; a first-run
prompt on a fresh clone; `ZoneFurnisher`, which builds ground, light, entry anchor, player capsule
and camera for any zone scene that lacks them; `VardholmStartupValidator`, a PASS/WARN/FAIL startup
self-check; and runtime camera-pivot attachment, which removed the last Inspector-assigned
dependency in the project.

**Scene assets still do not exist in the repository**, but they are no longer created by hand —
one menu command creates all four. See `SCENE_CONTRACT.md`.

**Added in Phase 2 — the vertical slice.** Progression (`WorldProgress` + `ProgressService`,
the third save participant); derived objectives; a proximity interaction system with three
interactable kinds (marker, discovery, gate); two runtime-built zones (`ZoneBuilder` +
`ZoneMeshes`); a `CharacterController`-driven player with gravity and ground following; touch
controls (floating joystick + look pad); a HUD with objective, prompt and narration; an
`AudioDirector` that synthesises every cue until clips are authored (ADR-0027); F5–F7 dev shortcuts. The
playable chain is: read the Standing Stone → take the Brass Tag → Fernmaw unlocks → cross the
Gully Mouth → read the Cut Channel Wall → take the Waterlogged Reel → return. Travel to a locked
zone is refused at the command layer.

**Added in Phase 3 (first half) — items and the first puzzle.** `Inventory` and `Combinations` in
Core (engine-free; no stacks, no weight, no slots); `InventoryService` as the fourth save
participant; three commands and handlers; `ItemPickup` and `Mechanism` interactables. A failed
combination consumes nothing — the soft-lock rule, asserted by name in `InventoryTests`. The puzzle
chain is: find the Dry Spindle on the shore → find the Waterlogged Reel in the channel → combine
them into the Rebound Reel → take the Sluice Key from its bracket → open the seized sluice → play
the reel on the tape deck.

**Added in the graphics pass — the island stopped looking like a greybox.** Five shaders under
`Assets/Resources/Shaders`: `Vardholm/Sky` (gradient, cloud deck, sun disc placed from the light's
own transform), `Vardholm/Water` (three crossing waves, analytic normals, ripples, Fresnel, crest
foam), `Vardholm/Terrain` (uses the vertex colours `Standard` was discarding; slope rock, shoreline
sand, world-space noise), `Vardholm/Prop` (world-space mottling, weathered upward faces) and
`Vardholm/Foliage` (procedural blade cut-outs, wind in the vertex shader, wrap lighting). The height
field now shapes an actual island — a noise-perturbed coastline with land inside it and a seabed
outside — the ground grid went 33 → 97 a side, the island 120 m → 170 m, and ambient went Flat →
Trilight. First run showed only sky — the coastline formula sank the island; fixed, re-run pending (§2).

**Added — the inventory tray.** Chips below the pause button (top of screen: both thumbs live along
the bottom edge), opened by a tab. Combining is tap-then-tap, with tap-again to cancel.
`InventoryChangedSignal` carries a snapshot so the panel never reads a service; `HudController`
turns a reported pair into a `CombineItemsCommand`. ADR-0019, ADR-0020, ADR-0021 recorded.

**Added — the Phase 6 radio slice.** The trawler hull, the set with its three faults, the tuning
band with inertia, fine knob and spectrogram ribbon, three stations and the transmission at 5.240.
`RadioService` is the fifth save participant. See the changelog for what the slice deliberately
leaves out (phone mic, hint timers, the Field Slate).

**Autosave now exists.** `AutosaveDirector` writes the ring on zone entry, the voice, a solved
mechanism and a zone unlock. Solved mechanisms are progression and survive a save. On touch, the
prompt card is the world verb; there is no raw tap binding.

**Added — the six hulls and the sightline.** A passive interactable observed every tick; the
chalk line snaps in at ±4°; SIX HULLS, ONE LINE is the notebook's first entry. Aligned from the
second, third or fourth hull, three chalk in and Nadia says "Try the far end" — a `RemarkCommand`
that records nothing (ADR-0024).

**Added — the aimed use (O-10).** A selected chip plus a tap on the prompt uses the item on the
current target; the prompt reads USE <item> while a chip is up. The recorder-cells route is live.

**Added — the Field Slate.** Three tabs derived from the record (ADR-0023); the UNRESOLVED
count is the quest system and the HUD's Slate tab shows it. Handwriting, sketches and the
crew-list page are art tasks, not faked.

**Added — hint escalation.** `HintLadder` (Core) and `HintDirector` (Game): the hull line's 6:00
fallback and the radio's tier 1 (3:00), tier 3 (10:00) and tier 4 (16:00, the set left on and
sweeping by itself until a hand touches the dial or she locks), reset by the design's actions,
stopped by the voice, forgotten on entering the world (ADR-0025). Radio tier 2 is not built.

**Added — the dry-fire problem.** The wrack's six pickups, fifteen rock nodes (three chert), three
fire sites; sparks, tinder, shelter, five honest failures; wet wood dries by the fire; FIRE in the
notebook; the fire's hint ladders and blow-out counts. `FireService` is the sixth save participant
(ADR-0026). Pickups no longer grow back once taken.

**Added — the bag.** The run starts empty-handed; the orange bag on the sand yields the kit, the
objective reads "Bag first." until then, and she says so at forty seconds.

**Added — the audio core.** Every cue synthesised at attach time until authored (ADR-0027): surf,
the pressure cycle, the radio's hiss and carrier on the tolerance ladder, the fire, knock and ring.

**Changed — the objective line follows the puzzles.** `ObjectiveFacts` from the radio and the fire
beside progression; `ObjectiveKeeper` restates them on their signals.

**Added — the beachcomber's Ribcage.** Seven optional inspectables (boot print, ringed cormorant,
tide marks, oxy slag, broom arc, canvas square, and the cut vine at the gully mouth — the hook),
Slate entries only; SOMEONE appears from any sign of a hand. The mic cord is the other fuse fix.
5240 in chalk on the trawler's plate is readable by firelight only. Reading the cut vine lifts the
pressure cycle to −14 dBFS and, six seconds on, "That's not the sea."

**Still not implemented.** Survival meters, camp and fuel burn-down (Phase 5, blocked on
CONFLICT-6), the recorder drying (it ships dry), the strike swipe (the chert is a use), weather,
radio hint tier 2 (needs the inspect view), an inspect view, a proper slot picker (a two-tap
overwrite stands in), and authored audio (every cue is an arithmetic stand-in; the voice is text).

## 2. Verification status — read this before trusting anything

| Check | Result |
|---|---|
| `ci/validate-structure.py` | **exit 0** — 14 checks (the fourteenth, `MEMBER`, catches a called member that no longer exists) |
| `ci/check-layering.sh` | **exit 0** — all 5 invariants hold |
| Core references `UnityEngine` | **0 occurrences** outside comments |
| `Update()` methods | **exactly 1** (`Ticker.cs`) |
| Assembly reference direction | Core ← Game ← UI, no upward refs |
| Banned C# features | none present |
| **First editor open** | **FAILED, 2026-09-14** — 88 × CS0619, all inside `com.unity.inputsystem@1.14.0` (wrong version for `6000.6.0f1`; `1.19.0` is the correct one). Zero errors in project code. Pin corrected; re-open pending. |
| **Second editor open** | **2026-09-14** — package errors gone, project code compiled for the first time: **3 errors, all real** (2 × CS0246 missing using, 1 × CS0102 name collision). Fixed, and the validator gained checks for both classes. |
| **Compilation** | **STILL UNCONFIRMED.** Three known errors are fixed but the result has not been seen in the editor. The two HIGH RISK areas (input binding strings, `experimental.animation`) remain untested — the compiler had not reached the UI or Input assemblies. | No Unity, no .NET SDK, no Mono in the dev environment; the proxy blocks Microsoft SDK downloads. |
| **Tests** | **UNCONFIRMED — 431 tests written (396 EditMode + 35 PlayMode), 0 executed.** The PlayMode suite self-skips without the scenes, so *ignored* must never be read as *passed*. |
| **Third editor open (graphics pass)** | **2026-09-15** — the project compiled and ran; the five shaders compiled (a procedural sky was on screen). The game view showed only sky: the island had been built 440–730 m under the sea by a `Mathf.SmoothStep` misuse in the new coastline. Root-caused by reading, fixed, **re-run pending.** |
| **Shaders** | Compiled once (2026-09-15, sky visible on screen). Subsequent edits **not recompiled.** The five files under `Assets/Resources/Shaders` have not been through Unity's shader compiler, and neither CI gate can look at them — both read C#. A shader that fails to compile renders magenta, so this is visible immediately in the editor and invisible until then. |

`ci/validate-structure.py` is a deliberate compiler substitute: brace balance, namespace
conformance, engine-free Core, asmdef validity, cross-file undeclared-type detection, LocKey
coverage, duplicate types, accidental nesting, phantom usings, shadowed locals and called members
on project types. It is not a compiler, it does not read shaders, and it cannot prove the project
builds.

## 3. CONFLICTS — unresolved contradictions in the documentation

**Root cause:** ADRs 0001–0014 declared reconciliations and assigned the edits to "Phase 1 Task 0".
**Task 0 was never executed.** The ADRs are correct; the older documents were never updated.

| # | Conflict | Authority | Stale source |
|---|---|---|---|
| ~~CONFLICT-1~~ | ~~Assembly names `Isle.*`~~ | **Resolved:** `production/02` reconciled to ADR-0001 and the shipped names | — |
| ~~CONFLICT-2~~ | ~~`SurvivalStat` set~~ | **Resolved:** `architecture/03` carries the ADR-0006 set; the old tick formulas are marked superseded and untuned (CONFLICT-6) | — |
| ~~CONFLICT-3~~ | ~~`ResourceDefinition` / vessels~~ | **Resolved:** `architecture/03` §2.3 and `InventoryState` reconciled to ADR-0007 | — |
| ~~CONFLICT-4~~ | ~~Tap-to-move~~ | **Resolved:** beat 2:00 re-authored joystick-first (ADR-0008); `production/02` reconciled | — |
| ~~CONFLICT-5~~ | ~~uGUI~~ | **Resolved:** `production/03` §4 marked superseded by ADR-0014 | — |
| **CONFLICT-7** | **Code contradicts every document.** All of `docs/` says the renderer is **URP** | **UNRESOLVED — needs a decision.** `Packages/manifest.json` has no `com.unity.render-pipelines.universal` and `GraphicsSettings.m_CustomRenderPipeline` is `{fileID: 0}`: the project runs on **Built-in** | `CLAUDE.md:5`, `PROJECT_HANDOFF.md`, `ARCHITECTURE.md`, ADR-0014 discussion |
| **CONFLICT-6** | **Code contradicts spec.** World clock: docs say `1 real second = 30 world seconds` (48-min day); `Ticker.DefaultWorldSecondsPerRealSecond = 60.0` (24-min day) | **UNRESOLVED — needs a human decision** | `architecture/02-core-systems.md:523,872` vs `Assets/Scripts/Game/Bootstrap/Ticker.cs:51` |

**CONFLICT-6 is the one that matters most.** The survival tick formulas were tuned against 30×.
At 60× every drain rate is effectively doubled in real time, so the 36-hour water deadline that
Act 1 is built around arrives in half the intended wall-clock. Decide before any survival work.

**CONFLICT-4's content cost is paid.** Beat 2:00 now teaches the joystick by the stick blooming
under the first thumb that lands, with the bag dead ahead; tap-to-move is the accessibility
assist that offers itself once at 90 s. The stand animation and the cues are not built.

## 4. UNCONFIRMED — believed but not verified

- **The project compiles.** Nothing here has been through a C# compiler.
- **The 243 tests pass.**
- **Every Unity 6 API claim** tagged `⚠ VERIFY` in `architecture/01-technical-architecture.md` §0:
  Render Graph, GPU Resident Drawer, Adaptive Probe Volumes, `Awaitable` allocation profile,
  `ProfilerRecorder` counter names, `AsyncOperation.progress` semantics with
  `allowSceneActivation = false`, Newtonsoft being engine-free, VContainer compatibility,
  TextMeshPro RTL shaping, `com.unity.addressables.android`.
- **`File.Replace` atomicity on Android scoped storage.** Acceptance item 28 is gating.
- **iPhone 12 per-app memory ceiling** (~2 GB assumed; the 1.35 GB working-set target is a design
  decision, not a measurement).
- **60 fps on iPhone 12 at native resolution.** Alpha-tested foliage overdraw — the dominant
  fragment cost in the jungle — is budgeted nowhere (open item O-3).
- **Every proper noun.** No trademark register has been searched, for any name, ever. "Vardholm"
  and "Orrimond" returned no web collisions; that is not clearance.
- **All package versions** in `Packages/manifest.json`.

## 5. Known gaps deliberately left open

- **O-8** — UI calls `GameStateMachine.TryTransition` directly instead of dispatching commands.
  Defensible (mode changes are navigation) but inconsistent with the rule its own screens follow.
- **O-9** — `MainMenu.unity` is documented as the cinematic backdrop but nothing loads it; the
  menu renders on a flat themed ground. `Assets/Scenes/README.md` carries a warning.
- Scene assets are generated by `Vardholm → Setup Project`, not committed. Unity scene files are
  editor-serialized YAML with GUID cross-references; hand-authoring one outside the editor risks a
  corrupt asset, which is worse than a missing one.
- **Two HIGH RISK compile areas** (`UNITY_RISK_AUDIT.md`): code-built input binding strings, which
  the compiler cannot check, and `experimental.animation` in three UI files.

## 6. How to get it running

`docs/dev-setup.md` — clean machine to running build. Summary: Unity Hub → install
`6000.6.0f1` → *Add project from disk* → **`Vardholm → Setup Project`** → Game view 390×844
portrait → open `Bootstrap.unity` → Play → read the `VARDHOLM STARTUP CHECK` block.
