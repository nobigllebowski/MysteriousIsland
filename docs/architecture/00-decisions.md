# ARCHITECTURE DECISION RECORD

**Status: BINDING.** Where any other document in this repository contradicts a decision here,
**this document wins** and the other document is wrong and must be amended.

## Why this file exists

The three architecture documents (`01-technical-architecture.md`, `02-core-systems.md`,
`03-data-and-save-architecture.md`) were authored in parallel against the Story Bible and were
never reconciled with each other. An adversarial review pass found that they specify **three
different assembly layouts, two incompatible streaming models, two `SurvivalStat` enums, two
inventory models, two world-clock scales and an incomplete save participant list.**

Each document is internally disciplined. Together they were not executable. This ADR set is the
reconciliation pass. **Phase 1 does not open until every decision below is reflected in the
documents it governs** — that is Phase 1 Task 0.

---

## ADR-0001 — Assembly layout

**Decision.** Five assemblies, exactly as named in `01-technical-architecture.md` §1.1:
`ForgottenIsle.Core`, `.Game`, `.UI`, `.Editor`, `.Tests.EditMode`, `.Tests.PlayMode`.

**Rejected.** `Isle.Domain` (Core Systems §0.2) and `Isle.Core`/`Isle.Unity`/`Isle.Authoring`
(Data & Save Part 0).

**Why.** The Technical Architecture naming is the only one that already carries the asmdef JSON
skeletons, the `AssemblyGraphTests` allowed-reference dictionary and the `ci/check-layering.sh`
gate. The other two would require rewriting a working enforcement mechanism to gain nothing.

**Consequence.** `02-core-systems.md` §0.2 and `03-data-and-save-architecture.md` Part 0 are
amended to use these names. A global rename is mechanical and must happen before any `.asmdef`
is created.

---

## ADR-0002 — `ForgottenIsle.Core` is engine-free without exception

**Decision.** `"noEngineReferences": true`. Core references **no** `UnityEngine` type. Not
`Vector3`, not `Quaternion`, not `ScriptableObject`.

**Rejected.** Core Systems §0.2's "does not reference `UnityEngine` except for `Vector3`/
`Quaternion` math types and `ScriptableObject` for authoring data." This is self-contradictory:
those types *are* `UnityEngine`, and `noEngineReferences` makes the assembly fail to compile the
moment one appears. §9.3's `PuzzleDefinition : ScriptableObject` in domain code is the same bug.

**Why.** The engine-free property is load-bearing for four things the project has already
committed to: a sub-10-second EditMode suite, assertable determinism, CI fuzzing of command
sequences, and the structural guarantee that UI cannot mutate state. "Engine-free except for the
convenient bits" buys none of them — a single `UnityEngine` reference costs the whole property.

**Consequence.** Core defines `readonly struct Vec3` and `Vec3Math` (~200 lines, per Technical
Architecture §1.2). Definitions in Core are immutable POCOs produced by the bake (ADR-0005);
`ScriptableObject` exists only in `ForgottenIsle.Game`/`.Editor` as the *authoring* surface.
`PuzzleDefinition` in Core is a plain record, not a `ScriptableObject`.

---

## ADR-0003 — C# language level and the `record` / `init` / `required` question

**Decision.** `record` and `init` are permitted in Core, unlocked by a one-line polyfill:

```csharp
// ForgottenIsle.Core/Compat/IsExternalInit.cs
namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }
```

`required` members are **banned** — use constructor parameters or a validated factory.
`ImmutableArray<T>` is **banned** in Core's public surface — use `IReadOnlyList<T>` backed by a
defensively-copied array at construction.

**Why.** `03-data-and-save-architecture.md` Parts 2–4 are written almost entirely in `record` +
`init` + `required` + `ImmutableArray<T>`. As shipped that is a **hard compile failure on day
one**, not a style question. The polyfill is standard and recovers the two features that carry
most of the value; the other two are not worth vendoring `System.Collections.Immutable` into a
mobile build.

