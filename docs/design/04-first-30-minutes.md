# VARDHOLM
## Prologue Shooting Script — "THE RIBCAGE"
### Minutes 0:00 → 30:00, cold launch to paywall
**Document owner:** Lead Design · **Status:** v1.0, implementation-ready · **Canon:** Story Bible v1.0

---

## 0. DESIGN CONTRACT FOR THIS 30 MINUTES

This is the free prologue. It has exactly one job: **convert the player from "another survival crafting game" to "I need to know what this island is."** Everything below is subordinate to that.

**Five load-bearing rules, enforced in review:**

1. **No tutorial text panels. Ever.** Every control is taught by an object that physically requires it. Where a glyph must appear, it appears *on the object*, not in a corner box, and it fades after two successful uses.
2. **Never hard-block, never nag.** There is no locked door in the prologue. Every gate is a *physical* obstacle whose solution exists in three redundant forms. Hints are diegetic and arrive on a timer, and the timer resets on any meaningful player action.
3. **Survival meters are a pressure, not a fail state.** Thirst exists. It never kills in the prologue. At its worst it desaturates the image and adds a dry-throat rasp to Nadia's VO. There is no death in the first 30 minutes except one scripted fall that is a cutscene.
4. **The signature verb is listening, and the prologue teaches the ear before it teaches the hands.** The very first interaction in the game is an audio one (0:35). The hydrophone itself is Act 2, but its *grammar* — aim, sweep, isolate — is rehearsed with unpowered objects here.
5. **Portrait, one thumb.** Every interaction in the prologue is performable with the right thumb inside the bottom 45% of a 6.1" screen. The one exception is the radio tuning dial (two-thumb optional, one-thumb sufficient).

**The three hard beats, called out up front:**

| Requirement | Where it lands | What it is |
|---|---|---|
| **Genuine DISCOVERY inside 3 min** | **2:40** | The six wrecked hulls on the beach are not scattered. Nadia's own line-of-sight, using the surveyor's trick of aligning two objects, shows they are set on a single straight line. Wrecks do not queue up. Somebody parked them. |
| **Real PUZZLE inside 10 min** | **7:10 → 8:30** | **The Dry-Fire problem.** Everything on this beach is soaked. The player must reason out a heat source, a dry tinder source, and a windbreak from three unrelated objects, with no recipe list shown and no prompt naming the answer. |
| **MAJOR MYSTERY landed by 30 min** | **25:20 → 29:40** | An unknown signal on the repaired radio: a human voice, live, close, reading a maintenance log in a language the player understands, dated **today**. And it does not answer back. |

---

## 1. MINUTE-BY-MINUTE BEAT SHEET

---

### **0:00 — COLD LAUNCH / THE INCIDENT**
**"Black. Then a sound you can't place."**

**SEES.** No logo card, no menu. The app opens on **black with an audio waveform** — a single horizontal line, phosphor-green on black, drawn left to right across the middle of the portrait screen, scrolling. It looks like an instrument readout, because it is one. Over it, a timecode in small mono type: `REC 02:14:06`. The line is almost flat. Ambient hiss, a boat's diesel at 900 rpm, rigging tick.

Then at `02:14:19` the trace **swells** — a smooth, fat, low bulge with no attack transient. Not a bang. A pressure. The phone's haptic engine gives a long, soft 340 ms rumble synced to it. Nadia, off-mic, flat and professional: *"There. Same shape as before."*

The waveform **whites out**. Cut to first-person, hands, a ketch's cockpit at night, rain sideways, one deck light swinging. The reef is already under them — the bilge alarm is already screaming. Weir's voice from forward, one line only: *"Get the bag, get off the—"* and the mast comes down across the frame.

Underwater. Muffled 200 Hz roar, the specific sound of a hull dying. The dry bag drifts past the camera, orange, three metres up and rising. **The screen shows nothing. No prompt. No icon.**

**DOES.** Nothing is required for four seconds. Then the player instinctively swipes up toward the bag — and *any* upward drag on the screen works, at any speed, from anywhere. Nadia's arm enters frame and closes on the strap. This is the first and only unprompted input in the game, and it cannot be failed: at 7 seconds her arm reaches for it on its own.

**TAUGHT.** Swipe = move/reach. Nothing else. We establish immediately that the game responds to intent, not to buttons.

**REVEALED.** The signal exists, she recorded it once before, and she recognised it. The wreck is a reef, at night, in rain — no mystery, no monster. Weir is forward when the mast comes down.

**FAILURE.** Impossible to fail. If the player does nothing at all for the full 7 s, the assisted grab plays and no one is told they missed anything. If the player mashes, the grab simply triggers early. **Do not** gate on input here — a failed first interaction in a free prologue is a churn event.

**TECH NOTE.** This whole beat is real-time in-engine at a locked 30 fps with heavy motion blur to hide the loading of Ribcage streaming volumes behind it. Budget: 42 s of content masking up to 18 s of load on low-end.

---

### **0:42 — THE TITLE**
**SEES.** Black. Two seconds of total silence — no room tone, nothing. Then, close: a single drip hits a hard surface. **THE FORGOTTEN ISLE.** White serif, no animation, held 1.8 s. The drip repeats once, slightly late.

**REVEALED.** Nothing yet. But the last sound of the prologue, 29 minutes from now, is this drip — the Quiet Room ceiling — and the last image of the whole game is this drip. Plant it here.

---

### **2:00 — THE PLAYER WAKES**
**"Face-down in black sand, one boot missing."**

**SEES.** Fade up from black, slowly, through a **closed eyelid** — a red-orange blur that resolves as the lids open. Camera is at ground level, cheek in wet black sand, horizon tilted 40 degrees. Grain, chromatic fringe at the edges, breathing wobble.

Light: **flat, high, sunless.** The cloud lid is directly overhead and it never breaks — establish the island's permanent overcast in the first frame of daylight so the player stops waiting for a sunset. Colour: black volcanic sand, grey-white surf, and exactly one saturated colour in the entire frame — the **orange dry bag**, ten metres up the beach, half-buried.

Audio: surf at a low, wide 60–90 Hz rumble; wind; and under it, barely, a sound the player will not consciously register — **a slow eleven-minute pressure cycle**, mixed at –38 dBFS as a sub-bass breathing. It is in the mix from this second onward. Nobody mentions it for two hours of play.

**DOES.** *(Re-authored for ADR-0008: a floating joystick is the primary locomotion; tap-to-move is an accessibility assist. The original beat taught movement by "there being exactly one thing worth walking to", which only works for tap-to-move.)*

Drag anywhere on the upper half to look. The camera responds before Nadia's body does — she's still on the ground. The player looks around and finds the orange bag, ten metres up the beach, and the frame is composed so that when they find it the bag sits dead ahead. **Nothing happens until the thumb lands.** The first touch on the lower half of the screen makes Nadia stand — one long, ugly, real animation; she is 41 and she has been beaten by a reef — and a stick blooms under the thumb where it landed. No stick is drawn before the thumb arrives, so there is nothing on screen to learn; the stick is *where your thumb already is*, and the first push any player makes is forward, because forward is the bag. She walks. The stick fades the moment the thumb lifts. Arriving at the bag, the prompt card appears for the first time — the game's one verb, TAKE — and tapping it is the interact lesson.

If the thumb never lands: after 40 s Nadia says *"Bag first. Everything I own is in that bag."* and the look drag alone does not move her. If it still never lands, at 90 s the accessibility assist offers itself once, as a single small line at the bottom of the frame: *tap where you want to go* — and tapping the bag then walks her to it. That path exists for the player who cannot hold a stick, and it is never shown to the player who can.

**UI SHOWN.** None until the thumb lands; then the stick, only under the thumb, only while it is down. The only affordance before that is that the bag is the only colour on the screen.

**TAUGHT.** Look (drag, upper half), move (thumb down, push, lower half), interact (the prompt card). Three gestures in one chain, with no text, and the stick is never a thing the player has to find.

**REVEALED.** She survived. She is alone on the sand. She has her bag.

