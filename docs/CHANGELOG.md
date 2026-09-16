# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Part of the project memory system — see [`PROJECT_HANDOFF.md`](PROJECT_HANDOFF.md) for the
reading order. For what is true *right now* rather than what changed, see
[`CURRENT_STATE.md`](CURRENT_STATE.md).

## [Unreleased]

### Changed — the objective line follows the puzzles

The HUD's one line named only the tag chain; the fire and the radio, the prologue's two puzzles,
never appeared on it. `Objectives.Current` now takes `ObjectiveFacts` (radio found, working,
heard; fire engaged, lit) beside progression: *"Spark, tinder, shelter."* while something is laid
and nothing burns; *"The set's dead. Power, contacts, fuse."* once the set is found; *"Find her
on the dial."* once it works. Each is named only after the player has met it — nothing is
required, and a fire never started on is never asked for. `ObjectiveKeeper` restates the facts to
`ProgressService` on radio, fire and state signals; the derivation stays engine-free (ADR-0015).
Three tests.

### Added — the decision that isn't flagged (§27:00)

The transmission sequence now ends on the consequence of the repair: with the torch's cells in
the set she holds the recorder to the speaker and *"Record it. Record it properly this time."*;
with the recorder's cells in the set, *"I heard it. I didn't capture it. I've done that before."*
and she writes it down by hand. Neither is announced. The Slate already carried the scar; this is
the moment it is made. Two tests.

### Fixed — a use on a pickup no longer takes it off the shore

`InteractionSystem.Activate` called the target's `OnInteracted` after any successful command,
including an aimed use the target refused ("that does nothing here" is a success as a command).
Using the multitool on the rope lying on the sand would have hidden the rope as if taken. Now the
target's own verb completing is the only thing that calls it; a target that changes under a use
does so inside `Use`. No EditMode test: interactables are MonoBehaviours and the suite builds no
GameObjects; the editor checklist covers it.

### Added — the dry-fire problem (ADR-0026)

The design's first real puzzle (§5:00 → §8:00): spark, tinder, shelter, and she is missing all
three. **The wrack** gives dry driftwood above the tide mark and wet below it, dry grass from a
crevice, sun-rotted poly rope, the orange fibreglass panel with its half-abraded stencil, and kelp
that is for nothing. **The rocks:** twelve basalt cobbles and three chert nodules, all looking
like rocks; the multitool on basalt knocks, on chert rings and chips, and only a chert that has
rung offers TAKE. Within three metres of the nearest chert, once, *"Basalt. Basalt. That's not
basalt."* **Three fire sites:** the lee of the near hull, where the blown sand lies still, and two
patches of open sand. Hold an item and use it on a site: grass or fibre is laid, wood stacked, the
panel wedged upright as a windbreak; the chert on the multitool's spine throws sparks. Sparks on
sand cost nothing. Grass flares and is gone. Fibre with no wood holds an ember eight seconds and
starves, and keeps the fibre. Fibre and wood in the open light, stream, and blow out; in the lee,
or behind the panel, they hold. Wet wood by a burning fire dries in ninety seconds and comes back
to the bag dry. FIRE is the notebook's entry and a solved mechanism in the record.

`Core.Fire.FireSiteState` is the whole table of outcomes, engine-free and pinned by tests;
`Game.Fire.FireService` (sixth save participant) adds the run's facts — sparks ever thrown, blow-
outs, the warm zone's clock — and announces each change. `FireSite`, `RockNode` and
`ProximityRemark` are the world half; `ZoneBuilder.BuildWrack` and `BuildFireSites` place it. The
rope teased with the multitool is a `Combinations` recipe, and a **tool now survives combining**
(`ItemIds.IsTool`). **Pickups no longer grow back:** `InventoryService.HasEverTaken` is saved with
the inventory, and a consumed spindle or rope stays gone across zone entries.

**The fire's hints** (§7:10 FAILURE): no spark for 2:30 after the first thing laid, *"Steel
spine…"*; 6:00, she picks up the chert herself. Sparks but nothing catching, 2:30 *"Grass is too
quick"*; 6:00 she tears the rope apart and the fibre is in the bag. Three blow-outs, *"It's the
wind"*; seven, she carries the kit into the lee and sets it down (`CarryFireKitCommand`). She never
does the last step: the strike is always the player's. Tier 2 of each (staging, animation) is not
built. Fuel does not burn down: fuel life is the camp system's, in world hours (CONFLICT-6), and
nothing here waits on it. Tests: `FireTests` (17), five fire cases in `HintTests`, three in
`InventoryTests`; the record pins fifteen ids. 400 written, none executed.

### Added — the beachcomber's Ribcage, and the hook

The design's reward for the player who looks (§28:20 FAILURE, item 30): seven optional
inspectables, Slate entries only, nothing locked behind any of them. A boot print in dried mud
above the tide line, going inland; a ringed cormorant on the wrack; and in and around the trawler
hull, chalked tide marks on the plate, oxy-cut slag under the doorway, a broom's arc in the sand,
and the folded canvas that was over the set. Any of the last four puts SOMEONE on the PEOPLE tab
before the boots do; a print and a dead bird do not. And beside the gully mouth, at chest height,
**the cut vine** — one clean stroke, the face pale and wet — whose entry is the notebook's last
and whose question, WHO CUT THE VINE, is the fourth the player leaves the prologue with (§2).
`ZoneBuilder.CreateInspectable` shapes each (a pad, a stem, a chalk slab) so they can be found by
eye. The record now has fourteen recordable ids; `RecordedTests` had pinned six, a count that was
already stale, and now pins fourteen.

### Added — the other fuse: the mic cord

The design's second valid fuse fix (§4.2). Once the contacts are clean, the multitool offered
again strips a loop of the hand-mic's curly cord bare and bends it across the holder.
`RadioRepair.FaultFor(item)` asks the set which fault an item would address now, so the same tool
fits twice; `RadioSet` uses it instead of the static table. `FuseFromCord` is a third repair flag
(bit 32; older saves read as the spring), the Slate's THE SET WORKS body says which conductor and
which cells (four bodies), and the mic still keys. Tests: three repair cases, three Slate cases.
375 written, none executed.

### Added — hint escalation: the timers (ADR-0025)

The design's "never hard-block, never nag" rule, as a system. `Core.Hints.HintLadder` is the
arithmetic: rungs in ascending seconds, each said once in order, the timer reset to zero by any
meaningful action, one rung per call so a long stall never says three lines at once. Engine-free
and pinned by tests. `Game.Hints.HintDirector` counts the session's play seconds between ticks
(a pause or a menu counts nothing) and says each rung through the dispatcher.

Two ladders exist. The hull line's fallback: never aligned, at 6:00 in the Ribcage Nadia says
*"That trawler's stem is dead on the schooner's. I keep looking at it."* and the Slate entry
writes itself — an `InspectCommand` said as a remark (`SaidAs`), which only a remark id may be.
The radio's ladder (§3.5): from power-up, tier 1 at 3:00 (*"Whoever this was, they wrote down the
ones that worked."*) and tier 3 at 10:00 (she reads the list out, all of it, and stops on the last
line without tuning it — two lines, said as a sequence). Reset by a coarse drag (needle travel
past 100 kHz), reading the taped list, taking a brass tag, or locking onto either false positive;
stopped by the voice. Entering the world — new run or load — forgets everything, which is the
design's "timers reset to zero on load"; so the director holds nothing worth saving and is not a
participant, the one deliberate exception to ADR-0011, recorded as ADR-0025.

**Tier 4, the safety net (T+16:00):** Nadia says *"I'm going to leave it on and sweep it,"* the
dial opens if it is down, and the needle crawls across the band by itself at 50 kHz a second,
toward the signal's side, locking onto each station on the way exactly as a thumb would — the
hull is heard, then, about ninety seconds in, her. `RadioService.BeginSweep/Sweep/IsSweeping`;
`BeginSweepCommand`, `SweepRadioCommand` (dispatched by the director every tick while sweeping,
so a lock found by the set alone is narrated by the same code as a hand-found one). Any hand on
the dial ends it: the panel raises `Touched` on pointer-down and the controller turns it into a
tune to the shown frequency. The sweep keeps its raw position apart from the snapped needle, or
the hull's lock window would have pulled it back every tick and it would never have left. Not
saved: a run resumes with the set put down.

**Not built:** tier 2 (the set turns itself over in inspect view; no inspect view exists). It is
absent, not faked with words. Tests: `HintTests` (19 cases), eight sweep cases in `RadioTests`.
370 written, none executed.

### Added — the partial line: "Three of them. Try the far end."

The design's reward for aligning the hulls from the wrong one (§2:40 FAILURE). Stand at the bow
of the second, third or fourth hull and look the same way: a shorter chalk snaps in through that
hull and the two beyond it, and Nadia says the line. `Sightline.ConfigureAsHint` reuses the stance
and look; the words go through a new `RemarkCommand` (ADR-0024), which validates like an
inspection and records nothing, so the hint can never credit the marker. Said once per zone
visit and never once SIX HULLS, ONE LINE is in the notebook. `ContentKind.Remark` and
`ContentIds.RemarkHullLinePartial`; the remark handler refuses any id that is not a remark.
Tests: three remark cases in `InteractionTests`, the kind pinned in `SlateTests`. 344 written,
none executed.

### Fixed — the held item is game state: one rule for every input

A review of the last three commits found the aimed use lived in the HUD: the tray remembered
which chip was selected, and `HudController` built the `UseItemCommand` itself and dispatched it
straight past the input router's gate. A keyboard press, the prompt button and a touch tap
therefore meant three different things. Now the selection is a command (`HoldItemCommand`,
`InventoryService.Held`); `InteractionSystem.Activate` applies it for every input path — held
item present → use it on the target, otherwise the target's own verb — and releases the hold
after the attempt. The HUD only reports taps; `HudController.Decide` turns a tap into hold,
release or combine and dispatches that. `InteractionTargetChangedSignal` carries the held id so
the prompt can read **USE <item>** without a second source of truth.

- **Tray taps are commands.** `InventoryPanel` no longer decides what a tap means; it raises
  `ItemTapped` and is told what is held via `SetHeldItem`. The combine decision moved out of the
  UI assembly, per ADR-0002.