**Action.** `⚠ VERIFY` the exact default C# language version of the pinned Unity 6 LTS build as
**Phase 1 Task 1**, before any definition type is written. If `record` is unavailable even with
the polyfill, Part 2 falls back to sealed classes with read-only properties and a constructor —
a mechanical transform, but one we must know about on day one rather than day thirty.

---

## ADR-0004 — Zone streaming: the curtain model

**Decision.** Adopt `01-technical-architecture.md` §6.3 — hard transitions through a loading
curtain, never more than two zones resident, outgoing zone hard-unloaded.

**Rejected.** The seamless proxy-LOD streaming described in Core Systems §12.3.

**Why.** The caldera geography gives natural chokepoints; the Field Slate entry at a zone boundary
is *content* that makes a short load read as a deliberate beat rather than a stall; and the proxy
model costs authoring on every zone boundary in the game for a benefit this structure does not
need. The two models also imply different memory budgets, and only one of them was costed.

**Consequence.** Core Systems §12.3's proxy-LOD paragraph is deleted. If seamless transitions are
ever wanted, they return as a scoped Phase 14 investigation with their own memory budget.

---

## ADR-0005 — Definition authoring and the bake

**Decision.** As `03-data-and-save-architecture.md` §1.4: author in ScriptableObjects, bake to
immutable Core records, ship a versioned binary catalog, consume through `IDefinitionRepository`.

**Amendment (blocking).** The catalog **must ship inside the player build**. As written,
`Assets/Data/Baked/isle.catalog` is outside `StreamingAssets`, `Resources` and any Addressables
group, so it would not be included in a build at all — and even inside `StreamingAssets`, Android
serves it from inside the APK where direct file reads fail.

**Resolution.** The baked catalog is an **Addressable**, loaded asynchronously as step 1 of boot.
The composition root awaits the catalog before registering `IDefinitionRepository`. This avoids
the `jar:` path problem entirely and gives us patchability for free.

---

## ADR-0006 — `SurvivalStat` is the Core Systems set

**Decision.** `enum SurvivalStat : byte { Health, Energy, Hydration, Satiation, CoreTemp }`,
values `0..100` (CoreTemp in °C, 30.0–40.0).

**Rejected.** Data & Save §3.3's `{ Hydration, Warmth, Fatigue, Morale, RecorderCharge }`.

**Why.** The Core Systems set is the one with tick formulas, band thresholds, collapse handling,
stat-interaction coupling and CI balance guardrails attached. The other is a bare enum.

**Note.** `RecorderCharge` is a real thing the game needs — the field recorder starts at 9%
battery — but it is **not a survival stat**. It belongs to `PlayerState` as equipment condition.
`Morale` is cut; the Story Bible has no sanity mechanic and §8 forbids one in spirit.

---

## ADR-0007 — One inventory model: items, not resources

**Decision.** Delete `ResourceDefinition`, `ResourceKind`, `InventoryState.Resources`,
`InventoryState.Vessels` and `VesselContents`. Water is a **stacking consumable item** held in a
vessel item with a charge count (`item.pouch_water` with `Charges: 0..4`), not a float quantity.

**Rejected.** The dual item/slot + resource/vessel model in Data & Save §2.3/§3.2.

**Why.** Two inventory models is one too many, and the resource model exists to serve exactly one
case — water in litres. A charge count on a vessel item expresses that adequately, keeps a single
capacity rule, and is the smaller system by a wide margin. `InventorySystem`'s 36 slots plus a
gram budget stand unchanged.

---

## ADR-0008 — Locomotion: floating joystick, not tap-to-move

**Decision.** `05-mobile-ux-plan.md` §2.3 wins. A floating dynamic joystick is the primary
locomotion control. Tap-to-move remains an **accessibility assist only**.

**Rejected.** The tap-to-move primary specified in the prologue script's 2:00 beat and in the
roadmap's Phase 2.