**FAILURE.** If the player walks *away* from the bag, they can. The beach is open. After 40 s of not approaching it, Nadia says, without irritation: *"Bag first. Everything I own is in that bag."* (the same line serves the thumb that never landed, above). After 90 s, the surf pushes the bag two metres closer and a wave slaps it — a movement cue in peripheral vision. There is no third nag. The player cannot leave the Ribcage without the bag because the *gully* (the exit) requires the multitool, so the world holds the line, not the UI.

*(Shipped state: the run starts with the multitool and the recorder already in the tray and no bag on the sand; the joystick and look pad are in, the stand animation and the 40 s / 90 s cues are not. See `CURRENT_STATE.md`.)*

---

### **2:20 — THE BAG / THE INVENTORY IS A THING, NOT A MENU**
**SEES.** She kneels. First-person, close. The bag's roll-top is crusted with salt. She works it open with her thumbs — a real 2.5 s animation with the correct squeaky-vinyl sound.

Inside, laid on the sand as she pulls each out: **hydrophone** (dented, cable coiled), **field recorder** (screen dark, water beading under the display glass — *bad*), **multitool**, **pencil**, **the Field Slate** (wire-bound, swollen, the cover warped).

**DOES.** Tap each item to pick it up. The inventory UI is a **bottom-edge strip of four slots** that slides up from the screen's lower border only while the bag is open, then retracts. It is never permanently on screen.

**UI SHOWN.** Bottom strip, four visible slots, swipe left for more. Each item renders as a photographic 3D object on a neutral chip, not an icon.

**TAUGHT.** Inventory exists, lives at the bottom edge, and is summoned by context. Items are objects.

**REVEALED.** The recorder has **water inside the display**. Nadia, quiet: *"Nine percent, and it's wet. That's a clock."* — establishing the Act 1 goal (dry the recorder) without a quest log.

**FAILURE.** If the player closes the bag without taking everything, items stay in the bag and the bag goes on her shoulder. Nothing is lost. Re-open anytime with a two-finger tap or the bag icon that appears at the bottom-left once the bag is worn.

---

### **2:40 — 🔴 THE DISCOVERY (hard requirement: inside 3 minutes)**
**"Wrecks don't queue up."**

**SEES.** She stands, shoulders the bag, and the camera does its **one and only automatic move in the prologue**: a slow 4-second pull to a wide, as she turns to face down the beach.

And there they are. **Six wrecked hulls** along the Ribcage. A steel trawler bow. A wooden schooner's ribs. A fibreglass hull split like a fruit. A landing craft, upside down. Two more further off, dissolving into the mist. They are **half-buried, at different angles, wildly different eras** — and the composition reads at first as chaos.

Wind drops out of the mix for the duration of this beat. Just surf, and one high rigging-wire tone.

**DOES.** The player is free. Most will walk toward them. Here is the actual discovery mechanic:

Nadia stops of her own accord, and says: *"Huh."* Then: *"Stand at the stem of the first one. Line the second one up behind it."*

**This is a real, player-performed action.** The player walks to the bow of the nearest hull, turns, and *looks* down the beach. There is no minigame and no prompt. When the camera's yaw falls within **±4°** of the true alignment axis, a **thin white surveyor's line** — not a UI element, a *thought*, rendered as a 1px chalk stroke that Nadia is imagining — snaps into existence along the beach and runs through all six hulls. It holds while the player holds the angle. Release the angle and it fades.

Nadia: *"...All six. On one line. Parallel to the reef, forty metres off the break."* Beat. *"Wrecks don't queue up."*

**TAUGHT.** **Sightline alignment** — the game's foundational observation verb, which returns as the Combs' terrace alignment in Act 4 and the Quiet Room's focus in Act 5. Also teaches: *the camera is an instrument, and where you stand is a puzzle input.*

**REVEALED.** The island has been *used*. Somebody towed six hulls into a line to make a breakwater. This is the first crack in "I'm shipwrecked on an empty rock" and it lands at **2 minutes 55 seconds**.

**FAILURE.** Three redundancies, no blocking:
- The player can walk right past. The line-up is **not required** for any progression.
- If they never align it, at 6:00 Nadia says it anyway while doing something else: *"That trawler's stem is dead on the schooner's. I keep looking at it."* — and a Field Slate entry writes itself.
- If they align it from the *wrong* hull, a partial line appears through three hulls only, and Nadia says *"Three of them. Try the far end."* — a real, useful hint that rewards the attempt.
- The Slate entry that results is titled **"SIX HULLS, ONE LINE"** and is flagged **UNRESOLVED**. It stays unresolved until Act 3. Do not resolve it in the prologue.

---

### **3:30 — THE FIELD SLATE OPENS ITSELF**
**SEES.** After the wreck line, Nadia sits on her heels, pulls the swollen notebook, and writes. The **Field Slate** UI: a full-screen, paper-textured, two-page spread, portrait, with her actual handwriting appearing wet-ink in real time. Left page: the entry. Right page: a pencil sketch of the beach with six ticks on a ruled line.

**DOES.** Player reads. One tap = close. Two-finger swipe or the bottom-edge tab = open at any time.

**UI SHOWN.** The Slate. Three tabs along the fore-edge: **OBSERVED**, **PEOPLE**, **UNRESOLVED**. UNRESOLVED has a **1** on it, and that number is the entire quest system.

**TAUGHT.** The evidence system, the fact that entries can be **revised** (the word *REVISE* is greyed out on this entry — it becomes live in Act 2, and the player will notice it was there from the start).

**REVEALED.** How Nadia thinks: *"Six hulls, one line, ~40 m off the break. Deliberate? Or I'm pattern-matching because I'm concussed. Check again when the light changes."* The caveat is the character.

**FAILURE.** N/A. Skippable with one tap. If skipped, the entry is still written.

---

### **5:00 — 💧 FIRST RESOURCES**
**"Everything on this beach is soaked, including the things that are supposed to burn."**

**SEES.** The player moves up-beach to the strand line — the wrack: a metre-wide ribbon of black kelp, bleached driftwood, plastic crate fragments, a coil of **blue polypropylene rope** sun-rotted to felt, a **fibreglass hull panel**, a nest of **dry grass** caught high in the rocks above the tide line where the sun has got at it, and **a dead cormorant**, which Nadia does not comment on and which cannot be picked up. (We do not do a "eat the bird" beat. This is not that game.)

Light shifts marginally — the cloud lid thins for 20 seconds, a pale grey brightening, then closes. Audio: the wrack crackles underfoot. The pressure cycle breathes once.

**DOES.** Gathering is a **hold-to-harvest**: press and hold on a resource node, a thin arc fills around the thumb in 0.6 s, item goes to the bottom strip with a physical "into the bag" sound. No harvest counters, no particle burst, no "+1 WOOD" floater.

The player will collect, in any order: **driftwood (dry)**, **driftwood (wet)** — visually distinct, the wet is darker and the hold-harvest sound is a dull thud vs. a click — **dry grass**, **poly rope**, **kelp**, **fibreglass panel**.

**TAUGHT.** Hold-to-harvest. **And, critically, the wet/dry distinction, taught by sound and colour, never by a label.** This is the seed of the 8:00 puzzle.

**REVEALED.** The fibreglass panel is bright **safety orange** and has stencilled lettering on the inside face, partly abraded: `...ELL HEAD 4` . Nadia turns it over, reads it, says nothing, and puts it in the bag. **Do not explain this.** (It is a Wellhead-era boat panel. It pays off in Act 3. A player who screenshots it and posts it is doing our marketing.)

**FAILURE.** There is more of every resource than needed, respawning nothing but distributed along 300 m of beach. If the player over-collects, the bag has 12 slots and shows a gentle "full" bump. If the player collects *nothing* and walks on, the fire beat's hint tier 1 fires and Nadia detours to the wrack herself on the way.

---

### **6:10 — THIRST ARRIVES, DIEGETICALLY**
**SEES.** No meter appears. Instead: Nadia coughs, once, wrong. Then the **screen's colour grade desaturates by 8%** over 90 seconds and a faint dry-mouth click enters her breathing loop.

**DOES.** If the player opens the Slate, a new page has appeared: a hand-drawn body outline with a water-drop mark and a time. That is the thirst meter. It lives **in the notebook**, not on the HUD.

