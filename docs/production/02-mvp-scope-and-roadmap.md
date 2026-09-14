# THE FORGOTTEN ISLE — MVP Scope & Phase Roadmap
## Production · v1.0 · Cites Story Bible v1.0, World Structure v1.0, Prologue Script v1.0, Core Systems v1.0

**Status:** Planning document. Every duration, headcount and budget figure in here is a stated assumption, not a commitment. Nothing in this document has been validated against a running build, because there isn't one.

---

# PART 1 — MVP SCOPE

## 1.0 What the MVP is, in one paragraph

A single-zone, ~35-minute, fully shippable slice of *The Forgotten Isle*: cold launch → The Ribcage → survive (water, warmth) → discover (the hull line, the paired boots, the brass tag) → solve (the venturi Catchment) → receive (the 5240 kHz transmission) → unlock (the cut vine at the gully) → end card. It is the free prologue from the Prologue Script, built for real, with the eight domain systems it actually needs and none of the ones it doesn't. It is not a demo scene, not a greybox, and not a tech test. **If we shipped it alone on a store it would be a coherent, finishable, refundable product.** That is the bar.

**The MVP's real job is not content. It is to answer four questions before we spend art money:**
1. Does tap-to-move + drag-to-look + drag-to-combine actually feel good in portrait with one thumb?
2. Can we hit the "premium, cinematic, grounded" art bar on an iPhone 12 at 60fps, with the team we have?
3. Does the deterministic-analytic audio model (Core Systems §16.3) produce a frequency-matching puzzle that works on Android's audio stack?
4. Does a player who has never heard of this game care about a brass tag with a date on it?

Question 4 is the one that kills projects. It gets playtested in the MVP or we are gambling twenty hours of content on a hunch.

---

## 1.1 IN SCOPE — Systems

| System | MVP depth | Explicitly deferred within the system |
|---|---|---|
| `TimeSystem` (L0) | Full. 30× scale, day/night, `HourElapsed`, `FastForward`. | Tide model — the Ribcage MVP has a **static** water line. Tide is Phase 8. |
| `SaveSystem` (L0) | Full format, `.tmp`/`.sav`/`.bak` rotation, CRC32, `ISaveParticipant` for all 8 shipping systems, 3 autosave + 1 manual slot. | Cloud sync, save-slot thumbnails, multi-profile. |
| `LocalizationSystem` (L0) | `LocKey` plumbing, EN table only, live text layer over document art, `#KEY` dev fallback. | 8 other locales, CJK atlas, RTL, localised VO. |
| `WorldSystem` (L1) | One zone (Ribcage), object persistent state, world flags, chokepoint at the gully. | Streaming/adjacency graph (nothing to stream to), 8 other zones. |
| `WeatherSystem` (L1) | **Stub only.** Returns constant `Overcast`, fixed ambient 14 °C, humidity 0.78, precipitation 0, wind vector authored per-volume. Implements `IWeatherSystem` fully so nothing downstream changes later. | The Markov chain, forecast, squalls, cold fronts, altitude bands — Phase 8. |
| `InventorySystem` (L2) | Full: 24-slot dry bag, dual mass/slot limit, atomic removal, reserved critical slots, partial-accept. | `ContainerId.Cache` (no camp footlocker in MVP), `Belt` container (equip leases directly from DryBag). |
| `ItemSystem` (L2) | Full catalog, wetness, corrosion, charge drain. | Salvage-on-break (`BreaksInto`), tool durability tiers beyond blunt/sharp. |
| `CombinationSystem` (L2) | Full: multiset shape hash, three input roles, gating on discovery, typed failure reasons, 8 distinct Nadia failure lines. | Probabilistic multi-output (all 10 MVP recipes are deterministic). |
| `CraftingSystem` (L2) | Timed jobs, two-phase reservation, unattended jobs, `AwaitingCollection`. | Concurrent job cap stays at 4 but MVP never uses >2. |
| `AudioSystem` (L2) | Analytic spectral model, `ListenMode`, 64-bin spectrogram, narrative-audio suppression flag, 28-voice pool, priority eviction. | Reel Deck, hydrophone aiming (hydrophone is inert in MVP), mix snapshots beyond 3. |
| `SurvivalSystem` (L2) | Hydration, CoreTemp, Energy, Health. Bands, collapse-not-death, hidden-by-default HUD. | **Satiation is stubbed at 100 and never drains.** No food in the MVP — see §1.5. |
| `DiscoverySystem` (L3) | Full: bitset, `TryRevise` with chain rendering, notification queue with coalescing + suppression. | Nothing. This system ships complete in MVP because the Slate *is* the game. |
| `PuzzleSystem` (L3) | State machine, declarative requirements, 4-rung hint ladder, checkpointed steps, mid-puzzle save. Six `StepKind`s: `PlaceItem`, `AlignSightline`, `SetDial`, `MatchFrequency`, `HoldValue`, `SequencePress`. | The other five `StepKind`s (`AimHydrophoneAt` et al.) — Phase 6/10. |
| `CampSystem` (L3) | Station placement (Fire, Windbreak, Catchment, DryingRack), fire fuel burn, ground-check placement validation. | Rest/`TryRest` — **cut entirely from MVP**, see §1.5. |
| `InteractionSystem` (L3) | Candidate arbitration with hysteresis, all six verbs, inspect view with free rotation, equip lease. | — |
| `StorySystem` (L4) | Node DAG (≈26 nodes), eligibility-on-event, playback manifests. | Act quorum machinery is built but only Act 1 exists; ending choice API stubbed and asserted-unused. |
| `QuestSystem` (L4) | The Slate's single *Next:* line. One active objective, zero background threads. | Background threads, player-chosen active quest. |

**Engineering deltas against Core Systems v1.0 that the MVP requires.** Three, all small, all flagged now so they are not discovered in sprint 6:

1. **`RecipeOutput.Kind` gains `Station` and `InstanceRepair`** alongside `Item`. Four of the ten MVP recipes produce a placed `StationType` (fire, windbreak, Catchment, drying rack) and two mutate an existing instance's state (dry the recorder, scrape the radio contacts). Core Systems §3.3 assumes item-only outputs. Without this, station construction and repair become bespoke code paths and we will write them three times.
2. **`StepKind.AlignSightline`** — the hull-line discovery (Prologue 2:40) is a real puzzle input: camera yaw within ±4° of an authored axis from within an authored trigger volume. It is not in the eleven `StepKind`s listed in Core Systems §9.3. It is one of the two most important interactions in the prologue. Add it.
3. **`IWeatherSystem` stub contract** must be written and merged in Phase 2, not Phase 8. Survival integrates against ambient temperature from day one; if the stub arrives late, `SurvivalSystem` gets written against hardcoded constants and then refactored.

---

## 1.2 IN SCOPE — The 18-item content manifest

Categories: `Tool` (never consumed, takes wear) · `Material` (consumed) · `Component` (crafted intermediate) · `Consumable` · `Key` (Critical flag, bypasses capacity, undroppable).

| # | ItemId | Display LocKey | Category | Stack | Grams | Flags | One line |
|---|---|---|---|---|---|---|---|
| 1 | `item.multitool` | `item.multitool.name` | Tool | 1 | 210 | Durable | Blade, hardened spine, driver, scraper. The only tool she brought that still works. |
| 2 | `item.field_slate` | `item.field_slate.name` | Key | 1 | 340 | Critical, Durable | Wire-bound notebook, swollen with seawater. The evidence system, and the only thing she will not put down. |
| 3 | `item.field_recorder` | `item.field_recorder.name` | Key | 1 | 480 | Critical, Charged, Corrodible, WaterSensitive | Solid-state recorder, 9% charge, water beading under the display glass. |
| 4 | `item.hydrophone` | `item.hydrophone.name` | Key | 1 | 900 | Critical, Durable, WaterSensitive | Dented directional hydrophone. Inert in the prologue; inspect-only. Her whole career, in a bag, on a beach. |
| 5 | `item.driftwood_dry` | `item.driftwood_dry.name` | Material | 8 | 900 | — | Sun-bleached wood from above the tideline. Pale, light, and it clicks when you knock it. |
| 6 | `item.driftwood_wet` | `item.driftwood_wet.name` | Material | 8 | 1500 | WaterSensitive | Dark, heavy wood from below the wrack. Will not light. Thuds instead of clicking. |
| 7 | `item.dry_grass` | `item.dry_grass.name` | Material | 12 | 15 | — | Bleached grass from rock crevices above the spray line. Flares beautifully for a second and a quarter. |
| 8 | `item.poly_rope` | `item.poly_rope.name` | Material | 6 | 260 | — | Blue polypropylene, sun-rotted to felt, crumbling at the lay. |
| 9 | `item.poly_fibre` | `item.poly_fibre.name` | Material | 10 | 30 | — | Teased rope fibre. Catches a spark and sulks. Smells appalling. |
| 10 | `item.chert_nodule` | `item.chert_nodule.name` | Tool | 1 | 340 | Durable | Pale banded stone, out of place on a basalt beach. Rings when struck; chips to an edge. |
| 11 | `item.copper_sheathing` | `item.copper_sheathing.name` | Material | 3 | 620 | — | Anti-fouling plate peeled off an eighteenth-century hull. Soft, flat, and the only workable metal here. |
| 12 | `item.salvage_cordage` | `item.salvage_cordage.name` | Material | 10 | 45 | — | 8 mm braided line off a trawler's net drum. Still strong. Stronger than anything green. |
| 13 | `item.drum_intact` | `item.drum_intact.name` | Material | 1 | 1800 | — | A washed-up 20-litre drum with its cap still on. The only rigid watertight thing on the Ribcage. |
| 14 | `item.drum_basin` | `item.drum_basin.name` | Component | 1 | 1100 | — | The drum, opened lengthwise into a trough. Crude, and exactly the right shape. |
| 15 | `item.water_pouch` | `item.water_pouch.name` | Consumable | 8 | 260 | — | 250 ml of condensate. Tastes of copper and plastic. Restores 18 hydration. |
| 16 | `item.radio_set` | `item.radio_set.name` | Key | 1 | 3400 | Critical, Durable, Corrodible | Portable HF set, cream and phenolic, wiped clean by somebody. Three things wrong with it. |
| 17 | `item.dcell` | `item.dcell.name` | Material | 4 | 140 | Charged | Salvaged D-cell, stored out of its housing by someone who understood corrosion. |
| 18 | `item.copper_spring` | `item.copper_spring.name` | Component | 2 | 12 | — | A torch's battery spring. Not a fuse. A decision to trust the wiring. |