**Why.** The UX rationale is a comfort and core-loop argument, not a preference: tap-to-move
induces involuntary camera yaw, which is the documented sickness trigger this game has already
committed to designing against, and it breaks the hydrophone sweep — the signature verb.

**Consequence — this one costs content.** The prologue beat at 2:00 teaches movement "by there
being exactly one thing worth walking to", which is a teaching moment that **only works for
tap-to-move**. `04-first-30-minutes.md` beat 2:00 must be re-authored for a joystick-first
tutorial. This is a real rewrite, not a find-and-replace, and it is scheduled as a Phase 2 design
task rather than being quietly dropped.

---

## ADR-0009 — The save participant list is 14, and `GameState` must match

**Decision.** `GameState` gains `CraftingState` (jobs + reservations), `CombinationState` (known
recipe set) and `AudioState` (played-once set, mix snapshot), and the equipped-tool lease moves
explicitly to `PlayerState.EquippedToolInstanceId`.

**Why.** Core Systems §15.3 enumerates 14 save participants and asserts
`_participants.Count == 14` at boot. Data & Save §3.13's `GameState` has 11 fields. The gap is not
cosmetic: **craft inputs are removed from inventory at reservation time**, so with no
`CraftingState` a reload destroys every item reserved by an in-flight craft. That is a data-loss
bug, discoverable only by a player who saves mid-craft.

**Consequence.** The `_participants.Count == 14` assertion stays, and a round-trip test asserting
that a mid-craft save restores its reservations is a **Phase 4 gating test**.

---

## ADR-0010 — The anti-softlock rule set has two holes; both are closed here

**Decision.**
1. **Diesel is no longer the finite exception.** World Structure declares diesel the one
   non-renewable resource and then requires it for the Act 3 generator solve on the critical path.
   A player who burns it is softlocked. Diesel becomes **renewable via a slow Fold Camp
   condensate-and-filter loop** (≈1 jerrycan per 6 in-game hours) once the camp is powered — slow
   enough to stay precious, impossible to exhaust.
2. **The Ash Throat valve order cannot depend on the Register.** Rule R10 guarantees two
   independent hint sources per puzzle, but for the valve order one of the two is the Register
   capability, which the player cannot obtain until Act 4 — and the valve puzzle is in Act 3.
   A second Act-3-reachable source is added: **Sabo's wire-taped valve diagram** in the Ash Throat
   pipe run, physically present and readable with no capability at all.

**Why.** A softlock in a game with no combat and no fail state is the single worst bug class this
product can ship, because the player has no way to recognise it as a bug.

---

## ADR-0011 — The save spine lands in Phase 1, not Phase 11

**Decision.** `SaveSystem` ships complete in Phase 1 with two participants. Every subsequent phase
adds its own `ISaveParticipant` in the **same PR** that adds its system. Phase 11 survives as
*Save Hardening* — the adversarial corruption, migration and out-of-space pass.

**Why.** Ten phases of systems written without `ISaveParticipant` means ten systems whose owned
state is scattered and partly implicit in scene-object positions. Retrofitting persistence into
that is a re-architecture wearing a feature's clothes. It is also fatal to the MVP specifically:
the MVP's Definition of Done requires resume-from-any-point and kill-during-write recovery, so as
the brief orders the phases, the MVP is not shippable until Phase 11.

**Consequence.** Adding a participant costs ~1 day per phase and forces every system to answer
"what do you own?" on the day it is written — which is exactly the question that keeps systems
from growing God-class state. See `../production/02-mvp-scope-and-roadmap.md` §2.18 Problem 1.

---

## ADR-0012 — Composition is hand-wired in Phase 1; VContainer is a Phase 2 go/no-go

**Decision.** `AppCompositionRoot.Build()` is one static method with one line per constructed
object. No DI container in Phase 1.

**Why.** Phase 1 constructs about a dozen objects. A container buys nothing at that size and costs
a dependency, a learning curve and a layer of indirection between a reviewer and the object graph
— during the exact phase whose purpose is to make the object graph legible.