**TAUGHT.** Survival state is real, is tracked, and is legible on demand — but it is never a bar in the corner of a cinematic frame.

**REVEALED.** She's rationing nothing, because she has nothing.

**FAILURE.** **Thirst cannot kill in the prologue.** It hard-floors at 20% and holds. At floor, the grade sits at –22% saturation and VO gets a rasp. The moment the player drinks at 23:00, it resets with an audible, satisfying swallow and the colour comes back in one second — which is the entire emotional point of that beat.

---

### **7:10 — 🧩 THE PUZZLE (hard requirement: real reasoning, inside 10 minutes)**
**"THE DRY-FIRE PROBLEM"**

This is the first real puzzle in the game and it must be solvable by thinking, not by reading.

**SEES.** The light is going — not to night, but to the flat blue-grey of an overcast dusk. Temperature is now *visible*: her breath, and the wet fabric of her jacket clinging. Nadia: *"Wet, cold and forty-one. Pick two."* The surf is louder. The wind has come up and is now a *problem* — you can see the dry grass in her hand streaming sideways.

She needs fire. She has: dry grass, wet driftwood, dry driftwood, poly rope, a multitool, a fibreglass panel, and — in the bag from the boat — **nothing that makes a flame**. No lighter. No matches. This is checked and stated: she pats the bag, finds nothing, and says *"No. Of course not."*

**THE PROBLEM, stated by her once and never again:**
> *"Spark, tinder, shelter. I'm missing all three."*

**THE THREE SUB-PROBLEMS AND THEIR REAL SOLUTIONS:**

**(a) SPARK.** The multitool has a hardened steel back-spine. Somewhere on the beach there is a scatter of **dark, glassy volcanic rock** — and one specific rock type on the strand line, pale and banded, is **chert**, washed out of a sediment layer. The player must notice that **one rock breaks differently**: tapping any rock with the multitool gives a dull knock; tapping the chert gives a **bright ring and a visible chip**. That audio difference is the whole clue. Strike chert against the multitool spine = sparks.
- *How the player could figure it out with no hint:* Nadia's line at the wrack — *"Basalt. Basalt. That's not basalt."* — fires **only if the player has walked within 3 m of the chert** at any point. It is an observation, not a directive.

**(b) TINDER.** Dry grass alone flares and dies — and the game **lets you do it**. Burn the grass, watch it go out, no penalty, no scolding. The actual answer: the **poly rope** is sun-rotted to felt; teasing it apart with the multitool makes a fibrous bird's nest that holds an ember. The rope is **plastic**, so it catches from a spark far more readily than grass, and it *stinks* — Nadia coughs and says *"God. That's going to taste like that for a week."* Correct solutions in this game are allowed to be ugly.
- The player reasons this out because they have physically *held* both and the rope's inspect view shows it **crumbling in her fingers**.

**(c) SHELTER.** The wind is killing every attempt. Placing a fire anywhere open = it lights, streams sideways for 3 seconds, and blows out with a sound like a slap. The answer is **the hulls**. The overturned landing craft's leeward side is a wind shadow, and the player can *see* it — the spindrift and blown sand visibly break around it, a VFX cue running from minute 5. Build there and it holds.
- Alternate valid solution: wedge the **fibreglass panel** upright in the sand as a windbreak. This also works. **Two solutions, both authored, both fully supported.** The panel solution gives a slightly worse fire (smaller radius, shorter burn) and Nadia notes it: *"It'll do. It'll do badly."*

**DOES.** No crafting menu is opened by the game. The player **drags one item from the bottom strip onto another item in the bottom strip, or onto the world.** That is the entire combination grammar. Drag rope onto multitool → teased fibre. Drag chert onto multitool → she holds both, and now a **strike** gesture (a short downward swipe on the screen) throws sparks. Drag wood onto the sheltered ground → a stack forms.

**UI SHOWN.** Only the bottom strip, and — the single concession — when two items in the strip *can* combine, their chips **lean toward each other by 4°**. No glow, no plus sign, no green tick. Players find this within two attempts and it is the only combinability affordance in the entire game.

**TAUGHT.** Item combination by direct manipulation; the wet/dry material property; that the world's physics (wind, shelter) is a puzzle input; and — most importantly — **that this game will let you be wrong without punishing you.**

**REVEALED.** Nothing story-side. This beat is pure mechanics, deliberately, so that the fire at 8:00 lands as relief and the object at 12:00 lands as a shock.

**FAILURE — the full escalation, all diegetic:**

| Trigger | Tier | What happens |
|---|---|---|
| 150 s with no successful spark | 1 | Nadia, to herself, turning the multitool over: *"Steel spine. So I need something harder than steel and dumber than me."* Camera does not move. |
| 240 s | 2 | She idles, and while idling **flicks the multitool spine against a rock at her feet** — the dull knock plays. The player now knows the test exists. |
| 360 s | 3 | She walks, unprompted, to the nearest chert, picks it up, and says *"There. Banded. That'll chip."* The item is now in the bag. **She never does step two for you.** |
| 150 s with spark but no tinder catching | 1 | *"Grass is too quick. I need something that'll sulk."* |
| 240 s | 2 | The rope, in the bottom strip, visibly **sheds a fibre** on a 6 s loop. |
| 360 s | 3 | She tears a handful of rope apart on her own and holds it up. Player still has to place and strike. |
| 3 consecutive blow-outs in the open | 1 | *"It's the wind. It's not the tinder, it's the wind."* |
| 5 blow-outs | 2 | Camera's depth-of-field **racks to the landing craft's lee** for 1.2 s and back. No line. |
| 7 blow-outs | 3 | She picks up the fire kit and walks to the lee herself, and sets it down. Player lights it. |

**The invariant: the game will eventually do everything for the player except the final input.** The last gesture is always theirs, so the fire is always theirs.

---

### **8:00 — 🔥 FIRE**
**SEES.** The spark catches the rope fibre. A 2-second hold where it might not take — real, animated, with the ember creeping. Then it goes.

And the **entire frame changes**. This is the first warm colour in the game since the orange bag. Firelight on wet black sand is spectacular and we spend our lighting budget here: one dynamic point light, baked bounce, and a shader that makes the wet sand *mirror* it. The hull above her becomes a ceiling of orange and rust.

Audio: the wind is still audible but now *outside*. A pocket of quiet. The score enters for the first time in the game — a single sustained cello note and a struck metal string, 11 seconds, then gone.

Nadia sits down. She has been standing for eight minutes and the animation of her sitting is 4 seconds long and we do not cut away from it.

She takes out the field recorder, opens its battery door with the multitool, and lays the cells and the body in the fire's warm zone on a flat stone. *"Warm. Not hot. Don't cook it."*

**DOES.** Player drags the recorder onto the **warm zone** — a subtly shimmering patch of air beside the fire, visible as heat haze. Drop it in the flame instead and Nadia stops her own hand: *"No."* The mistake is impossible, but the player gets to feel like they avoided it.

**TAUGHT.** Fire as a *station* — it has zones (flame, warm, light radius) and objects interact with the zones. This is the first appearance of the "place an object in a field of influence" grammar that the damper console in Act 4 is built on.

**REVEALED.** The recorder is drying. The 9% battery is a real, tracked number, and the player will be asked to spend it at **25:20**.

**FAILURE.** If the player never builds a fire, the game does **not** stop. At 11:00 the beat at 12:00 fires anyway, in the cold, in the dark, with a much worse-looking scene — and Nadia is audibly colder. The prologue completes. We lose the money shot; we do not lose the player. (Telemetry flag: `PROLOGUE_NO_FIRE` — if this exceeds 4% of sessions, the chert placement is wrong, not the player.)

---

### **9:30 — THE SLATE, SECOND ENTRY / FIRST QUIET MOMENT**
**SEES.** Firelight. She writes. Rain starts — not a storm, a steady fine drizzle hissing on the hull above her, which she is dry under. This is the prologue's only comfortable moment and it lasts 40 seconds.

**DOES.** Optional. If the player opens the Slate, there is a page that is **not an entry** — it is a list of nine names in her handwriting, crossed-out, rewritten, crossed out again. The names of the crew lost eighteen months ago. It has no tab, no title, and tapping it does nothing.