- **A pending autosave no longer crosses a run boundary.** `AutosaveDirector` clears its pending
  request whenever the state leaves `InGame`, so a beat requested in the last tick of one run
  cannot be flushed into the next.
- **Passive refusals are latched.** A sightline whose observation the dispatcher refuses no
  longer re-dispatches every tick and floods the log; `Interactable.OnObservationRefused` lets it
  latch until the player is unaligned again.
- **`ContentKind` replaces string sniffing in the Slate.** `ContentIds.KindOf` is one table for
  what an id is (discovery, marker, mechanism, radio); `Slate.IsRecorded` reads it instead of
  prefix checks. A mechanism is recorded only when solved.
- **The near hull is nearest the spawn, bow toward the stand point.** `BuildHullLine` placed the
  first hull with its stern to the player; the yaw and the stand point are now derived from the
  same axis.
- Comments corrected where they described the pre-review behaviour.

Tests: three holding cases in `InventoryTests`, one `ContentKind` case in `SlateTests`;
`InventoryPanelTests` rewritten around `HudController.Decide` and the reporter panel.
Repository total is now 340 written (310 EditMode, 30 PlayMode), none executed.

### Added — six hulls, one line: the sightline

The design's first discovery (§2:40), and the game's foundational observation verb. Six wrecked
hulls along the Ribcage, half-buried at six angles in three materials, on one exact straight line.
Stand a few metres off the near bow and look down the beach: when the heading is within the
design's ±4° of the axis a thin chalk stroke snaps in through all six and holds while the angle
holds. No prompt, no press. The first time it holds, the observation is recorded — through the
same inspect command a standing stone uses — and SIX HULLS, ONE LINE becomes the notebook's first
entry and its first open question.

`Interactable` gains a passive kind: never a prompt target, observed every tick with the rig's
position and heading (`InteractionSystem.Tick` now takes the yaw). `SightlineMath` in Core holds
the compass arithmetic so the rule is pinned by tests rather than by a quaternion. Not built: the
"three of them, try the far end" partial line from the wrong hull.

### Added — the aimed use: a selected item, then the prompt (O-10)

With a chip selected in the tray, tapping the prompt uses that item on the thing in front of the
player. The prompt reads **USE <item>** from the moment the chip is picked, so what will happen is
on screen before it does. `InteractionTargetChangedSignal` now carries the target's content id —
an opaque token to the HUD, read by the handler — and `HudController` turns the pair into a
`UseItemCommand`. This is how the recorder's cells go into the radio: by the player's choice, never
by a lookup that fits whatever they happen to carry. The tray's hint says so.

### Fixed — from a review of the last three commits

- **The "arriving in a zone" autosave could never fire**: `ZoneChangedSignal` is published while
  the machine is still in Loading, so the `InGame` guard refused every one. The beat is now the
  transition Loading → InGame. Every autosave is also **deferred to the next tick**: the beats are
  raised from inside command handlers (a solve is announced before the part is consumed), and a
  capture at that instant would restore a fixed machine with its part still in the pockets.
- `RecordKeeper` is built before `AutosaveDirector`, so the percent a header carries is not one
  step stale in exactly the saves that carry the change. One `SaveHeaders.Build` for both paths.
- **A mechanism counted as recorded when looked at.** Examining the seized sluice empty-handed is an
  inspect, and both the notebook and the percent took it as done. Mechanisms count when solved.
- The notebook is not rebuilt on every needle move; the installer disposes the slate controller;
  the dead idle-line parameter is gone from `Mechanism`; the Input System claim behind the touch
  verb carries its VERIFY and a docs link.

### Added — the Field Slate: OBSERVED, PEOPLE, UNRESOLVED

Nadia's notebook, from `design/04-first-30-minutes.md` §3:30. Full screen, paper-coloured, a ruled
margin, three tabs down the fore-edge, and a number on UNRESOLVED that is the entire quest system.
One tap on the page closes it. The HUD carries a Slate tab with the same number.

**Derived, never stored.** The same rule as objectives (ADR-0015): `Slate.Build` computes the three
tabs from progression and a handful of radio facts, `SlateDirector` announces the result on every
change, and the controller translates keys into lines. An entry appears the moment its fact is
true and can never be lost, because the facts are in the save. "The record is never lost in this
game" is a property of a function, not a promise.

What it writes on this slice: the standing stone, the tag (which shows SOMEONE and asks WHO
RE-WICKS A LANTERN), the cut wall, the reel, the sluice, the deck (which asks WHO KEEPS THE MAINS
ALIVE — this slice's own question), the radio found and working (with T.R. from the battery door),
the two false positives, and the transmission — as an entry, or as TRANSCRIPT — NO AUDIO when the
recorder's cells went into the set: the permanent scar the design asks for. The voice redraws
SOMEONE into THE VOICE and asks WHAT ARE THE GATES and WHY WON'T SHE ANSWER.

Deviations, named: SIX HULLS, ONE LINE is not derived — the hull-sightline beat it comes from is
not built; the Slate tab sits with the other top-right controls rather than on the bottom edge,
because that edge is where both thumbs live on this layout; the handwriting, wet-ink reveal,
sketches and the crew-list page are art tasks and are not faked. Recorded as ADR-0023.

Both gates pass; six new EditMode cases. Not compiled.

### Fixed — NEW GAME after three runs, and a recorded figure that was always 0%

**O-11.** With every manual slot full the menu refused NEW GAME forever; there was no delete path.
Now the first tap names what will happen and arms it, and a second tap within eight seconds
overwrites the oldest readable run. A corrupt slot is never chosen: it may be the one save a
player could still recover. No modal widget exists in this UI yet; a two-tap confirm with a plain
sentence is a smaller thing to get right.

**O-12.** `RecordedPercent` was rendered on the pause summary and in every save header and
computed by nothing. `Recorded.Percent` is a fraction of the recordable content ids (markers,
discoveries, mechanisms — six in this slice), rounded; `RecordKeeper` writes it into the session
whenever progress changes.

Both gates pass; four new EditMode cases. Not compiled.

### Fixed — an end-to-end read of every player chain, and what it found

Six chains traced hop by hop — boot → menu → new game; the Ribcage progression; the item chain;
the radio; save → pause → quit; every composed localization key. Findings, all applied:

- **`HudScreen.SetInventory` had been deleted** by a rewrite of the narration sequence while the
  controller kept calling it: a compile error in the UI assembly that neither gate could see.
  Restored, and the validator gained a fourteenth check, **`MEMBER`**: a member reached through a
  field typed as a project class must be declared on that class or its bases (CS1061). Proven
  against the bug: with the method deleted it reports it; restored, it is silent.
- **The waterlogged reel was a discovery and never an item**, so the combination it starts, the
  rebound reel and the tape deck were unreachable. `DiscoveryItems` names the discoveries that are
  also objects; `CollectHandler` records the beat and puts the reel in hand.
- **A solved mechanism re-seized on every zone entry** and could be solved again with the same key.
  `WorldProgress` records solved mechanisms; the save carries them; `Mechanism` restores its pose.
- **A mechanism examined empty-handed showed `#narration.mechanism.…#`**: the idle rows were keyed
  `.idle` and the inspect handler composes the plain key. Rows renamed.
- **The prompt vanished on pause and never came back** while the target was unchanged — and a
  press still fired it. `InteractionSystem` now follows the mode: suppressed outside `InGame`,
  re-offering on return.
- **Every screen tap was also the world verb** on a phone: the Input System read
  `<Touchscreen>/primaryTouch/tap` from the device with no idea what was under the finger, so a
  tap on Pause or a chip could take the brass tag. The binding is gone; the prompt card is a real
  48 dp button that reaches the router through the same gate a key press does.
- **The autosave ring had no writer** since Phase 1: CONTINUE read a slot nothing filled.
  `AutosaveDirector` writes it on the unlosable beats — entering a zone, hearing the voice, making
  a machine work, unlocking a zone — and only while a run is in the world.
- Narration is cleared when a run ends rather than paused and resumed on the next one; the
  dead torch has its line; a discovery that is not an object grants nothing.

Left open and recorded (`DECISIONS.md` §Open items): after three saved runs NEW GAME is refused
and there is no delete path in the UI (O-11); `RecordedPercent` is never computed (O-12).

**IMPLEMENTED BUT NOT RUNTIME VERIFIED.** Both gates pass; six new EditMode cases. Nothing compiled.

### Fixed — pickups, mechanisms and the radio were unreachable from the world

`InteractionSystem.Dispatch` routed exactly three command kinds — inspect, collect, travel — and
logged everything else as unroutable. Every `ItemPickup`, every `Mechanism` and the radio built a
correct command from the prompt and were silently refused by it. This has been true since Phase 3
landed; nothing in the item chain was ever reachable by a player. Found by an adversarial review of
the radio slice. Take, use and open-radio are now routed, and the unroutable warning names the type.

Also from that review, all applied:

- **The clue was unreachable.** The frequency list was handed out only from a working set, which
  cannot be examined any more. It is found on the second look at the broken set, as the design has
  it at 15:00, and remembered in the save.
- **Story beats belong to the game.** The HUD had been assembling the transmission from key names.
  A `NarrationSequenceSignal` of keys now travels from the handler; the HUD translates and paces
  it, each line for its own reading time, nothing blank between lines. The power-up pair (the fix
  line, then the click and the hiss) goes the same way whichever fault went last, and the use
  handler no longer says "that does nothing" over a target that has already spoken.
- The first hearing of the voice is read for its own duration before the transmission starts; the
  narration and prompt cards climb above the tuning band while it is open, so the transmission is
  not drawn underneath the dial; narration timers pause with the game.
- The copper spring stays in the fuse holder; the dead torch does not grow back on the nail once
  taken apart; the recorder's cells are no longer auto-fitted from the prompt (O-10).
- The hull's walls and crate are solid; every other primitive collider in the zone is disabled
  before its deferred destroy, so a continue saved inside a mechanism's footprint is not lifted
  onto its roof by a probe that hit a collider that would not exist a frame later.
- `VerifyPlayable`'s waterline test applies to the procedural island only; the ground probe says
  so when it finds nothing; the diagnostics probe starts above the world; two unverified "needs a
  physics tick" claims are marked as such; `IsChildOf(self)` carries a VERIFY.
- Station caption only on a lock; below that, "Something in there". Flywheel velocity normalised to
  time; pointer capture released when the band hides; fine zone needs a laid-out strip; chip
  outline stays 1 dp when deselected; `Reception` travels typed.

### Changed — the sea follows the camera

The sea was a 240 m disc centred on the island's origin, so from the shore — 60–80 m out — its near
rim was only 160 m away and the fog had not hidden it. The water shader now shifts every vertex by
the camera's XZ, re-centring the disc on the viewer each frame: the rim is always the full radius
out and the dense inner rings are always underfoot. The wave function is evaluated at world
position, so the surface does not slide, only the sampling grid does. Ring spacing went back to
dense-at-centre (mostly quadratic), and the mesh's culling bounds are grown by the island's width
so the sea is never culled from a viewpoint the geometry is actually under (`VERIFY`).

### Added — the radio (Phase 6 slice): one set, three faults, one frequency list, one transmission

The prologue's spine, built to `design/04-first-30-minutes.md` §15:00–§27:00 and §3.

**In the world.** A hole cut in a trawler's flank on the rise toward the ridge — a shell of slabs
with the open side facing the shore, a swept floor (the island's own sand), a crate for a table,
the set on it, and eleven brass tags on eleven nails with a dead torch hanging among them.

**Three things wrong with it, and you have three things.** Power (the torch's cells, or the field
recorder's own — the prologue's one real decision, never flagged as one, and remembered), contacts
(the multitool), fuse (the torch's copper spring, which the player is holding the moment they take
the torch apart for its cells). Diagnosis is by inspection, one fault at a time, in any order; no
minigame. Closing the battery door reveals the inscription. `RadioRepair` in Core owns the table
of what fixes what, for the same reason `Mechanism` does: it is the set that knows what fits it.

**The dial.** A full-width band in the bottom third: the strip drags with inertia and friction at
the design's coarse rate (180 kHz per screen-width), the right fifth is the fine knob at ten times
the resolution with no mode switch, and above it the spectrogram ribbon — phosphor green on black,
grass for noise, a vertical line for a carrier. The ribbon draws from `RadioTuner.Spectrum`, the
same Core arithmetic the lock rule uses, so what the player sees and what the game grants cannot
disagree. Tolerance is the design's ladder: ±0.8 kHz lock (with a magnetic detent so the lock
cannot be lost by breathing on the screen), ±4 detuned, ±12 smudge, grass beyond.

**On the band.** Two honest false positives — the hull's own pressure cycle at 8.291 (the game's
thesis, hidden in a red herring) and a Portuguese weather bulletin at 12.510 for a sea area nine
hundred kilometres away — and the signal at **5.240**: the one line on the list written without a
unit, in a list whose every other line is in kHz. A reading puzzle, not a trivia puzzle. First
lock on the voice plays the forty-four-second transmission as a sequence of narration lines,
then Nadia working it out; it is recorded on first hearing and never lost.