**Consequence.** Every class takes its dependencies through its constructor, with no service
locator and no `FindObjectOfType`. That is the actual discipline; the container is just one way to
automate it. Because the discipline holds, adopting VContainer later changes
`AppCompositionRoot.cs` and nothing else. The spike is a scheduled Phase 2 task with a written
go/no-go.

---

## ADR-0013 — Save codec: Newtonsoft if Core can reference it, hand-rolled if not

**Decision.** Attempt `com.unity.nuget.newtonsoft-json` as a precompiled reference from
`ForgottenIsle.Core`. If it is not engine-free, fall back to a hand-rolled
`SaveWriter`/`SaveReader` over `Span<byte>` (~250 lines).

**Why.** ADR-0002 makes Core engine-free without exception, and a serializer that transitively
references `UnityEngine` would break that — the one property the architecture is least willing to
trade. Reflection-based serializers are also the classic IL2CPP + managed-stripping failure, which
is why acceptance item 27 tests deserialization at the shipped stripping level.

**Note.** The hand-rolled writer is the shape this eventually migrates to anyway: MessagePack-style
explicit integer keys, which is what makes the migration framework tractable. Resolving this is the
**first sub-task of Phase 1 Task 8**, not a discovery mid-sprint. See risk R1.

---

## ADR-0014 — UI Toolkit, not uGUI. **This reverses Phase 1 Plan §4.**

**Decision.** The UI layer is **UI Toolkit**, built in code, ported from the NATION: WORLD ORDER
project's UI framework.

**Superseded.** `03-phase-1-plan.md` §4 chose uGUI.

**Why the earlier decision was right then and wrong now.** The uGUI reasoning was: Phase 1 has
three screens, and we should not learn a UI framework while simultaneously proving an
architecture. That was sound **under the assumption that we were starting from nothing.**

We are not. `nobigllebowski/MobileGame` contains a complete, working, code-built UI Toolkit
premium-mobile framework written by this same team: `UIService` (already at a 390×844 portrait
reference — the exact figure in our Mobile UX Plan), `ScreenStack` with 260 ms transitions,
`UIScreen`, `SafeAreaElement` reading real insets, `BottomSheet` with drag detents, `ModalLayer`,
`ToastLayer`, and a design-token palette.

So the decision is no longer "learn UI Toolkit vs. use familiar uGUI." It is "**inherit a working
framework the team already wrote, or rebuild it in a less suitable technology.**" The bottom-sheet
inventory, the layered HUD and the discovery toasts in the Mobile UX Plan map onto the ported
components almost one to one.

**Consequence.**
- Phase 1 ports `UIService`, `ScreenStack`, `UIScreen`, `SafeAreaElement`, `ToastLayer`,
  `Buttons` and `Typography`, stripping their country/economy dependencies.
- Nation's `Palette` is **not** ported as-is — its cold blue (`#05080F` / `#3B82F6`) is wrong for
  a tropical caldera. `Theme.cs` recolors to deep green, dark blue, sunset orange, gold for
  discoveries, red for danger.
- Screens receive a narrow `IUiContext` (`ILocalizedText` + `ICoreLog`) rather than the whole
  context object. This is how ADR-0002's "UI never mutates state" becomes a **type-level**
  guarantee rather than a code-review convention.
- The Phase 12 UI polish phase gets materially cheaper.

**What we deliberately do not inherit:** Nation's `GameContext.Current` static singleton. A
service locator lets any class reach any service, which is the coupling ADR-0012 exists to
prevent. Vardholm passes dependencies through constructors.

---

## ADR-0015 — Objectives are derived, never stored

**Decision.** The current objective is a pure function of progression and the player's zone —
`Objectives.Current(WorldProgress, zoneId)` in engine-free Core. No objective is written to the
save, advanced by a handler, or held as state anywhere.