**TAUGHT.** Nothing. This is character.

**REVEALED.** Her wound, shown not told, in an optional object. The player who finds it understands the whole game. The player who misses it loses nothing — it returns as a *required* read in Act 5.

**FAILURE.** N/A.

---

### **12:00 — 🕯️ THE FIRST ABANDONED OBJECT**
**"Someone paired their boots and never came back for them."**

**SEES.** The rain stops. Nadia stands to piss or to stretch — we cut before either — and walks twenty metres along the hulls to get out of the smoke. The camera is handheld-close. Her torch is not yet a thing; she is lit by the fire behind her, so everything ahead is silhouette and the light *falls off*.

And at the edge of the firelight, under the overhang of the trawler's flank, out of the rain: **a pair of rubber boots, placed side by side, toes aligned, facing the hull.**

Not scattered. Not floating in. **Paired.** Size 44. Grey-green, perished at the flex crease, one heel worn far more than the other. Sand has drifted over the toes. A brass tag on a wire is looped around the left boot's pull-strap.

Audio: total silence except drips off the hull and, for the first time since minute 2, the **eleven-minute pressure cycle audibly swells** — the player may or may not consciously hear it. Mix it up to –28 dBFS on this beat only, then back down. We are training an ear.

**DOES.** Tap the boots → **inspect view**: the object lifts into a close, rotatable 3D view against a darkened background. Drag to rotate. This is the first inspect and it is taught by the object being irresistible.

Rotate to find the **brass tag**. Stamped, not engraved:

> `LANTERN 4 — RE-WICKED`
> `12 · 04 · 09`

**TAUGHT.** Object inspection (rotate, find the detail on the reverse). **This is the grammar the entire Act 3 maintenance-ledger system runs on: the information is always on the back.**

**REVEALED — and this is the pivot of the prologue:**

Nadia, reading it aloud, and the delivery must be flat, not shocked:
> *"Twelve, oh-four, oh-nine."* Pause. *"That's not a serial number. That's a date."*
> Longer pause. The drip.
> *"...That's a date, and it's the day-month format, and it's within the last few years."*

She looks up. The boots. The overhang. The fact that they are **dry**.

> *"Who re-wicks a lantern on an island with nobody on it?"*

**SLATE:** Entry auto-writes: **"BOOTS, PAIRED. BRASS TAG."** Tab **PEOPLE** appears for the first time, containing a single card: an empty silhouette, captioned only **`SOMEONE`**. Tab **UNRESOLVED** ticks to **2**.

**FAILURE.** The boots are placed on the **only** route out of the Ribcage — the beach narrows to a single gravel throat at the trawler and the boots are in it. The player cannot miss them without deliberately refusing to leave. If the player walks past without tapping: at 8 metres, Nadia stops walking on her own and turns her head toward them. If they still walk on, at the next loading beat she says *"I'm going back for those boots"* and the player is free to ignore her; the tag is then found at 15:00 *on the radio's own tag* instead, and the beat replays there with different staging. **No content is ever locked behind an optional inspect.**

---

### **15:00 — 📻 THE RADIO IS FOUND**
**SEES.** Past the boots, inside the trawler's flank, there is a **hole cut in the steel** — cut, not torn. Oxy-acetylene, and the slag beads are still sharp, meaning it was cut in the last decade, not in 1985. Nadia runs a thumb along it: *"Somebody made a door."*

Inside: a dry space, three metres across. Sand floor swept — actually swept, with a broom-line arc visible. Against the bulkhead: a **plastic crate**, upside down, used as a table. On it, under a folded square of canvas:

**A radio.** A 1970s-era portable VHF/HF marine set — cream-and-black, phenolic knobs, an analogue dial behind a scratched perspex window, a hinged carry handle, a coiled hand-mic on a curly cord. It is **clean**. Dust-free. Somebody wiped it.

There is a **brass tag wired to its handle**. And beside it, a **second brass tag** on a nail, and a third, and a fourth — a row of eleven tags on eleven nails, hung like keys. All stamped with dates. The most recent is **within the last three weeks.**

Light: the player's only light is the fire, 20 m back, spilling through the cut hole. It is dim and it is *directional* and the player will naturally use the doorway light like a torch beam. This is deliberate staging — it teaches light-as-resource before the game gives a torch in Act 2.

Audio: inside the hull the surf is gone, replaced by a resonant steel hum. The pressure cycle is *very* audible in here — the hull is a drum. Nadia notices: *"There's a note in this hull. Two-forty, maybe. Steady."* First direct statement of the game's actual subject, delivered as a passing technical remark.

**DOES.** Tap radio → inspect. Rotate it. Find: the battery compartment is **empty and clean** (no corrosion — it was emptied deliberately, for storage); the fuse holder is empty; a hand-written **frequency list** is taped to the underside of the carry handle, protected from weather, in a small tidy hand.

**TAUGHT.** That inspecting an object thoroughly, on all faces, is how this game gives information. Reinforcement of the 12:00 lesson.