**The mic.** Press to talk. Five different things to say, each less formal; hiss every time. The
world lets the player do the thing they want and still does not give them what they want.

Architecture: `RadioService` is the **fifth `ISaveParticipant`**, registered in this change
(ADR-0011). `RadioSet` is its presence in the world and holds no state. Four commands
(`OpenRadio`, `CloseRadio`, `TuneRadio`, `SqueezeMic`) and one signal carrying the full tuning
state. `HudController` pauses the dial through a command like any other change.

**Fixed on the way — a new game inherited the previous run's pockets.** `StartNewGameHandler`
reset progression and nothing else, so a second run started with the first run's items and, now,
its repaired radio. Every participant resets; the starting kit (multitool, field recorder) is
handed out there.

Deviations from the design, all UNCONFIRMED and named: the phone's real microphone is not opened
(no permission flow yet); the hint escalation timers (§3.5) are not built; the Field Slate is not
built, so the transmission is narration rather than an entry; the frequencies are the design's own
and flagged there for clearance.

**IMPLEMENTED BUT NOT RUNTIME VERIFIED.** Both CI gates pass. Twenty-three EditMode cases cover
the tolerance ladder, the snap, the spectrum, the repair table, the save round trip, the
new-game reset and the drag arithmetic. Nothing has been compiled or played.

### Fixed — the island was built 440–730 m under the sea

The first run after the graphics pass showed nothing but sky. The player was not falling and the
world was not missing: **the player was standing on the island, and the island was half a
kilometre below the water plane and the sky dome.** Both screenshots read `xyz 0.0 <y> 0.0` — the
spawn axis — with `y` at the spawn height of that sunken world (−496.9 in one; the other, read as
+36.0 through very small text, was almost certainly −436.7, the other branch of the same formula).

The cause is one line in `ZoneMeshes.HeightAt`. The new coastline called `Mathf.SmoothStep(a, b, t)`
as if it were GLSL's `smoothstep(edge0, edge1, x)`. It is not: Unity's is an **interpolator** —
`t` in 0..1, result *between* `a` and `b` ([docs](https://docs.unity3d.com/ScriptReference/Mathf.SmoothStep.html)).
Handed two distances as the edges and a third distance as `t`, it clamped `t` and returned a value
between 47 and 79, so the island factor was about −47 to −78 everywhere and the terrain, the spawn
computed from it, and every rock and plant (all discarded by the new below-waterline guards) went
with it. The apron and the ridge use `SmoothStep(0f, 1f, t)`, where the two functions coincide,
which is why those worked. `Smooth01` is now a real edge-based smoothstep.

**A correction to yesterday's diagnosis.** The previous entry attributed the screenshots to a
poisoned save restoring a falling pose. That was wrong: a new game would have spawned in the same
place. The two changes made under that diagnosis remain — `LiftAboveGround` now probes from above
the world rather than around the eye, and `PlayerRig` catches a genuine fall through the terrain —
because both defects were real; they were not the cause.

Also found by the same reading, and fixed:

- **The ground probe was hitting the rig itself.** The camera pivot sits at rig + 0.6 and the top
  of the rig's own capsule at rig + 1.0, so the unfiltered downward ray in `LiftAboveGround` (and
  the identical one in `ZoneDiagnostics`) entered the player before the terrain. Every zone entry
  it "found" the player, lifted the rig 2.1 m and logged that the camera had been under the
  terrain — naming the capsule as the terrain. The one measurement this project makes of where the
  ground is had never measured the ground. Both probes now skip the rig. `CreateRig` also disables
  the primitive's collider before destroying it, since `Destroy` is deferred to end of frame and
  the doomed collider was live through the whole of `Furnish`.
- `VerifyPlayable` gained the one check that is about the world rather than the rig: the ground's
  colliders must straddle the waterline. Every existing check passed while the island was sunk.
- `HighestColliderTop` seeded its maximum at 0 and so reported 0 for any world below it.
- `HasColliderBelow` reads bounds after `Physics.SyncTransforms()`; `RepairLighting` raises the
  Trilight terms it actually shades with rather than the Flat-mode field.
- The ridge centre was 60.9 m out against a coast that starts falling at 47.6 m; pulled inside.
- Shaders: the foliage lighting dropped a Unity-4-era `× 2`; the broadleaf profile keeps a stem
  (it pinched to zero width at the base, so every fern hung detached); `addshadow` removed from a
  shader whose renderers never cast; the sky guards `pow(0, y)`; all three hash functions replaced
  with the precision-safe "hash without sine" family.
- Fog figures in comments were for the old density and are corrected; `CLAUDE.md` now says
  Built-in, which is what the project runs (CONFLICT-7 — shipped code wins).

**IMPLEMENTED BUT NOT RUNTIME VERIFIED.** Both CI gates pass. The height field arithmetic above is
reproduced in the commit message. Nothing has been compiled or played.

### Added — the inventory tray, and the first thing the player can do with two items

Carrying items was implemented with no way to see or use them: the player could pick up a spindle
and a reel and had no route to putting them together. The tray closes that.

Chips in a strip below the pause button, opened by a tab. **At the top of the screen, not the
bottom** — both of this game's thumbs live along the bottom edge (movement stick left, look pad
right), so a tray down there is an opaque sheet over the controls the player is holding.

Combining is **tap, then tap**: the first tap selects, a second tap on a different chip requests
that combination, a second tap on the same chip cancels. No drag (needs two points of contact with
a moving world behind it, and has no cancel), no long press (no affordance, and no cancel either).
Given that combinations consume their inputs, having a way out of a half-made choice is not a
nicety — `InventoryPanelTests` asserts the cancel by name, along with what happens when the
inventory changes underneath a pending selection.

Architecture: `InventoryChangedSignal` now carries a snapshot of the whole inventory rather than
just the item that moved. That is a requirement, not a convenience — the panel is UI code, ADR-0002
forbids it from touching a service, so "re-read the inventory" is not available to it. Either the
list travels in the signal or the guarantee is a comment. The panel renders strings it is handed and
reports taps by id; `HudController` is what turns a reported pair into a `CombineItemsCommand`.

`PrimeInventory` mirrors `PrimeObjective` for the same reason: a continued run restores its
inventory during load, long before the HUD exists, so without a pull at build time the player comes
back from a save with an empty tray and no way to reach the items a puzzle needs.