**Why.** A stored objective is a second copy of the truth, and the two copies drift the moment a
player does something out of order. The Phase 2 chain is short enough to skip a beat in: take the
brass tag before reading the standing stone and a stored "read the stone" instruction survives
into a state where it is already satisfied. Derivation cannot drift, because there is nothing to
drift from — any state, reached in any order, yields the line its state implies.

**Consequence.**
- Adding a beat means adding a branch to one function, not a migration.
- The objective needs no save section, and a restored run shows the right line with no extra step.
- The HUD must be able to *pull* the current objective as well as receive the change signal
  (`HudController.PrimeObjective`), because it is built after a restore has already published.
- The cost is real: the function is a chain of conditions, and it will get long. When it does, the
  answer is a table of (predicate → key) rows, not a stored field.

---

## ADR-0016 — Zones are furnished at runtime from a recipe, not authored as assets

**Decision.** Zone scene assets stay empty. `ZoneBuilder` holds one `Recipe` per zone — seed,
terrain amplitude, fog, rock and flora counts, landmark, and its interactables — and constructs
everything on entry via `ZoneMeshes`. `ZoneFurnisher` skips a zone that already has authored
content, so hand-built content can replace a recipe later without a code change.

**Why.** Unity scene files are editor-serialized YAML with GUID cross-references. This project is
developed in an environment with no Unity, so an authored scene could only be written blind, and a
corrupt scene asset is worse than a missing one. Beyond that constraint, a recipe is reviewable in
a diff and a scene is not: "Fernmaw has 46 flora and a fog density of 0.045" is a line someone can
disagree with.

**Consequence.**
- `SCENE_CONTRACT.md` holds: the scene is the contract, the builder is the content.
- Zones are cheap to add — a second `Recipe`, not a day in the editor.
- Persistence-by-rebuild follows necessarily: a zone destroyed and rebuilt on each entry must
  re-apply collected state, which is what `Interactable.ApplyRestoredState` exists for.
- **The look is unverified.** `CreateMaterial` walks a shader fallback chain (URP Lit → Standard →
  Unlit/Color → Sprites/Default) and which one resolves decides whether the island looks lit or
  flat. Tagged `⚠ VERIFY` in the source.
- This is a scaffold for a vertical slice, not a terrain pipeline. It should be replaced by
  authored art before the MVP boundary, and the empty-scene check is the seam that allows it.

---

## ADR-0017 — Interaction is a registry scan, not physics

**Decision.** `InteractionSystem` keeps a list of registered `Interactable`s and picks the nearest
eligible one by squared distance each `LateUpdate`. No `OverlapSphere`, no trigger colliders, no
`OnTriggerEnter`.

**Why.** A physics query answers "what is near me" — but the question is "what may I act on now",
and eligibility is a progression rule (`CanInteract(IInteractionServices)`), not a geometric one. A
trigger-based design ends up asking physics a question and then re-filtering the answer, which is
two mechanisms where one will do. A registry is also testable without a physics tick, and a zone
that is rebuilt on every entry registers its contents anyway.

**Consequence.**
- The scan is O(n) over one zone's interactables — tens of objects, not thousands. If a zone ever
  holds enough to matter, the fix is a spatial bucket, not colliders.
- Nothing interacts by being touched, so an interactable needs no collider at all.
- `Interactable` is an abstract `MonoBehaviour` rather than an interface, deliberately: registration
  and lifetime belong to the base, and every implementation is a component anyway.
- The prompt is published as a signal, so the HUD learns of a target without gameplay knowing a
  HUD exists.

---

## ADR-0018 — No placeholder audio binaries, ever

**Decision.** `AudioDirector` is fully wired and the project ships **zero** audio files. Every
clip lookup may return null; every play call is then a no-op.

**Why.** The alternative — generating placeholder `.wav` files so the audio path has something to
play — puts unreviewable binary bytes in git and, far worse, makes "the audio works" look true when
nothing has been authored. A silent game that is honestly silent is a smaller problem than a noisy
one that has hidden the fact that its content does not exist.