**REVEALED.** Someone lives here, maintains equipment, stores batteries separately (which is what you do if you're keeping gear alive for decades), and keeps a frequency list. **The person is competent and the person is current.**

**FAILURE.** The radio is the prologue's spine and is placed in the only sheltered structure on the route. If the player leaves without it, Nadia will not: at the hull's exit she stops and says *"I'm not walking away from a radio."* and picks it up herself after 30 s. She carries it. Progression is never blocked; only authorship of the moment is lost.

---

### **17:00 → 20:00 — 🔧 THE RADIO IS REPAIRED**
**"Three things wrong with it, and you have three things."**

**SEES.** She carries the radio back to the fire and sets it on the flat stone. Warm light, close macro shots of hands and components. The repair is shot like a watchmaking scene — shallow depth of field, real tool sounds, nothing sparkles.

**THE THREE FAULTS, discovered by inspection, not by a checklist:**

1. **No power.** Battery bay empty. → The **fibreglass panel** is not the answer; the answer is the **two D-cells in the boots-owner's dead hand-torch**, found on the nail row beside the tags, *or* the pack the player has been carrying since 2:20 — **the field recorder's own cells**, now warm and dry. Both work. Using the recorder's cells costs the recorder's 9% and changes a line at 25:20; **this is the prologue's one real decision and it is never flagged as one.**
2. **Corroded contacts.** Green bloom on the spring terminals. → Scrape with the multitool. A hold-and-scrub gesture; a genuinely satisfying 1.5 s of grinding audio; the bloom visibly comes away.
3. **Blown fuse.** The holder is empty because somebody removed a dead one and never had a spare. → The answer is **wire**. A strand from the poly rope will not do (plastic). The player must find conductor: the **hand-mic's curly cord** can be stripped, or — better, and the solution most players find — the **dead torch** has a copper spring. Either works. Nadia: *"That's not a fuse. That's a decision to trust the wiring."*

**DOES.** All three by direct manipulation — drag cells into the bay, hold-scrub the terminals, drag copper onto the fuse holder. **No repair minigame. No timing bar. No QTE.** The pleasure is in the diagnosis, which the player does by rotating the object and noticing three wrong things.

**UI SHOWN.** Inspect view only. When a fault is fixed, the corresponding part of the object **stops being subtly highlighted by a rim of contrast** — we mark faults by making the surrounding area 6% darker, not by glowing the fault. Fix all three and the darkening lifts.

**TAUGHT.** Diagnostic repair: look at an object, find what's wrong, fix what's wrong. Full dress rehearsal for the Fold Camp power restoration in Act 3.

**REVEALED (story, and it's the best line in the prologue).** As she closes the battery door she finds, scratched on the *inside* of it with a knife point, not stamped — private, not official:

> `IF YOU ARE READING THIS I AM PROBABLY DEAD.`
> `THE SET WORKS. THE ISLAND DOESN'T.`
> `— T.R.`

Nadia: *"T.R."* Slate: **PEOPLE** tab now has a second card, **`T.R.`** — initials only, no face, no dates. (Canon: Tomás Ré, second-term caretaker, 1979–1989. Nothing in the prologue says so. Payoff is Act 3 at his maintained grave.)

**FAILURE.** Escalation is per-fault and runs on independent timers, all diegetic:

| Fault | 120 s | 210 s | 330 s |
|---|---|---|---|
| Power | *"It needs feeding."* | She opens the bay and holds it toward the firelight so the empty contacts are unmissable. | She takes the D-cells out of the dead torch and sets them beside the radio. Player must still seat them. |
| Contacts | *"Green. That's not dirt, that's a bad connection."* | The multitool chip in the bottom strip leans 4° toward the radio. | She scrapes *one* of the two terminals and stops. Player does the other. |
| Fuse | *"Open circuit somewhere."* | Inspect view auto-rotates once to the fuse holder face and stops there. | She holds the torch's copper spring up to the firelight, turning it. Does not fit it. |

If all three stall past 330 s, at **22:00** she completes the repair herself in a 12-second montage and says, with real self-disgust, *"I used to be good at this."* **The prologue continues at full quality.** A player who could not do the puzzle still gets the signal, still gets the hook, and still hits the paywall wanting more. This is non-negotiable.

---

### **20:00 — IT POWERS UP**
**SEES.** A **click** — a real, heavy, phenolic toggle click, recorded from a real 1970s set. The dial lamp comes up **amber** behind the perspex, and it is the second warm colour in the game.

Then: **hiss.** Wide-band white noise, loud, filling the whole stereo field. The needle sits wherever it was left — mid-band, on nothing.

Nadia lets out a breath she has been holding for eighteen minutes of gameplay.

**TAUGHT.** Nothing new. This is a reward beat. Hold on her face (reflected in the perspex — we never show her face directly in the prologue except as reflections; establish this).

---

### **21:00 — THE TUNING UI IS TAUGHT BY WANTING**
**SEES.** The hiss is *unpleasant*. The player will want to change it. That is the entire tutorial.

**DOES.** **Tap the dial → the tuning UI opens.** It is a full-width horizontal band at the bottom third of the screen: a linear frequency scale rendered as the radio's own printed dial strip, with a hairline needle. **Drag the strip with one thumb** — it has physical inertia and friction, and it *ticks* mechanically. A second, smaller knob to the right is **fine tune**, engaged by dragging on the right 20% of the band — 10× resolution.

Above the strip, a **live spectrogram ribbon**, 40 px tall, phosphor-green on black. Noise is grass. A signal is a vertical line. **This is the same visual language as the game's opening frame at 0:00 and as the Act 2 hydrophone overlay.** The player learns to read a spectrogram in the prologue and uses it for twenty hours.

**TAUGHT.** Analogue tuning, fine tune, and — critically — **read the spectrogram, not just the audio.** A player can find the signal by ear or by eye and both are supported.

**FAILURE.** N/A. There is nothing to fail here; it is exploration of an instrument.

---

### **22:00 → 25:20 — 📡 THE RADIO PUZZLE**
*(Full spec in §3 below. Summarised here as a beat.)*

**SEES.** The player sweeps the band. Mostly grass. Two false positives are authored in and both are honest:
- **8.291 MHz** — a slow, hollow, rhythmic thump, 11 minutes apart. It is not a transmission; it is the radio's own chassis picking up the island's pressure cycle through a loose gland. Nadia: *"That's not radio. That's the hull. The hull is doing that."* **This is the game's thesis, hidden in a red herring.**
- **12.510 MHz** — a genuine, distant, uninterested commercial maritime weather bulletin in Portuguese, reading a forecast for a sea area **nine hundred kilometres away.** It confirms the radio works. It confirms nobody is looking for her. It ends and does not repeat for 40 minutes.

Then the frequency list under the handle, which the player has been carrying since 15:00.

**REVEALED.** See **THE SIGNAL**, below.

---

### **25:20 — 🔊 THE UNKNOWN SIGNAL**
**"It is a voice, it is live, and it is close."**

**SEES.** The needle settles. The spectrogram's grass parts around a single hard vertical line. The hiss ducks. And a **woman's voice** comes up — close-mic'd, unhurried, slightly hoarse, reading aloud in the flat cadence of somebody doing paperwork out loud because there is no one to do it to.

**The transmission (verbatim, 44 seconds):**

> *"...tide table corrected for the twelfth. Gate two, ballast at four. Gate three, ballast at four. Gate four is stiff again, I'll get to it."*
> *[paper]*
> *"Lantern seven replaced. Bridge two re-roped — new line, the hemp, not the steel. Steel's gone at the eye."*
> *[a long breath]*
> *"Day eleven thousand, five hundred and ninety-one."*
> *[pause]*
> *"No vessel."*
> *[the carrier stays open for four full seconds — she is still there, not speaking]*
> *[click. The carrier drops. Hiss returns.]*

**DOES.** The player will grab the hand-mic. It is on the cord. It is right there. They will squeeze it and talk into their phone's actual microphone if we let them — and **we let them.** The mic key is a press-and-hold on-screen button shaped like the real mic's PTT bar, and pressing it:
- opens the phone's mic (with an OS permission prompt handled gracefully; declining changes nothing mechanically),
- makes Nadia say — over whatever the player says — **"This is Nadia Vesk. I'm on the island. Please respond."**
- and produces **hiss.**

She tries four more times. Different words each time, getting less formal: *"Hello."* … *"I heard you."* … *"I know you're there."* … and last, quietly, not into the mic: *"Why aren't you answering."*

**TAUGHT.** The mic is a real verb that will matter again. And: **this game will let you do the thing you want to do, and the world will still not give you what you want.**

**REVEALED — the MAJOR MYSTERY, landed at 26:10:**

Nadia, looking at the radio, working it out on the player's behalf in real time, with the caveat that is her whole character:

> *"Eleven thousand, five hundred and ninety-one days."*
> *[she does it in her head; we hear the pencil]*
> *"That's thirty-one years. Thirty-one years and eight months."*
> *[beat]*
> *"She's on a schedule. She's reading a list. She said 'no vessel' like she says it every day."*
> *[she looks up, out through the cut hole in the hull, at the black mass of the island]*
> *"And that signal was strong. That was a local carrier. She's not far away, and she's not on a boat, and she's been here since I was ten years old."*
> *[the last line, flat]*
> *"Doing what?"*

**SLATE:** Big beat. The **PEOPLE** tab's `SOMEONE` silhouette redraws into a second card with a hand-sketched waveform where a face should be, captioned:
> **`THE VOICE`** — *Female, 60s. Local carrier. Day 11,591. Reads a maintenance list. Does not answer.*
**UNRESOLVED** ticks to **4**:
> `SIX HULLS, ONE LINE` · `WHO RE-WICKS A LANTERN` · `WHAT ARE THE GATES` · `WHY WON'T SHE ANSWER`

**FAILURE.** If the player never squeezes the mic, Nadia squeezes it herself after 45 s. If the player finds the frequency but wanders off mid-transmission, the transmission **completes anyway and is recorded to the Slate** — it can be replayed from the entry forever, because the record is never lost in this game (Canon rule 7).

---

### **27:00 — THE DECISION THAT ISN'T FLAGGED**
**SEES.** She sits back. The fire's low. She looks at the field recorder — dry now, warm, and either at 9% or at 0% depending on whose batteries went into the radio at 18:00.

**If the recorder still has its cells:** she powers it on, holds it to the radio's speaker, and **re-records the transmission**, archiving it. Slate entry gets a playable audio attachment. Her line: *"Record it. Record it properly this time."* — the entire backstory, paid off in six words, for players who were paying attention.

**If the player spent the recorder's cells on the radio:** the recorder is dead. She looks at it. Puts it down. *"I heard it. I didn't capture it."* Beat. *"I've done that before."* The transmission is in the Slate as **text only, transcribed by hand** — and the entry is flagged **TRANSCRIPT — NO AUDIO**, which is a permanent, visible scar in the evidence system for the rest of the game.

**TAUGHT.** Choices in this game are made of resources, not dialogue wheels, and they leave marks on the record.

**FAILURE.** Neither branch is a fail. Both are shipped, both are voiced, and neither is announced.

---

### **28:20 — 🌿 THE JUNGLE OPENS**
**SEES.** She needs water and she needs *her* — and the only way off this beach is the **gully** at the head of the Ribcage, which the player has seen since minute 5 as a dark green notch in the cliff.

She walks to it with the radio in one hand. The fire is behind her, guttering.

At the gully mouth the camera stops. The wall of vegetation is absolute — tree-fern and vine, black-green, dripping. The path in is a slot barely wide enough for a person, and it is **choked with vine**.

Nadia takes out the multitool, opens the blade, and reaches for the first vine.

And stops.

**Because the vine is already cut.**

Camera pushes in — a slow, 3-second, entirely uncharacteristic dolly, our second and last automatic camera move in the prologue — onto the vine stem at chest height.

A **clean, angled cut.** One stroke. A machete, or something like it. And the cut face is **pale green and wet** — not blackened, not scabbed over, not healed.

Nadia, and this is the last thing she says before the hook:

> *"That's this week."*

**TAUGHT.** The route to Act 1's back half exists and is walkable. (Machete acquisition and Fernmaw traversal is post-paywall content.)

**REVEALED.** Landing the bible's Act 1 revelation dead on schedule: *the machete cuts on the vines are days old, not decades.* The Voice on the radio and the hand on the machete are, the player now assumes, the same person — and the game will not confirm that for six hours.

**FAILURE.** The gully is the only exit and is signposted by topography (the beach is a bowl; the gully is the notch). If the player has been in the prologue for 45 minutes without reaching it, Nadia begins walking toward it at the end of every idle loop. The prologue has **no time limit** and a player can beachcomb for two hours if they want; the Ribcage is authored with 11 optional inspectables (a boot-print in dried mud above the tide line; a chalked tide mark on the trawler's plate; a cormorant ring band; etc.) to reward that player specifically.

---

## 2. 🎬 THE HOOK — THE FINAL 60 SECONDS (29:00 → 30:00)

This is storyboarded to the second. It is the most important minute in the build.

**29:00.** Wide. Nadia at the gully mouth, small in frame, back to camera, the black wall of Fernmaw in front of her. Fire is a dot 200 m behind. The drizzle has come back. **Score: nothing.**

**29:06.** She switches the radio's toggle **off** — that phenolic click again, and the amber dial lamp dies. The frame loses its last warm colour. She clips the set to the bag.

**29:12.** She raises the multitool to the cut vine. The blade **hovers**. She doesn't cut it. She lowers her hand and just looks at the cut face, and reaches out and **touches** it with two fingers, the way you check if paint is dry.

**29:20.** Macro insert: her fingertips come away wet with sap. Sound: one fat drip off the canopy hitting a broad leaf. **The same drip from the title card at 0:42.**

**29:26.** She turns her head, slowly, and looks **up** — not at the jungle, at the mountain above it, which is invisible, because the cloud lid has come all the way down and there is nothing above the treeline but grey.

**29:32.** And the mix does the one thing it has been waiting 29 minutes to do: the **eleven-minute pressure cycle** rises — slowly, over four seconds, from –38 dBFS to **–14 dBFS**, until the player *cannot not hear it*, until it is in the phone's speaker as a physical buzz and in the haptics as a long low roll.

Nadia's head turns a few degrees toward it. She's an acoustician. She has heard it the whole time. She says it out loud for the first time:

> **"That's not the sea."**

**29:42.** Hard cut to black on the peak of the pulse. The pulse continues in black for **two full seconds** and then stops dead.

**29:46.** In the silence, in the centre of the black screen, in the Field Slate's handwriting, one line writes itself in wet pencil, letter by letter:

> **Day 1.**

**29:54.** It sits. Then, beneath it, much smaller, in the Slate's ruled-margin hand — as though she has gone back and added it later, which she has:

> *rev. — see Day 11,591.*

**30:00.** Cut. Paywall card. Title, tagline, **UNLOCK THE ISLAND**, price, and a single button. Behind the card, a static plate: **the six hulls on their one line**, in flat overcast grey, shot from the sightline position the player found at 2:40. No animation. No music. The drip continues over the store UI.

**Why this works, stated for review:** The player leaves with four questions (`who cut the vine`, `who is the voice`, `what is 11,591`, `what is the sound`), one of which — the last — they were given the answer to in the first thirty seconds of the game and did not know it. The "Day 1 / rev. — see Day 11,591" card is the whole game in nine words: *she is starting a count that somebody else is 11,590 days into.* And "rev." teaches, silently, that this notebook revises itself — which is the theme.

---

## 3. 📻 THE RADIO PUZZLE — FULL SPECIFICATION

### 3.1 The clue and where the frequency is written

**Primary source — the handle list.** A strip of paper, pencil, laminated in packing tape, taped to the **underside of the radio's carry handle**. Visible only in inspect view when the object is rotated to expose the handle's inner face. It reads, in a small tidy hand:

```
    2182 ....... dist. — dead since '89
    4125 ....... dist. — dead
    6215 ....... dead
    8291 ....... the hull. ignore.
   12510 ....... Recife wx, 0400/1600 only
   ——————————————————————
    5   2   4   0      sked. 0600 + 1800 local.
```

**The puzzle:** the last line is not written as a frequency. It is written as **four digits, spaced, with no decimal and no unit**, in the same hand but slower and more carefully — because it is the one the writer actually used, written for themselves, and people writing for themselves don't write units.

Every other line on the list is in **kHz** (2182, 4125, 6215, 8291, 12510). So `5 2 4 0` is **5240 kHz** — **5.240 MHz**. The player must notice the list's own unit convention and apply it. **That is the entire puzzle and it is a reading-comprehension puzzle, not a trivia puzzle.**

**Redundant source 1 — the tag row.** The eleven brass tags on nails in the trawler. One of them, the second-oldest, is stamped:
> `SET 2 — SKED 5240 — 0600/1800`
Inspecting the tag row is optional and most players will do it after they've solved it, as confirmation.

**Redundant source 2 — the chalk.** On the trawler's steel plate beside the crate, in weathered chalk, half rained-off, someone has written `5240` and under it a tally of five-bar gates — dozens of them, faded to nothing. Visible only when the fire's light angle is right, which happens naturally at 23:00 as the fire burns down and reddens.

**Redundant source 3 — the spectrogram.** A player who ignores all documents and simply sweeps the dial slowly will *see* the carrier as a vertical line in the phosphor ribbon before they hear it. The band is 15 minutes wide at the default drag speed; a thorough sweeper finds it in ~3 minutes. **We do not punish the player who refuses to read.**

### 3.2 What the player does with the tuning UI

1. **Tap the dial** → tuning band opens (bottom third, full width).
2. **Drag the printed dial strip** horizontally. Coarse: ~180 kHz per screen-width of drag. Mechanical detent tick every 25 kHz, haptic click on each.
3. **Fine tune**: drag within the right-hand 20% of the band → 10× resolution (~18 kHz per screen-width). No mode switch, no button — it is a different part of the same physical control, exactly as on the real object.
4. The **spectrogram ribbon** above the strip updates live. The carrier at 5240 appears as a stable vertical line ±6 kHz out.
5. **Audio behaviour:** off-frequency, the voice is present but unintelligible — that classic SSB donald-duck detune, a pitch-shifted mush. This is the strongest guidance in the puzzle: at ±4 kHz you *know* something human is there and you *know* you haven't got it. The player is pulled in by their own ear.

### 3.3 Tolerance

| Offset from 5240 kHz | Result |
|---|---|
| **±0.8 kHz** | **LOCK.** Carrier is clean, voice fully intelligible, the spectrogram line goes solid, one haptic thunk. The needle **magnetically detents** into the exact centre with a small snap so the player cannot lose it by breathing on the screen. |
| ±0.8 → ±4 kHz | Voice present, pitch-shifted and unintelligible. Spectrogram line visible but smeared. Full audio, zero content. |
| ±4 → ±12 kHz | Rhythmic "there's a person in there somewhere" warble under heavy hiss. Line visible as a faint smudge. |
| > ±12 kHz | Grass. |

The lock window is generous by design (**±0.8 kHz ≈ 1.6 kHz wide, roughly 9 px of thumb travel at fine-tune resolution** — comfortably above the 44 px minimum touch target once the detent assist is applied). We are not testing dexterity.

**Schedule handling — critical:** the list says the transmission is at **0600 and 1800 local**, and the prologue's clock is at roughly 1810 when the player arrives at the radio. **This is not a timing puzzle.** If the player tunes to 5240 at any point in the prologue, the transmission begins **within 20 seconds** of lock, framed as her running late. If a player somehow reaches the radio outside that window, the in-game clock advances during the repair montage to put them inside it. **The player must never be told to wait.**

### 3.4 What plays on success

The 44-second transmission in §1 at 25:20, in full, uninterrupted, unskippable for the first 20 seconds and skippable thereafter (skipping still writes the complete transcript to the Slate). Mixed as: **close-mic'd, dry, no reverb** — she is in a small hard room — with HF band noise around it and a very slight AGC pump on her breaths. Not radio-drama-clean. She is not performing. She is doing paperwork.

The four-second open carrier after "No vessel" is the single most important sound design decision in the prologue. **She holds the key down and says nothing.** Do not fill it. Do not add a sigh. The silence of someone with nothing left to say is what sells the next twenty hours.

### 3.5 Hint escalation — 3 tiers, all diegetic, timed

Timer starts the moment the radio powers up at 20:00 and **resets to zero on any of:** a coarse dial drag > 100 kHz, opening the handle-list inspect, inspecting a brass tag, or tuning into either false positive.

| Elapsed | Tier | Staging | Text/asset |
|---|---|---|---|
| **T+3:00** | **1 — Nadia thinks aloud** | She is holding the radio; she turns it over in her hands, half-looking at the handle. No camera move, no highlight. | *"Whoever this was, they wrote down the ones that worked."* — points at documentation existing, names nothing. |
| **T+6:00** | **2 — the object shows itself** | In inspect view, the radio does a single slow 360° auto-rotate and **comes to rest with the handle's underside facing camera.** The taped list is now dead centre of frame at readable scale. No zoom, no glow, no arrow. If the player is not in inspect view, the radio chip in the bottom strip leans 4°. | No VO. Pure staging. |
| **T+10:00** | **3 — she reads it out** | She reads the list aloud, all of it, in order, and stops on the last line — and does the reasoning *out loud but incompletely*: *"Two-one-eight-two. Four-one-two-five. Six-two-one-five... and then five, two, four, zero, which they've written like a phone number."* Beat. *"Same units as everything above it, presumably. Kilohertz."* **She does not tune it. She does not say 5.240 megahertz. She does not touch the dial.** | The player still performs the final input. |

**Tier 4 (safety net, T+16:00, undocumented in marketing):** Nadia sets the radio down, says *"I'm going to leave it on and sweep it,"* and the dial begins **slowly auto-sweeping on its own** at coarse resolution, with the spectrogram live. It will pass through 5240 approximately 90 seconds later and the carrier will rise in the audio. The player need only **tap once** to stop the sweep. This exists so that no session ends at the radio, and it has never once been described to a player as a hint.

---

## 4. 📦 PROLOGUE ITEM AND RECIPE LIST — COMPLETE

### 4.1 Every item the player can touch

| # | Item | Where | Type | What it's for | Can it be lost? |
|---|---|---|---|---|---|
| 1 | **Dry bag (orange)** | Surf line, 2:00 | Container | Holds everything. Becomes the worn inventory. | No |
| 2 | **Hydrophone** | In bag, 2:20 | Story tool | Nothing in the prologue. Inspectable; dented; Act 2 signature tool. | No |
| 3 | **Field recorder** | In bag, 2:20 | Tool / resource | Wet; must be dried at fire. Contains 2× AA-equivalent cells at 9%. | No (cells can be spent) |
| 4 | **Recorder cells (2)** | Inside #3 | Power | Can power the radio *instead of* the D-cells. Spending them kills the 27:00 audio-archive branch. | Yes, deliberately |
| 5 | **Multitool** | In bag, 2:20 | Tool | Striker spine (spark), blade (cut/tease), screwdriver (battery door), scraper (corrosion). Never consumed. | No |
| 6 | **Pencil** | In bag, 2:20 | Story | Slate entries. Not player-manipulable. | No |
| 7 | **Field Slate** | In bag, 2:20 | System | Evidence. Cannot be dropped. | Never (canon rule 7) |
| 8 | **Driftwood, dry** | Strand line, above tide mark | Fuel | Fire fuel. Burns. | Consumed |
| 9 | **Driftwood, wet** | Strand line, below tide mark | Fuel (bad) | Will not light. Dries in the fire's warm zone over 90 s → becomes #8. **Authored dead-end that teaches a real rule.** | Consumed |
| 10 | **Dry grass** | Rock crevices above tide line | Tinder (poor) | Flares 1.2 s and dies. Authored failure. | Consumed |
| 11 | **Poly rope, rotted** | Wrack | Tinder (good) precursor | Teased → #12. | Consumed |
| 12 | **Teased poly fibre** | Crafted | Tinder | Catches a spark reliably; stinks; holds ember 8 s. | Consumed |
| 13 | **Chert nodule** | Strand line (3 spawn points, all within 40 m of the fire site) | Striker | Struck on multitool spine → sparks. Not consumed. | No |
| 14 | **Basalt cobble** | Everywhere | Red herring | Dull knock, no chip, no spark. Teaches the audio test. | No |
| 15 | **Fibreglass panel (orange)** | Wrack | Windbreak / story | Alternate windbreak. Stencil reads `...ELL HEAD 4`. | No |
| 16 | **Kelp, wet** | Wrack | Red herring / later use | Cannot burn. Can be inspected (Nadia: *"Not food. Not yet."*). No prologue recipe. | Consumed if dropped |
| 17 | **Rubber boots, paired** | Trawler overhang, 12:00 | Story | Cannot be taken. Inspect only. Wearing them is not an option and she doesn't try. | N/A |
| 18 | **Brass tag — `LANTERN 4 / 12·04·09`** | Wired to #17 | Story | First maintenance tag. Slate entry. | No (stays on boot) |
| 19 | **Brass tag row (11)** | Nails, trawler interior | Story | Dates spanning ~20 years; most recent 3 weeks old. One carries `SET 2 — SKED 5240`. | No |
| 20 | **Radio (VHF/HF portable)** | Crate, trawler, 15:00 | Key object | The prologue's spine. Three faults. | No |
| 21 | **Frequency list (taped paper)** | Under #20's handle | Clue | The radio puzzle's primary clue. | No |
| 22 | **Hand-mic + curly cord** | Attached to #20 | Tool / story | PTT. Cord can be stripped for conductor (alt fuse fix). | No |
| 23 | **Dead hand-torch** | Nail row, trawler | Parts donor | Yields #24 and #25. Torch itself is dead (bulb burnt, and Nadia says so). | No |
| 24 | **D-cells (2)** | Inside #23 | Power | Primary radio power. Preserves the recorder. | Consumed |
| 25 | **Copper spring** | Inside #23 | Conductor | Fuse bypass. | Consumed |
| 26 | **Canvas square** | Folded over #20 | Story | Someone covered the radio. Inspect: dry underneath. Nadia: *"Covered. Not dumped."* | No |
| 27 | **Crate (plastic, upturned)** | Trawler | Set dressing / surface | Used as a table. Not portable. Stencilled with a shipping mark, illegible. | N/A |
| 28 | **Chalk `5240` + tally** | Trawler steel plate | Redundant clue | Visible at low fire-light angle. | N/A |
| 29 | **Cut vine face** | Gully mouth, 28:20 | Story | The hook. Inspect for the wet cut. | N/A |
| 30 | **11 optional inspectables** | Scattered (boot print in dried mud; cormorant leg band; chalked tide mark; oxy-cut slag; broom arc in sand; etc.) | Flavour | Slate entries only. No mechanical effect. | N/A |

### 4.2 Every combination that works

| A | B | Method | Result | Notes |
|---|---|---|---|---|
| Poly rope (11) | Multitool (5) | Drag A→B | Teased poly fibre (12) | 1 rope → 3 fibre |
| Chert (13) | Multitool (5) | Drag A→B, then **downward swipe** = strike | Sparks (not an item) | Repeatable, infinite |
| Sparks | Teased fibre (12) | Strike while fibre is placed | **Ember** | 100% success if fibre is in a wind shadow |
| Sparks | Dry grass (10) | Same | Flare → dies in 1.2 s | **Authored failure.** No penalty, no VO scolding. |
| Ember | Dry driftwood (8) | Drag wood onto ember | **FIRE** | Requires ≥3 dry wood |
| Ember | Wet driftwood (9) | Drag | Smoke, hiss, ember dies | Authored failure. Nadia: *"It's wet through."* |
| Wet driftwood (9) | Fire's warm zone | Drag onto warm zone | Dry driftwood (8) after 90 s | Teaches the warm zone before the recorder needs it |
| Fire site | Landing craft lee **or** fibreglass panel (15) | Place fire in the lee, or drag panel upright into sand | Wind-shadowed fire site | Two authored solutions; panel gives smaller radius |
| Field recorder (3) | Fire's warm zone | Drag | Dries over 4 min real-time | Required to read its screen; cells recoverable either way |
| Field recorder (3) | Fire (flame) | Drag | **Blocked.** Nadia stops her own hand: *"No."* | Un-failable |
| Multitool (5) | Recorder battery door | Tap with tool equipped | Door opens, cells accessible | |
| D-cells (24) **or** Recorder cells (4) | Radio battery bay | Drag | Power restored | The prologue's one real decision |
| Multitool (5) | Radio spring terminals | **Hold-and-scrub** gesture | Corrosion removed | 1.5 s grind audio |
| Copper spring (25) **or** stripped mic cord (22) | Radio fuse holder | Drag | Circuit closed | Both valid |
| Multitool (5) | Cut vine (29) | Tap | **Blocked at 29:12** — she lowers the blade | Deliberate: the hook is a *non*-action |
| Kelp (16) | Anything | — | Nothing | Honest dead end; inspect line only |
| Basalt (14) | Multitool (5) | Strike | Dull knock, no spark | The negative half of the chert lesson |
| Hydrophone (2) | Anything | — | Nothing, in the prologue | Inspect line: *"Dry it out. It's the only thing I've got that's worth anything."* |

**Total recipes: 18. Total that work: 13. Total authored failures: 5.** The ratio is deliberate — **a quarter of the prologue's crafting outcomes are honest, unpunished failures**, because that is how the player learns the world has rules rather than a recipe list.

---

## 5. ▶️ COLD-OPEN ALTERNATIVE — THE RETURNING PLAYER TAPS *CONTINUE*

A returning player must never sit through the wreck again, and must never be dumped into a world with no idea what they were doing. **The re-entry is built as a 90-second diegetic recap that is itself a piece of the game.** It is called **THE SLATE OPEN.**

**0:00 — 0:06.** No logo. The app opens on the **Field Slate, closed**, resting on whatever surface Nadia last set it down on (sand / the flat stone by the fire / a crate), lit by whatever light was current at save time. Rain or not, fire or not — the save's environment state is restored *behind* the book. Ambient audio is live and running. The eleven-minute pressure cycle is at its saved phase.

**0:06 — 0:14.** Her hand enters frame and opens it. The book falls open — **not to the front, to the last page written.** The player watches the pages riffle past, and the riffle is the recap: a half-second glimpse of each entry they've made, in order, at speed. No text is legible. The *volume* of it is the point. A player 12 hours in gets a 4-second riffle and feels it.

**0:14 — 0:30.** The last page holds. It is the most recent entry, fully legible, and **underneath it, a line in fresh pencil that was not there when they quit:**

> *Next: [current objective, in her own words, as a note to self.]*

e.g. *"Next: get up the gully before the light goes. Take the radio."*

This is the entire objective system. It is one line, handwritten, on the last page, in the character's voice. **There is no quest log anywhere in this game.**

**0:30 — 0:40.** She closes the book. The camera **does not cut** — it pulls back from the Slate to her point of view, in place, in the world, exactly where she was. Control returns. **There is no loading screen between the recap and gameplay because the recap *is* the loading screen** — the Slate is a full-screen object with a 4 MB memory footprint rendered over the streaming world.

**0:40 — 0:90 (conditional layers).** Three variants, chosen by time-since-last-session:

| Absence | What changes |
|---|---|
| **< 2 hours** | Skip the riffle entirely. Book opens straight to the last page, 6 seconds total, control in 9. Never make a returning player wait for a recap they don't need. |
| **2 hours – 5 days** | Full 40-second version above. |
| **> 5 days** | Full version, **plus**: after the book closes, Nadia does one unprompted 8-second re-orientation — she looks up at the current objective's direction in the world and says one line placing it. *"Gully. North end of the beach. Before dark."* Then control. Never more than one line, never a map marker. |

**Save-state edge cases, all handled diegetically:**
- **Quit mid-puzzle (e.g. radio half-repaired):** the Slate's *Next* line is written at puzzle granularity — *"Next: the radio's dead. Power, contacts, fuse. In that order."* The hint timers from §3.5 **reset to zero on load.** A player must never come back to an escalated hint state and be told the answer they were about to get themselves.
- **Quit mid-transmission at 25:20:** the transmission is already in the Slate. On resume, the last page is the transcript, and the *Next* line reads *"Next: she's on a schedule. 0600 and 1800. Be listening."*
- **Quit before any entry exists (< 3:30):** no riffle, no Next line. The book opens to a blank page with the date at the top and nothing under it. She looks at it for a beat and closes it. This is, accidentally and correctly, the most ominous version of the cold open in the game.

---

## 6. IMPLEMENTATION NOTES AND FLAGGED ASSUMPTIONS

**Unity 6 LTS / URP / portrait mobile — technical constraints this script is written against:**
- **Two dynamic lights maximum on screen** (the fire; the radio's dial lamp — never simultaneously at full cost, as the dial lamp is a 3 cm emissive with a tiny attenuation radius). Everything else is baked. The Ribcage's overcast is a single baked lightmap set plus one directional for shadow direction. This is why the prologue has no sun, no night, and no torch: **each of those is a rendering budget we are choosing not to spend before the paywall.**
- **The spectrogram ribbon** (0:00, 21:00, and the entire Act 2 hydrophone) should be a single-channel R8 texture updated on a compute shader or, on the 30 fps low-end path, a 64-bin CPU FFT at 15 Hz upscaled with bilinear smoothing. *Unverified assumption: I have not validated compute shader availability across the full target Android device matrix on URP in Unity 6 — this needs an engineering spike before the ribbon is committed as the game's signature UI motif.*
- **New Input System:** everything in this script is `Touchscreen` primary-touch: tap, hold (0.6 s), drag, and one short downward flick (the striker). No gesture in the prologue requires more than one finger except the optional two-finger Slate summon, which has a redundant edge-tab. No gesture requires a target smaller than 44 pt after assist.
- **Haptics** are load-bearing three times (0:00 pressure pulse, radio detent lock, 29:32 hook). *Unverified assumption: iOS Core Haptics and Android's `VibrationEffect` compose differently and I have not verified a shared authoring path in Unity 6 — assume a platform-split implementation and budget for it, or degrade the Android path to a simple amplitude envelope.*
- **The 44-second transmission** must be a single streaming asset, not a mixer chain, so it survives an app backgrounding mid-playback and resumes at the correct sample. This has a known failure mode on Android audio focus loss; spike it.

**Content and legal flags:**
- Every proper noun in this document originates in the Story Bible v1.0 or is newly invented here (`Pellamar`, the frequencies, `T.R.`, the tag text). **Unverified assumption flagged for clearance: the frequencies cited (2182, 4125, 6215, 8291, 12510, 5240 kHz) are used as fictional set-dressing. 2182 kHz is, to the best of my knowledge, historically associated with maritime distress — this is the sort of claim that must be verified with a maritime radio consultant before it appears in shipped text, and if verification is not obtained, all six numbers should be replaced with values confirmed to be outside any real allocated service.**
- No mechanic, proper noun, document, puzzle solution or line of dialogue in this document is drawn from any existing game, film or show. The genre conventions used (item combination, hold-to-harvest, survival meters, analogue tuning) are unprotectable mechanics.

**The one thing to protect in review:** the four seconds of open carrier after *"No vessel."* Somebody will ask to fill it. Don't.