**Deliberately NOT items** (world interactables only, to hold the manifest at 18): basalt cobbles, the dead hand-torch shell (a container you open in inspect view to get #17 and #18), the paired rubber boots, the brass tags, the canvas square, the crate, the frequency list, the kelp.

**Carry limits, MVP values:** DryBag 24 slots / 11 000 g, slots 20–23 reserved for `Critical`. The four Key items total 5 130 g, which is 47% of the mass budget before the player picks up a single stick. That is intentional and it is the first thing playtest will complain about. **Do not raise the limit; lower the Key masses.** The correct dial is item mass, not capacity, because capacity is what makes the beach feel like a beach.

---

## 1.3 IN SCOPE — The 10 recipes

Input roles per Core Systems §3.3: `C` = Consumable (removed), `T` = Tool (wear only), `K` = Catalyst (untouched, may be a station or trigger volume).

| # | RecipeId | Inputs | Output | Gated on | Time (world) | Attended |
|---|---|---|---|---|---|---|
| 1 | `rcp.tease_fibre` | C `poly_rope` ×1 · T tag:`CuttingEdge` ≥0.10 | `poly_fibre` ×3 | — | instant | yes |
| 2 | `rcp.cut_drum` | C `drum_intact` ×1 · T tag:`CuttingEdge` ≥0.25 | `drum_basin` ×1 | — | 15 min | yes |
| 3 | `rcp.windbreak` | C `driftwood_dry` ×2 · C `salvage_cordage` ×1 · T tag:`CuttingEdge` | Station `Windbreak` (spawns a `WindShadow` volume, r = 2.2 m) | `disc.wind_kills_fire` | 20 min | yes |
| 4 | `rcp.fire_lay` | C `driftwood_dry` ×3 · C `poly_fibre` ×1 · T `chert_nodule` · T `multitool` · K volume:`WindShadow` | Station `Fire` (4 h fuel) | `disc.wind_shadow` | instant (on strike gesture) | yes |
| 5 | `rcp.dry_wood` | C `driftwood_wet` ×1 · K station:`Fire.WarmZone` | `driftwood_dry` ×1 | `disc.warm_zone` | 6 min | **no** |
| 6 | `rcp.dry_recorder` | InstanceRepair `field_recorder` · K station:`Fire.WarmZone` | recorder `WetnessSeconds → 0`, corrosion clock halted | `disc.warm_zone` | 20 min | **no** |
| 7 | `rcp.drying_rack` | C `salvage_cordage` ×2 · C `driftwood_dry` ×2 | Station `DryingRack` | `disc.gear_moisture` | 15 min | yes |
| 8 | `rcp.catchment` | C `drum_basin` ×1 · C `copper_sheathing` ×1 · C `salvage_cordage` ×2 · C `driftwood_dry` ×2 · T `multitool` · K volume:`VenturiGap` | Station `Catchment` | `disc.venturi_gap` | 40 min | yes |
| 9 | `rcp.draw_water` | (no inputs) · K station:`Catchment` | `water_pouch` ×1 | `disc.venturi_gap` | 6 h | **no**, caps at 2 held |
| 10 | `rcp.scrape_contacts` | InstanceRepair `radio_set` · T `multitool` | clears radio fault `Corroded` | `disc.radio_faults` | 1.5 s hold gesture | yes |

**Five authored failures, shipped as first-class content** (they are not "invalid combinations", they are recipes that resolve to a scripted negative outcome, so the player gets a real line and a real observation, never a shrug):

| Attempt | Result | Discovery it grants |
|---|---|---|
| spark + `dry_grass` | flares 1.2 s, dies | `disc.tinder_too_quick` |
| ember + `driftwood_wet` | hiss, smoke, ember dies | `disc.wet_dry_fuel` |
| `basalt` (world) struck on multitool | dull knock, no chip | `disc.striker_test` (the negative half) |
| `rcp.fire_lay` outside a `WindShadow` | lights, streams, blows out in 3 s | `disc.wind_kills_fire` |
| `field_recorder` dragged into flame | **blocked**, Nadia stops her own hand: *"No."* | none — an un-failable guard |

Four of those five failures *grant a discovery*. That is the design thesis in the recipe table: being wrong is how you learn the rules, and the Slate writes it down.

---

## 1.4 IN SCOPE — The exact discoveries (16)

| DiscoveryId | Category | Source | Granted by | Unlocks |
|---|---|---|---|---|
| `disc.six_hulls_line` | Location | Observation | `pzl.hull_sightline` solved, **or** auto at T+6:00 | Story node `n.hulls`; Slate entry, **UNRESOLVED, never resolved in MVP** |
| `disc.no_epirb` | Personal | Observation | Inspect `int.ketch_transom` at low water | Story node `n.nobody_coming`; gates the *Next:* objective change |
| `disc.striker_test` | Technique | Observation | Strike any rock on multitool (either outcome) | `AbilityFlag.StrikeSpark`; makes `chert_nodule` a valid Tool input |
| `disc.tinder_too_quick` | Technique | Combination | `dry_grass` flare-out | Hint rung 1 for the fire puzzle |
| `disc.wet_dry_fuel` | Technique | Combination | Ember dies on wet wood | Enables `rcp.dry_wood` prompt line |
| `disc.wind_kills_fire` | Technique | Observation | 1st fire blow-out **or** inspect blown sand VFX | `rcp.windbreak` |
| `disc.wind_shadow` | Technique | Observation | Enter the landing-craft lee volume **or** build a windbreak | `rcp.fire_lay` |
| `disc.warm_zone` | Technique | Observation | Fire lit → inspect the heat-haze volume | `rcp.dry_wood`, `rcp.dry_recorder` |
| `disc.gear_moisture` | Technique | Observation | Recorder wetness crosses 50% | `rcp.drying_rack` |
| `disc.venturi_gap` | Mechanism | Observation | **The environmental puzzle.** See §1.6 | `rcp.catchment`, `rcp.draw_water`; story node `n.geometry_makes_water` |
| `disc.someone_was_here` | Person | Document | Rotate the boot tag to its reverse face | PEOPLE card `SOMEONE`; story node `n.boots` |
| `disc.cut_door` | Mechanism | Observation | Inspect the oxy-cut hole in the trawler flank | Enables `int.trawler_interior` entry |
| `disc.radio_faults` | Technique | Observation | Complete a full rotate-inspect of `item.radio_set` | Unlocks `pzl.radio_repair`, `rcp.scrape_contacts` |
| `disc.freq_list` | Technique | Document | Rotate the radio to expose the taped handle list | Sets `pzl.radio_tune` tolerance assist; hint rung 2 content |
| `disc.the_voice` | Person | Audio | The 44-second transmission completes | **Revises `disc.someone_was_here` → `disc.someone_is_here`**; story node `n.the_voice` |
| `disc.cut_vine` | Mechanism | Observation | Inspect the vine face at the gully mouth | Story node `n.hook`; **ends the MVP** |

`disc.someone_is_here` is the sixteenth id and exists only as the revision target. **The revision is the single most important thing the MVP proves**, because Rule 7 of the fiction contract and the entire Field Slate design hang off `TryRevise` working, persisting, and rendering a struck-through line. If it does not land in the MVP, it will not land.

---

## 1.5 IN SCOPE — The Ribcage interactable manifest

**Story-critical (must all be present and hooked):**

| InteractableId | Verbs | Yields |
|---|---|---|
| `int.dry_bag` | Take | Items 1–4, opens the inventory strip |
| `int.hull_01_trawler` … `int.hull_06` | Look, `AlignSightline` | `disc.six_hulls_line` |
| `int.net_drum` | Take ×3 | `salvage_cordage` |
| `int.hull_copper_face` | Use (multitool) ×3 | `copper_sheathing` |
| `int.ketch_transom` | Look | `disc.no_epirb` |
| `int.venturi_dry_patch` | Look, Listen | Puzzle step 1 (see §1.6) |
| `int.boots_paired` | Look → inspect, rotate | `disc.someone_was_here` |
| `int.oxy_cut_hole` | Look | `disc.cut_door` |
| `int.crate_table` | Look | staging only |
| `int.canvas_square` | Take | reveals `int.radio_set` |
| `int.radio_set` | Take, inspect (6 faces) | `item.radio_set`, `disc.radio_faults`, `disc.freq_list` |
| `int.torch_dead` | Use (multitool) | `dcell` ×2, `copper_spring` ×1 |
| `int.tag_row` (11 tags) | Look, rotate | one carries `SET 2 — SKED 5240`; redundant clue |
| `int.chalk_plate` | Look (fire-lit only) | redundant clue: `5240` + tally |
| `int.gully_vine` | Look → inspect | `disc.cut_vine`, MVP end |

**Resource nodes:** `node.driftwood_dry` ×7, `node.driftwood_wet` ×9, `node.dry_grass` ×4, `node.poly_rope` ×3, `node.chert` ×3 (all within 40 m of the authored fire site), `node.basalt` ×12, `node.drum` ×2 (one intact, one split and useless — an honest object lesson).

**Optional inspectables, 11 exactly** (Slate entries only, zero mechanical effect): a boot print in dried mud above the tideline; a cormorant leg band; a chalked tide mark on the trawler plate; oxy-cut slag beads; a broom arc in swept sand; a rusted shackle wired shut with modern stainless seizing wire; a plastic crate stencil, illegible; a bleached rope end whipped in a pattern nobody uses any more; a bolt hole drilled in bedrock with a modern masonry bit; a gull skull; the nine crossed-out names page in the Slate (the character beat from 9:30).

---

## 1.6 IN SCOPE — The one environmental puzzle, in full

### `pzl.the_wrong_wave` — "The Wrong Wave"

**The break.** The Catchment is buildable but Nadia doesn't know where to put it, and sited anywhere on the open beach it condenses at a rate that would take four days to make a litre. She needs moving air across a cold metal plate.

**The world truth.** Between hull four (the inverted landing craft) and hull five (the steel tender), the 34 m gap narrows to 3.1 m between their flanks, and the prevailing onshore wind accelerates through it. Consequence, staged as art: the black sand in that gap is **dry** — a pale grey patch two metres wide — while the entire rest of the beach below the wrack is wet and mirror-dark. The gap is also audibly different: a thin steady 900 Hz–2 kHz hiss of wind over plate edges, against the beach's 60–90 Hz surf rumble.

**PuzzleDefinition (authored data, zero code):**

```
Key: pzl.the_wrong_wave
Unlock (ALL):
  HasDiscovery(disc.striker_test)          // she has been paying attention to materials
  HasItem(item.drum_basin, 1)              // she has a vessel
  HasItem(item.copper_sheathing, 1)        // she has a cold plate
StepsOrdered: true
Steps:
  1. PlaceItem   target=int.venturi_dry_patch, item=any,        // the feather/strap test
     Tolerance: within 1.2 m of the volume centre
     IsCheckpoint: true
  2. HoldValue  target=vol.venturi_flow, Target=1.0, Tolerance=0.25, HoldSeconds=2.0
     // "hold a loose object in the draught and watch which way it streams"
     // satisfied by holding ANY held item up inside the volume for 2 s
     IsCheckpoint: true
  3. SetDial    target=stn.catchment_placement_yaw, Target=90.0, Tolerance=22.0
     // the rig must sit ACROSS the draught, not facing into it
OnSolved:
  UnlockDiscovery(disc.venturi_gap)
  AdvanceStory(n.geometry_makes_water)
  EnableRecipe(rcp.catchment, rcp.draw_water)
  SetWorldFlag(flag.catchment_sited)
```

**The player's actual experience, in order.** They notice a dry patch on a wet beach (or they don't, and the hint ladder walks them to it). They stand in it and the wind noise changes in their headphones. They hold something up — anything, the strap of the bag, a piece of grass — and it streams sideways, not toward them. Nadia: *"It's coming through the gap, not off the sea."* They build the Catchment there, and when they place it, the placement ghost has a yaw handle. Face it into the wind and it reads `0.31 L/day`. Turn it side-on and it reads `1.04 L/day`. **The number is the feedback. There is no "correct!" chime.**