**Consequence.**
- Clips resolve by convention (`Audio/ambient_<zone>`, `Audio/sfx_<verb>`) through `Resources`, the
  same mechanism the localization tables use, so adding sound is a file drop, not an Inspector pass.
- Misses are cached, so an absent effect is not looked up on every interaction.
- The director warns **once** that it is running silent, so the state is visible in the log without
  drowning it.
- This generalizes: no fabricated asset of any kind stands in for content that has not been made.

## ADR-0019 — A failed combination costs the player nothing

**Status.** Accepted. Implemented in `InventoryService.Combine`, asserted in `InventoryTests`.

**Decision.** Two items are consumed only after the recipe is known to match. A wrong pairing
changes nothing at all and answers with a line of narration.

**Why.** This game has no shop, no respawn and no way to get an item back. A combination that eats
its inputs on a guess is a soft lock the player cannot see coming and cannot undo, and it is the
single most common way this genre breaks itself. The cost of the alternative is not a frustrating
moment — it is a save file that can no longer be finished.

**Consequence.**
- `Combine` checks possession, then the table, and only then removes anything.
- Silence is not an acceptable answer to a wrong pairing: a player who gets no response cannot tell
  a refusal from a broken control, and starts distrusting every combination they have not seen work.
- The rule generalizes to `UseItem`: the target decides whether the item is spent, and a refusal
  spends nothing.

---

## ADR-0020 — The inventory is a tray at the top of the screen, and combining is tap-then-tap

**Status.** Accepted. Implemented in `InventoryPanel`.

**Decision.** Carried items appear as chips in a strip below the pause button, opened by a tab. The
first tap on a chip selects it; a second tap on a different chip requests that combination; a second
tap on the same chip cancels. No drag, no long press, no separate combine mode, and no full-screen
bag.

**Why, for the position.** Both of this game's thumbs live along the bottom edge — the movement
stick on the left, the look pad on the right. A tray down there, which is where every desktop game
puts one, is an opaque sheet over the controls the player is holding. The top strip of a portrait
screen is the only part a thumb never rests on.

**Why, for the gesture.** A drag needs two points of contact with a moving world behind it and has
no cancel; a long press has no affordance and no cancel either. Tap-then-tap is one thumb, nothing
to learn, and the cancel is the obvious thing to try — tapping the thing again. Given ADR-0019's
subject matter, having a way out of a half-made choice is not a nicety.

**Why not a full-screen bag.** The game holds five or six specific objects, not an economy. A grid
of slots is a promise about what kind of game this is, and it is not this one.

**Consequence.**
- The panel renders strings it is handed and reports taps by id; it holds no service and cannot
  change what is carried. `HudController` turns a reported pair into a `CombineItemsCommand`.
- `InventoryChangedSignal` carries a snapshot of the whole inventory, because "re-read the service"
  is not something a UI listener is permitted to do (ADR-0002).
- `InventoryPanel.TapItem` is public: it is the panel's input entry point, which puts the selection
  rule somewhere it can be exercised without a panel, an event system or a frame.

---

## ADR-0021 — The world is drawn with this project's own shaders, kept in `Resources`

**Status.** Accepted. Implemented as five shaders under `Assets/Resources/Shaders`.

**Decision.** Terrain, props, foliage, water and sky each get a purpose-written shader. Every one
lives under `Resources`, is loaded with `Resources.Load` before `Shader.Find`, and every material
falls back to the stock lit shader when its own cannot be found.

**Why not `Standard`.** It ignores mesh vertex colours. `ZoneMeshes` has always written a height
gradient into every ground vertex and `Standard` has always discarded it, so the terrain rendered as
one flat tint — the computation was happening and being thrown away. Everything else follows from
the same place: procedural world, no imported art, no texture budget, so what a surface looks like
has to be arithmetic.