Recorded as ADR-0019 (a failed combination costs nothing), ADR-0020 (the tray and the gesture) and
ADR-0021 (this project's own shaders, kept under `Resources`).

### Added — the island looks like an island: sky, sea, shore and five shaders

The world was geometrically correct and visually a greybox. Four things were missing, and each one
was load-bearing rather than decorative.

**There was no sky.** The camera cleared to the fog colour, so there was no horizon — and a world
without a horizon is a diorama, whatever is standing in it. `Vardholm/Sky` draws a zenith-to-horizon
gradient, a cloud deck projected through the dome so it foreshortens toward the horizon the way a
real one does, and a sun disc with a glow around it. The disc direction is written from the
directional light's own transform, so the scene is never lit from one direction and glared at from
another.

**There was no sea, so it was not an island.** The ground was a square of noise ending at a hard
edge with nothing past it — the player was standing on a tile. The height field now multiplies the
land by a noise-perturbed radial falloff, so there is a coastline with bays and headlands, the land
drops to a seabed outside it, and `Vardholm/Water` lays a radial sheet at the waterline: three
crossing sine waves displacing the vertices, the surface normal derived analytically from the same
expression (so highlights track the swell instead of sliding over it), ripples, Fresnel and crest
foam. The rings are spaced by a cubed parameter — metre-scale quads underfoot, one huge ring at the
horizon. No `Update()` is involved anywhere: the animation is `_Time` in the vertex shader.

**`Standard` throws vertex colours away.** `ZoneMeshes` has always written a height gradient into
every ground vertex, and the stock shader has always ignored it — the terrain was one flat tint and
the entire gradient was computed and discarded. `Vardholm/Terrain` uses it, and adds the three cues
that make ground read as ground: rock breaking through where the slope is too steep to hold soil,
sand where the land meets the water, darkening to wet sand at the tideline, and two octaves of
world-space noise so no two square metres are the same shade. The noise is also mixed into the slope
term, which breaks the clean contour ring that a pure slope threshold draws across a hillside — the
single most recognisable tell of procedural terrain.

**Every rock was the same grey and nothing moved.** `Vardholm/Prop` mottles by world position, so
two rocks side by side differ for free and upward faces weather; rocks are now placed in clusters
rather than sprinkled evenly, scaled non-uniformly, and bedded into the ground rather than resting
on it. `Vardholm/Foliage` cuts blade and frond shapes out of the quads procedurally and moves them
in the wind, with the phase taken from world position so a field does not sway in unison. Its
lighting model is wrap rather than Lambert, which is both how thin leaves behave and the only way
two-sided quads with one set of normals can be lit from both faces.

Supporting changes: ambient is **Trilight**, not Flat — flat ambient lights the underside of a rock
as brightly as its top, which is the most reliable way to make a lit scene look like a mock-up. The
ground grid went from 33 to 97 vertices a side (5 m quads to 1.8 m; at 5 m every hillside was a
visible staircase and no shader can hide a silhouette that coarse), the island from 120 m to 170 m,
and the camera clears to the skybox when there is one. Fog is thinner, because fog dense enough to
hide a missing horizon is not needed once there is a horizon.

All five shaders live under `Assets/Resources/Shaders`, which is a guarantee they are in the build
rather than a hope: `Shader.Find` resolves anything in the project in the editor and only what the
build included on a device, which is the classic "it worked in Play mode" failure. Every material
falls back to the stock lit shader if its own is missing, so the worst case costs the island its
looks and not its playability.

**IMPLEMENTED BUT NOT RUNTIME VERIFIED.** There is no Unity, no .NET SDK and no Mono in this
environment. No shader here has been compiled, and shader compilation is exactly the kind of thing
the two CI gates cannot check — they read C#. Both gates pass; that is all that has been verified.

### Added — items, combination, and the first maintenance puzzle (Phase 3, first half)

Adventure-game verbs on our own terms: carry a specific object, put two of them together, use one
on a machine somebody stopped servicing. `Inventory` and `Combinations` in Core (engine-free, no
stacks, no weight, no slots); `InventoryService` as the **fourth `ISaveParticipant`**, registered in
the same change per ADR-0011; `TakeItem`/`CombineItems`/`UseItem` commands and handlers;
`ItemPickup` and `Mechanism` interactables.

A failed combination consumes nothing. In a game with no shop and no respawn, eating the inputs on
a wrong guess is a soft lock the player cannot see coming and cannot undo, and it is the single most
common way this genre breaks itself. `InventoryTests` covers that case by name.

A `Mechanism` says three different things depending on what the player is holding: what is wrong
with it (which is the clue), why the wrong part does not fit, and what happens when the right one
does. That is the shape of every puzzle in this game — the mystery is a maintenance problem, so a
puzzle is a machine with a part missing, and the part is somewhere a person would have left it.

Content: the Dry Spindle on the shore, the Waterlogged Reel in the channel, the Sluice Key in its
bracket, the seized sluice, and the tape deck that plays a woman reading pressures aloud, one every
hour, for as long as the tape runs.

### Fixed — the flicker while walking: depth precision and shadow bias

A 24-bit depth buffer's precision is dominated by the **near/far ratio**, and the camera was
`0.05 / 500` — **1:10000**. Surfaces metres apart land on the same depth value and swap places as
the camera moves. That is z-fighting, and it reads exactly as "it flickers strangely when I walk".

Now `0.2 / 260` — **1:1300**, about eight times the precision. Neither number costs anything
visible: a first-person camera has no use for seeing 5 cm from the lens, and the Ribcage's fog
leaves 0.3% visibility at 200 m, so 260 m of far plane is already well past where the world has
faded out entirely.

The other half is shadows. A 120 m ground mesh under one directional light is the case Unity's
shadow defaults handle worst: the terrain shadows itself in moving bands, and cascades spread over
the default 150 m put almost no resolution where the player actually is — a coarse cascade near the
camera is what makes shadow edges crawl. `shadowBias`, `shadowNormalBias` and `shadowNearPlane` are
now set, and `shadowDistance` is 80 m, past which the fog has already hidden everything.

### Changed — `ReplaceAll` now sweeps the whole layer

The loop removes the screens the stack knows about; a second pass removes anything else that
reached the layer by any route. The guarantee worth having is "exactly one screen is on screen",
and it should not depend on the bookkeeping having been perfect — a leftover full-bleed screen is
an opaque sheet over the game, and that failure has cost this project enough already.


### Fixed — the player spawned inside the rib arch

The ribs spanned `z = -16 … +16` with the player appearing at the origin, so the spawn was **inside
the arch**: the nearest rib hung directly over the camera and filled a third of the screen with a
grey slab, and the landmark that exists to be seen from a distance could not be seen at all.

They now start at `z = +6`. The whole arch reads ahead of the spawn — which is what the comment
above that code has always described — with a walk through it toward the gate at `z = +30`.


### Fixed — the UI was painting an opaque sheet over the entire game

`UIService.cs:81`

```csharp
Root.style.backgroundColor = Theme.Background;
```

The UI root is a **full-bleed element containing every screen, drawn in screen-space overlay on top
of whatever the camera rendered**. Filling it with the theme's background colour covered the whole
game, in every state, permanently.

That is why nothing done to the camera, the materials, the colour space, the lighting or the
geometry ever changed the picture: **none of them were the picture.** Every structural check passed
because everything it checked was correct — 58 renderers, a real `Standard` shader, one enabled
camera tagged `MainCamera` looking straight at a correctly lit island. The scene view proved the
world was fine. It was simply covered up, and the one soft circle floating in the dark was the main
menu's own backdrop bloom showing through its own background.

A container is not a screen, and must never assume it is the bottom of the stack. In a 3D game it is
not — the world is. Screens that want a background paint their own; `MainMenuScreen` deliberately
does, and should.

### Fixed — `ReplaceAll` left the outgoing screen in the tree

The same failure by a second route. Leaving gameplay removed the old screen on a **timer**, while
fading it with `experimental.animation` — an API this project's own risk audit lists as HIGH RISK
because it fails silently. Every screen here is full-bleed and opaque, so an outgoing one still in
the tree is a black sheet over whatever replaced it.

Outgoing screens are now removed synchronously. `ReplaceAll` is called when the game has **already**
changed mode, so the old screen has no claim on the display for even one more frame, and a
cross-fade is not worth a mode change that can get stuck.

### Added — the test that would have found this in one line

`TheUiRoot_DoesNotCoverTheGame` asserts the UI root's resolved background alpha is below 0.05. No
other test looked at whether the layers between the camera and the eye let light through, because
every one of them was written to check that things *existed* — and everything did.


### Fixed — CS0136 in my own diagnostic, and the validator now catches that class

```
ZoneDiagnostics.cs(184,21): error CS0136: A local or parameter named 'eye' cannot be
declared in this scope because that name is used in an enclosing local scope
```

The ground-probe block I added declared `var eye` while the renderer loop further down the same
method already had one. **C# forbids this even though the two blocks never overlap** — a name used
anywhere in a method's scope is off limits to every block inside it. Renamed to `probeOrigin`, with
the reason written where the next person will hit it.

**Validator gained a `SHADOW` check** (now 13). Ancestry is tracked by block identity rather than by
depth, because two locals of the same name in *sibling* blocks are perfectly legal and a depth-only
test would report every one of them. Regression-tested by reintroducing the exact line: it names the
file, both line numbers and the rule. Clean across all 106 files.

That is the third class of compile error this session that only Unity caught. Each one is now a
check, which is the only way this environment — no compiler, no runtime — gets less expensive to
work in over time.


### Fixed — runtime geometry was asking for baked lighting data that cannot exist

Three Built-in-pipeline defaults, none of them set anywhere in this project, and all three wrong for
a world that is generated at runtime in a scene that is itself created empty:

- **`Renderer.lightProbeUsage` defaults to `BlendProbes`** and **`reflectionProbeUsage` likewise**.
  Every generated object was asking for probe data that does not exist and, for a procedural world,
  can never exist. Both are now `Off`, so shading depends on exactly the two things this code
  controls — the ambient colour and the directional light — and on nothing that would have to be
  baked.
- **Ambient set at runtime does not reach the shaders until the environment is rebuilt.**
  `DynamicGI.UpdateEnvironment()` is now called after the atmosphere is applied. Without it the zone
  was lit by whatever the scene asset carried, which for a scene the editor script creates empty is
  nothing at all. `ambientIntensity` is also set explicitly rather than inherited.

### Changed — ambient now carries the world on its own

The previous values needed the directional light to work. If a runtime-created light contributes
nothing — and under Built-in with no baked data there are several ways for that to happen — the
world goes black again with every structural check still passing.

Ambient is now strong enough that the zone is readable with **no sun at all**:

| | sun working | sun contributing nothing |
|---|---:|---:|
| Ribcage ground | 0.395 | **0.238** |
| Fernmaw ground | 0.301 | **0.183** |

Both floors are above the 0.18 readability threshold, so the failure mode that has cost this many
rounds cannot recur silently: the worst case is flatter and duller, not invisible.


### Added — `docs/PHASE_3_REPORT.md`

The full account of the invisible-world investigation: both root causes with their arithmetic, the
six hypotheses eliminated by reading rather than by assumption, the nine tests written, and an
explicit statement that acceptance criteria C, D and F cannot be confirmed without the editor.

### Added — the categorised visibility block

The zone dump now sorts every renderer into the one category that explains its fate, checked in the
order the pipeline applies them:

```
WORLD VISIBILITY: renderers 58 · enabled 58 · withTriangles 58 · visibleInFrustum N
  · behindCamera N · outsideClip N · culledByLayer N · zeroBounds N · nonFiniteBounds N
```

The counts are exclusive, so exactly one is non-zero when something is systematically wrong and the
line names it without further reading. Non-finite bounds are checked explicitly, because a mesh
with a NaN vertex produces bounds that fail every frustum test silently.


### Fixed — the Standing Stone was 2.45 m behind the player's head

This is the defect that made the symptom so confusing, and it is exactly the distinction between
*a logical object existing* and *a visible mesh existing*.

The rig spawns at the origin facing **+Z** with no yaw. The marker was placed at `(1.5, ·, -1.5)`:

```
camera        (0.00, 2.80, 0.00)  forward (0, 0, 1)
stone         (1.50, 1.58, -1.50)
camera→stone  (1.50, -1.22, -1.50)   length 2.45 m
dot with forward = -1.50            → BEHIND THE CAMERA
angle = 128°
```

Proximity does not care about facing, so `INSPECT` was up from the first frame while the stone was
never once on screen. The code comment above it read *"the reason to walk into it"* — describing
something the player could not walk toward, because they were never shown it. Now at
`(2.5, ·, 7.5)`: ahead, far enough to be approached rather than auto-prompted, inside the arch's
z range so it reads against the ribs.

### Fixed — a redundant 120 × 1 × 120 ground cube under every zone

`HasColliderBelow` tested `bounds.max.y <= spawn.y` — whether the collider lay **entirely** below
the spawn point. A rolling terrain never does; its peaks rise well above the player. So the check
failed on every zone and `EnsureGround` built a second, flat ground under the real one every time.
Now tested against `bounds.min.y`.

### Fixed — my own test asserted the wrong world size

`WorldGeometryTests` bounded the player to ±30 m because I assumed the ground was 60 m square.
`ZoneBuilder.GroundSize` is **120**. Corrected to ±60 — read, not assumed.

### Added — the per-object dump

The zone dump now reports the ground, the Standing Stone and the landmark individually: position,
scale, `activeInHierarchy`, `enabled`, `isVisible`, vertex and triangle counts, local and world
bounds, material, shader, colour, render queue, distance from the camera, **angle off camera
forward** (naming "BEHIND THE CAMERA" outright), and the frustum test. The angle is the line that
separates the two facts above, so every object carries it.

A PlayMode case asserts the stone is within 75° of forward. It shipped at 128°.


### Fixed — the world rendered perfectly and was shaded to 7.7% grey

**Phase 2.1.** The geometry was never the problem. 58 renderers, a real `Standard` shader, Built-in
pipeline, Linear colour space, camera enabled and pointed at the world, nothing culled — and the
Ribcage's ground resolved to **luminance 0.077**. Seven per cent grey is black on any display.

**The trap is the sRGB→linear conversion.** In Linear colour space Unity converts a material's
colour before shading, so an albedo of `0.13` is `0.014` linear — near coal. With a sun at 24°
elevation (N·L = 0.41) and intensity 0.85:

```
Ribcage ground  screen RGB (0.07, 0.08, 0.07)  luminance 0.077   ← black
Ribcage stone   screen RGB (0.18, 0.18, 0.17)  luminance 0.178   ← the faint gradient on screen
Fernmaw ground  screen RGB (0.04, 0.08, 0.05)  luminance 0.070
```

Those numbers are computed from the shipped values, not estimated. Values that look like a
reasonable dark palette in an inspector arrive a fifth as bright once converted.

**The recipes now carry physical values.** Ribcage ground **0.406**, stone **0.479**; Fernmaw ground
**0.308**, stone **0.381** — still cold, desaturated and bleak, and Fernmaw still darker than the
shore, because that contrast is the zone's character. Darker than the shore, not darker than
visible. The camera also clears to the zone's fog colour instead of a fixed near-black, so distance
fades into sky rather than into a hole.

**The earlier Gamma→Linear change was right for the wrong reason.** It is correct on its own merits
and stays, but it did not make the world brighter — computed properly it moved the ground from
0.066 to 0.077. The arithmetic I used then omitted the conversion of the albedo itself.

### Added — the one number that settles this class of failure

`ZoneDiagnostics.EstimateGroundLuminance` computes what the ground's lit colour will look like on
screen, and the dump reports it with the light's intensity and elevation. Every check written
before it asked whether an object *existed*, and the object always did; a surface that renders
correctly and resolves to 7% grey is indistinguishable, on screen and in every structural test,
from no surface at all. The verdict line now says outright when a zone is shaded to black.

Two PlayMode cases cover it: the ground's luminance must exceed 0.18 **and** stay below 0.75 — a
test that only pushes one way invites the fix of turning everything white — and the camera must be
within 37° of level and above the ground mesh.


### Fixed — my own regression: `SetActiveScene` on the DontDestroyOnLoad scene

```
ArgumentException: SceneManager.SetActiveScene failed;
the internal DontDestroyOnLoad scene cannot be set active.
```

`RestoreBootAsActiveScene` read `gameObject.scene` to find the boot scene. That host is on
`DontDestroyOnLoad`, so its scene **is** the DontDestroyOnLoad pseudo-scene. The comment above the
code said exactly that, and then the guard tested `IsValid()` and `isLoaded` — both of which are
**true** for that pseudo-scene. The guard passed and the call threw.

The boot scene is now addressed by name, and every candidate goes through one check:
`buildIndex >= 0`. That is the property that actually separates a real scene from an
engine-internal one; Unity rejects the pseudo-scene precisely because it created it rather than
loading it. A fallback scans for any loaded non-zone scene if Bootstrap is somehow gone.

Writing the correct reason in a comment and then not testing for it is a specific kind of mistake,
and the fix is to test the thing the comment names.


### Added — `ZoneDiagnostics`, because four diagnoses from screenshots produced three wrong answers

A zone that renders nothing and a zone that was never built look identical on screen, and the
furnish report could not tell them apart: it said "player yes · camera yes" and stopped, which was
true the entire time the world was invisible. `ZoneDiagnostics.Dump` reports the facts that actually
discriminate — renderer count, how many are enabled, how many are on a layer the camera's mask
includes, how many have a **null shader**, how many are inside the frustum, the nearest one's
distance, plus the camera's position, facing, clip planes, clear flags and mask, and the pipeline
and colour space. It ends with a verdict naming the first link in the chain that is broken, and
logs as an **error** when nothing can reach the screen.

### Changed — the loaded zone is now the active scene

`active scene 'Bootstrap'` while `ZoneRibcage` was loaded was a real finding. Two things follow the
active scene and both belong to the zone:

- A GameObject created without a scene specified lands in the active scene, so anything the zone
  made for itself accumulated in Bootstrap and outlived the zone — Bootstrap is never unloaded.
- **`RenderSettings` are per scene**, and the active scene's are the ones used.
  `ZoneBuilder.ApplyAtmosphere` writes the zone's fog and ambient through that API, so it was
  writing the Ribcage's atmosphere into the boot scene and leaving it there afterwards.

The zone becomes active before furnishing; the boot scene is restored when gameplay ends.

### Fixed — the player's own capsule was the one thing always in front of the camera

The camera pivot sits at eye height *inside* the body capsule, and its `MeshRenderer` was never
disabled. A first-person rig shows the world, not the inside of its own collision shape.

### Added — `WorldGeometryTests` (6 PlayMode cases)

Every existing test passed while the world was invisible, because they asserted that a player, a
controller and a camera existed — and all three did. These assert the chain that ends in a pixel:
a mesh with vertices, an enabled renderer, a layer the camera can see, a material with a real
shader, bounds inside the frustum, the player inside the built area, and the zone owning the active
scene. One of them exists specifically to separate "the Standing Stone registered itself with the
interaction system" from "the Standing Stone has a mesh" — the prompt reading correctly proves only
the first.


### Fixed — `??` on a `UnityEngine.Object`, which is why the world may render as nothing

`ZoneBuilder.CreateMaterial` resolved its shader with

```csharp
Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? ...
```

**That is a bug, not a style opinion.** `Shader` is a `UnityEngine.Object`, and Unity overloads `==`
so a destroyed or unloadable object compares equal to null while still being a live C# reference.
The null-coalescing operator does **not** use that overload — it tests the raw reference — so the
chain can hand back an object that every other line of code agrees is null. A material built on it
renders nothing at all, with no error and no magenta, which looks exactly like an empty world.

It now asks `GraphicsSettings.currentRenderPipeline` which family to look in — this project has no
URP and runs on Built-in (CONFLICT-7), so searching for a URP shader first was looking for
something that cannot exist here, and a URP shader under Built-in renders black regardless — then
resolves with explicit `!= null` checks, the comparison that knows about Unity's object lifetime.

It logs the pipeline, the colour space and the shader it settled on, once.

### Changed — the furnish diagnostic put its decisive numbers where the console cut them off

Unity's console list shows only a message's first line, and `renderers` and `colour space` were at
the end of a long single line. Those are the two numbers that separate "the zone is empty" from
"the zone is there and invisible", and they were the part that got truncated. They lead now, with
the camera's position, facing, clear flags and culling mask on a third line.


### Fixed — the island rendered black: the project was in Gamma colour space

The zone was never broken. It loaded, built its terrain and its interactables, registered them,
raised the prompt and played the narration — all of which the HUD was showing correctly. It was
being **displayed in the wrong colour space**, and in Gamma the shading result reaches the screen
unchanged:

```
Ribcage shore, albedo 0.13, sun 0.85 at 24° elevation
  direct  = 0.13 × 0.85 × cos(66°) = 0.045
  ambient = 0.13 × 0.16            = 0.021
  total   =                          0.066   →  6.6% grey
```

The same numbers in Linear come out at `0.066^(1/2.2)` = **0.29** — dim, cold and readable, which
is the shore that was designed. Unity defaults new projects to Gamma, and nothing in the project
had ever set it.

`Vardholm → Setup Project` now sets Linear, and the first-run check triggers on a wrong colour
space as well as on missing scenes — a clone whose scenes already exist has everything it needs
*except* this, and the symptom is a black screen with no error anywhere.

Linear is right on its own merits, not as a workaround: it is the default for new 3D projects, it
is what every value in `ZoneBuilder`'s recipes assumes, and it is supported on every device this
game targets.

**Also recorded: the project has no URP.** `Packages/manifest.json` contains no
`com.unity.render-pipelines.universal` and `GraphicsSettings` has no pipeline asset, so the game
runs on the Built-in pipeline. Every document in `docs/` says URP. That is **CONFLICT-7**, and it is
why `ZoneBuilder.CreateMaterial`'s shader chain silently lands on `Standard`.

### Fixed — two diagnostics that were lying

- The panel-size report printed `NaN` because one scheduled tick is not enough for layout to have
  run. It now waits for `GeometryChangedEvent` — the only reliable signal; a longer delay would
  just be a longer guess.
- The furnish report now also prints the renderer count and the active colour space. Those two
  numbers separate "the zone is empty" from "the zone is there and too dark to see", which is
  precisely the distinction a black screen hides.


### Fixed — the two console entries left after the menu came right

The menu now lays out correctly (see the panel-height entry below). Two things remained.

- **`NullReferenceException` in `HudScreen.SetPrompt`.** Screens build lazily on first show, and the
  HUD is told to hide its controls the moment the app reaches the main menu — before it has ever
  been shown, so before `Build` has run and while every field is still null. Asking a screen that
  does not exist yet to hide something is a reasonable thing for a caller to do; throwing at it is
  not. All four public setters now return early until `IsBuilt`.
- **"No Theme Style Sheet set to PanelSettings".** Added `Assets/Resources/UI/VardholmTheme.tss`,
  which imports Unity's default runtime theme and adds nothing else. It lives under `Resources`
  because the panel settings are built in code and have no inspector for anyone to assign an asset
  in. Every colour and size in this game stays in `Theme.cs` — a look split between a stylesheet and
  code is a look nobody can predict — so the theme exists to supply base control styles, not to
  become a second place where the design is decided.


### Fixed — the unclickable menu: the panel was 219 logical pixels tall

The real cause, arithmetic rather than inference. `CreateFallbackPanelSettings` used
`PanelScreenMatchMode.Shrink`, which picks the **larger** of the two scale factors. On the editor's
2560 × 1440 Game view against a 390 × 844 portrait design that is `max(6.56, 1.71) = 6.56`, so the
layout was handed a logical panel of **390 × 219** — 219 pixels of height for a design needing 844.

Everything downstream follows from that one number:

- Flex children shrink by default, so every element was squeezed to roughly a quarter.
- **A shrunk `Label` does not shrink its text.** The glyphs overflowed the crushed box and drew
  across their neighbours — the title over the subtitle, the slot label over CONTINUE. That is the
  "ghosting", and it was never a frame-buffer problem or a second UI tree.
- **A shrunk `Button` still draws its label at full size**, so the text sat well outside the
  rectangle that actually receives the tap. The menu looked right and could not be pressed.

Three changes, each closing the hole at a different level:

- `PanelSettings` now matches **height** (`MatchWidthOrHeight`, `match = 1`). The scale is
  `height / 844`, the column always gets the height it was designed for, and a wide window simply
  widens the side gutters.
- `Typography` and `Buttons` set `flexShrink = 0`. A line of type is a fixed amount of space or it
  is unreadable, and `minHeight` alone never guaranteed the 48 dp touch target — Yoga goes below a
  minimum to fit a column that is too short. Now no layout anywhere can crush them.
- The menu's spacers keep `minHeight = 0` and absorb a short screen instead.

The diagnostic now prints the logical panel size on every menu show, and errors when it drops below
three quarters of the design height — the number that would have answered this in one Play press.

**Two earlier explanations in this changelog were wrong and are retracted:** the ghost text was not
an uncleared colour buffer, and the UI was not being built twice. The clear-only camera and the
one-panel guard added for those are each correct on their own terms and stay.


### Fixed — the menu was being built twice, which is why nothing was clickable

**And the earlier "uncleared frame buffer" explanation for the ghost text was wrong.** The proof is
simple and was available all along: `MainMenuScreen` adds the title and the subtitle as **siblings
in one flex column**. Two siblings in a column cannot occupy the same pixels. They were overlapping
on screen — so there were two columns, not one, each laid out by a panel with its own scale. Two
copies of the whole UI, the one on top swallowing every tap aimed at the one being looked at.

Two holes let that happen, and both are closed:

- **`CreateDocument` built into a panel that might already hold a tree.** A `UIDocument` keeps what
  it has; building again *adds* rather than replaces. It now clears the root first and says so.
- **The duplicate-install guard only checked the host it was installing on.** A panel from any
  other source — a second host, a document authored into a scene, a play session that did not
  reload the domain — was invisible to it. Every `UIDocument` outside the host is now destroyed,
  loudly.

**And a diagnostic, because this cost two wrong diagnoses.** Entering the menu now logs panel
count, root children, screen depth, whether the stack is stuck transitioning, and whether the
curtain is still up. Each of those four makes a menu that looks perfect and does nothing, none is
visible in a screenshot, and all four are one line of state.


### Fixed — NEW GAME did nothing: the scenes had never been created

Not a bug in the button, the controller, the command or the handler. `Assets/Scenes/` holds only a
README, and `ProjectSettings/EditorBuildSettings.asset` has `m_Scenes: []`. `ZoneRibcage` is not in
the build, `LoadSceneAsync` throws, the loader returns `SceneNotFound`, and the menu stays exactly
where it was. The button worked perfectly; there was nowhere to go.

**Why setup never ran.** `VardholmFirstRunCheck` offered a dialog on editor load. A project that
opens in **Safe Mode never runs `[InitializeOnLoadMethod]` from its own assemblies**, so through
every compile-error round the dialog did not appear once. And a dialog answered "Later" leaves a
project whose NEW GAME silently does nothing.

Setup now runs **automatically and unattended** on first load, and logs what it created. It is safe
to do so: it creates only files that do not exist and overwrites nothing. Phase 1.5's goal was
`clone → open → Play`; a prompt that can be missed or declined was never that.

`SceneLoader` also names the cause now instead of returning a bare code: *"scene 'X' is not in
Build Settings … run Vardholm > Setup Project, then press Play again."*


### Fixed — first runtime failure: "Display 1 - No cameras rendering"

The menu rendered and the game did not. Two separate facts, only one of them a bug.

**The menu did need a camera — this entry originally said it did not, and that was wrong.** UI
Toolkit panels with no `targetTexture` do render in screen-space overlay with no camera, which is
why the menu appeared to work. What has no camera is the **clear**. With nothing clearing the
colour buffer, every frame's UI composited on top of the last one still sitting there, and the menu
smeared into itself — the title and subtitle from an earlier layout pass showing through behind the
current one, at the wrong size, permanently. A screenshot of the running menu is what made it
visible; "renders" and "renders correctly" are not the same claim.

The fix is a clear-only camera on the persistent host: `cullingMask = 0`, so it draws nothing and
costs one clear per frame. It is **not** tagged `MainCamera`, so `Camera.main` can never resolve to
a camera that renders nothing, and it is disabled whenever a zone supplies a real one — by the
state machine on entering `InGame`/`Paused`, and independently by the furnisher's own sweep.

**The gameplay camera was one early-return away from never existing.** `ZoneFurnisher.EnsureCamera`
returned the moment `rig.CameraPivot != null`. **A pivot is not a camera.** Any path producing a
pivot without one — an authored rig, a re-furnish after a camera was disabled or destroyed — left
the zone with no camera and nothing anywhere saying so. The same method also adopted any camera it
found in the scene without checking whether it was enabled or its object active, and never tagged
the camera it built, so `Camera.main` was null even when rendering worked.

Now: a pivot without a camera gets one; an adopted camera is activated, enabled and tagged; the
tag is set *before* the component is added, because `Camera.main` is a cached tag lookup.

**Nothing could fail quietly any more.** `Furnish` returns null unless the player, its
`CharacterController` and an enabled tagged camera all exist, and logs one line naming every one of
them; `PlayerRig.Initialize` refuses to bind silently without a usable camera; `AppBootstrap`
records a zone that came up unplayable. Travel can no longer leave two cameras rendering — during a
transition both zones are briefly resident, and every camera but the current zone's is now
disabled, as is every `AudioListener` but its own.

**Tests.** New `CameraLifecycleTests` (5 PlayMode cases): exactly one enabled camera after New
Game with `Camera.main` resolving to it, still exactly one after travel, and a disabled camera
repaired on re-entry, something always clearing at the menu, and the fallback camera retiring when
a zone opens. Deliberately separate from `GameplayLoopTests`, which asserted only that a
*pivot* existed — the assertion that let this through.

**Not verified.** None of this has been run. The `⚠ VERIFY` note in `Commission` stands: under URP
a runtime-added `Camera` also needs `UniversalAdditionalCameraData`, which URP is documented to add
on demand rather than this code referencing the URP assembly for one component.


### Fixed — sixth editor open: CS0234 `ForgottenIsle.UI.Components` in `HudScreen.cs`

`using ForgottenIsle.UI.Components;` names a namespace that does not exist. The **folder**
`Assets/Scripts/UI/Components/` is real, but `Typography.cs` and `Buttons.cs` deliberately declare
`ForgottenIsle.UI.Core` — their own header comments say so, *"because the contract's namespace
list has no UI.Components entry"*. `HudScreen.cs` already imported `ForgottenIsle.UI.Core`, so the
line was pure surplus; deleting it is the whole fix.

**Validator gained a `PHANTOM` check.** The `USING` check asks whether a *type's* namespace is in
scope; nothing asked whether a namespace *named in a using directive* exists at all. Only
namespaces under `ForgottenIsle.` are checked — `UnityEngine.*`, `System.*` and `NUnit.*` live in
assemblies this validator cannot see, and guessing at them would cry wolf.

One subtlety cost a round: the first version exempted anything whose parent namespace was real, to
accommodate `using static Some.Namespace.Type;`. That exemption swallowed this very bug —
`ForgottenIsle.UI` is real, so the wrong leaf passed. It now applies only when `static` is actually
present. Regression-tested by reintroducing the exact line; clean across all 105 files.


### Fixed — fifth editor open: CS0103 `ResultCode` in `InteractionSystem.cs`

`CommandResult.Fail(ResultCode.NoHandler)` with only `ForgottenIsle.Core.Commands` imported —
`ResultCode` lives in `.Core.Primitives`. One missing `using`.

**The `USING` check should have caught this and did not**, which is the more useful finding. It
collected references only from *type positions* (`new X`, `typeof(X)`, `X field;`, generic
arguments) and never from **static member access** — `ResultCode.NoHandler`,
`LogCode.MissingLocKey`, `ContentIds.ZoneRibcage` — where the type name is a qualifier rather than
a type. Unity reports that form as **CS0103** ("the name does not exist in the current context")
rather than CS0246, which is part of why it read as a different class of problem. Worse, the
check's own fully-qualified guard (*"if the raw token carries a dot, the author qualified it
deliberately"*) would have suppressed exactly these references, since a dot always follows.

The check now scans both streams and applies that guard only to type positions. Extending it
immediately surfaced **a second instance of the same bug** in `InteractionTests.cs`, which Unity
had not reported because the test assembly had not been reached. Regression-tested by removing the
fix and confirming it names the file, the line and the `using` to add; clean across all 105 files.


### Fixed — fourth editor open: 6 × CS0246 in `CommandHandlers.cs`

`InspectCommand` and `CollectCommand` "could not be found", at six call sites. Both types existed
and both were spelled correctly. The cause was in `GameCommands.cs`: when the two structs were
added, the insertion landed one line early and **swallowed the closing brace of
`TravelToZoneCommand`**, so they were parsed as *nested types of it* —
`TravelToZoneCommand.InspectCommand`. The file's own trailing brace compensated, so brace counts
balanced and every existing check passed while nothing that used them could compile.

**Validator gained a `NESTING` check.** It compares what the author indented against what the
braces actually say: a deliberately nested type is indented past its parent, an accidentally
nested one sits at namespace-level indentation because its author believed it was one. The check
was regression-tested by reintroducing the exact bug — it reports both sites and exits 1 — and
then confirmed clean across all 105 files, so it is not trading one silent failure for a noisy one.

This is the second time a brace-level insertion error has shipped from this environment (the first
left an orphaned fragment, which `BALANCE` caught). `BALANCE` cannot catch this one by
construction, because the damage is brace-neutral.


### Added — Phase 2: the playable vertical slice

The technical prototype became a game you can walk around in. Full account, including everything
that is written but unverified, in [`PHASE_2_REPORT.md`](PHASE_2_REPORT.md).

**Progression.** `WorldProgress` (Core, engine-free) holds inspected markers, collected
discoveries and unlocked zones as ordinal string sets with idempotent mutators.
`ProgressService` (Game) owns it and is the **third `ISaveParticipant`**, per the rule that every
new system saves in the PR that adds it. Objectives are **derived, never stored** —
`Objectives.Current(progress, zoneId)` is a pure function, so a player who finds the tag before
reading the stone still gets an objective line that is true.

**Interaction.** An `Interactable` base plus a registry-based proximity scan. The nearest eligible
target each `LateUpdate` becomes a prompt signal; pressing interact builds an `ICommand` and
dispatches it. Three kinds ship: `AncientMarker` (re-readable), `DiscoveryPickup` (once, then
gone), `ZoneGate` (travels, or narrates why it will not). Two new commands, `Inspect` and
`Collect`, with handlers. `TravelToZone` now refuses a locked zone with `NotAllowedInState` —
the lock is a rule at the command layer, not a hidden button in the view.

**Two zones, built at runtime.** `ZoneMeshes` generates ground (33 × 33, two Perlin octaves, the
spawn apron faded flat, vertex colours), rocks and ribs; `ZoneBuilder` holds one recipe per zone —
the Ribcage with its six-rib arch, Fernmaw with its cut aqueduct wall. The scene assets stay
empty, exactly as `SCENE_CONTRACT.md` says: the scene is the contract, the builder is the content.

**A player that walks.** `PlayerRig` now drives a real `CharacterController` with acceleration,
gravity and ground following, and pitches the camera pivot alone so the body never tips. Touch
controls arrived as a floating joystick and a look pad in UI Toolkit, with look consumed on read
and the stick released whenever the controls hide — a pause can no longer leave the player
walking.

**HUD.** Objective, interaction prompt and narration, every string through `LocKey`; 28 new rows
in `en.csv`. `HudController` is the only place that knows both a signal and a `VisualElement`.

**Audio, wired and silent.** `AudioDirector` resolves clips by convention through `Resources`.
**No audio files were fabricated.** Every lookup may return null and every play is then a no-op,
so the day a `.wav` lands in `Resources/Audio` it plays with no code change — and until then
nothing pretends the audio works.

**Dev shortcuts.** F5 travels to the other zone, F6 takes the brass tag, F7 resets progression.
Each goes through the same command the game itself uses, so a shortcut that works is evidence the
real path works rather than a back door around it, and all of it compiles out of release.

**Tests.** 28 new cases — 14 progression, 9 interaction, 5 PlayMode loop tests covering furnishing,
the unlock chain, persistence-by-rebuild, and save/quit/continue. **None has been executed.** The
PlayMode suite self-skips with `Assert.Ignore` when the scenes are absent, so a green run means
nothing until you check it was not simply ignored.

### Changed

- `SaveSections` gained `Progress`, and `ProgressService` now takes its section id from there.
  The file's own docstring says these ids live as constants rather than as literals scattered
  through the participants; the new participant was the one exception, and no longer is.
- `ZoneFurnisher` calls `ZoneBuilder` and adds the `CharacterController`.
- `DevOverlay` reports position, discoveries found, registered interactables and the objective.
- `ci/validate-structure.py` whitelist extended for the Unity types Phase 2 introduced.

### Verified, and not

`validate-structure.py` exit 0 · `check-layering.sh` exit 0 · namespace, symbol-existence, asmdef,
duplicate-type, save-participant, localization, scene-key and entry-point audits all clean.
**Still never compiled, never executed, never played.**


### Fixed — the last two compile errors

- **`InputActionSetupExtensions.AddAction` has no `expectedControlType` parameter** (CS1739, 2
  sites in `VardholmControls.cs`). The named argument is **`expectedControlLayout`**. Verified
  against the Input System API docs rather than guessed.

This lands in the file the risk audit named HIGH RISK, which is some vindication of the audit —
but the failure mode was *better* than predicted. The audit warned that a mistake here "compiles
cleanly and produces an action that silently never fires". This one did not compile at all, which
is the good outcome. **The binding strings themselves are still unverified**: `"2DVector(mode=2)"`,
`<Keyboard>/w`, the processor strings. Those are parsed at runtime and only pressing a key proves
them.

The validator's `DEPRECATED` table gained the CS1739 named argument, so this exact mistake cannot
return silently.

### Fixed — third editor open: all 11 warnings

The project code now compiles far enough to produce warnings rather than stopping. Two errors
remain and are not yet diagnosed (Console was filtered to warnings).

- **`FindObjectsByType<T>(FindObjectsInactive, FindObjectsSortMode)` is obsolete** in Unity 6.x
  (CS0618, 4 sites). Switched to the overload without a sort mode.
- **The `DEVELOPMENT_BUILD` preprocessor symbol is deprecated** (UAC0009, 6 sites). Replaced with
  `DEBUG`, which Unity defines in the editor and in development builds and omits from release —
  the same semantics the code wanted.

**Validator gained a `DEPRECATED` check** carrying a table of Unity APIs this editor reports as
obsolete, each entry earned by actually appearing in the Console rather than guessed. It scans
`code_with_strings` rather than the string-stripped source, because a first pass missed
`Conditional("DEVELOPMENT_BUILD")` and `#if DEVELOPMENT_BUILD` — both live in text the normal
scanner blanks out. Regression-tested on both forms.

These were warnings, not errors, which is exactly why they needed a gate: a warning scrolls past,
and the next editor version turns it into CS0619 — which is precisely what happened to the Input
System package.

### Fixed — second editor open: the first real compile of project code

88 errors became 3, and for the first time they were ours.

- **`GameContext.cs` used `SaveSlotService` without `using ForgottenIsle.Game.Saves;`** (CS0246,
  2 sites). Introduced when the shared slot service was added and the using was not.
- **`SessionService` declared both a `PlayerParticipant` property and a nested
  `PlayerParticipant` class** (CS0102). The nested type is now `PlayerSaveParticipant`; the public
  property keeps its name, since four call sites use it.

**The validator missed both, so it gained two checks:**

- `USING` — a project type referenced whose namespace is not in scope. The existing contract check
  only asked "is this type declared anywhere", so a type that exists but was never imported passed
  cleanly and failed in Unity. Fully-qualified references are ignored.
- `COLLISION` — a member and a nested type sharing a name inside the same type. Owner is resolved
  by brace depth, not regex proximity: a naive first version reported six false positives on
  legal code like `public readonly Severity Severity;` inside a *different* nested type, and a
  check that cries wolf gets ignored.

Both were regression-tested by reintroducing the original errors and confirming each is caught at
the right line with the right fix named.

### Fixed — first editor open

- **`com.unity.inputsystem` pinned at `1.14.0`, which targets Unity `6000.1`, not `6000.6.0f1`.**
  The first attempt to open the project produced 88 × CS0619 inside the package's own
  `HIDDescriptorWindow.cs`, which uses `TreeViewState` / `TreeView` / `TreeViewItem` — deprecated
  in Unity 6.3 and treated as obsolete-as-error. Bumped to `1.19.0`, the version released for
  `6000.6`.
- Not a single error was in project code. Unity halts at the first failing assembly and package
  assemblies compile first, so **our C# still has not been compiled** — the risk audit's verdicts
  are unchanged, neither confirmed nor cleared.

### Added — Phase 1.5: Unity integration

Closes the gap between "the code exists" and "clone, open Unity, press Play".

- **`ForgottenIsle.Editor`** — ADR-0001's fifth assembly, which Phase 1 never created.
- **`Vardholm → Setup Project`** — one idempotent menu command that creates the four scene assets
  through `EditorSceneManager`, writes Build Settings in `SceneKeys.All` order, and verifies the
  localization resource. Plus `Validate Project` (read-only) and `Open Bootstrap Scene`.
- **First-run prompt** — a fresh clone offers setup on first editor load rather than failing
  mysteriously. Offered, not forced: silently writing assets on project open is the kind of
  surprise that makes a toolchain untrustworthy.
- **`ZoneFurnisher`** — builds ground, directional light, entry anchor, player capsule and camera
  for any zone scene that does not provide them. This is what makes an empty scene playable, and
  it is why the scene assets can stay empty and therefore un-corruptible. Authored art always
  wins; the furnisher only fills gaps.
- **`VardholmStartupValidator`** — a development-only startup self-check printing PASS / WARN /
  FAIL for services, scenes in Build Settings, localization, and duplicate bootstrap hosts.
  Editor and development builds only, compiled out of release by `[Conditional]`.
- **Runtime camera-pivot attachment** on `PlayerRig`, removing the last dependency in the project
  that required Inspector assignment. Nothing now needs manual wiring.
- **`docs/SCENE_CONTRACT.md`** — the dependency graph and what each scene must contain.
- **`docs/UNITY_RISK_AUDIT.md`** — per-subsystem compile risk, honestly rated.
- Two new test files: `StartupValidatorTests` (EditMode) and `FirstPlayableFlowTests` (PlayMode),
  the latter walking new game → furnished zone → pause → resume → save → quit → continue →
  restored, which is the loop that had no coverage at all.

### Fixed

- `AppBootstrap` searched a loaded zone for a player rig, found none in an empty Phase 1 scene,
  and **returned silently** — leaving the player in a zone with no body, no camera and nothing
  underfoot. It now furnishes the zone instead.
- The structural validator did not know `UnityEditor` types, so the new Editor assembly tripped
  its undeclared-type check. It now carries a separate `UNITY_EDITOR_TYPES` set, kept distinct so
  the editor-only boundary stays visible.
- `Assets/Scenes/README.md` and `docs/dev-setup.md` told the reader to build four scenes and their
  contents by hand. Both now point at the menu command.

### Added — Project memory system

Eight documents that make the repository the authoritative source of truth, so a session can be
picked up cold without re-deriving the project.

- **`CLAUDE.md`** (root) — operating rules, hard invariants and the source-of-truth ordering.
- **`docs/PROJECT_HANDOFF.md`** — the cold-start entry point and the exact next task.
- **`docs/CURRENT_STATE.md`** — verified present state, the verification ledger, and six live
  CONFLICTS.
- **`docs/DECISIONS.md`** — the decision ledger: 6 owner-locked decisions, 14 ADRs, 9 recovered
  design decisions, 4 producer amendments, 9 open items, and a list of decisions deliberately
  *not* made.
- **`docs/ARCHITECTURE.md`**, **`docs/GAME_VISION.md`**, **`docs/ROADMAP.md`** — the map, the
  pitch, the plan.
- The changelog moved from the repository root to `docs/`, with a pointer left behind.

**Recovered, not invented.** Every decision cites the instruction or document that established it.
Uncertain facts are marked `UNCONFIRMED`; contradictions are marked `CONFLICT` rather than being
silently resolved.

**The headline finding:** ADRs 0001–0014 assigned their document edits to "Phase 1 Task 0", and
**Task 0 was never executed**. Six contradictions are therefore live in the documentation,
including one where the shipped code contradicts the specification — the world clock is 60× in
`Ticker.cs` and 30× in the design documents, and the survival formulas were tuned at 30×.

### Added — Phase 1: Project Spine

The first code in the project. Five assemblies, ~16,000 lines of C# across 79 files.

- **`ForgottenIsle.Core`** — engine-free (`noEngineReferences: true`), so the rules of the game
  are a library that can be tested without opening Unity. Primitives (`Vec3`, `LocKey`,
  `ResultCode`), a seeded PCG random whose state serializes into the save, a signal bus, the
  command pattern with a validate/execute split, game state, the versioned save envelope with a
  real migration seam, engine-free JSON and CSV parsing, and localization.
- **`ForgottenIsle.Game`** — bootstrap and composition root, the state machine, the single
  `Update()` in the project, additive scene loading with a 20-second watchdog, the zone registry,
  the session service, an atomic save store (flush-then-rename with `.bak` rotation), input,
  a placeholder player rig and the dev overlay.
- **`ForgottenIsle.UI`** — UI Toolkit framework ported from NATION: WORLD ORDER and rethemed,
  plus the menu, pause and settings screens and their controllers.
- **Tests** — 7 EditMode files, 1 PlayMode file.
- **`ci/validate-structure.py`** — a static validator standing in for the compiler this
  environment does not have: brace balance, namespace conformance, the engine-free Core rule,
  asmdef validity, undeclared-type detection across files, LocKey coverage and duplicate types.
- **`ci/check-layering.sh`** — the architecture gate. It has already caught a real violation.

### Changed

- Title is **VARDHOLM**; the prior working title is retired from all document titles.
- ADR-0014 reverses the Phase 1 plan's uGUI decision in favour of UI Toolkit, because the team
  already owns a working code-built UI Toolkit framework in the Nation project.
- The Phase 1 plan's state-machine table was wrong: it had no edge into `MainMenu` from `InGame`
  or `Paused`, which made QUIT TO MENU unreachable. Corrected to 10 edges, routing quit through
  `Loading` so the curtain can cover scene unloading.

### Added — Phase 0: Design, Architecture, MVP, Story Foundation

Initial design and architecture documentation. **No game code yet** — Phase 0 is design only,
and implementation is blocked pending approval of the Phase 1 plan.

- **Story Bible v1.0** — canonical original IP: the island of Vardholm, protagonist Nadia Vesk,
  four occupation layers, the five-act structure, both endings, and a 10-rule anti-inconsistency
  contract. Establishes the project's naming authority and glossary.
- **Game Vision** — one-page pitch: player fantasy, audience, differentiation, session length and
  the free-prologue/premium-unlock commercial model.
- **Core Game Loop** — the loop diagram, three nested loops (micro/mid/macro), the no-arrow rule
  and the three-tier diegetic hint ladder.
- **World Structure** — nine zones fully specified, the gating graph, a capability/backtracking
  table, a 25-beat critical path and the anti-softlock rule set.
- **First 30 Minutes** — minute-by-minute prologue shooting script with the complete prologue item
  and recipe manifest, and the radio puzzle specified end to end.
- **Mobile UX Plan** — first-person recommendation with a cinematic Body Camera, full portrait
  control scheme, HUD, inventory and combine flow, and accessibility commitments.
- **Technical Architecture** — five assemblies with an engine-free `Core`, the command pattern,
  event flow, DI, scene streaming, performance and localization architecture, and CI enforcement.
  Opens with a verification ledger tagging every unconfirmed Unity 6 claim.
- **Core Systems** — all 17 systems specified with APIs, owned state, dependency graph, tick
  order, shipping-first-pass survival formulas and a named test matrix.
- **Data & Save Architecture** — ScriptableObject authoring baked to immutable records consumed by
  the engine-free Core, ten definition types, runtime state mirrors, and a versioned save envelope
  with a chain-of-migrations framework.
- **Title Evaluation** — five candidates scored against live store searches. Recommends renaming
  from *The Forgotten Isle* to **Vardholm**, with a clearance checklist.
- **MVP Scope & Phase Roadmap** — MVP content manifest, exclusions, risk register and Phases 0–16.
- **Phase 1 Plan** — the executable plan: preconditions, file manifest, code sketches, test list
  and acceptance criteria.

### Fixed — blockers found by adversarial review

A three-way review pass (IP risk, Unity 6 technical accuracy, cross-document consistency) raised
67 issues, 14 of them blockers. Resolved in this commit:

- **Title research error corrected.** The title evaluation claimed no exact "The Forgotten Isle"
  app existed. That was wrong: at least five games use the exact title on itch.io, including a
  $4.99 "mysterious survival-exploration experience" and one built "around immersive sound."
  Recorded with URLs, risk re-scored 2 → 1, and the working title is now recommended for
  retirement rather than carried.
- **"The Mercator Trust" renamed to "The Orrimond Trust"** throughout. The original collided with
  a real, live Guernsey trust company — and the fiction depicts an identically-named trust as
  negligent, which makes it worse than an ordinary name collision.
- **Core temperature formula corrected.** `k = 0.34` is an exponential rate that reaches
  hypothermia in ~3 minutes, not the ~55 the surrounding prose claimed. Re-derived to
  `k = 0.0187`, verified numerically, and a CI balance test now pins the three stated durations.
- **13 binding ADRs added** (`docs/architecture/00-decisions.md`) resolving the structural
  contradictions between the three architecture documents: one assembly layout, an engine-free
  Core with no exceptions, a C# language-level polyfill for `record`/`init` (the definition layer
  as written did not compile under Unity 6), one streaming model, one `SurvivalStat` enum, one
  inventory model, one locomotion scheme, a complete save participant list, and closure of two
  anti-softlock holes on the critical path.

### Known open items

Tracked in `docs/architecture/00-decisions.md` — unacknowledged LOST/DHARMA structural
convergence, proper-noun clearance, unbudgeted foliage overdraw, over-promised volumetric
effects, no privacy-manifest plan, and one unreconciled world-clock scale.

### Notes

- No trademark register has been searched. No name in this repository is cleared for use.
- All science in the fiction is dramatised, not claimed.