**Why this puzzle and not another one.** It is the entire game's thesis rehearsed as a survival chore: *geometry plus a thermal gradient produces water.* The Combs in Act 4 are this puzzle with three more zeroes on it, and the recognition has to be physical. Any MVP puzzle that isn't this one is the wrong puzzle.

**Hint ladder (rungs 2 and 3 require an explicit request):**

| Rung | Trigger | Content |
|---|---|---|
| 0 Ambient | immediate | Blown-sand VFX visibly kinks through the gap. The dry patch has 1.4× the specular contrast of the surrounding sand. |
| 1 Observation | 90 s in `InProgress`, no valid input | *"Everything below the wrack is wet. That isn't."* |
| 2 Direction | 240 s + request | *"Air moves. Sand dries. I should stand in it and find out which way it's going."* |
| 3 Method | 420 s + second request | *"Across the flow, not into it. You want the air to sweep the plate, not push on it."* — still does not name a yaw value. |

**Anti-softlock guarantees (R1–R10 compliance):** every input consumed is respawning (`copper_sheathing` restocks from the hull face on a spring-tide flag; `drum_intact` has two spawns); the puzzle is `reversible: true` — the Catchment can be picked up and re-sited, refunding nothing and costing nothing; `TryReset` returns to `Available`; two redundant `hintSources` with distinct media (the dry sand = environment; Nadia's 8:00 fireside line about the hull gap = audio).

---

## 1.7 OUT OF SCOPE — and where it actually lands

Ruthless. Each of these has been argued for in a real meeting on a real project and each one has killed a schedule.

| Cut | Why it is tempting | Why it must not be built now | Lands in |
|---|---|---|---|
| **Any NPC, including Lorvik** | "Just a silhouette in the distance for the trailer." | An NPC means animation state machines, pathing, LOD'd characters, facial rig, dialogue system. It is a six-month subsystem and the MVP's entire emotional engine is that **there is nobody here and someone is maintaining the place anyway.** A visible person destroys the thing we are testing. | Phase 9 (voice only) / Phase 10+ (Lorvik, Act 5) |
| **Combat, weapons, any hostile entity** | Never. | Canon Rule 3. There is no version of this game with combat. Do not build a damage system "just for falls" — fall damage is a flat authored value (Core Systems §7.5.7). | Never |
| **Weather variety** | The Ribcage would look great in a squall. | `WeatherSystem`'s Markov chain, six states × altitude bands × VFX × wet-surface shader variants × audio beds is ~5 weeks of work that changes nothing about whether the prologue hooks a player. The MVP ships **one** lighting condition: permanent overcast, 14 °C. Which is also canon. | Phase 8 |
| **Day/night cycle as a visual** | `TimeSystem` already tracks it. | Time advances; the *sky* does not. A night lighting pass on the Ribcage is a second full lighting bake, a second set of shadow-caster decisions and a torch mechanic the prologue explicitly refuses to spend budget on (Prologue §6). Clock runs, sky is locked. | Phase 8 |
| **Any zone but the Ribcage** | Fernmaw is the prettiest zone in the bible. | Nine zones at MVP fidelity is the whole game. The gully is a locked chokepoint with a vine and an end card behind it. | Phases 7, 10 |
| **Tides** | The Ribcage floods; it's in the zone spec. | Tidal water-plane animation + traversal blocking + the cutoff-in-a-hull sequence + the `int.ketch_transom` low-water reveal is a system *and* a level-design pass. MVP: the transom is visible from a rock spur instead. Static water. | Phase 8 |
| **The hydrophone as a verb** | It is the signature mechanic. | It is the **Act 2** signature mechanic and the prologue is explicit that the prologue rehearses its *grammar* (aim, sweep, isolate) using the radio dial. Building the hydrophone overlay for a zone with three acoustic sources proves nothing and costs the spectrogram UI twice. | Phase 6 |
| **Monetization of any kind** | "We need the paywall card in the MVP." | We need the *card*, not the *purchase*. MVP ships a static end card with a placeholder price and a dead button. IAP plumbing, receipt validation, restore-purchases, store-specific entitlement checks and refund handling are a phase, and they are a phase that cannot start until a business decision is made. **See the ordering critique — this decision is a Phase 0 blocker, not a Phase 15 task.** | Phase 15 |
| **Ads, energy timers, daily rewards, any F2P hook** | Somebody will suggest it. | The tone brief says explicitly: not a free-to-play ad game. This is a premium product or it is a different product. Building the hooks "so we have the option" is how a game becomes the other thing. | Never |
| **Cloud save / cross-device** | "Players expect it." | It requires an account system, a conflict-resolution UI, a backend, a privacy review and a support burden, for a single-player offline game whose sessions are 22 minutes. Local save with `.bak` rotation is the actual reliability requirement. | Post-launch, if telemetry justifies |
| **Voice acting** | The 44-second transmission is the hook. | **One exception, and it is the only exception:** the transmission itself ships with real recorded VO in the MVP, because we are testing whether it lands. Nadia's ~90 prologue lines ship as **scratch VO recorded by anyone in the building**, clearly marked, with final casting and direction in Phase 13. Recording 90 final lines before we know which survive playtest is how you pay for a performance twice. | Phase 13 (all but the transmission) |
| **Localisation beyond EN** | Cheap if done early. | The *plumbing* is in scope from Phase 1 (`LocKey` everywhere, zero literal strings in the domain assembly — that is a build-failing CI rule). The *tables and fonts* are not. Localising a script that will change is the most reliably wasted money in games. | Phase 12 |
| **The Slate's full evidence board** | It's the coolest UI in the doc. | MVP ships the Slate as a three-tab book (OBSERVED / PEOPLE / UNRESOLVED) with entries, revision rendering and the *Next:* line. The pinned-string physical board in Hut 1 is a Fold Camp object. | Phase 9 |
| **The Seventh Hull secret** | Two days of work, huge payoff. | It is a payoff that requires Act 5 to exist. Building it now means it is a boat on a beach with no meaning, and it will be re-authored when Act 5 is written. | Phase 10 |
| **Satiation / food / cooking** | Every survival game has it. | The bible says survival is the first hour, not the game. Food in a 35-minute prologue is a meter that never moves and a shellfish-gathering loop nobody asked for. `Satiation` is stubbed at 100. The cold-hunger coupling term stays in the formula, dormant, so Phase 5 turns it on with a constant. | Phase 5 |
| **Rest / sleep / `TryRest`** | `CampSystem` has it in the spec. | Fast-forwarding the clock requires the weather chain and crafting jobs to step correctly, which requires weather to exist. Rest is a Phase 8 dependency wearing a Phase 5 costume. MVP fire gives warmth, not time-skip. | Phase 8 |
| **Achievements, analytics dashboards, telemetry beyond 6 events** | "We need data from the MVP." | We need **six** events: `PROLOGUE_NO_FIRE`, `PROLOGUE_NO_ALIGN`, time-to-first-water, time-to-signal, hint-rung-reached-per-puzzle, session-abandon-timestamp. Six events, one endpoint, no dashboard. A dashboard is a Phase 16 task. | Phase 16 |
| **Controller / tablet / landscape support** | Free with the New Input System. | It is not free. It is a second UI layout, a second interaction-arbitration tuning pass and a second QA matrix. Portrait phone only. | Post-launch |

---

## 1.8 THE MVP DEFINITION OF DONE

A checklist an outsider — a publisher's producer, a QA lead on their first day — can verify on a device in under two hours. Every line is pass/fail. No line says "feels good".

**A. It runs and it persists**
1. Installs from TestFlight and from an Android internal-test track on a physical iPhone 12 and a physical mid-range Android, no sideload instructions.
2. Cold launch to first player input is **under 12 s** on the Android reference device.
3. Force-quit the app at any point in the 35 minutes; relaunch; the Slate Open recap plays and the player resumes with world objects, inventory, puzzle dial positions, survival stats, fire fuel and the in-game clock all restored.
4. Kill the app *during* an autosave write (pull the USB / use the debug "kill now" button 200 ms after an autosave fires) ×10 attempts; every relaunch loads, and at least one reports `RecoveredFromBackup` in the log rather than failing.
5. Fill the device storage to under 4× the save-blob size; the game reports `SaveFailed(OutOfSpace)` with a visible non-blocking banner and **keeps running**.
6. Background the app for 40 minutes; on resume, in-game time has advanced by **zero**.

**B. The critical path completes**
7. A tester who has never seen the game finishes START → SURVIVE → DISCOVER → SOLVE → SIGNAL → JUNGLE → END card without a developer in the room.
8. The path is completable **without reading a single document**, using only the redundant environmental and audio clue sources (R10 compliance), and completable **without listening**, using only documents and visual clues. Both runs verified separately.
9. A tester who does nothing but the minimum still reaches the transmission: all hint ladders, including the radio's undocumented Tier 4 auto-sweep, verified to fire on their timers.
10. The 44-second transmission plays, in full, with the four seconds of open carrier after "No vessel" **intact and unfilled**. This is a named, checkable line item because somebody will fill it.
11. `disc.someone_was_here` visibly revises to `disc.someone_is_here` in the Slate, with the original struck through and still readable, and the revision survives a save/load.
12. The end card displays and the app returns cleanly to the main menu.

**C. It cannot be broken**
13. `Architecture_NoAssemblyReferencesUpward` and the Roslyn layer analyzer pass in CI.
14. `Isle.Domain.Tests` — the MVP subset of the Core Systems §20 matrix, minimum **68 tests** — passes in under 20 s, headless, no Play Mode.
15. `Save_RoundTrip_AllParticipants_ProducesIdenticalState` passes over 200 generated states.
16. Content validators pass and fail the build when violated: no critical-path recipe draws on a `finite: true` node (R1); every puzzle object declares `reversible` or `respawns` (R4); zero `PlayerDeath` transitions exist (R5); every `criticalPath: true` item is `destructible: false, droppable: false` (R6); `activeObjectives.count > 0` at every reachable world-state (R7); every critical-path solve declares two `hintSources` with distinct `medium` (R10).
17. A scripted bot performs 500 random `TryAdd`/`TryRemove`/`TryCombine`/`TryCraft` operations with a full pack; zero item duplications, zero item losses, zero exceptions.
18. Zero managed allocations per frame in steady-state domain code, measured over 60 s in the Unity Profiler on device.

**D. It hits the bar**
19. Sustained **60 fps** on iPhone 12 across the full 35-minute path, with the 1% low above 50 fps, measured on device with the profiler attached.
20. Sustained **30 fps** on the low-end Android reference device across the same path.
21. Peak resident memory **under 1.4 GB**; the build fails at 1.6 GB.
22. Save-collection main-thread cost **under 8 ms**; the `OnApplicationPause` synchronous path under 30 ms.
23. Every interaction in the 35 minutes is performable with the right thumb inside the bottom 45% of a 6.1" screen; measured by a heatmap overlay in a debug build, verified with a tester who has been told to keep their left hand in their pocket.
24. No touch target smaller than 44 pt after arbitration assist.

**E. It is honest**
25. No literal display string exists in the `Isle.Domain` assembly (CI-enforced).
26. Every `LocKey` referenced in code or data exists in the EN table (CI-enforced).
27. Scratch VO is audibly and visibly marked as scratch in the build, and the build is never shown externally without that marking stated.
28. The trademark/title clearance pass on all proper nouns is **complete or explicitly logged as outstanding**, with the outstanding list attached to the build notes. (Per the bible's §9 flag: none of these names have been cleared. This is a gate on external showing, not on internal builds.)

---

## 1.9 RISKS — top 8

Likelihood and impact are my judgement calls, not measurements.

| # | Risk | L | I | Mitigation |
|---|---|---|---|---|
| **1** | **The "realistic premium" art bar is unreachable at 60fps on an iPhone 12 by a team this size.** This is the elephant and it is addressed head-on below the table. | **High** | **Critical** | Phase 2 ships a *vertical slice of one 30 m stretch of beach* at intended final fidelity, profiled on device, before any other art is made. Go/no-go on the fidelity target happens there, in week 8, not in month 14. Fallback art direction defined in advance — see §1.9a. |
| **2** | **The analytic audio model doesn't survive Android's audio stack**, and `MatchFrequency` feels laggy or wrong on real devices with real Bluetooth headsets. | Medium | High | The design already decouples correctness from playback (Core Systems §16.3): the puzzle checks the analytic model, never device audio. Phase 1 includes a two-day spike on three Android devices measuring output latency and the >120 ms widening path. If the spike fails, the radio puzzle degrades to a spectrogram-visual lock with audio as flavour — playable, less magical, and known by week 4. |
| **3** | **Nobody cares about the brass tag.** Playtesters finish the 35 minutes and say "it was fine" — the mystery does not hook. | Medium | **Critical** | This is precisely why we are building an MVP. Ten unmoderated remote playtests at the end of Phase 6, scored on one question: *can you state, unprompted, what you want to know next?* Fewer than 7 of 10 naming the voice, the date or the cut vine = the hook is broken and we re-author the prologue's back half before content production, not after. Budget two weeks for that possibility in the plan. |
| **4** | **Save gets retrofitted.** As the brief orders phases, `SaveSystem` lands at Phase 11, after ten phases of systems that were written without `ISaveParticipant`. | **High** (if the brief order is followed) | High | Move the save spine to Phase 1. See the ordering critique in Part 2. This risk is entirely self-inflicted and entirely avoidable. |
| **5** | **Scope creep into Fernmaw.** The gully is right there, the team is bored of a beach, "just a corridor". | **High** | High | The gully is a hard chokepoint with an end card behind it. A single named producer sign-off is required for any commit that adds a new zone asset before Phase 7 opens. The one-line test: *does this change the answer to one of the four MVP questions?* If no, it waits. |
| **6** | **No-fire / no-align telemetry blows out** — more than 4% of sessions never light a fire, or more than 40% never find the hull sightline, meaning core signposting is wrong. | Medium | Medium | Both are already instrumented as named flags in the Prologue Script. Chert spawn points and the sightline trigger volume are data, not code — they are tunable in an afternoon. Two tuning passes budgeted in Phase 6. |
| **7** | **Name clearance fails.** "The Forgotten Isle" or one of ~30 invented proper nouns collides with an existing mark or title. None have been searched. | Medium | High | Commission a trademark and title clearance pass in **Phase 0**, before a single asset is named. Names live in the loc table and content IDs are hashes of stable string keys, so a rename is a table edit and a content re-bake, *provided* nobody hardcodes a name in a filename or a class. That is a code-review rule from day one. |
| **8** | **Mobile-store review or platform policy surprises** — entitlement handling, privacy manifests, age rating for a game about institutional harm and a suicide-adjacent grave beat. | Medium | Medium | I have not verified any current store policy or rating criterion and will not assert one. Mitigation: engage a submissions consultant or the platforms' own developer support in Phase 0 to establish actual current requirements, and re-verify before Phase 16. Treat everything anyone on the team "remembers" about store rules as unverified. |

### 1.9a The art elephant, addressed directly

**Can a small team hit "premium, cinematic, grounded, not cartoonish" on mobile at 60fps?** Honestly: a team of 3 artists cannot, at the fidelity the World Structure document describes, in a normal schedule. The Combs' cliff face and Oleander's fourteen rooms are already flagged in that document as unprototyped assumptions, and they are the *later* zones. The Ribcage is the cheapest zone in the game and it still needs six hero wrecks, wet-sand shading that sells a mirror at 60fps, and volumetric-looking haze without volumetrics.

**What makes it reachable:**

- **The overcast cloud lid is the single biggest gift in this design.** No sun, no hard shadows, no dynamic time-of-day for the whole MVP means one baked lightmap set, one directional for shadow *direction* only, and two dynamic lights maximum on screen. That is not a compromise forced on us; it is canon. Lean on it everywhere.
- **Silhouette-and-specular, not surface detail.** The Ribcage reads by silhouette against sky and by wet-sand specular sheen. That is a lighting and material problem, not a texel-density problem. Budget the money on one outstanding wet-sand shader and one outstanding haze solution, and be cheap on everything else.
- **Photogrammetry-derived kitbash for rock, rusted steel and weathered wood**, processed to mobile budgets, rather than hand-sculpted hero assets. Three material families cover 80% of the island.
- **A locked camera height and a portrait frame** mean we never have to make the top of anything.

**The fallback art direction, decided now so it is not decided in a panic:** if the Phase 2 vertical slice misses 60 fps at the intended fidelity by more than ~20%, we move to **reduced-geometry, high-contrast tonal realism** — think a strong grade with crushed blacks and a narrow palette, heavier atmospheric depth cueing, simplified normal detail, and more of the frame carried by fog, silhouette and a film grain / halation pass than by surface fidelity. It stays grounded and premium; it stops being photoreal. It is cheaper per asset by a large factor and it is *more* distinctive, not less. Crucially it is a decision we can make in week 8 for near-zero sunk cost, and cannot make in month 14 for any price.

**What I will not accept as a mitigation:** "we'll optimise it later." Nothing in this project's design is rescued by late optimisation, because the cost is in asset count and asset authoring, not in draw calls.

---

## 1.10 Team shape and duration — stated as assumptions

**These are planning assumptions for argument, not estimates I can defend with data, because there is no codebase, no prototype and no measured velocity.**

Assumed core team for the MVP:

| Role | Count | Note |
|---|---|---|
| Lead / gameplay engineer | 1 | Owns the domain assembly and the layer rules |
| Gameplay engineer | 1 | The parallel track (see Part 2 §2.18) |
| Technical artist | 1 | Shaders, lighting, profiling. **Load-bearing. Do not start without one.** |
| Environment artist | 1.5 | The Ribcage, and the vertical slice first |
| Designer (lead) | 1 | Already has the three design docs; authors puzzle/recipe/discovery data |
| UI/UX designer | 0.5 | The Slate, the inventory strip, the tuning band |
| Audio designer | 0.5 → 1 | Ramps up at Phase 13; needs 2 days in Phase 1 for the latency spike |
| Producer | 0.5 | Me |

**Assumed MVP duration: 18–22 weeks** to the Definition of Done, with the ±4 weeks driven almost entirely by risk 1 and risk 3. If the Phase 2 vertical slice forces the fallback art direction, add 3 weeks for the re-direction and subtract more than that later; if playtest says the hook is broken, add 2–4 weeks at Phase 6.

**Assumed full-game duration: I will not give one yet, and neither should anyone else.** The honest answer is that the full game's schedule is a function of the per-zone art cost, and that number does not exist until the Ribcage is finished and measured. The correct output of the MVP is not just a build — it is the first real number for "weeks per zone", multiplied by eight remaining zones plus the Oleander interior, which is worth two zones on its own.

---

# PART 2 — PHASE ROADMAP

Every phase ends in a playable build. "Playable" means installable on a device and touchable by a person who is not on the team, not "runs in the editor."

---

### PHASE 0 — DESIGN & DE-RISKING
**GOAL.** Convert three design documents into authored data schemas, cleared names and answered technical unknowns, so that no one is designing during production.

**DELIVERABLES**
- `ScriptableObject` schemas authored and reviewed for `ItemDefinition`, `RecipeDefinition`, `PuzzleDefinition`, `DiscoveryDefinition`, `StoryNode`, `InteractableDesc`.
- The full MVP content manifest (§1.2–§1.6) entered as data, with placeholder art references.
- Three technical spikes, each time-boxed to 3 days, each producing a written go/no-go: (a) Android audio latency + the analytic spectrum model; (b) compute-shader availability for the spectrogram ribbon across the target Android matrix, with the CPU-FFT fallback measured; (c) Unity 6 Addressables + `File.Replace` behaviour on iOS sandboxed storage.
- **Trademark and title clearance pass commissioned** on all ~34 proper nouns (bible §9 and World Structure §7 both flag this as outstanding; it is a Phase 0 gate).
- **The business-model decision, in writing, signed.** Premium paid, paid-with-free-prologue, or something else. Everything from store metadata to the end card to whether Phase 15 exists at all depends on it.
- Reference device matrix fixed and devices physically purchased.
- Art-direction target board plus the pre-agreed fallback direction (§1.9a) written down.

**NEW SYSTEMS TOUCHED.** None. This is the last phase where that is true.

**EXIT CRITERIA.** All three spikes have written verdicts. Business model signed. Clearance commissioned with a named firm and a date. Content manifest entered as data and passing a schema validator. Devices in hand.

**PLAYABLE STATE.** Nothing is playable. **This is the only phase where that is acceptable**, and it should be short.

**DEPENDENCIES.** None.

---

### PHASE 1 — PROJECT, BOOTSTRAP, MENU *(and the save spine — see the ordering critique)*
**GOAL.** Stand up the repository, the assembly architecture, the CI that enforces it, and a main menu that can start and resume a game.

**DELIVERABLES**
- Unity 6 LTS / URP project, portrait-locked, New Input System, IL2CPP, both platform build targets green.
- Assembly definitions: `Isle.Domain` (no `UnityEngine` beyond math types and `ScriptableObject`), `Isle.Presentation`, `Isle.Tests`.
- `IEventBus` with the deferred queue; `Pcg32`; all ID structs; `IItemCatalog`, `IRecipeCatalog`, `IZoneDataCatalog` interfaces.
- `TimeSystem` complete (L0). `LocalizationSystem` with the EN table (L0).
- **`SaveSystem` complete (L0)** — header, component table, LZ4, CRC32, `.tmp`/`.sav`/`.bak` rotation, `ISaveParticipant` registration, the Phase-12 tick slot. It saves exactly two components today (Time, Settings) and grows one component per system thereafter, at a cost of about a day each.
- `GameLoop` MonoBehaviour with the 13-phase tick order and the dev-build write-guard.
- Main menu: NEW GAME / CONTINUE / SETTINGS (volume, language, quality tier). The Slate Open cold-open recap scaffold.
- CI: builds both platforms, runs `Isle.Domain.Tests`, runs `Architecture_NoAssemblyReferencesUpward`, runs the Roslyn layer analyzer, runs the no-literal-strings check.

**NEW SYSTEMS TOUCHED.** TimeSystem, SaveSystem, LocalizationSystem, IEventBus, GameLoop.

**EXIT CRITERIA.** CI green on both platforms from a clean clone. `Time_TwentyFourInGameHours_EqualsFortyEightRealMinutes` passes. Save round-trip of the Time component passes. A layer violation committed deliberately fails the build.

**PLAYABLE STATE.** A player installs the app, sees the menu, taps NEW GAME, and lands in an empty grey scene with a working in-game clock they can watch advance. They quit, relaunch, tap CONTINUE, and the clock resumes at the right time. That is genuinely all — and it is the most valuable phase in the project, because everything after it inherits the architecture.

**DEPENDENCIES.** Phase 0.

---

### PHASE 2 — PLAYER, CAMERA, INTERACTION, THE BEACH
**GOAL.** Make the Ribcage a place a person can stand in, walk around, and touch, at final intended fidelity in one 30 m stretch.

**DELIVERABLES**
- Tap-to-move locomotion with real animated transitions (the 41-year-old-beaten-by-a-reef stand, the four-second sit). Drag-to-look. No virtual stick.
- `InteractionSystem`: candidate arbitration with the 0.05 hysteresis term, all six verbs, the inspect view with free rotation and back-face detail discovery.
- `WorldSystem` (L1): one zone, object persistent state, world flags, the gully chokepoint.
- `WeatherSystem` **stub** implementing the full interface with constants.
- The Ribcage greybox at final scale: 320 m arc × 60 m deep, six hulls, three enterable interiors, the gully notch.
- **The vertical slice: 30 m of beach at intended final art fidelity**, profiled on both reference devices. Wet-sand shader, haze solution, overcast lighting bake, one hero wreck.
- The art go/no-go decision, made and minuted.

**NEW SYSTEMS TOUCHED.** InteractionSystem, WorldSystem, WeatherSystem (stub).

**EXIT CRITERIA.** 60 fps sustained on iPhone 12 and 30 fps on the Android reference inside the vertical slice, measured on device. Two overlapping hotspots with a thumb resting between them do not flicker (the hysteresis test passes). Every interaction reachable in the bottom 45% of the screen, verified by heatmap. Object open/taken state survives a save/load.

**PLAYABLE STATE.** The player walks the full length of the Ribcage, looks around, walks between and inside the hulls, taps objects and gets inspect views with rotatable detail. Nothing can be picked up yet. **The hull sightline works and the white surveyor's line snaps in** — the first real content beat is playable in Phase 2, deliberately, because it is the thing we most need to watch people do.

**DEPENDENCIES.** Phase 1.

---

### PHASE 3 — INVENTORY & ITEMS
**GOAL.** Give her the bag, and make 18 objects real.

**DELIVERABLES**
- `InventorySystem` complete: 24-slot dry bag, dual mass/slot limits, stacking rules including wetness-max merge, atomic removal with total ordering, reserved critical slots, partial-accept with the non-despawning pickup.
- `ItemSystem` complete: catalog bake, wetness, corrosion clock, charge drain, condition evaluation.
- All 18 items authored with final IDs, masses, stack sizes, flags, and placeholder-but-recognisable meshes.
- The bottom-edge inventory strip UI: photographic 3D chips, contextual summon, swipe-for-more, the 4° lean combinability affordance.
- Hold-to-harvest with the 0.6 s arc, and the wet/dry audio distinction (click vs thud).
- The dry-bag open sequence.

**NEW SYSTEMS TOUCHED.** InventorySystem, ItemSystem.

**EXIT CRITERIA.** The full §20 inventory and item test subsets pass, including `Inventory_AddWhenOverMassLimit_AcceptsPartialAndReportsOverflowQuantity` and `Inventory_StackTwoWetnessStates_ResultTakesMaxWetnessNeverMin`. The 500-operation fuzz bot produces zero dupes and zero losses. Recorder wetness visibly climbs and the corrosion clock fires at 14 wet in-game hours.

**PLAYABLE STATE.** The player wakes on the sand, finds the orange bag, opens it, takes out five objects, walks the beach and collects wood, rope, grass, chert and copper. They fill the bag and get a real overflow that leaves the remainder in the world. They can inspect everything. They cannot yet do anything with any of it — which is, for one phase, a legitimate and quite tense state.

**DEPENDENCIES.** Phase 2 (inspect view, interaction verbs).

---

### PHASE 4 — COMBINATION, CRAFTING, DISCOVERY
**GOAL.** Make items mean something, and make the Slate the game's spine.

**DELIVERABLES**
- `CombinationSystem` complete: shape hashing, the three input roles, discovery gating, the exact 7-step commit sequence with the pre-mutation capacity dry-run, the eight typed failure lines.
- `CraftingSystem` complete: two-phase reservation, world-second timers, attended/unattended, `AwaitingCollection`.
- `DiscoverySystem` complete: bitset, `TryRevise` with chain rendering, notification queue with 2.5 s drain, 6 s same-category coalescing, narrative-audio suppression.
- The three engineering deltas from §1.1: `OutputKind.Station`, `OutputKind.InstanceRepair`, `StepKind.AlignSightline`.
- All 10 recipes and all 5 authored failures, authored as data.
- The Field Slate UI: full-screen paper spread, wet-ink handwriting reveal, three tabs, the UNRESOLVED counter, struck-through revision rendering.
- Drag-to-combine grammar: strip-to-strip and strip-to-world.

**NEW SYSTEMS TOUCHED.** CombinationSystem, CraftingSystem, DiscoverySystem, StorySystem (minimal — the ~26 Act 1 nodes).

**EXIT CRITERIA.** All combination and discovery tests pass, including `Combine_OutputWouldNotFit_AbortsBeforeRemovingInputs` and `Discovery_FourUnlocksSameCategoryWithinSixSeconds_CoalesceToSingleNotification`. A revision performed in-game survives save/load with the original still legible. Zero combinations produce a generic failure message.

**PLAYABLE STATE.** The player teases rope into fibre, cuts the drum, strikes chert on the multitool and gets sparks, fails to light dry grass and gets a Slate entry for the failure, discovers that basalt doesn't chip. The Slate fills up. The boot tag is readable and `SOMEONE` appears in PEOPLE. **They cannot yet light a fire**, because the wind-shadow discovery needs Phase 5's fire station to exist.

**DEPENDENCIES.** Phase 3.

---

### PHASE 5 — SURVIVAL & CAMP
**GOAL.** Put a clock on her body and a fire on the beach.

**DELIVERABLES**
- `SurvivalSystem`: Hydration, CoreTemp, Energy, Health with the §7.3 formulas and constants. Satiation stubbed at 100. Bands, the three-warnings rule, collapse-not-death with the inventory-drop cost and the Slate gap entry.
- Hidden-by-default survival HUD: a stat appears only at `Low` or worse and hides 20 s after recovery. The thirst body-outline page in the Slate. The desaturation grade ramp and the dry-throat VO layer.
- `CampSystem`: station placement with the 3-ray ground check and 12° slope limit, four station types, fire fuel burn at 1.0/hr, the fire's warm zone as an addressable volume, the wind-shadow volume.
- `SurvivalSystem.PushPause`/`PopPause` refcounting, wired so documents and cinematics never drain a meter.
- The fire lighting sequence: the two-second ember hold, the frame turning warm, one dynamic light with baked bounce and the wet-sand mirror.

**NEW SYSTEMS TOUCHED.** SurvivalSystem, CampSystem.

**EXIT CRITERIA.** `Survival_BalanceGuardrail_ThirstToCollapseNeverUnderThirtyHours` passes in CI. `Survival_DuringDocumentRead_NoStatChangesAtAll` passes. Fire in the open blows out in 3 s; fire in a wind shadow holds. Collapse fires, drops one non-critical stack, and never shows a Game Over screen — verified by the `zero PlayerDeath transitions` validator.

**PLAYABLE STATE.** The full fire sequence is playable: spark, tinder, wind failure, wind shadow, fire, warm zone, drying the recorder and the wet wood. The player gets thirsty and the screen desaturates. They can build a windbreak and a drying rack. **They still cannot make water** — the Catchment is Phase 6. This is a deliberately uncomfortable playable state and it is the correct one to playtest.

**DEPENDENCIES.** Phase 4 (recipes produce stations), Phase 1 (TimeSystem), Phase 2 (WeatherSystem stub supplies ambient temperature).

---

### PHASE 6 — ENVIRONMENTAL PUZZLES · **MVP CONTENT COMPLETE**
**GOAL.** Ship the Catchment puzzle, the radio, and the signal — closing the MVP loop.

**DELIVERABLES**
- `PuzzleSystem`: state machine, declarative `PuzzleRequirement`/`PuzzleStep` authoring, the six MVP `StepKind`s, checkpointed ordered steps, mid-puzzle save with quantised dial values, the 4-rung hint ladder with request-gated rungs 2 and 3.
- `pzl.the_wrong_wave` fully authored per §1.6, plus the Catchment station with its live litres-per-day readout on the placement ghost.
- `pzl.hull_sightline` using `AlignSightline`.
- `pzl.radio_repair` — three faults as `PlaceItem` steps plus `rcp.scrape_contacts`, with the fault-marking-by-darkening visual and per-fault independent hint timers.
- `pzl.radio_tune` — the tuning band UI (printed dial strip with inertia and detent ticks, right-20% fine tune), the spectrogram ribbon, the ±0.8 kHz magnetic lock, the two honest false positives, the undocumented Tier 4 auto-sweep safety net.
- `AudioSystem` MVP: the analytic spectral model, `ListenMode`, the 64-bin ribbon, narrative-audio suppression, voice pooling with priority eviction.
- **The 44-second transmission**, recorded with real VO, with the open carrier intact.
- The PTT hand-mic with live device-microphone passthrough and Nadia's five escalating unanswered calls.
- The cut vine, the end card, the "Day 1 / rev. — see Day 11,591" beat.
- Two signposting tuning passes driven by the six telemetry events.

**NEW SYSTEMS TOUCHED.** PuzzleSystem, AudioSystem, QuestSystem (the single *Next:* line).

**EXIT CRITERIA.** **The full MVP Definition of Done (§1.8), all 28 items.** Plus: ten unmoderated remote playtests completed and scored on the "what do you want to know next" question.

**PLAYABLE STATE.** **The complete MVP.** Cold launch → wreck → beach → bag → hull line → resources → fire → boots → radio → repair → tune → the voice → the cut vine → end card → menu. 35 minutes, start to finish, on a phone, by a stranger.

**DEPENDENCIES.** Phases 2–5. This is the MVP boundary.

---

### PHASE 7 — JUNGLE (FERNMAW)
**GOAL.** Prove the zone pipeline by building the second zone, and prove the shortcut/hub structure by connecting it.

**DELIVERABLES**
- Fernmaw: 1.1 km of trench in three branches, canopy layer, the Split Aqueduct.
- Zone streaming: the adjacency graph, proxy LOD for adjacent zones, the chokepoint-hidden async load at the gully.
- "The Grade" puzzle — the diversion sill, the impounded water, the silt plug.
- Fold Camp greybox as the hub terminus, with the workbench, dry store and Slate board as stations.
- Two shortcuts: the Berm Stair and the Sluice Ride.
- The fern-rachis ladder and rope-traversal capability.
- The moisture-meter-on-gear pressure.

**NEW SYSTEMS TOUCHED.** WorldSystem (streaming, multi-zone), CampSystem (hub stations).

**EXIT CRITERIA.** Zone transition under 3 s on the Android reference with no frame drop below 25 fps during the load. Resident memory still under 1.4 GB with two zones plus one proxy. R8 verified: two independent routes reach Fold Camp and closing either leaves the hub reachable. **Measured weeks-per-zone recorded** — the number the whole full-game schedule depends on.

**PLAYABLE STATE.** The player completes the MVP, cuts through the gully, walks Fernmaw, solves The Grade, rides the sluice down to Fold Camp, and can walk back up. Roughly 2 hours of play. Act 1 is complete.

**DEPENDENCIES.** Phase 6.

---

### PHASE 8 — WEATHER & DAY/NIGHT
**GOAL.** Turn the environment from a constant into a simulation, and turn the tide into a schedule.

**DELIVERABLES**
- `WeatherSystem` real: the six-state Markov chain with the §14.3 matrix, 1.5 h minimum dwell, 9 h maximum, deterministic forecast, serialized RNG, the refcounted `PushOverride`, the cinematic deferral.
- The four gates only: bridge closure, Catchment yield, hydrophone noise floor, fire extinction. No fifth gate.
- Night lighting pass for the Ribcage and Fernmaw; the 15/9 light-dark split.
- The tide model, and the Ribcage's tidal cutoff with the strand-in-a-hull cost.
- `CampSystem.TryRest` with stepped `FastForward` that advances weather, crafting and survival correctly.
- Satiation turned on with the cold-hunger coupling.

**NEW SYSTEMS TOUCHED.** WeatherSystem (full), TimeSystem (tide), CampSystem (rest).

**EXIT CRITERIA.** `Weather_SameSeedAndClock_ProducesIdenticalSequence`, `Weather_ForecastSixHoursAhead_MatchesWhatActuallyOccurs`, `Weather_LoadedSave_ContinuesChainIdenticallyToUnsavedRun` and `Camp_RestEightHours_AdvancesWeatherChainNotJustClock` all pass. The in-fiction barometer reads correctly.

**PLAYABLE STATE.** Everything from Phase 7, now with weather that changes, nights that fall, tides that strand you, and a camp you can rest at. The Catchment's yield visibly varies with humidity.

**DEPENDENCIES.** Phase 7. **Parallelisable with Phase 9.**

---

### PHASE 9 — STORY, RADIO, JOURNALS
**GOAL.** Build the document, recording and narrative-delivery layer that carries 340 documents and 60 reels.

**DELIVERABLES**
- `StorySystem` full: the node DAG, event-driven eligibility caching, act quorum, the bake-time reachability validator.
- The document reader: scanned-paper background plus a **live text layer** in period fonts (the localisation-cost decision from Core Systems §17.3, locked here).
- The Reel Deck and carryable playback.
- The brass-tag maintenance-ledger system: dated objects as a navigation layer, sortable by date.
- The Slate's physical board at Fold Camp with pinned clues and string.
- Poulter, Sabo, Ferrier, Solheim, Castellar, Obuya and Ré document sets for Acts 1–2.
- The darkroom film-development minigame.

**NEW SYSTEMS TOUCHED.** StorySystem (full), AudioSystem (narrative streaming), LocalizationSystem (document text layer).

**EXIT CRITERIA.** `Story_BakeValidation_EveryActCriticalNodeIsReachableFromStart` passes. A document at 400 px portrait width with a +35% expansion dummy locale does not overflow its container. Narrative audio survives an app backgrounding and resumes at the correct sample.

**PLAYABLE STATE.** Acts 1 and 2 playable with their full document and audio layer. Roughly 5 hours.

**DEPENDENCIES.** Phase 7. **Parallelisable with Phase 8.**

---

### PHASE 10 — RUINS & CAVES
**GOAL.** Build the game's hardest zones and the capability systems that make them legible.

**DELIVERABLES**
- The Sweatways, Rime Shoulder, Ash Throat, The Combs, Substation Oleander, The Quiet Room.
- The hydrophone as a verb: directional spectrogram overlay, aim-and-sweep, the cardioid gain model, the remaining five `StepKind`s.
- The Register (Tolo Vardh notation reading), the damper console, the safe-isolation procedure.
- The remaining shortcuts: Warm Chimney, Scree Run, Service Trench, Settling Gallery Drain, Cable Riser.
- Lorvik: the one NPC, Act 5 dialogue, and the Act 5 HUD strip enforced by `Story_Act5_StripsAllSurvivalAndCraftingVerbs`.
- Both endings.

**NEW SYSTEMS TOUCHED.** PuzzleSystem (all step kinds), AudioSystem (hydrophone), a dialogue system (new), character rendering (new).

**EXIT CRITERIA.** Full critical-path bot completion in CI. All ten anti-softlock validators green over the complete content set. The Combs and Oleander both hold their frame-rate targets on device — the two zones flagged as unprototyped in World Structure §7.

**PLAYABLE STATE.** The complete game, start to either ending, ~13 hours golden path.

**DEPENDENCIES.** Phases 8 and 9.

---

### PHASE 11 — SAVE *(as briefed — see the ordering critique; this phase should not exist)*
**GOAL, as briefed.** Add persistence across all systems.
**GOAL, as recommended.** This phase becomes **SAVE HARDENING**: the format is already shipping since Phase 1, so Phase 11 is the adversarial pass, not the build.

**DELIVERABLES (hardening version)**
- The kill-during-write harness, 500 automated iterations across both platforms.
- Schema-version migration tests: every participant reads a v1 blob at vN.
- `Save_IncompatibleComponent_ResetsOnlyThatComponentAndReportsPartial` verified by deliberate corruption of each of the 14 components in turn.
- Out-of-space, permission-denied and read-only-storage paths.
- The five-deep autosave ring reaching back across at least one full zone (R9).

**EXIT CRITERIA.** Zero unrecoverable saves in 500 kill-during-write iterations. Every component independently corruptible without losing the save.

**PLAYABLE STATE.** Unchanged from Phase 10, but you can no longer lose your progress.

**DEPENDENCIES.** Phase 10. **Parallelisable with Phase 12 and 13.**

---

### PHASE 12 — UI POLISH & ACCESSIBILITY
**GOAL.** Make the interface survive real hands, real eyes and real languages.

**DELIVERABLES**
- Full localisation: the nine proposed launch locales, CJK dynamic SDF atlas, the per-locale build path, RTL Slate layout.
- Accessibility: subtitle size and background options; colourblind-safe spectrogram palettes (the phosphor-green ribbon is the highest-risk element in the game for deuteranopia and it must be validated, not assumed); a hold-duration multiplier; a "reduce motion" option that kills the two automatic camera moves; the ceramic-fragment style diegetic assists where they exist.
- Touch-target audit across all screens.
- The container-overflow validator running every `LocKey` in every locale.

**EXIT CRITERIA.** `Loc_GermanExpansion_AllDocumentContainersFitAtFourHundredPixelWidth` passes. `Loc_EveryKeyUsedInCodeOrData_ExistsInEnglishTable` passes. Zero touch targets under 44 pt. Spectrogram legibility validated with a colourblind simulator and with at least two colourblind testers.

**PLAYABLE STATE.** The complete game, in nine languages, playable by more people.

**DEPENDENCIES.** Phase 9 (document text layer). **Parallelisable with Phases 11 and 13.**

---

### PHASE 13 — AUDIO
**GOAL.** Deliver the final mix for a game whose subject is listening.

**DELIVERABLES**
- Final VO: Nadia's full performance, Lorvik's Act 5 dialogue, the archive voices (Sabo, Ferrier, Solheim, Poulter, Obuya, Castellar, Ré).
- Nine zone ambient beds, each with its sensory identity per the World Structure spec, and the sub-bass layer that drops out when the gates are balanced.
- The eleven-minute pressure cycle as a global, phase-persistent bed present from minute one.
- Mix snapshots, ducking, the narrative-priority voice policy.
- Score: sparse, cello-and-struck-metal, used roughly a dozen times in thirteen hours.

**EXIT CRITERIA.** `Audio_SpectralPeak_IsDeviceIndependentForIdenticalWorldState` passes across the device matrix. `Audio_VoiceStarvation_NeverEvictsNarrativePriority` passes. The four seconds of open carrier are verified present in the shipping asset. Full mix pass on phone speaker, cheap earbuds and good headphones.

**PLAYABLE STATE.** The complete game, finally sounding like itself.

**DEPENDENCIES.** Phase 10. **Parallelisable with Phases 11 and 12.**

---

### PHASE 14 — PERFORMANCE
**GOAL.** Hold 60/30 fps and the memory ceiling across the whole game on the whole device matrix.

**DELIVERABLES**
- Per-zone profiling reports on every reference device.
- The Combs' per-cell audio source collapsed to a baked composite bed (flagged in World Structure §7 as a likely early cut — this is where it happens, or where it is proven unnecessary).
- Draw-call, batching, lightmap and texture-streaming passes.
- Domain tick held under 1.2 ms; zero steady-state allocation verified.
- Thermal-throttle behaviour measured over a 45-minute continuous session.

**EXIT CRITERIA.** Every zone meets 60 fps on iPhone 12 and 30 fps on the low-end Android with 1% lows above target-minus-10. Resident memory under 1.4 GB everywhere. Build fails at 1.6 GB. Battery drain and surface temperature measured and documented.

**PLAYABLE STATE.** The complete game, at frame rate, without cooking the phone.

**DEPENDENCIES.** Phases 10–13.

---

### PHASE 15 — MONETIZATION
**GOAL.** Implement whatever the Phase 0 business decision was.

**DELIVERABLES** *(assuming the Phase 0 decision is free prologue + single unlock — if it is premium-paid, this phase shrinks to almost nothing, which is a good reason to decide early)*
- Store entitlement plumbing on both platforms, receipt validation, restore purchases.
- The paywall card at 30:00, with real price display from the store, and the static six-hulls plate behind it.
- Refund and entitlement-revocation handling.
- Zero ads, zero timers, zero currencies, zero daily rewards. This is enforced by there being no system capable of them.

**EXIT CRITERIA.** Purchase, restore, and revoke all verified on both platforms in sandbox. An entitlement failure never blocks access to already-unlocked content offline.

**PLAYABLE STATE.** The complete commercial product.

**DEPENDENCIES.** Phase 0's signed decision; Phase 14.

**Unverified assumption:** I have made no claims about either platform's current IAP requirements, review criteria or entitlement APIs, because I have not verified them. This phase's real content is defined by whatever the Phase 0 consultant engagement establishes.

---

### PHASE 16 — STORE PREP
**GOAL.** Ship.

**DELIVERABLES**
- Store listings, screenshots, trailer, icon, ratings questionnaires for every territory.
- Privacy policy and the platform privacy disclosures, matching the six telemetry events actually collected and nothing more.
- The completed trademark and title clearance, with any forced renames executed via the loc table and content re-bake.
- Release-candidate certification passes on both platforms.
- Day-one patch process, crash reporting, support inbox, and a rollback plan.

**EXIT CRITERIA.** Both platform submissions accepted. A rollback has been rehearsed, not just documented.

**PLAYABLE STATE.** Shipped.

**DEPENDENCIES.** Phase 15.

---

## 2.17 MVP boundary and parallelisation

| Phase | MVP? | Second engineer can run in parallel with… |
|---|---|---|
| 0 Design | **MVP** | n/a — but the three spikes are three parallel tracks |
| 1 Bootstrap | **MVP** | No. One architect writes the layer rules. |
| 2 Player/camera/beach | **MVP** | Yes — E2 takes `WorldSystem` + streaming groundwork while E1 takes interaction/locomotion |
| 3 Inventory/items | **MVP** | Yes — E2 takes `ItemSystem` while E1 takes `InventorySystem`; they meet at the catalog interface |
| 4 Combination/crafting/discovery | **MVP** | Yes — E2 takes `CraftingSystem` + the Slate UI while E1 takes `CombinationSystem` + `DiscoverySystem` |
| 5 Survival/camp | **MVP** | Yes — E2 takes `CampSystem` + stations while E1 takes `SurvivalSystem` |
| **6 Puzzles/radio/signal** | **MVP — BOUNDARY** | Yes — E2 takes `AudioSystem` + the tuning UI while E1 takes `PuzzleSystem` |
| 7 Jungle | post-MVP | Yes — E2 takes streaming while E1 takes the Grade puzzle |
| 8 Weather/day-night | post-MVP | **Parallel with Phase 9**, different engineers |
| 9 Story/radio/journals | post-MVP | **Parallel with Phase 8** |
| 10 Ruins/caves | post-MVP | Yes, heavily — this phase needs three engineers |
| 11 Save hardening | post-MVP | **Parallel with 12 and 13** |
| 12 UI/accessibility | post-MVP | **Parallel with 11 and 13** |
| 13 Audio | post-MVP | **Parallel with 11 and 12** |
| 14 Performance | post-MVP | No. Serialising here is the point. |
| 15 Monetization | post-MVP | Yes, with 14 |
| 16 Store prep | post-MVP | No. |

---

## 2.18 ORDERING CRITIQUE — where the brief's phase order is wrong

Four genuine problems. I am going to be direct about all four.

### Problem 1 — Save at Phase 11 is the most expensive mistake in the plan. Fix it in Phase 1.

Ten phases of systems written without `ISaveParticipant` means ten systems whose owned state is scattered across MonoBehaviours, whose mutation points are undisciplined, and whose "state" is partly implicit in scene-object positions. Retrofitting persistence into that is not "adding a save system" — it is re-deriving what every system actually owns, which is a re-architecture wearing a feature's clothes. On projects I have seen do this, it costs six to ten weeks and it reintroduces bugs in every system it touches.

**It is also fatal to the MVP specifically.** The MVP's Definition of Done requires resume-from-any-point, kill-during-write recovery and a 40-minute-background test. As the brief orders the phases, the MVP is not shippable until Phase 11.

**Recommendation:** `SaveSystem` lands complete in **Phase 1**, saving two components. Every subsequent phase adds its component in the same PR that adds its system — about one day each, which is roughly free, and which forces every system to answer "what do you own?" on the day it is written. Phase 11 survives as **Save Hardening**: the adversarial corruption, migration and out-of-space pass, which genuinely does belong late because it needs the full component set. Same total work, a tenth of the pain, and the MVP becomes shippable at Phase 6 as intended.

### Problem 2 — The MVP as specified cannot be built inside phases 0–6 as ordered.

The MVP requires the radio, the transmission and the Field Slate's document/audio layer — which the brief puts in **Phase 9 (story/radio/journals)** — and it requires save, which the brief puts in **Phase 11**. So the brief's own MVP definition straddles phases 0–11, and the phrase "MVP boundary" has no meaning as ordered.

**Recommendation:** pull the *slice* of Phase 9 that the MVP needs forward into Phase 6 — one radio object, one 44-second transmission, one frequency list, the Slate's three tabs — and leave Phase 9 as what it actually is: the *scalable* document and reel pipeline for 340 documents and 60 tapes. That is a different job from playing one WAV file, and conflating them is why the boundary blurred. With that and the save fix, **Phase 6 is a clean, defensible MVP boundary.**

### Problem 3 — Audio at Phase 13 contradicts the game's thesis, and one spike is not enough.

The signature verb is listening. The MVP's climax is a frequency-matching puzzle read off a live spectrogram. Putting audio at Phase 13 means the single most differentiating mechanic in the product is the last thing validated.

To be fair to the brief: Phase 13 is clearly meant as the *final mix and VO* phase, and that genuinely belongs late. The problem is that there is no earlier audio phase at all.

**Recommendation:** audio splits in two. **Phase 0** carries the Android latency and spectrum-model spike (already in my Phase 0 deliverables). **Phase 6** carries the analytic `AudioSystem`, the spectrogram ribbon and the real transmission VO. **Phase 13** keeps final VO, ambient beds, mix and score. Do not let the phase-13 label defer the mechanic.

### Problem 4 — Monetization at Phase 15 is fifteen phases too late for the *decision*.

The *implementation* belongs at 15. The **decision** — premium versus free-prologue-plus-unlock — determines whether the game has a paywall card at 30:00, whether the prologue must be a self-contained satisfying artefact, whether store metadata needs two SKUs, and whether the MVP is a demo or a product. All of that is settled in the first six phases. Deciding at Phase 15 means either guessing in Phase 6 or rebuilding in Phase 15.

**Recommendation:** the business-model decision is a **Phase 0 exit gate**, signed, in writing. Phase 15 implements it. If the answer is "premium paid," Phase 15 becomes two days of work and we should be glad.

### Recommended revised order

```
0  Design + 3 spikes + clearance commissioned + BUSINESS MODEL SIGNED
1  Bootstrap + TimeSystem + LOCALIZATION + SAVE SPINE + menu
2  Player / camera / interaction / beach + VERTICAL SLICE ART GO-NO-GO
3  Inventory / items              (+ save component)
4  Combination / crafting / discovery   (+ save components)
5  Survival / camp                (+ save components)
6  Puzzles + AUDIO CORE + RADIO SLICE + signal   ◄── MVP BOUNDARY
   ── PLAYTEST GATE: 10 sessions, "what do you want to know next?" ──
7  Jungle + streaming             ◄── first real weeks-per-zone number
8  Weather / day-night / tide  ∥  9  Story / documents / reels pipeline
10 Ruins / caves / Oleander / Quiet Room / Lorvik / endings
11 Save HARDENING  ∥  12 UI + accessibility + loc  ∥  13 Final audio + VO
14 Performance
15 Monetization implementation
16 Store prep
```

Four changes, all cheap to make now and all expensive to make later. The one I would fight hardest for is the save spine in Phase 1 — it is the difference between a Phase 11 that costs a week and a Phase 11 that costs two months.

---

## 2.19 Unverified assumptions in this document

Stated plainly so nobody treats them as verified fact:

- All headcounts, week counts and phase durations are planning assumptions derived from experience with comparable projects, not measurements of this team or this codebase. There is no codebase.
- The 60/30 fps, 1.4 GB, 1.2 ms and 8 ms budgets are inherited from Core Systems v1.0, which flags them as unvalidated targets. They remain unvalidated here.
- No claim is made about Unity 6 LTS API behaviour, Addressables semantics, iOS or Android storage behaviour, platform IAP requirements, store review processes, or age-rating criteria. Phase 0's spikes and consultant engagements exist specifically to replace these unknowns with verified answers.
- "The Forgotten Isle" and every proper noun inherited from the Story Bible and World Structure documents carry those documents' **outstanding trademark/title clearance flag**. Nothing here has been searched. Clearance is a Phase 0 gate and a Phase 16 re-verification.
- The acoustic, hydrological and tidal premises remain dramatised fiction per Canon Rule 10, and no public realism claim should be made without the consultant reviews those documents call for.