**Why `Resources` and not `Shader.Find` alone.** `Shader.Find` resolves anything in the project
while running in the editor and only what the build actually included once the game is on a device.
A shader found that way in Play mode can be missing at runtime, which is the classic "it worked in
the editor" failure. A folder under `Resources` is a guarantee of inclusion rather than a hope.

**Consequence.**
- A missing shader costs the island its looks and not its playability: every factory has a fallback.
- Shaders are outside what either CI gate can check — both read C# — so a shader error is invisible
  here and immediate in the editor, where it renders magenta.
- Animation that is purely visual (wind, waves) lives in the vertex shader, not in `Ticker`. It is
  not an exception to the one-`Update()` rule: no C# code ever learns that anything moved.

---

## ADR-0022 — The radio is one service, one world object, and one shared piece of arithmetic

**Status.** Accepted. Implemented as `Core.Radio.*`, `Game.Radio.RadioService`,
`Game.Interaction.RadioSet`, `UI.Hud.RadioPanel`.

**Decision.** The radio's state lives in `RadioService`, the fifth save participant. `RadioSet` is
that service's presence in the scene and holds nothing: a zone is rebuilt on every entry, and a
scene object that remembered anything would be forgetting it on every entry. The tuning band draws
its spectrogram from `RadioTuner.Spectrum`, and the lock rule the game grants comes from
`RadioTuner.Receive` — the same Core arithmetic, so the ribbon a player reads and the rule that
judges them cannot drift apart. What fixes which fault is a table in `RadioRepair`, in Core, next
to the faults, for the reason `Mechanism` already established: it is the set that knows what fits.

**Why.** The prologue's climax is a frequency-matching puzzle read off a live spectrogram. If the
picture and the rule were two implementations, the first time one was tuned without the other the
puzzle would become either unfair or unreadable, and nobody would know which. One function, two
readers.

**Consequence.**
- Opening, closing, tuning and squeezing the mic are commands. The dial is game state, not UI
  state: a pause puts the set down through the dispatcher, and a refused tune leaves the needle
  where the game says it is rather than where the thumb wanted it.
- `RadioChangedSignal` carries the full tuning state on every publish, so the panel — UI, which
  may not read a service — always has what it needs to draw.
- A first lock on a station is recorded and never removed. The record is never lost in this game.
- The panel exposes one static function, `MhzForDrag`, pinning the design's coarse rate, fine
  ratio and drag direction in a test.
- Story beats are composed by the game, not the HUD: a multi-line beat travels as a
  `NarrationSequenceSignal` of keys, and the HUD only translates and paces it.
- The tuning band's flywheel and ribbon tick on the UI Toolkit scheduler, not the `Ticker`. That
  is cosmetic animation the game never reads and that stops with the panel; the one-`Update()`
  rule is about game logic having one clock, and this is not game logic.
- The recorder's cells are not offered from the proximity prompt: a prompt that fits whatever
  the player happens to carry would make the prologue's one real decision for them. They go in
  by the aimed use -- a selected chip and a tap on the prompt (O-10, resolved).

---

## ADR-0023 — The Field Slate is a derived view, and it is not a save participant

**Status.** Accepted. Implemented as `Core.Progress.Slate`, `Game.Progress.SlateDirector`,
`UI.Screens.SlateScreen`, `UI.Controllers.SlateController`.

**Decision.** The notebook's three tabs are computed from progression and the radio's facts by a
pure function, on every change. Nothing about the Slate is stored. Opening it is navigation on the
screen stack, not a game-state change; the world continues underneath.

**Why.** ADR-0015 already established that objectives are derived so they can never disagree with
the record. The Slate is the same thing with three tabs: a second record of "what has been found"
would be a second thing to migrate and the first thing to drift. Deriving also gives the design's
rule for free — the record is never lost, because its inputs are in the save.

**Consequence.**
- Adding content means adding a line to `Slate.Build` and its rows; there is no schema.
- The UNRESOLVED count is `SlateContents.OpenQuestions`, and the HUD badge shows that number.
- `Resolved` exists on every line and is false everywhere in the prologue; Act 2 sets it.
- When the radio's facts grow, `SlateFacts` grows; the derivation stays engine-free and testable
  with a struct literal.

---

---

## Open items — tracked, not resolved

| # | Item | Owner | Due |
|---|---|---|---|
| O-1 | **LOST / DHARMA convergence.** The review flagged a dense structural convergence (an isolated island, a secretive mid-century research programme with station branding, a sealed installation, a lone caretaker performing an endless maintenance duty). The Originality Statement omits it while listing three weaker comparables. This is genre convergence rather than copying — but the statement is weaker for not naming it. | Narrative Director | Before any public pitch |
| O-2 | **Proper-noun clearance.** No trademark register has been searched for any name in this project. "Orrimond" (replacing "Mercator", which collided with a live Guernsey trust company portrayed negatively) returned no web collisions but is **not cleared**. | Counsel | Before announce |
| O-3 | **Foliage overdraw is unbudgeted.** Alpha-tested foliage is the dominant fragment cost in Fernmaw and appears in no performance budget. The "native resolution, 60 fps, iPhone 12" High tier is the least-supported number in the package. | Tech Director | Phase 0 spike, before Phase 7 |
| O-4 | **Three zone specs promise volumetric-looking effects** the URP renderer budget does not fund. Either the budget grows or the specs change. | Tech Director + Art | Before Phase 7 |
| O-5 | **No privacy-manifest plan**, while the package calls two required-reason APIs. | Tech Director | Before Phase 16 |
| O-8 | **The UI layer transitions the mode machine directly.** `UiInstaller.OnReturnToMenu` and `PauseController` call `GameStateMachine.TryTransition` rather than dispatching a command. This is defensible — mode changes are navigation, not world state, and the layering gate's "UI never touches `SessionService`" rule still holds — but it is inconsistent with the UI→controller→command rule the same screens follow for everything else. Resolve in Phase 2 by adding `PauseCommand`, `ResumeCommand` and `DismissFailureCommand`, then extend `ci/check-layering.sh` to ban `TryTransition` from the UI assembly. | Lead Gameplay | Phase 2 |
| O-9 | **The cinematic menu backdrop is documented but not wired.** `Assets/Scenes/README.md` specifies `MainMenu.unity` as a backdrop scene, and the Mobile UX Plan calls for island coastline, ocean and moving trees behind the menu. Phase 1 loads no such scene — the menu renders on the persistent `UIDocument` over a flat themed ground. Deliberate scope discipline, not an oversight, but the README and the code must not disagree. Wire a menu-scene lifecycle owner in Phase 2, or delete the scene from the README. | Tech Director + Art | Phase 2 |
| O-7 | **Nation port provenance.** Every ported file carries a header naming its Nation origin. If MobileGame is ever made private or relicensed, that provenance is the record. Same owner, so no licensing issue today. | Tech Director | Ongoing |
| O-6 | **World clock scale** is stated as two different values across documents. Pick one and assert it in a test. | Lead Gameplay | Phase 1 Task 0 |
| O-10 | ~~No "use this item on that" verb.~~ **Resolved:** a selected chip plus a tap on the prompt dispatches `UseItemCommand(item, target)`; the prompt reads USE <item> while a chip is up. The recorder's cells are now the player's choice. | Lead Gameplay | Done |
| O-11 | ~~No slot-delete path in the UI.~~ **Resolved:** a second tap on NEW GAME within eight seconds overwrites the oldest readable run; corrupt slots are never chosen. A proper slot picker remains a UX-polish item. | Lead UX | Done (two-tap confirm) |
| O-12 | ~~`RecordedPercent` is never computed.~~ **Resolved:** `Recorded.Percent` over the recordable content ids, kept live by `RecordKeeper`. Weighting for the full document set is a later concern. | Lead Gameplay | Done |
