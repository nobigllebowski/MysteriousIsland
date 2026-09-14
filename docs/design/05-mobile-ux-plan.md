# MOBILE UX PLAN — THE FORGOTTEN ISLE
## Portrait Mobile Adventure, Unity 6 LTS / URP / New Input System
**Reference frame: 390 × 844 pt (iPhone 12/13/14 logical). All coordinates below are in pt, origin top-left, +Y down, unless stated.**

**Safe-area contract (assumed device metrics — verify against `Screen.safeArea` at runtime on each target device; these figures are my working assumptions, not verified specs):**

| Region | Top inset | Bottom inset | Notes |
|---|---|---|---|
| iPhone 12/13/14 (notch) | 47 | 34 | notch ~ 209 wide, centred |
| iPhone 14 Pro / 15 / 16 (Dynamic Island) | 59 | 34 | island ~ 125 × 37, centred, floats at y≈11–48 |
| iPhone SE 3 (no cutout) | 20 | 0 | 375 × 667 — shortest supported frame |
| Android hole-punch (Pixel 7/8, S23) | 24–48 | 24–48 | punch top-left or top-centre; gesture bar 24–48 |
| Android tall 20:9 / 21:9 | var | var | letterbox never; extend background art |

**Rule:** all UI anchors derive from `Screen.safeArea` at runtime via a single `SafeAreaDriver` MonoBehaviour writing to a `SafeAreaRect` ScriptableObject; nothing in the UI hardcodes 47 or 34. Layout below uses **SA.top**, **SA.bottom**, **SA.left**, **SA.right**. Where I write a literal, it is the value on the 390×844 reference device and the layout formula is given alongside.

---

# 1. CAMERA PERSPECTIVE DECISION

## 1.1 Recommendation

> **FIRST PERSON, with an authored third-person "Body Camera" reserved for ~14 scripted moments.**
> Locked, non-rotating vertical FOV. No player avatar body except hands/forearms and a visible torso-down look ("legs when you look down") built as a rigged FP body, not a full TP character.

This is a hybrid but it is *not* a camera toggle. The player never chooses. FP is the game; the Body Camera is a cutscene grammar (Act openers, the grave, the Quiet Room approach, both endings).

## 1.2 The evaluation, criterion by criterion

| Criterion | First person | Third person | Weight for *this* game |
|---|---|---|---|
| **Production cost** | No locomotion rig, no IK-to-slope, no 360° traversal anim set. Budget goes to hands, tools and props. Est. **~10–14% of total art/anim budget** on character. | Full rig: idle/walk/run/crouch/climb/ladder/vault/turn-in-place/slope-IK/hand-IK to every prop, plus a face for Act 5. Realistically **30–40%** of art/anim budget, and it's the part that looks cheapest if underfunded. | **Decisive.** A premium look on a mid-size budget means spending on the island, not on a person we mostly see from behind. |
| **Immersion** | The signature verb is *listening*. FP puts the hydrophone in the player's hands and the headphones on the player's head. Nadia's voice is interior monologue, not overheard. | Third person makes Nadia an object we watch listen. Kills the acoustic intimacy. | **Decisive.** |
| **Readability of interactables (390pt wide)** | Interactable fills more of the frame; a brass tag can be 60pt tall at reading distance. But **peripheral awareness is worse** — things beside you are simply off-screen in portrait. | You see more of the room, so you spot the object earlier; but at 390pt wide with a character occupying ~25% of frame width, each object is *smaller*. | **FP wins on final read, loses on discovery.** Mitigated in §1.4. |
| **Motion sickness** | Highest-risk. FP + head bob + FOV changes + swipe-look is the classic trigger stack. | Lower risk; the avatar acts as a fixed reference frame. | **TP wins.** Mitigated hard in §1.4 — comfort defaults ON. |
| **Portrait framing (tall, thin)** | Excellent. A tall frame in FP = you see *up* (canopy, the Combs cliff face, the Quiet Room fins) and *down* (your hands, the floor, the chalk). Both are content. Vertical is the axis this island is built on. | Bad. TP wants horizontal room for the character plus lookahead. In portrait the avatar eats the middle third and the camera boom has nowhere to go laterally. Standard fix is a tight over-shoulder, which reintroduces most FP sickness risk *and* costs the rig. | **Decisive. This is the single strongest argument.** |
| **Traversal legibility** | Ledges, ladders, rope bridges read poorly in FP — you can't see your feet on a 40cm plank at 240m. | TP is meaningfully better here. | **TP wins.** Mitigated by authored traversal (§1.4). |
| **Cinematic potential** | Strong for intimacy and dread; weak for "hero in landscape" marketing shots and for Act 5's two-hander conversation. | Strong for both. | **Split.** Resolved by the Body Camera. |
| **Animation/VFX cost** | Hands + tools only, but every tool needs a bespoke first-person prop anim (hydrophone raise, recorder scrub, Register rubbing, damper console). ~45 FP prop animations. | All of the above *plus* the full body set, plus prop-to-body IK. | **FP wins, clearly.** |

## 1.3 What we give up (state it plainly)

1. **Nadia's body language.** The player never sees her flinch, hesitate, or sag. We lose the cheapest tool for characterising a protagonist. *Compensation:* voice + hands. The hands are the performance — cold-stiff on Rime Shoulder, shaking at 12% hydration, steady and slow in Act 5.
2. **Marketing hero shots.** No "figure on a ridge" key art from gameplay capture. *Compensation:* the Body Camera moments are shot-designed for capture; we build a camera-only cinematic rig for Nadia used in 14 sequences and in all trailers. This is a real animation cost we are accepting — budget ~3 weeks of anim for a cinematic-only rig with no locomotion loop.
3. **Peripheral awareness.** In portrait FP you will walk past things. *Compensation:* §1.4 item 3 and §1.4 item 5.
4. **Platforming feel.** No precision ledge work, ever. Traversal becomes authored, which also removes a whole class of mobile-touch frustration. We are trading a mechanic we would have executed badly for one we can execute well.
5. **The Act 5 two-shot.** A conversation where you never see your own face is one-sided. *Compensation:* Act 5 is the heaviest Body Camera sequence — three cuts to a wide two-shot at the chalk floor, then back to FP for the choice. The choice itself is made in FP so the player owns it.

## 1.4 Mitigation plan (these are commitments, not aspirations)

**Motion sickness — comfort defaults are ON at first launch, discoverable in a first-run comfort card:**
- Vertical FOV **locked at 62°** (≈ horizontal 34° in portrait 9:19.5). Never animated for sprint, damage, or drama. Zero FOV kick anywhere in the game. *(A FOV *reduction* for the hydrophone focus mode is the one exception and it is an opt-out setting.)*
- **Head bob OFF by default.** When enabled: max ±0.9° roll, ±1.1cm vertical, no lateral translation.
- **Camera roll clamped to 0°** in all normal play. Roll is used only in Body Camera shots.
- **Snap-turn option** (30° / 45° increments, double-tap camera zone edge) as an alternative to swipe-look, for players who cannot tolerate smooth yaw.
- **Vignette-on-move** (0 → 0.22 strength, 180ms in / 320ms out) — default ON at low strength, slider 0–100%.
- Acceleration curves: yaw/pitch use a **linear** response by default (no exponential ramp), because exponential response correlates with discomfort. Exponential is an option for twitch-comfortable players.
- **Motion sickness is a compliance item, not a polish item:** every build gates on a 6-person comfort pass at 20 min continuous play.

**Peripheral awareness:**
3. **The Attention Ring.** Interactables outside the frustum but within 4m produce a **3pt-wide, 40pt-long arc** on the screen edge at the correct bearing, at 28% opacity, only while the player is stationary for >1.2s. It is not a waypoint marker — it does not name the object, it does not persist, and it never appears for scenery. It appears for *serviced* objects (brass tags, chalk marks, fresh cuts), which is thematically exactly right: the island tells you what someone has been maintaining.
4. **Wide-listen.** Raising the hydrophone (§2) renders a **directional spectrogram ring** around the frame edge — this is the game's built-in 360° awareness tool and it is a *narrative* mechanic, not a UI crutch. FP's peripheral weakness is repurposed as the reason the signature verb exists.
5. **Zone lighting and audio lead the eye vertically.** Level design rule handed to environment art: in portrait FP, place the payoff on the **vertical axis** — up the Combs face, down the Sweatway shaft, up the canopy in Fernmaw. Horizontal reveals need a doorway or a turn to justify them.

**Traversal legibility:**
6. **No free climbing. Ever.** Ladders, rope bridges and scrambles are **authored traversal**: approach the anchor, a contextual `CLIMB` prompt appears, one tap starts an on-rails animated move with player look-control retained (you can look around while climbing; you cannot fall). Cancel with a second tap in the first 0.6s only.
7. **The rope bridges on Rime Shoulder** use a widened collider (1.1m) and a hand-on-rope FP anim with a visible forearm on each guide line, which solves "where are my feet" by giving continuous hand contact reference.
8. **Foot-visible look-down.** Looking down past −52° pitch reveals a rigged FP lower body (thighs, boots) with simple two-bone IK to the ground plane. Not full-body awareness — just enough that ledges read. ~2 weeks of anim, and it also sells the Ash Throat scree and the Quiet Room chalk.

**Small-screen readability:**
9. **Interaction distance is generous**: the interact cone is 26° half-angle at ≤2.2m, falling to 11° half-angle at 4.5m. You do not have to aim precisely on a touchscreen.
10. **Art direction rule:** every interactable carries a **value contrast ≥ 25% against its 3×3m surround** under that zone's lighting, verified by a luminance-diff editor tool run per-scene. This replaces outline shaders, which look cheap and cost fill rate on mobile.

**Cinematic:**
11. **The Body Camera** — a separate Cinemachine virtual camera set, triggered at 14 authored beats. Transitions are **cuts, never blends** (a blend from FP to TP is nauseating and reads as a bug). Cut out on an audio hit. Nadia appears in the frame only in these 14 shots plus both endings.

---

# 2. CONTROL SCHEME

## 2.1 Global input philosophy

Three zones, no overlap, no hidden gestures that duplicate a visible button. Every action has a **visible, reachable affordance**; gestures are accelerators for actions that also have buttons. Unity **New Input System** with an `EnhancedTouch` layer; all touch handling goes through one `TouchRouter` that owns zone arbitration, so no two systems fight for the same finger.

## 2.2 The frame, divided

```
 0                                                   390
 ┌────────────────────────────────────────────────────┐ 0
 │                  SAFE-AREA TOP (SA.top = 47)       │
 ├────────────────────────────────────────────────────┤ 47
 │                                                    │
 │            C A M E R A   S W I P E   Z O N E       │
 │                                                    │
 │   (entire frame minus the two thumb zones and      │
 │    minus any open panel; a swipe starting here     │
 │    looks, a tap here interacts-at-reticle)         │
 │                                                    │
 │                                                    │
 ├──────────────────┐                ┌────────────────┤ 560
 │                  │                │                │
 │  MOVE ZONE       │   CAMERA       │  ACTION ZONE   │
 │  (floating       │   (continues)  │  (contextual   │
 │   joystick)      │                │   button +     │
 │  x: 0–150        │  x: 150–240    │   inventory)   │
 │  y: 560–844      │  y: 560–844    │  x: 240–390    │
 │                  │                │  y: 560–844    │
 └──────────────────┴────────────────┴────────────────┘ 844
```

**Arbitration rule:** the zone a touch **begins in** owns that touch for its whole life. A finger that starts in the move zone and slides to x=300 is still driving the joystick. This prevents the single most common mobile-adventure bug: accidental camera swipe while walking.

## 2.3 MOVE — floating dynamic joystick (decision: floating, not fixed)

**Decision: FLOATING, with a "home ghost" and an optional Fixed mode in Accessibility.**

Rationale: fixed sticks force the thumb to a spot the player can't see under their own hand, and portrait phones vary 667–932pt tall, so any fixed anchor is wrong on some device. Floating puts the stick where the thumb already is. The known weakness of floating sticks — losing your sense of centre — is solved by the home ghost.

| Property | Value |
|---|---|
| Activation zone | `x ∈ [SA.left, 150]`, `y ∈ [560, SA.bottom_edge]` — 150 × 250pt |
| Home ghost | A 72pt-diameter ring at **(72, 700)**, 12% opacity, always visible during movement tutorial (first 8 min), then 0% opacity but still drawn for 900ms after the stick releases |
| Stick base | Spawns centred at the touch-down point, **108pt diameter** |
| Knob | **52pt diameter** (≥48dp), travels 40pt max from base centre |
| Dead zone | 7pt radius (≈ 18% of travel) |
| Full-tilt radius | 40pt |
| Recentre | If the thumb travels >40pt from base, the base **drags** with it (classic "sticky follow") so the player can walk indefinitely without hitting the stick ceiling |
| Response | Radial magnitude → speed, **squared curve** (`m²`) so slow precision walking is easy and full speed needs commitment |
| Walk / Run | Magnitude < 0.62 = walk (1.45 m/s). Magnitude ≥ 0.62 = run (3.1 m/s). No run button. See §2.8. |
| Release | Knob returns to base over 120ms `OutQuad`, base fades over 260ms |
| Visual | Thin 2pt ring, 1 ring inside 1 ring, no gradient, no glow — reads as an instrument, not a toy. Colour `#CFC6B4` at 34% opacity rising to 62% while active. |

**Left-hand mirroring** (§8) swaps the move zone and action zone horizontally. This is a single bool on `TouchRouter` and a `Horizontal Layout Reverse` on the bottom bar.

## 2.4 LOOK — camera swipe

| Property | Value |
|---|---|
| Zone | Entire frame **except** the move zone and the action zone and any open panel. Includes the whole upper 513pt of the screen — so two-handed players swipe with either thumb up top, one-handed players swipe with the middle of the screen. |
| Gesture | Drag. **No touch-to-look-continuously** (no "look joystick") — this is a swipe-to-turn game, like a photograph you rotate. |
| Base sensitivity | **0.185 °/pt** yaw, **0.155 °/pt** pitch (pitch deliberately 84% of yaw — portrait screens make vertical drags shorter and players overshoot vertically) |
| Sensitivity slider | 0.06 – 0.40 °/pt in 14 steps; separate X and Y sliders behind an "Advanced" disclosure |
| Response curve | Linear by default (comfort). Optional "Accelerated" curve: `out = in * (1 + 0.9 * clamp01(speed/1400pt-s⁻¹))` |
| Inversion | **Invert Y** and **Invert X** independent toggles, both default OFF. Invert Y is surfaced in the first-run comfort card, not buried. |
| Pitch clamp | −78° to +82°. (Asymmetric: you can look almost straight up — the canopy, the Combs face, the Quiet Room ceiling are content.) |
| Flick / momentum | **None.** Inertial camera is a nausea vector and makes precise aiming at a 48dp target impossible. Camera stops when the finger stops. Non-negotiable. |
| Smoothing | 1-frame exponential (`α = 0.62`) to kill touch jitter, nothing heavier. |
| Tap (no drag) in camera zone | See §2.5 — tap-to-interact. |
| Two-finger pinch in camera zone | Hydrophone **focus narrowing** only (Act 2+). Pinch in = narrow the listening cone from 60° to 12°. Pinch out = widen. Never a camera zoom. |

**Snap-turn alternative** (accessibility + comfort): with Snap Turn enabled, a tap in the left or right 44pt gutter of the camera zone turns 30° (or 45°) with a 90ms blink-to-black-and-back. Swipe-look remains available simultaneously.

## 2.5 INTERACT — the contextual action button + tap-to-interact

Two paths to every interaction, always both available:

### Path A — the Contextual Action Button (primary, thumb-driven)

```
Anchor: bottom-right, inset from safe area
Centre: (318, 716)     ← x = W - SA.right - 72 ; y = H - SA.bottom - 94
Diameter: 88pt         ← hit target 96pt (≥48dp by a wide margin)
```

**Thumb-reach analysis, 390 × 844:**

| Grip | Thumb pivot (approx) | Comfortable radius | Reaches (318, 716)? |
|---|---|---|---|
| Right hand, one-handed | (330, 812) | ~200pt arc | **Yes** — 96pt from pivot, dead centre of the easy arc |
| Left hand, one-handed | (60, 812) | ~200pt arc | **Marginal** — 278pt. *Requires left-hand mirroring* (§8), which moves the button to (72, 716). |
| Two-handed, thumbs | right thumb at (330, 812) | — | **Yes**, and the left thumb is free on the joystick |
| Two-handed, index finger | anywhere | — | Yes |

The **top 47–330pt band is unreachable one-handed on any grip** — hence no interactive element ever lives above y = 560 except the vitals/compass readouts, which are **display-only and never tappable** (tapping them does nothing; opening them is done from the bottom bar).

**Button states:**

| State | Appearance | Behaviour |
|---|---|---|
| No target | 88pt ring, 2pt stroke, `#8E8madeA` → use `#8E8A7A` at **18% opacity**, no icon | Inert. Tap does nothing (no error, no sound). |
| Target in range | Ring goes to 78% opacity, fills to 14% background, **verb glyph** appears (hand / ear / wrench / eye), ring draws in over 140ms clockwise | Tap = perform |
| Hold-action target | A 3pt progress arc rides the ring | Press and hold; arc completes in the action's duration (400–1400ms) |
| Multiple targets | A **stacked-card indicator**: two 2pt chevrons on the ring's left edge | See §2.6 |
| Performing | Ring collapses to 64pt over 90ms, then springs back | Locked out for the action's duration |

**Verb glyph set (only 7 exist — a hard constraint):**
`HAND` (pick up / move), `EAR` (listen), `EYE` (examine / read), `WRENCH` (operate / service), `FOOT` (climb / enter), `HAND-PULL` (hold-to-drag), `SPEAK` (Act 5 only).

### Path B — Tap-to-interact (secondary, precision)

A **tap** (touch down + up within 220ms, <12pt travel) anywhere in the camera zone raycasts through that screen point with a **34pt radius sphere cast**. If it hits an interactable in range, it performs the same action the contextual button would. This is how two-handed players who are looking at the top of the screen actually play — they tap the brass tag directly.

If the tap hits nothing interactable: **nothing happens.** No "can't do that" sound, no shake. Silence.

## 2.6 Two or more interactables in range

This is the hardest small-screen problem in the genre. The rule:

1. **Score each candidate**: `score = (1 - angleFromReticle/coneHalfAngle) * 0.62 + (1 - dist/maxDist) * 0.28 + priorityBias * 0.10`. `priorityBias` is authored per-object (a maintained brass tag out-scores a rock).
2. The **highest scorer becomes the active target** and its name appears in the interaction prompt (§4).
3. The contextual button shows the **stacked-card chevrons**. The count is *not* shown numerically — this isn't an inventory.
4. **Swipe up on the contextual button** (≥24pt) expands a **Target Wheel**:

```
                                ┌──────────────────────┐
                                │  BRASS TAG  L-7      │  ← active, highlighted
                                ├──────────────────────┤
                                │  LANTERN HOUSING     │
                                ├──────────────────────┤
                                │  CHALKED TIDE TABLE  │
                                └──────────────────────┘
                                          ▲
                                       (  ⊙  )   ← button
```

- Max **4 entries**. If more than 4 candidates exist, the level is badly built — the tooling flags it in the editor as a warning.
- Rows are **56pt tall**, right-aligned, stacked upward from the button so the thumb never covers the list.
- Slide the thumb up/down over the list without lifting → highlight moves. **Lift = select and perform.** Single continuous gesture, ~0.6s.
- Lift outside the list = cancel, no action.
5. **Alternative, always available:** just tap the thing you want (Path B). The Target Wheel exists for the thumb-on-button player.
6. **Prevention beats resolution.** Level design rule: two interactables whose interact cones overlap by >40% at the natural approach position must be merged into one composite interactable (e.g. "Lantern 7" is *one* target; examining it reveals the tag, the housing and the wick as sub-elements inside the Inspect view).

## 2.7 COLLECT

Collecting is an interaction with a distinct feel, because acquisition is the pleasure of the genre.

1. Target scores as `HAND`.
2. Tap. FP hands enter frame, the object is picked up with a real animation (**280–420ms**, per-object).
3. The object **travels to the inventory button** as a 3D object rendered in an overlay camera at 1/4 scale — it arcs from its world position to (318, 792) on a 340ms `InOutCubic` path while shrinking to 20%.
4. The inventory button pulses once: scale 1.0 → 1.09 → 1.0 over 180ms, and a **thin 2pt ring sweep** completes around it.
5. Haptic: `LightImpact` at pickup, `Selection` at arrival.
6. A **sub-toast** appears above the bottom bar for 1.9s: `+ CORDAGE ×1` at 13pt, right-aligned, 68% opacity, fading over 400ms. Toasts stack to a max of 3, each offset 26pt up, each with its own timer.
7. **No full-screen interrupt for ordinary items.** Only Discovery-tier items (§6) get the cinematic treatment.

## 2.8 RUN

**No run button.** Magnitude on the joystick is the run control (§2.3). Rationale: a run button on the right side competes with the contextual button for the only reachable thumb, and this game has no chase, no combat, and no timer — so run is a comfort feature, not a skill.

- **Auto-run assist:** holding the joystick at ≥0.62 magnitude for >2.0s latches run, so the thumb can relax back to ~0.5 without dropping to a walk. Releasing below 0.25 unlatches.
- Run has a **stamina-free** model. Nadia slows to a walk automatically on grades >22° and in water >0.6m. No meter, no punishment.
- Accessibility: `Toggle Run` option puts a run latch on **double-tap in the move zone**.

## 2.9 CROUCH / CLIMB

**No manual crouch.** Nadia auto-crouches (a camera-height lerp from 1.62m to 1.05m over 300ms, plus a hands-forward anim) when entering an authored low volume — the Sweatways baffle gaps, the Combs cells, under the Fold Camp stilts. There is no crouch button because there is nothing to hide from (Fiction Rule 3: no combat, no creature threat). Crouch is a *place*, not a *state*.

**Climb** is authored traversal (§1.4 item 6):
1. Approach the anchor → contextual button shows `FOOT`.
2. Tap → on-rails climb begins. The joystick is **disabled**; the camera swipe zone remains **fully live** so you can look around while you climb (this is both comfort and content — climbing the Combs ladder past four thousand years of carving is a designed look-around moment).
3. Climb speed is fixed. Holding the joystick forward does **not** speed it up (that would make players mash and then feel unresponsive).
4. **Cancel:** tap the button again within the first 600ms → she steps back down. After 600ms the move commits.
5. At the top, control returns with a **150ms** input lockout and the camera pitch is gently levelled to the horizon over 400ms (comfort).

## 2.10 OPEN INVENTORY

- **Button:** bottom bar, right-of-centre. Centre **(318, 792)**, **56pt** icon, **64pt** hit target. Directly below the contextual action button — one thumb, two functions, vertically separated by 76pt (no mis-tap risk; minimum comfortable separation on a 48dp target is ~20pt and we have 76).
- **Gesture accelerator:** swipe up from the bottom bar (from `y > 770`, travel ≥ 60pt) opens inventory. This is deliberately the same physical gesture as the OS home swipe — so the bar's swipe-up zone is inset to `y ∈ [758, 800]` and stops **above** the 34pt home-indicator band. Test on Android gesture-nav where the system swipe region is taller.
- Opens as a **bottom sheet** (§5).

## 2.11 COMBINE

Full spec in §5.4. Short form: open inventory → **long-press an item card (380ms)** to lift it → drag onto another card → release. See ASCII flow in §5.4.

## 2.12 CANCEL — one gesture, everywhere

A single, learned, universal cancel:

| Context | Cancel |
|---|---|
| Bottom sheet open (inventory, slate, sheet) | **Swipe down** anywhere on the sheet, OR tap the dimmed world above it, OR the ✕ at (352, sheet_top + 24) |
| Inspect view | Swipe down, or ✕ |
| Combine drag in progress | Release outside any valid card → item returns home with a 220ms spring. **No penalty, no sound.** |
| Target Wheel | Lift the finger outside the list |
| Climb (first 600ms) | Tap the contextual button again |
| Hold-action in progress | Release the finger before the arc completes; arc unwinds over 200ms |
| Modal dialog (only from player-opened menus) | `✕` and a Back gesture; Android hardware back maps here |
| Reel Deck / playback | ✕, or swipe down; playback continues in the background (that's the point — §3) |
| Cinematic (Body Camera) | **Not cancellable** after the first 1.0s. Tap in the first 1.0s = skip, for repeat players. |

**Android hardware/gesture back** maps to Cancel at every level, and at the root gameplay level it opens the pause menu (never quits).

## 2.13 Auto-walk / tap-to-move — decision

**Decision: NO tap-to-move as the primary locomotion. YES to two bounded assists.**

Reasoning: tap-to-move (point-and-click navmesh) is tempting for a portrait adventure and it is a real accessibility win, but it fights FP hard — the pathing camera has to yaw itself toward the destination, which is exactly the involuntary camera motion that causes sickness, and it makes the hydrophone's "walk slowly while sweeping" core loop impossible.

**Assist 1 — Auto-walk latch (everyone).** Double-tap in the move zone with no drag → Nadia walks forward at 1.45m/s along the current camera forward, hands-free. The camera swipe zone steers her (she walks where you look). Tap anywhere or touch the move zone to stop. This exists so long traverses in Fernmaw don't cramp a thumb, and so one-handed play works on the couch.

**Assist 2 — Travel Points (everyone, after first visit).** Serviced landmarks (a tagged lantern, a hut door, the Combs ladder foot) become **Travel Points** once visited. From the Field Slate map, tap a Travel Point → a **Body Camera** montage transition (2.4s, cut-cut-cut, Nadia walking) → arrive. This is not a loading screen dressed up; it's the Body Camera earning its rig. Disabled during three authored sequences where the walk *is* the content (the Act 1 gully, the approach to the grave, the Quiet Room corridor).

**Assist 3 — Full tap-to-move (Accessibility only).** Under Accessibility → Motor, `Tap to Move` can be enabled. Tapping a floor point walks there via navmesh. Camera yaw does **not** auto-align (she walks sideways/backward if needed) precisely to protect comfort. Clearly labelled as an accessibility option because it changes pacing.

## 2.14 Verb table — every action, exactly how

| Action | Primary input | Secondary | Notes |
|---|---|---|---|
| **Move** | Floating joystick, bottom-left 150×250pt | Auto-walk latch (double-tap move zone) | Squared magnitude curve |
| **Look** | Drag anywhere in the camera zone | Snap-turn taps (opt-in) | No momentum, ever |
| **Interact** | Tap contextual button (318, 716) | Tap the object directly in the camera zone | Both always live |
| **Collect** | Same as interact; verb glyph = `HAND` | — | 3D fly-to-inventory, 340ms |
| **Open inventory** | Tap (318, 792) | Swipe up from bottom bar | Bottom sheet, 3 detents |
| **Combine** | Long-press card 380ms → drag → release on target card | Tap card → `USE WITH…` button in card actions | Drag is primary, button is the discoverable fallback |
| **Run** | Push joystick past 62% | Latches after 2.0s | No button, no stamina |
| **Crouch** | Automatic in authored volumes | — | No button exists |
| **Climb** | Tap contextual button, verb `FOOT` | Tap the ladder directly | On-rails; look retained; cancel in 600ms |
| **Listen (hydrophone)** | **Hold** the contextual button when verb = `EAR`; OR tap the Hydrophone tool slot at (72, 792) to raise it persistently | Pinch to narrow the cone | Act 2+ |
| **Cancel** | Swipe down (sheets) / release outside (drags) / ✕ | Android back | One mental model |

---

# 3. HUD — DIEGETIC MINIMALISM

## 3.1 The rule

> **The HUD is Nadia's attention, not her interface.** Nothing persists that she would not be actively thinking about. The HUD's resting state is **three elements**: the contextual button ring (dim), the inventory button (dim), and the menu dot. Everything else earns its way on screen and leaves.

## 3.2 Persistent element budget

**Never more than 4 persistent interactive elements on screen during gameplay.** Currently 3: contextual button, inventory button, menu dot. The 4th slot is reserved for the Hydrophone tool slot from Act 2 onward. When the Reel Deck is carried (Act 3+) it does **not** take a fifth slot — it replaces the Hydrophone slot, and both are reached via a 2-item arc that opens on long-press of the tool slot.

## 3.3 Top-left — VITALS

Vitals are **not meters**. There are no bars. This is a premium, grounded game and a hydration bar is the single strongest "free-to-play survival" signal in the medium.

Instead: **one glyph per vital, which is invisible until it matters.**

| Vital | Representation | Appears at | Escalation |
|---|---|---|---|
| **Hydration** | A small stylised **drop outline** that fills downward from empty as thirst rises (i.e. the *glyph fills as the problem grows*) | Never below 55% thirst. Fades in over 600ms at 55%. | 55% → outline only, 34% opacity. 30% → 62% opacity + a 4.5s pulse. 15% → 88% + Nadia's voice line + a single haptic `Warning` every 60s. |
| **Warmth** (Rime Shoulder, night shore, wet clothing) | A **three-line chevron** (like heat rising) that loses lines as warmth drops | Only when cold load is active | 3 lines → 2 → 1 → flashing 1 |
| **Recorder battery** | A **9-segment horizontal tick row**, deliberately reading as 1970s gear | Only when the recorder is in hand, or when ≤2 segments remain | At ≤2 it stays on-screen permanently until charged |
| **Injury** | **None.** There is no health bar because there is no combat. A bad fall causes a limp (camera + audio + slower walk) that heals over 4 minutes. Communicated entirely diegetically. | — | — |

Layout, left-aligned, stacked vertically:
```
x = SA.left + 20  (= 20)
y = SA.top + 14   (= 61)   first row
row pitch: 34pt
glyph box: 26 × 26pt, colour #D8D2C4
```
**Vitals are display-only. They are never tappable.** (They live in the unreachable top band — see §2.5.) Full detail lives in the Field Slate.

## 3.4 Top-right — BEARING / TIME / CONDITION

A single **horizontal strip**, right-aligned, that reads like an instrument bezel.

```
Right edge: x = 390 - SA.right - 20 = 370
y = SA.top + 14 = 61
Height: 26pt
```

Contents, right to left:

1. **Condition glyph** (26 × 26) — rain / fog / clear / wind, drawn as 2pt line art. Changes trigger a 700ms cross-dissolve.
2. **Clock** — `04:11` at 15pt tabular. **Nadia's watch time**, 24h, always correct (Fiction Rule 8 — clocks work; never write a broken-instrument beat). Time is a real in-game clock; a day is 48 real minutes.
3. **Bearing tape** — a 92pt-wide horizontal compass tape showing degrees, with cardinal letters at the 8 points, scrolling as the player turns. **1.5pt tick marks**, `N` in `#E8E2D2`, everything else in `#9A9484`. A hairline centre index at the tape's midpoint.

Bearing matters in this game — Sabo's logs cite bearings, the fumarole arc is described by bearing, the Register's flow diagrams are oriented. So the compass is content, not decoration.

**No minimap.** Ever. The map lives in the Field Slate and is a hand-drawn, incomplete, revisable thing Nadia is making. A live minimap would destroy the entire premise that the record is under construction.

## 3.5 Bottom — the action bar

```
Bar band: y ∈ [700, 844 - SA.bottom] = [700, 810]
Never draws below y = 810 (home indicator band 810–844 stays clear)
```

| Element | Centre | Size | Notes |
|---|---|---|---|
| Contextual action button | (318, 716) | 88pt ⌀ | §2.5 |
| Inventory | (318, 792) | 56pt ⌀ / 64 hit | §2.10 |
| Tool slot (Act 2+) | (72, 792) | 56pt ⌀ / 64 hit | Hydrophone; long-press → 2-item arc for Reel Deck |
| Field Slate | (159, 792) | 56pt ⌀ / 64 hit | Opens the evidence system |
| Menu dot | (231, 792) | 40pt ⌀ / 48 hit | Three dots; pause/settings |

Wait — that's 5 bottom elements plus the contextual button. **Correction to meet the persistent-element rule:** the **Field Slate and Menu are merged into one bottom-centre control** at (195, 792), a 56pt slab reading `SLATE`. Tapping opens the Field Slate; the pause/settings menu is the first tab *inside* the Slate ("this is her notebook; settings live in the back cover"). That is diegetic and it keeps the bar at **4 persistent elements**: tool slot, slate, inventory, contextual button.

## 3.6 THE DIEGETIC MINIMALISM RULE — exact fade behaviour

**Idle definition:** no touch input, no movement input, no interactable in range, no vital in warning state, for **`T_idle` = 4.0 s**.

| Element | Idle behaviour | Return trigger | Fade out | Fade in |
|---|---|---|---|---|
| Contextual button (no target) | → 0% opacity | Any target enters range; any touch anywhere | 900ms `InQuad`, starting at T_idle | 140ms `OutQuad` |
| Contextual button (target in range) | **Never fades.** Stays at 78%. | — | — | — |
| Inventory / Slate / Tool slot | → 0% opacity | Any touch in the bottom 180pt; item collected; any sheet closes | 900ms `InQuad` at T_idle | 180ms `OutQuad` |
| Vitals glyphs | → 0% unless in warning state (<34%) | Vital crosses a threshold; Slate opens | 1400ms | 600ms |
| Bearing / clock / condition | → 0% | Player yaws >25° in <1.5s (you're orienting, so you want the compass); touch anywhere; Slate opens | 1200ms `InQuad` at T_idle + 1.5s (this strip fades *last*, 5.5s total) | 260ms |
| Interaction prompt card | Follows its own rules (§4) | — | — | — |
| Subtitles | **Never fade.** Not part of minimalism. | — | — | — |
| Toasts | Own 1.9s timer | — | 400ms | 200ms |

**Full-fade state** (everything gone) is reached at **T_idle + 5.5s ≈ 9.5s** of true stillness. This is the screenshot state, and it is what the trailer is shot in.

**Return-on-demand, explicit:** a **single tap anywhere in the camera zone that hits nothing** brings the entire HUD back at full opacity for a fresh T_idle window. This is the one case where a nothing-tap *does* do something (§2.5 said a nothing-tap is silent — it is silent, but it un-fades the HUD, which is a display change, not an action).

**Hard exception:** the HUD never fades during the first 12 minutes of play (tutorial window), never during an active hold-action, and never while a sheet is open.

## 3.7 HUD ASCII WIREFRAME — 390 × 844, resting state (target in range)

```
┌──────────────────────────────────────────────────────────┐  y=0
│ ░░░░░░░░░░░░  SAFE AREA / STATUS / ISLAND  ░░░░░░░░░░░░░ │
│ ░░░░░░░░░░░░░░░░░░  (Dynamic Island)  ░░░░░░░░░░░░░░░░░░ │
├──────────────────────────────────────────────────────────┤  y=47  SA.top
│                                                          │
│  ◍                            ☁  04:11  ┤·│·┤·N·┤·│·┤   │  y=61
│  ░                                       └ bearing tape  │
│  ↑hydration (only if <55%)                               │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                       W O R L D                          │
│                                                          │
│              (camera swipe zone = everything              │
│               not a thumb zone, not a panel)             │
│                                                          │
│                                                          │
│                                                          │
│                     ╭──────────────────────────╮         │  y=596
│                     │  BRASS TAG · L-7         │         │  ← prompt card
│                     │  Stamped 08·94. Re-wicked│         │     §4
│                     │  three days ago.         │         │
│                     │  ─────────────── ◉ EXAMINE│        │
│                     ╰──────────────────────────╯         │  y=692
│  ╭ ─ ─ ─ ╮                                     ╭──────╮  │
│  │  ⊙    │  ← floating joystick               │  ◉   │  │  y=716
│  │ (ghost)│     spawns under thumb             │ EYE  │  │  ← 88pt
│  ╰ ─ ─ ─ ╯                                     ╰──────╯  │
│                                                          │
│   ( 🎧 )          [ S L A T E ]           ( ▣ )          │  y=792
│   tool slot         56pt slab           inventory        │
│                                                          │
├──────────────────────────────────────────────────────────┤  y=810
│ ░░░░░░░░░░░░░  home indicator — KEEP CLEAR  ░░░░░░░░░░░░ │
└──────────────────────────────────────────────────────────┘  y=844
 x=0                                                    x=390
```

**Fully-idle state (T_idle + 5.5s) — the screenshot state:**

```
┌──────────────────────────────────────────────────────────┐
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
├──────────────────────────────────────────────────────────┤
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                       W O R L D                          │
│                    (nothing else)                        │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
├──────────────────────────────────────────────────────────┤
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
└──────────────────────────────────────────────────────────┘
```

---

# 4. INTERACTION PROMPT

## 4.1 Design intent

The prompt is a **card of evidence**, not a button label. Every prompt in this game is a chance to say something specific about maintenance, age, or who touched this last. "Open door" is banned. "Hut 3 door — latch replaced, hinge pin new" is the standard.

## 4.2 Position and the world-occlusion rule

The card sits in the **lower third but above the thumb band**: its bottom edge at **y = 692**, which is 24pt above the contextual button's top edge (716 − 44 = 672… card bottom 692 overlaps by 20pt). **Correction:** card bottom edge = **y = 664**, giving 8pt clearance above the 88pt button whose top edge is at 672.

```
Card frame:
  left:   x = SA.left + 24  = 24
  right:  x = 390 - SA.right - 24 = 366   → width 342
  bottom: y = 664
  height: auto, 68–116pt (1-line to 3-line description)
  → top edge floats between y = 548 and y = 596
```

**Why lower-centre, not centre-screen:** the reticle area (screen centre, y≈445) must stay clear — you are aiming with it. Lower-centre keeps the card in the natural downward glance and, critically, **the object being described is in the upper two-thirds where the player is looking**, so the card never covers its own subject. Level design gets a rule: **an interactable's screen-space bounding box must not intersect `y > 540` at the natural approach distance.** The editor tool that checks interact cones also checks this and warns.

**World-occlusion mitigations:**
- Card background is **not opaque**. It is a 14% black fill with a **backdrop blur of 8px** (URP: a small downsampled blur, rendered once per frame into a 1/4-res RT — measured cost target **< 0.35ms on iPhone 12**; if it exceeds that on the low-end profile, the fallback is a vertical gradient scrim with no blur, which is the 30fps-tier default).
- The card has **no border and no corner radius above 4pt** — it reads as a caption, not a dialog.
- If the described object's screen bounds *do* intersect the card (dynamic case, e.g. player walks very close), the card **slides down 40pt and drops to a single line** (name only, no description) for as long as the overlap persists. Transition: 200ms.

## 4.3 Anatomy and typography

```
╭──────────────────────────────────────────────────────────╮
│                                                          │  ← 14pt pad
│  BRASS TAG · L-7                                         │  ← NAME
│                                                          │
│  Stamped 08·94. Wick replaced three days ago; the         │  ← DESC
│  solder on the hasp is still bright.                      │
│                                                          │
│  ──────────────────────────────────────────────────────  │  ← 1pt rule, 22% op
│                                          ◉  E X A M I N E │  ← ACTION
│                                                          │
╰──────────────────────────────────────────────────────────╯
   ↑24                                                  366↑
```

| Element | Font | Size | Weight | Tracking | Colour | Notes |
|---|---|---|---|---|---|---|
| **NAME** | Sans (UI face) | **17pt** | Medium | +0.04em | `#F2EEE2` | All caps for object class, mixed case for proper designations. Max 2 lines, truncate with `…`. Middot separator for a designation suffix. |
| **DESC** | Sans | **13.5pt** | Regular | 0 | `#C6C0B0` | Line height 19pt. **Max 3 lines, hard cap.** Writers get a 120-character budget. If it needs more, it's an Examine, not a prompt. |
| **ACTION** | Sans | **12pt** | SemiBold | **+0.16em** | `#E8E2D2` | All caps, letterspaced wide. Preceded by the verb glyph at 18×18. |
| **Divider** | — | 1pt | — | — | `#FFFFFF` @ 22% | Full card width minus padding |

**Dynamic Type:** all three scale with the system text-size setting via a single `TextScale` multiplier (0.85× – 1.60×, §8). At 1.60× the DESC drops to 2 lines and the card grows to 138pt. At >1.35× the card's `bottom` moves to y = 640 to keep clearance.

## 4.4 Show / hide animation

| Phase | Duration | Curve | What moves |
|---|---|---|---|
| **Show** | 180ms | `OutCubic` | Card translates **up 12pt** from y+12 → y, opacity 0 → 100%. NAME appears immediately; DESC has a **60ms stagger**; ACTION row has a **110ms stagger**. The contextual button ring draws in clockwise over 140ms, synced to the card. |
| **Target swap** (new target while a card is shown) | 130ms | `InOutQuad` | Card does **not** hide and re-show. Text cross-fades in place (old 0% over 70ms, new 100% over 90ms, 40ms overlap) and the card **height animates** to the new content over 130ms. This is the single most important polish detail — cards flickering on every micro-turn is the #1 way this HUD feels cheap. |
| **Hide** | 220ms | `InQuad` | Opacity → 0, translate **down 8pt**. No scale. |
| **Hysteresis** | — | — | A target must be lost for **280ms** before the card hides. Prevents flicker at cone edges. A target must be held for **90ms** before the card shows. |

**Audio:** show = a single soft `tick` (−28 LUFS, 40ms, no pitch variation). Target swap = no sound. Hide = no sound. Haptic on show = `Selection` (the lightest available), and **only if the player is stationary** — haptics while walking read as noise.

## 4.5 Distance behaviour

The prompt has **three tiers**, so distance is legible without a health-bar-style range meter.

| Distance | Tier | Card content | Opacity |
|---|---|---|---|
| **> 4.5 m** | Out of range | **Nothing.** No card, no button. | — |
| **2.2 – 4.5 m** | **Aware** | NAME only, single line, 44pt-tall card. No description, no action row. Contextual button ring is at 34% with **no glyph**. | Card 62% |
| **≤ 2.2 m** | **Actionable** | Full card: NAME + DESC + ACTION. Button ring 78% with glyph. | Card 100% |

The transition Aware → Actionable is the same 130ms height animation as a target swap, so walking up to an object makes the card **unfold**. That unfolding is a deliberate reward gesture: it is the small pleasure of arriving.

**Read-at-distance rule:** at the Aware tier the NAME must be legible at arm's length on a 390pt screen, so Aware-tier names are capped at **22 characters**. Writers get a short-name field per object.

## 4.6 ASCII WIREFRAME — the three tiers

```
── AWARE (2.2–4.5 m) ────────────────────────────────────────
┌──────────────────────────────────────────────────────────┐
│                                                          │
│                       W O R L D                          │
│                          ▯  ← the object                 │
│                                                          │
│              ╭────────────────────────────╮              │ y=620
│              │  BRASS TAG · L-7           │              │
│              ╰────────────────────────────╯              │ y=664
│                                             ╭────────╮   │
│                                             │  ○     │   │ ← ring, no glyph
│                                             ╰────────╯   │
└──────────────────────────────────────────────────────────┘

── ACTIONABLE (≤ 2.2 m) ─────────────────────────────────────
┌──────────────────────────────────────────────────────────┐
│                                                          │
│                       W O R L D                          │
│                       ▯▯▯  ← object, upper 2/3           │
│                                                          │
│  ╭────────────────────────────────────────────────────╮  │ y=548
│  │                                                    │  │
│  │  BRASS TAG · L-7                                   │  │ 17pt
│  │                                                    │  │
│  │  Stamped 08·94. Wick replaced three days ago;      │  │ 13.5pt
│  │  the solder on the hasp is still bright.           │  │
│  │  ────────────────────────────────────────────────  │  │
│  │                                    👁  E X A M I N E│  │ 12pt +0.16em
│  ╰────────────────────────────────────────────────────╯  │ y=664
│                                             ╭────────╮   │
│                                             │  👁    │   │ y=672
│                                             ╰────────╯   │ y=760
│   ( 🎧 )        [ S L A T E ]          ( ▣ )            │ y=792
└──────────────────────────────────────────────────────────┘

── HOLD-ACTION variant ──────────────────────────────────────
│  │  ────────────────────────────────────────────────  │  │
│  │                            🔧  H O L D   T O  T U R N│  │
│  ╰────────────────────────────────────────────────────╯  │
│                                             ╭────────╮   │
│                                             │ ◜◝ 🔧  │   │ ← progress arc
│                                             ╰────────╯   │    rides the ring
```

---

# 5. INVENTORY

## 5.1 Decision: BOTTOM SHEET with three detents — NOT full screen

**Reasoning:**
1. This game's core verb is *comparing an item to the world*. A full-screen inventory severs that. With a bottom sheet at the medium detent, you can hold the cordage up against the ladder rung you're standing at.
2. A full-screen panel over gameplay violates the UI rule set (§9, rule 4) more often than a sheet does, because it forces a hard context switch for a 2-second lookup.
3. Portrait is the ideal shape for a sheet: vertical drag is the natural gesture, and the sheet's grabber lands exactly in the thumb arc.
4. The **Expanded detent is functionally full-screen** when the player wants it (combine work, tab browsing), so we lose nothing.

| Detent | Sheet top edge (y) | Height | Use |
|---|---|---|---|
| **Peek** | 620 | 190pt visible | One row of items. Auto-detent when an inventory item becomes relevant to a nearby interactable. |
| **Half** (default) | 430 | 380pt | 2 rows + tabs. This is what the button opens to. |
| **Expanded** | **SA.top + 60 = 107** | 703pt | 5 rows, combine workspace, inspect. World dims to 62% black behind it. |

Drag the grabber to move between detents. Velocity-based: fling up >900pt/s → Expanded; fling down >900pt/s → dismiss. Detents snap with a 280ms `OutBack(0.6)` spring.

**World behind the sheet:** at Peek and Half the world **keeps rendering at full quality and the game does not pause** (adventure game, no threats — Fiction Rule 3 means there is no reason to pause). At Expanded the world renders at half resolution behind the dim to reclaim GPU for the 3D inspect turntable. Camera look is disabled whenever a sheet is above Peek; the joystick is disabled at Half and above.

## 5.2 Grid metrics

```
Content area: x ∈ [20, 370]  → 350pt usable
Columns: 4
Gutter: 10pt
Cell: (350 - 3×10) / 4 = 80pt wide
Cell height: 96pt   (80 art + 16 label strip)
Row pitch: 106pt (96 + 10)
```

Four columns on a 390pt screen is the right density: an 80pt cell renders a recognisable object at a glance, and the whole cell is a 80×96 hit target — nearly double the 48dp minimum. Five columns would give 66pt cells; too small for "is that the brass wire or the copper wire".

## 5.3 Card anatomy

```
╭──────────────╮  80 × 96
│              │
│      ▣       │   ← 3D-rendered thumbnail, 64×64, lit by a
│              │     fixed 3-point rig, rendered once and cached
│              │     to an atlas; re-rendered only on state change
│ ▪            │   ← STATE DOT 8pt, top-left inset 6,6  (see below)
│           ×2 │   ← QUANTITY 11pt, bottom-right of art area,
│              │     only shown when >1
├──────────────┤
│  CORDAGE     │   ← LABEL 11pt SemiBold, +0.02em, 1 line,
╰──────────────╯     truncate with …; colour #DDD7C8
```

| State dot | Meaning | Colour | Shape (colourblind-safe, §8) |
|---|---|---|---|
| (none) | Ordinary item | — | — |
| Filled circle | **New** — collected, not yet inspected | `#E8E2D2` | ● solid circle |
| Open square | **Component** — known to combine with something | `#B9C6C4` | ▢ open square |
| Half-filled bar | **Consumable** — has a charge/quantity that depletes | `#D2C29A` | ▮ half bar |
| Triangle | **Degrading** — wet, corroding, or spoiling; has a timer | `#C99A82` | ▲ triangle |

Shape carries the meaning; colour is redundant reinforcement. This is the colourblind contract.

**Tabs** — a 5-tab strip pinned to the sheet top, below the grabber:

```
╭──── ▁▁▁▁▁▁ ────────────────────────────────────── ✕ ──╮   grabber + close
│                                                        │
│  ALL │ TOOLS │ MATERIALS │ DOCUMENTS │ SAMPLES         │   tabs
│  ▔▔▔                                                   │   2pt underline
```

- Tab row height **44pt**, each tab a **≥64pt** hit target, horizontally scrollable if labels overflow at large text sizes.
- **DOCUMENTS** is where journals, contact sheets, reels and tags go. It is the bridge into the Field Slate — each document card has a `→ SLATE` action that files it as evidence.
- **SAMPLES** holds physical evidence with a Register reading (Act 4+).
- Tab state persists across opens within a session.

## 5.4 ASCII WIREFRAME — the grid (Half detent, default open)

```
┌──────────────────────────────────────────────────────────┐  y=0
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
├──────────────────────────────────────────────────────────┤  y=47
│                                                          │
│                                                          │
│                    W O R L D                             │
│                (still live, not paused,                  │
│                 not dimmed at Half)                      │
│                                                          │
├──────────────────────────────────────────────────────────┤  y=430  ← sheet top
│                    ▁▁▁▁▁▁▁                          ✕    │  grabber (36×4)
│                                                          │  y=452
│  ALL   TOOLS   MATERIALS   DOCUMENTS   SAMPLES           │  y=474 (44pt row)
│  ▔▔▔                                                     │
├──────────────────────────────────────────────────────────┤  y=498
│                                                          │
│  ╭──────╮  ╭──────╮  ╭──────╮  ╭──────╮                 │  y=508
│  │●  ▣  │  │   ▣  │  │▢  ▣  │  │▲  ▣  │                 │
│  │      │  │   ×3 │  │      │  │      │                 │
│  ├──────┤  ├──────┤  ├──────┤  ├──────┤                 │
│  │CORD- │  │DRIFT-│  │HYDRO-│  │FIELD │                 │  y=588
│  │AGE   │  │WOOD  │  │PHONE │  │REC.  │                 │
│  ╰──────╯  ╰──────╯  ╰──────╯  ╰──────╯                 │  y=604
│                                                          │
│  ╭──────╮  ╭──────╮  ╭──────╮  ╭──────╮                 │  y=614
│  │   ▣  │  │▮  ▣  │  │   ▣  │  │      │                 │
│  ├──────┤  ├──────┤  ├──────┤  │  +   │  ← empty slot   │
│  │MULTI-│  │MATCH-│  │TARP  │  │      │     (dashed)    │
│  │TOOL  │  │ES ×7 │  │SCRAP │  ╰──────╯                 │
│  ╰──────╯  ╰──────╯  ╰──────╯                           │  y=710
│                                                          │
│                    ▔▔▔▔▔  scroll for more                │
├──────────────────────────────────────────────────────────┤  y=810
│ ░░░░░░░░░░░░░░░░  home indicator  ░░░░░░░░░░░░░░░░░░░░░░ │
└──────────────────────────────────────────────────────────┘  y=844
     80pt cells, 10pt gutters, x-origins: 20, 110, 200, 290
```

## 5.5 The Inspect view — YES to a 3D turntable, with conditions

**Decision: yes, a real 3D turntable — but it is a *reading* view, not a toy.**

Justification: the whole game is "read the object, find the date, find the tool mark". A 2D illustration cannot show you that the solder on the hasp is bright while the rest is green. The turntable is the mechanic.

**Performance conditions (binding):**
- Inspect renders on a **dedicated camera to a 340×340 RT** (not full screen), forward renderer, **one shadow-casting light + one fill**, no post except a tonemap and a slight bloom.
- Inspect meshes are the **world LOD0 mesh**, capped at **14k tris**. Items with a higher world mesh get a bespoke inspect mesh.
- Behind the Expanded sheet the world renders at **half res** and **camera culling is frozen**. Target: inspect view holds 60fps on iPhone 12 with ≥4ms headroom. On the 30fps fallback profile, inspect locks to 30fps and the world behind is **frozen to a single captured frame** (no re-render at all).

**Controls:**
- **Drag** anywhere on the model → orbit. Yaw unlimited, pitch clamped ±72°. Momentum: yes here (this is a held object, not a camera — 0.86 damping, ~1.1s to rest).
- **Pinch** → zoom, 0.7× to 3.2×.
- **Double-tap** → snap back to the authored "reading pose" over 380ms. Every item has an authored pose that shows its most informative face.
- **Hotspots:** up to 4 per item, drawn as **9pt rings** that appear only when the relevant surface faces the camera within 55°. Tapping a hotspot slides a caption strip up from the bottom of the RT. This is how "Stamped 08·94" is delivered.

```
── INSPECT VIEW (Expanded detent) ───────────────────────────
┌──────────────────────────────────────────────────────────┐  y=0
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
├──────────────────────────────────────────────────────────┤  y=47
│                 (world, half-res, dimmed 62%)            │
├──────────────────────────────────────────────────────────┤  y=107  ← Expanded
│  ‹ BACK                ▁▁▁▁▁▁▁                      ✕    │  y=127
│                                                          │
│  BRASS MAINTENANCE TAG                                   │  y=162  20pt
│  Orrimond Trust · caretaker rotation                     │  y=184  12pt, 62%
│                                                          │
│   ╭────────────────────────────────────────────────╮     │  y=212
│   │                                                │     │
│   │                    ╭───╮                       │     │
│   │               ○────┤ ▣ ├────○  ← hotspot rings │     │  340×340 RT
│   │                    ╰───╯          9pt, appear  │     │
│   │                      ○            when facing  │     │
│   │                                                │     │
│   │        drag to turn · pinch to zoom            │     │  y=530
│   │        double-tap to reset                     │     │
│   ╰────────────────────────────────────────────────╯     │  y=552
│                                                          │
│  ╭────────────────────────────────────────────────────╮  │  y=566
│  │ HOTSPOT 2 — reverse face                           │  │  caption strip
│  │ "L-7 · 08·94". The 8 is struck twice; the die      │  │  13.5pt
│  │ slipped. Every tag after 1994 has the same fault.  │  │
│  ╰────────────────────────────────────────────────────╯  │  y=644
│                                                          │
│  ╭──────────────╮ ╭──────────────╮ ╭─────────────────╮  │  y=664
│  │  USE WITH…   │ │  → SLATE     │ │   DROP          │  │  56pt tall
│  ╰──────────────╯ ╰──────────────╯ ╰─────────────────╯  │  y=720
│                                                          │
│  Notes: three tags on this run share a die fault.        │  y=740 12pt 54%
│         → cross-referenced in Field Slate                │
├──────────────────────────────────────────────────────────┤  y=810
└──────────────────────────────────────────────────────────┘
```

`DROP` is a destructive-ish action: it is **always reversible** — dropped items land at Nadia's feet as a world interactable and are never deleted. Quest-critical items refuse to drop with a one-line inline message (not a modal): *"Not this one."* (§9 rule 8.)

## 5.6 THE COMBINE FLOW

This is the heart of the inventory and the place where the game either feels like experimentation or like guessing. The design goal:

> **Every attempt must produce information.** An invalid combination is a *result*, not an *error*.

### Gesture sequence — the primary path (drag)

```
STEP 1 — ARM
  Long-press any card for 380ms.
  · At 0ms:    card scales 1.00
  · At 140ms:  a 2pt ring begins drawing around the card (radial, 240ms)
  · At 380ms:  ring completes → card LIFTS
               - scale 1.00 → 1.12 over 120ms OutBack
               - drop shadow appears (y+6, blur 18, 34% black)
               - haptic: MediumImpact, once
               - ALL OTHER CARDS re-render:
                   · known-compatible partners → normal opacity, 2pt ring at 40%
                   · everything else            → 46% opacity
                 ⚠ CRITICAL: "known-compatible" means the player has
                   ALREADY discovered this pairing, or the game has
                   explicitly taught the component class. It does NOT
                   pre-reveal undiscovered recipes. Undiscovered valid
                   partners look identical to invalid ones. The dimming
                   is a memory aid, never a solution.

STEP 2 — CARRY
  Drag. The lifted card follows the finger with 1-frame lag.
  A 2pt hairline tether draws from the card's origin slot to the card.
  Cards passed over scale to 1.06 (hover) with a 90ms ease.

STEP 3 — RELEASE
  Lift the finger over a target card.
  → resolve (see below)
```

### Alternative path — the button (discoverability + motor accessibility)

Tap a card → Inspect → `USE WITH…` → the grid re-renders in **selection mode** with a persistent header `USE CORDAGE WITH…` and a `CANCEL` button. Tap any second card to attempt. Identical resolution. This path is fully keyboard/switch-accessible and requires no drag.

### Resolution — VALID

```
1. Both cards snap together at the centre of the sheet (180ms, OutCubic)
2. A 1pt horizontal light-line sweeps across them (220ms)
3. Both cards collapse to 0 scale (140ms) as the RESULT card scales
   0 → 1.06 → 1.00 (260ms, OutBack)
4. Haptic: SuccessNotification
5. Audio: a physical, specific sound — NOT a chime. Cordage on a haft
   is rope-on-wood. This is a rule: every successful combine plays the
   real-material sound of that assembly. No generic "craft success" stinger
   exists in the project.
6. Result card holds centre for 900ms with its name at 15pt beneath it,
   then flies to its grid slot (320ms)
7. If the result is story-significant → escalate to a Discovery (§6)
```

### Resolution — INVALID (the important one)

**It must feel like experimentation, not punishment.** Therefore:

- **No red.** No shake. No buzzer. No "That doesn't work!" No error modal.
- The two cards **touch, pause for 160ms, and part** — a 200ms `OutQuad` return to their slots with a slight overshoot. It reads as *"she held them together, turned them over, and set them down."*
- Haptic: **`Selection`** only — the lightest tick, the same one a valid target hover gets. Not a warning haptic.
- Audio: the real sound of the two materials touching once, at −32 LUFS. Metal on metal clicks. Rope on cloth is almost silent.
- A **12pt caption** appears at the bottom of the sheet for 2.4s, 58% opacity, and it is **written per item-pair class, never generic**:

| Pair class | Caption |
|---|---|
| Two inert materials | `Nothing holds.` |
| Tool + wrong substrate | `The blade skates off it.` |
| Needs a third thing | `It would need something to bind it.` |
| Right idea, wrong state | `Too wet. Not yet.` |
| Right idea, missing knowledge | `She turns it over twice and puts it down.` |
| Genuinely absurd | `No.` |

There are **~14 caption classes**, authored by the writer, assigned per-pair-category in data. A generic fallback exists (`Nothing holds.`) but any pairing the player is plausibly likely to try gets a bespoke line. This is a **content commitment**: the writer owns a combine-caption pass.

- **The near-miss tier.** If a pairing is **one step away** (the recipe exists but a prerequisite item/state is missing), the caption uses the `Needs a third thing` / `Right idea, wrong state` classes **and** the missing prerequisite's *category* is hinted — `It would need something to bind it.` This is the difference between a puzzle and a lottery.
- **Attempt memory.** Failed attempts are recorded. Trying the same invalid pair a second time shows the same caption but **skips the animation** (instant part, 80ms) so re-testing is fast and never feels like a scolding. Trying it a **third** time adds, at 54% opacity: `(tried)`.
- **The Field Slate logs near-misses.** Any `Needs a third thing` result writes a line into the Slate's Working Notes: *"Cordage + haft — needs a binder."* The player can revise or strike it later. This directly serves the theme: the record is built from failed attempts, and it is revisable.

### ASCII WIREFRAME — the combine flow

```
── STEP 1: ARM (long-press 380ms) ───────────────────────────
┌──────────────────────────────────────────────────────────┐
├──────────────────────────────────────────────────────────┤ y=430
│                    ▁▁▁▁▁▁▁                          ✕    │
│  ALL   TOOLS   MATERIALS   DOCUMENTS   SAMPLES           │
├──────────────────────────────────────────────────────────┤
│  ╔══════╗  ╭┄┄┄┄┄┄╮  ╭┄┄┄┄┄┄╮  ╭──────╮                 │
│  ║  ▣   ║  ┊  ▣   ┊  ┊  ▣   ┊  │  ▣   │                 │
│  ║      ║  ┊      ┊  ┊      ┊  │      │                 │
│  ╠══════╣  ├┄┄┄┄┄┄┤  ├┄┄┄┄┄┄┤  ├──────┤                 │
│  ║CORD- ║  ┊DRIFT ┊  ┊MATCH ┊  │DRIFT-│                 │
│  ║AGE   ║  ┊SPAR  ┊  ┊ES    ┊  │WOOD  │                 │
│  ╚══════╝  ╰┄┄┄┄┄┄╯  ╰┄┄┄┄┄┄╯  ╰──────╯                 │
│   LIFTED     dimmed    dimmed    RINGED                  │
│   1.12×      46%       46%       (known partner)         │
│                                                          │
│   ▸ haptic MediumImpact · shadow y+6 blur18              │
└──────────────────────────────────────────────────────────┘

── STEP 2: CARRY ────────────────────────────────────────────
│  ╭┄┄┄┄┄┄╮            ╭──────╮                            │
│  ┊ ·  · ┊┄┄┄┄┄┄┄┄╮   │  ▣   │ ← 1.06× hover             │
│  ┊origin┊        ┊   │      │                            │
│  ╰┄┄┄┄┄┄╯   ╔════╧═╗ ├──────┤                            │
│   tether ─→ ║  ▣   ║ │DRIFT-│                            │
│             ║CORD- ║ │WOOD  │                            │
│             ╚══════╝ ╰──────╯                            │
│              under                                       │
│              the finger                                  │

── STEP 3a: VALID ───────────────────────────────────────────
│                                                          │
│              ╔═══════════════╗                           │
│              ║               ║  ← light-line sweep       │
│              ║      ▣▣       ║     220ms                 │
│              ║               ║                           │
│              ╚═══════════════╝                           │
│                                                          │
│               C U T T I N G   E D G E                    │  15pt, 900ms hold
│                                                          │
│   ▸ SuccessNotification · rope-on-wood SFX (real sound)  │

── STEP 3b: INVALID ─────────────────────────────────────────
│                                                          │
│         ╭──────╮ ╭──────╮                                │
│         │  ▣   │ │  ▣   │   ← touch, 160ms pause,        │
│         ╰──────╯ ╰──────╯      then part (200ms)         │
│                                                          │
│   ▸ Selection haptic only · materials-touch SFX −32 LUFS │
│                                                          │
│  ╭────────────────────────────────────────────────────╮  │
│  │  It would need something to bind it.               │  │ 12pt, 58%, 2.4s
│  ╰────────────────────────────────────────────────────╯  │
│                                                          │
│     ↳ writes to Field Slate → Working Notes              │
│       "Spar + edge — needs a binder."                    │
└──────────────────────────────────────────────────────────┘
```

### Three-item combines

Some assemblies (the Catchment) need three parts. **We never require a 3-finger gesture.** Instead: combining A+B produces an explicit **intermediate item** with its own name (`LASHED FRAME`), which then combines with C. Every recipe in the game is a chain of binary steps. This is a hard constraint on the design team — it keeps the gesture vocabulary at exactly one.

---

# 6. DISCOVERY NOTIFICATION

## 6.1 What qualifies

A Discovery is **not** an item pickup. It fires for exactly four things:

1. A **document** entering the record (a Poulter page, a Sabo reel, a Solheim variance report, a contact sheet).
2. A **Field Slate revision** — a previously-logged conclusion is overturned by new evidence. *(This is the thematic centre of the game and gets the strongest treatment.)*
3. A **first entry into a zone**.
4. A **mechanism understood** — the Register decodes a new notation class; a damper gate's function becomes readable.

Budget: **roughly 40–48 Discoveries across an 8-hour game.** One every ~10 minutes. If it fires more often it stops being cinematic. The design tool tracks Discovery density per zone and flags any 5-minute window with more than one.

## 6.2 Timing and interruption policy

> **A Discovery never takes control away, and never pauses the game.**

Because there is no combat and no failure state, the correct treatment is a **cinematic overlay that the player continues to play under**. Movement, look and interaction all remain live throughout. If the player walks away mid-Discovery, the card simply completes its timeline and leaves.

**The exception, and the only one:** a **Revision** (type 2) briefly **slows player walk speed to 40% for 1.8s** and drops the world audio to −9dB. It does not stop input. It is a held breath, not a cutscene. This is authored, deliberate, and used ~9 times in the game.

| Phase | Time | What happens |
|---|---|---|
| **Pre-roll** | 0 – 420ms | World audio ducks −6dB over 420ms. A single **low sustained tone** enters (per-zone pitch: Fernmaw = 92Hz, the Sweatways = 61Hz, Rime Shoulder = 220Hz — the island's zones have pitches and this is where the player learns that). HUD fades to 0 over 300ms except subtitles. |
| **Entry** | 420 – 900ms | A **1pt horizontal rule** draws from screen centre outward to x=44 and x=346 (480ms, `OutExpo`). |
| **Title** | 900 – 1500ms | The category word (`DOCUMENT` / `REVISION` / `ZONE` / `MECHANISM`) fades in above the rule at **11pt, +0.24em tracking, 54% opacity**, over 260ms. Then the **title** fades in below at **24pt, Light weight**, letter-by-letter with a 22ms stagger. |
| **Body** | 1500 – 2400ms | A single line of **14pt** context fades in, 340ms. Max 90 characters. |
| **Hold** | 2400 – 4600ms | Everything holds. The world is still live behind it. |
| **Exit** | 4600 – 5300ms | Text fades over 400ms; the rule retracts to centre over 520ms `InExpo`; the tone releases over 900ms; audio returns to 0dB over 700ms; HUD returns over 300ms. |
| **Total** | **5.3s** | — |

**Skip:** tap anywhere → jump immediately to the Exit phase (0.7s tail). The Discovery is still logged. A player who taps through 40 of them loses nothing mechanically.

**Auto-skip:** if the player is **running** when it fires, the whole timeline compresses to **2.6s** (Hold drops to 400ms). Respect the player's momentum.

## 6.3 Audio design

- One tone, per-zone pitch, **sine with a 3rd harmonic at −18dB**, 2.1s attack, 1.4s release. No reverb tail added — it uses the room's real reverb bus, so a Discovery in the Sweatways *sounds like the Sweatways*.
- **No stinger, no swell, no orchestral hit.** This is the premium/grounded rule.
- Haptic: one `SoftImpact` at the Title phase (t=900ms). Nothing else. **A Revision gets two**, 240ms apart — a heartbeat.
- On a Revision, the tone is **a semitone below** the zone pitch. Players will not consciously notice; they will feel it.

## 6.4 Queuing

Discoveries **never overlap**. A `DiscoveryQueue` singleton:

1. Max queue depth **3**. Overflow beyond 3 is **collapsed**, not dropped: the 4th+ are logged silently and a single `+3 MORE IN THE SLATE` line appears under the last card's body text.
2. Inter-card gap: **700ms** of clean world audio between Exit-complete and the next Pre-roll. The rule retracts fully before the next one draws. Never a cross-fade between two Discoveries.
3. **Priority reorder:** a `REVISION` always jumps the queue to position 0. Everything else is FIFO.
4. If the player opens a sheet or enters a Body Camera cinematic, the queue **pauses** and resumes 900ms after the sheet closes / the cinematic cuts out.
5. Queued-but-unseen Discoveries put a **1pt dot** on the SLATE button. It clears when the Slate is opened.
6. Hard limit: if the queue would exceed 3 twice within 90 seconds, that's a level-design bug. The editor flags it.

## 6.5 ASCII WIREFRAME

```
── DISCOVERY, at t = 2400ms (Hold phase) ────────────────────
┌──────────────────────────────────────────────────────────┐  y=0
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
├──────────────────────────────────────────────────────────┤  y=47
│                                                          │
│                                                          │
│           W O R L D   —   S T I L L   L I V E            │
│        (player can walk, look, interact throughout;      │
│         HUD has faded to 0; audio ducked −6dB)           │
│                                                          │
│                                                          │
│                                                          │
│                     D O C U M E N T                      │  y=382  11pt +0.24em 54%
│                                                          │
│  ────────────────────────────────────────────────────    │  y=404  1pt rule
│                                                          │           x:44→346
│              Sabo — Reel 14, Line Three                  │  y=436  24pt Light
│                                                          │
│         "I have now fixed it twice."                     │  y=472  14pt, 68%
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
│                                                          │
├──────────────────────────────────────────────────────────┤  y=810
└──────────────────────────────────────────────────────────┘  y=844

── REVISION variant (the heavy one) ─────────────────────────
│                                                          │
│                     R E V I S I O N                      │  same slot
│  ────────────────────────────────────────────────────    │
│           The Combs were never burial works              │  24pt Light
│                                                          │
│     Struck from the Slate: "Cormorant funerary terrace"   │  13pt, 52%
│     ─────────────────────────────────────────────         │  ← strikethrough
│                                                          │     drawn L→R, 600ms
│                                                          │
│  ▸ walk speed 40% for 1.8s · world audio −9dB            │
│  ▸ tone a semitone below the zone pitch                  │
│  ▸ two SoftImpact haptics, 240ms apart                   │
└──────────────────────────────────────────────────────────┘

── QUEUE OVERFLOW ───────────────────────────────────────────
│              Sabo — Reel 14, Line Three                  │
│         "I have now fixed it twice."                     │
│                                                          │
│                 + 3   M O R E   I N   T H E   S L A T E  │  11pt +0.2em 44%
```

---

# 7. MAIN MENU + NEW GAME FLOW

## 7.1 The background

**A live 3D scene, not a video.** A single fixed camera in the Quiet Room — but **before** the player has been there, the scene is the Ribcage at dawn, six hulls in silhouette, tide moving. The camera holds a locked-off shot with a **very slow 0.4°/min drift** and real rain/fog. Cost is controlled: the menu scene is its own lightweight scene (< 180k tris, 1 realtime light, baked everything else), and it loads in under 2s on an iPhone 12 target.

**The menu scene changes with progress** — 5 variants, unlocked by act: Ribcage dawn → Fernmaw canopy → Fold Camp washing line → the Combs face → the Quiet Room ceiling. This is the single cheapest way to make a menu feel like a game that remembers you. After completing the game once, the variant is the ending the player chose.

**Audio:** the zone's real ambience bed, no music. The title stinger is one low tone, once, on first load only.

## 7.2 Layout

```
┌──────────────────────────────────────────────────────────┐  y=0
│ ░░░░░░░░░░░░░░  (Dynamic Island)  ░░░░░░░░░░░░░░░░░░░░░░ │
├──────────────────────────────────────────────────────────┤  y=47
│                                                          │
│          LIVE 3D SCENE — THE RIBCAGE, DAWN               │
│          fixed camera, 0.4°/min drift, real rain         │
│                                                          │
│                                                          │
│                                                          │
│              T H E   F O R G O T T E N                   │  y=286  32pt Light
│                      I S L E                             │  y=330  +0.18em
│                                                          │
│      SURVIVE THE ISLAND. DISCOVER THE TRUTH.             │  y=368  11pt +0.22em 48%
│                                                          │
│                                                          │
│                                                          │
│  ╭────────────────────────────────────────────────────╮  │  y=548
│  │  CONTINUE                                          │  │  ← 64pt tall
│  │  Act 3 · Fold Camp · Day 4, 19:20 · 61% recorded   │  │     PRIMARY
│  ╰────────────────────────────────────────────────────╯  │  y=612
│                                                          │
│       N E W   E X P E D I T I O N                        │  y=644  15pt
│                                                          │
│       C H A P T E R S                                    │  y=692  15pt
│                                                          │
│       S E T T I N G S                                    │  y=740  15pt
│                                                          │
│                                              ⓘ  ⚙       │  y=782  credits/access
├──────────────────────────────────────────────────────────┤  y=810
└──────────────────────────────────────────────────────────┘
```

**Hierarchy is unambiguous:**
- **CONTINUE** is the only element with a container. Full-width slab, 64pt tall, 8% fill, 1pt 24% stroke. It is the default action, sitting in the thumb arc at y=580.
- Everything else is **text-only, 15pt, +0.14em, 78% opacity**, 48pt row pitch, each with a **56pt hit target**. No boxes. The menu reads as a list of choices on a photograph.
- On a **fresh install** CONTINUE is absent and `NEW EXPEDITION` promotes into the slab at y=548.
- `CHAPTERS` is greyed until Act 2 is reached. It is **not hidden** — showing a locked entry tells the player the game has chapters.

## 7.3 What CONTINUE shows

The subline is a **status line, not a save slot**. Four fields, middot-separated, 12pt, 58% opacity:

`Act 3 · Fold Camp · Day 4, 19:20 · 61% recorded`

| Field | Source | Why |
|---|---|---|
| Act | Current act | Orientation in the story |
| Zone | Last zone entered | "Where was I?" — the actual question players ask |
| In-game day + clock | Nadia's watch | Diegetic, and it re-establishes the survival frame |
| **% recorded** | Field Slate completion, not "% complete" | Naming the progress metric after the *record* is the entire theme stated in the menu. Never say "61% complete". |

There is **one save**, autosaved, plus **one rollback slot** (the state at the last zone entry) reachable from Settings → Save → `Return to last zone`. No manual save slots. Rationale: no failure state, no combat, nothing to save-scum.

## 7.4 New Expedition flow

```
NEW EXPEDITION
  ↓ (no modal — the menu list slides left 390pt, 340ms)

┌──────────────────────────────────────────────────────────┐
│                                                          │
│   ‹ BACK                                                 │
│                                                          │
│   B E F O R E   Y O U   B E G I N                        │  17pt
│                                                          │
│   ╭────────────────────────────────────────────────────╮ │
│   │  HEADPHONES RECOMMENDED                            │ │  ← 72pt card
│   │  This game is about listening. Direction and       │ │
│   │  distance are information.                         │ │
│   ╰────────────────────────────────────────────────────╯ │
│                                                          │
│   ╭────────────────────────────────────────────────────╮ │
│   │  COMFORT                                    [ON]   │ │  ← the comfort card
│   │  Head bob off · No camera shake · Motion vignette  │ │     (§1.4). Defaults
│   │  Invert look Y                              [OFF]  │ │     are ON. Two
│   ╰────────────────────────────────────────────────────╯ │     toggles here,
│                                                          │     everything else
│   ╭────────────────────────────────────────────────────╮ │     in Settings.
│   │  TEXT SIZE          A   A   ⒶA   A    A          │ │  ← 5-step, live preview
│   ╰────────────────────────────────────────────────────╯ │
│                                                          │
│   ╭────────────────────────────────────────────────────╮ │
│   │              B E G I N                             │ │  64pt slab
│   ╰────────────────────────────────────────────────────╯ │
│                                                          │
│   All of this can be changed at any time.                │  11pt 48%
└──────────────────────────────────────────────────────────┘
```

- **No difficulty selection.** There is no combat and no fail state; a difficulty menu would be a lie. There is a **Survival Pressure** setting (`Standard` / `Relaxed` / `Off`) buried in Settings → Gameplay, which scales hydration/warmth drain, including to zero. It is not offered up front because framing it as a choice at minute zero mis-sells the game as a survival sim.
- `BEGIN` → hard cut to black (300ms) → the prologue. **No loading screen art with lore text.** If a load bar is needed it is a 1pt line at the bottom of a black frame.

---

# 8. ACCESSIBILITY

Accessibility is a **first-run flow and a settings surface**, not a hidden menu. The comfort card in New Expedition is the front door.

## 8.1 Text scaling

- **Five steps**: 0.85× / 1.00× / 1.20× / 1.40× / 1.60×, applied as a global multiplier to every text style via one `TextScaleService`.
- Respects the **OS text-size setting on first launch** (iOS Dynamic Type category, Android `fontScale`) and then becomes an independent in-game setting.
- **Every container that holds text must be height-flexible.** No fixed-height text boxes anywhere. QA gate: a full playthrough at 1.60× with zero clipped or truncated strings that carry information. Truncation is allowed only on inventory card labels (which have a full name in Inspect).
- At ≥1.40×: the inventory grid drops to **3 columns** (cell 110pt), and the interaction prompt's DESC cap drops from 3 lines to 2 with the remainder moving to Examine.
- **Separate subtitle size control** (5 steps, independent), because subtitle needs differ from UI needs.

## 8.2 Colourblind-safe status encoding

**The contract: colour is never the only channel.** Enforced by a lint pass — any UI element registering a state must declare a non-colour channel.

| Status | Colour | Shape | Position | Text |
|---|---|---|---|---|
| Inventory: New | `#E8E2D2` | ● solid circle | top-left dot | "New" in Inspect |
| Inventory: Component | `#B9C6C4` | ▢ open square | top-left dot | "Combines" in Inspect |
| Inventory: Consumable | `#D2C29A` | ▮ half bar | top-left dot | count shown |
| Inventory: Degrading | `#C99A82` | ▲ triangle | top-left dot | "Drying / corroding" |
| Vitals: warning | `#D8D2C4` | pulse rhythm (4.5s) | — | voice line |
| Vitals: critical | `#C99A82` | faster pulse (1.8s) + haptic | — | voice line |
| Combine: valid | — | motion (snap together) | — | result name |
| Combine: invalid | — | motion (part) | — | caption line |
| Slate: contradicted entry | `#C99A82` | **strikethrough rule** | — | "Revised" label |

Plus three simulation-tested palettes (protanopia / deuteranopia / tritanopia) checked in CI with a colour-distance test; the base palette is deliberately **low-saturation warm neutrals**, which is both the art direction and a large part of the accessibility answer. **No red/green pair carries meaning anywhere in the game.**

## 8.3 Reduced motion

A single `Reduced Motion` master toggle (auto-enabled if the OS flag is set), which:

| Normally | With Reduced Motion |
|---|---|
| Prompt card slides up 12pt | Cross-fade only, no translation |
| Sheet detent spring `OutBack` | Linear, no overshoot |
| Collect fly-to-inventory arc | Item fades out at source, inventory button pulses |
| Discovery rule draws outward | Rule cross-fades in at full width |
| Discovery title letter-stagger | Whole title cross-fades |
| Inspect turntable momentum | No momentum; 1:1 drag only |
| Body Camera cut sequences | **Unchanged** (they are cuts, not motion — cuts are the comfort-safe option and this is why we chose them) |
| Motion vignette | Unchanged (it *is* a comfort feature) |
| Combine light-line sweep | Static flash at 22%, 100ms |
| Camera FOV narrowing (hydrophone focus) | Disabled; the cone narrows in the spectrogram UI only |

Separate sub-toggles under it for: `Camera shake` (there is none anyway — this toggle exists for the 3 Body Camera shots with a handheld feel), `Screen flash`, `Parallax`, `Auto-camera in cinematics` (replaces the 3 moving cinematic shots with locked-off framings).

## 8.4 Hold vs tap

**Every hold action in the game has a tap alternative**, via `Settings → Motor → Hold Actions: Hold / Tap-to-toggle`.

| Action | Default | Tap-to-toggle mode |
|---|---|---|
| Hold contextual button to turn a valve | Hold until arc completes | Tap to start, tap to stop; arc auto-advances |
| Hold to raise hydrophone | Hold | Tap on, tap off |
| Long-press 380ms to lift an inventory card | Long-press | Single tap lifts the card; second tap on a target combines. (The `USE WITH…` button path is always available regardless.) |
| Hold to skip a cinematic | Hold 0.8s | Tap, then confirm chip |

**Long-press duration** is itself a setting: 200 / 380 / 600 / 900ms.

**No action anywhere requires simultaneous multi-touch**, except pinch-to-narrow the hydrophone cone — which has a **two-button alternative** (`◂ ▸` chips beside the tool slot) that appears automatically when `One-Handed Mode` or `Motor` assists are on.

## 8.5 Left-hand mirroring

`Settings → Controls → Handedness: Right / Left`.

Left-handed layout mirrors the **x-axis of the bottom 284pt only** (the world and the camera zone are untouched):

| Element | Right-handed | Left-handed |
|---|---|---|
| Move zone | x ∈ [0, 150] | x ∈ [240, 390] |
| Action zone | x ∈ [240, 390] | x ∈ [0, 150] |
| Contextual button | (318, 716) | (72, 716) |
| Inventory | (318, 792) | (72, 792) |
| Tool slot | (72, 792) | (318, 792) |
| SLATE | (195, 792) — unchanged | (195, 792) |
| Target Wheel | stacks up-left of button | stacks up-right of button |
| Sheet ✕ | (352, top+24) | (38, top+24) |
| Inspect action row | unchanged order | unchanged order |

Mirroring is a **live toggle** — it re-anchors in one frame, no restart. Everything reads from `LayoutSide` on the layout ScriptableObject.

## 8.6 Subtitles and captions

**Always on by default.** This game is 70% voice and document text; subtitles off is a worse experience for everyone.

```
── SUBTITLE BLOCK ───────────────────────────────────────────
│                                                          │
│         ╭──────────────────────────────────────╮         │  bottom edge y=684
│         │  [ NADIA ]                           │         │  10pt +0.2em 62%
│         │  Two-forty hertz, steady. Or my      │         │  15pt (scalable)
│         │  recorder's dying.                   │         │
│         ╰──────────────────────────────────────╯         │
│                                                          │
│        ‹‹ a wire hums, off to the left ››                │  13pt italic 72%
│                                                          │  ← caption, own line
```

| Property | Spec |
|---|---|
| Position | Bottom-centre, **bottom edge at y = 684** (above the prompt card's slot; when both are active the prompt card moves up 96pt) |
| Width | Max **310pt** (x 40–350), centred |
| Lines | Max **2 lines**, ~42 chars/line at 1.00× scale |
| Font size | 15pt default, 5 independent steps 12 / 15 / 18 / 22 / 26pt |
| Colour | `#F4F0E6` on a **48% black plate with 6px backdrop blur**, corner radius 4pt. Plate opacity is a setting (0 / 25 / 48 / 70 / 90%). |
| Speaker label | `[ NADIA ]`, 10pt, +0.2em, 62% opacity. **On by default** — this game has five voices from four decades and the player must know who is talking. Toggle available. |
| Speaker colour coding | Optional. Each of the 6 voices gets a distinct hue at low saturation **plus** the always-present name label, so colour is never load-bearing. |
| **Closed captions** (non-speech) | Separate toggle, **default ON**. Rendered in italic between `‹‹ ›› `, on their own line below the dialogue plate. Given this game's entire premise, captioning the audio is not optional polish — it is the mechanic. Examples: `‹‹ eleven-minute pressure pulse, rising ››`, `‹‹ drip, off-rhythm ››`, `‹‹ tape hiss, Reel 14 ››` |
| **Directional captions** | When a sound is off-screen, the caption carries a direction chip: `‹‹ ← water behind the wall ››`. This is a full substitute for the audio-spatial information the hydrophone provides. **Required feature, not a stretch goal** — without it a deaf player cannot complete the acoustic puzzles. |
| Timing | Min on-screen 1.1s; ≤ 18 chars/sec reading rate; no subtitle is cut by the next line starting |
| Documents | All journal/reel/tag text is real UI text, fully scalable, never baked into a texture. **Hard rule** — no readable in-world text is ever a texture. |

## 8.7 Haptics policy

Haptics are an **information channel**, not decoration. Budget: **no more than one haptic per 2 seconds in ordinary play.**

| Event | Haptic | Notes |
|---|---|---|
| Interaction prompt appears | `Selection` | **Only when stationary** |
| Collect | `LightImpact` → `Selection` on arrival | 2 events, 340ms apart |
| Combine: card lift | `MediumImpact` | |
| Combine: valid | `SuccessNotification` | |
| Combine: invalid | `Selection` | Deliberately identical to a neutral tick — no punishment signal |
| Discovery: title | `SoftImpact` | Revision gets 2, 240ms apart |
| Vital critical | `WarningNotification` | Max once per 60s |
| Sheet detent snap | `Selection` | |
| Climb start / top-out | `LightImpact` | |
| Damper console: gate seats | `MediumImpact` | This is the one place haptics are *load-bearing* — you feel the gate seat, which is how Act 4's tuning puzzle is solved by touch as much as by sight |
| **Everything else** | **Nothing** | No haptic on button-down, no haptic on menu navigation, no haptic on walking |

Settings: `Haptics: Full / Reduced / Off`. `Reduced` keeps only Discovery, Combine-valid, Vital-critical and the damper console. Android: implemented via `Vibrator`/`VibrationEffect` with amplitude control where available — *unverified assumption: amplitude-controlled haptics are inconsistent across Android OEMs and need a device-matrix test pass; fall back to a two-tier duration-based scheme where unsupported.* iOS: Core Haptics via a thin plugin. **Never vibrate as a substitute for an unavailable haptic** — a buzz reads as an error on every phone.

## 8.8 The no-timed-input guarantee

> **No input in this game is ever timed, and no puzzle is ever solved by reaction speed or dexterity.**

Concretely, this is a promise about eight specific systems:

1. **No QTEs.** None exist and none may be added.
2. **No falling damage from missed platforming** — traversal is on-rails (§2.9).
3. **The damper console (Act 4)** is tuned against a **tide table**, not a stopwatch. The tide state advances in discrete 90-second steps and **pauses entirely while the console is open**. You can sit at the console for an hour.
4. **The eleven-minute pressure cycle** in the Sweatways is a *navigation* rhythm — doors that are passable at one pressure state and not another. Missing a window costs **waiting**, never damage, and the wait can be skipped by a `WAIT` action at any bench or anchor point.
5. **Hydration and warmth are pressure, not timers.** Reaching zero causes Nadia to black out and wake at the last shelter with a lost hour and a Slate entry — never a death screen, never lost progress, never a lost item. `Survival Pressure: Off` removes them entirely and **locks no content**.
6. **Reel playback and document reading have no timers.** Playback pauses when a sheet opens.
7. **Cinematics do not require input.** Skip is available, never required.
8. **The ending choice is untimed.** Act 5's decision has no clock, no pressure prompt, and can be reconsidered until confirmed. This one matters most: a timed ending choice would betray the entire theme of a woman who needs to be allowed to revise.

Additionally: **no double-tap or long-press is ever the only way to do something**, and hold durations are adjustable (§8.4).

---

# 9. THE UI RULE SET — 10 HARD RULES

These are review gates. A build that violates one does not ship.

| # | Rule | Enforcement |
|---|---|---|
| **1** | **Never more than 4 persistent interactive elements on screen during gameplay.** Currently: contextual button, tool slot, SLATE, inventory. Adding a 5th requires removing one. | Automated: a `PersistentUIRegistry` asserts count ≤ 4 in play mode. |
| **2** | **Never a modal over gameplay unless the player opened it.** No pop-up tutorial, no "did you know", no unrequested confirmation, no reward screen. Discoveries (§6) are overlays the player plays under, never modals. The only unrequested full-screen event is a Body Camera cinematic, which is authored and never interactive. | Code review; a modal presenter that isn't on a player-initiated call stack throws in editor. |
| **3** | **Every destructive action is reversible or confirmed — and we prefer reversible.** `DROP` places the item in the world (reversible, no confirm). Overwriting a Slate entry keeps the struck original visible (reversible). `NEW EXPEDITION` over an existing save is the one confirmed action, and it names what will be lost: *"Act 3, Day 4, 61% recorded."* **Nothing in this game deletes a record** — Fiction Rule 7 is a UI rule too. | Design review; every `Destroy`/overwrite on player data requires a reversibility note in the PR. |
| **4** | **Minimum touch target 48dp (≈48pt), minimum 20pt separation between adjacent targets.** Visual size may be smaller than the hit target; hit targets may not overlap. | Automated: an editor test walks every `Selectable` in every prefab and fails on `rect < 48` or `gap < 20`. |
| **5** | **Nothing interactive lives above y = 560** (the one-handed reachability line). The top band is display-only: vitals, bearing, clock, condition. Sheets and menus — which the player opened deliberately and may use two-handed — are exempt, but their *primary* action always sits below 560. | Automated lint on gameplay HUD canvases. |
| **6** | **Colour is never the only carrier of meaning.** Every state has a shape, position, motion or text channel too (§8.2). | Automated: `StatusBadge` components require a non-colour channel field; CI colour-distance test under 3 CVD simulations. |
| **7** | **No text is baked into a texture if a player is meant to read it.** Every journal page, tag, label, chart and dial marking that carries information is live, scalable, translatable text. | Automated: art-import validator flags textures tagged `readable`. |
| **8** | **No failure feedback. Ever.** No red, no shake, no buzzer, no error sound, no "invalid" language, anywhere in the game — combine, interaction, puzzle or menu. Unsuccessful attempts get a neutral haptic and a specific, written observation (§5.6). The word "wrong" does not appear in the shipped string table. | Automated: string-table lint against a banned-word list (`wrong`, `failed`, `invalid`, `error`, `incorrect`, `can't do that`). Audio review: no asset in `/UI/Negative`. |
| **9** | **The HUD must be able to reach zero.** Every gameplay HUD element must have a defined idle-fade rule and a defined return trigger (§3.6). An element that cannot fade must be justified in writing (currently: subtitles, and the contextual button when a target is in range). | Design review; `HudElement` base class requires a `FadePolicy` asset — no default. |
| **10** | **Never take the camera from the player without a cut.** No forced look-at, no auto-pan, no "the camera turns to show you the thing", no blended FP↔TP transition. Attention is directed with light, sound and level shape. If we must change the view, we cut, and it is authored. | Design review; `Cinemachine` blend time to/from gameplay cameras is asserted `== 0`. |

**Standing eleventh rule, stated separately because it governs the other ten:** *Any rule may be broken exactly once, in the Quiet Room, if the break is written down and signed off by the creative director.* Act 5 strips the crafting HUD entirely (canon, §6 of the bible) — that is already a deliberate violation of nothing here, but the Quiet Room is where an exception would be earned. Everywhere else, the rules hold.

---

## APPENDIX A — Performance budget for UI (binding targets)

| Item | iPhone 12 target | Low-end / 30fps fallback |
|---|---|---|
| Total UI CPU per frame | ≤ 0.9 ms | ≤ 1.4 ms |
| Total UI GPU per frame (HUD resting) | ≤ 0.4 ms | ≤ 0.6 ms |
| Backdrop blur (prompt card + subtitle plate) | ≤ 0.35 ms combined, 1/4-res RT | **Disabled**; gradient scrim instead |
| Canvas count on gameplay HUD | 4 (Static / Dynamic / Prompt / Subtitle), split so a text change never dirties the static canvas | Same |
| Draw calls, HUD resting | ≤ 6 | ≤ 6 |
| Draw calls, inventory Expanded + inspect | ≤ 22 + 1 RT camera | ≤ 18, world frozen to a captured frame |
| UI atlas budget | 2 × 2048² (ASTC 6×6) | 2 × 1024² |
| Font atlases | 1 × 2048² SDF per script; dynamic atlas growth disabled in release builds | Same |
| Garbage allocated per frame by UI | **0 bytes** in the resting state; ≤ 2 KB on a sheet open | Same |

*Unverified assumption:* these are targets set from general mobile UI practice, not measured. They must be validated against a real Unity 6 URP build on device during the first vertical slice; treat any number here as a hypothesis to profile, not a spec to trust.

## APPENDIX B — Open items requiring verification before lock

1. **Safe-area insets** in §0 are working assumptions. Read them from `Screen.safeArea` and validate on a physical device matrix (iPhone SE 3, 12, 15 Pro, Pixel 7a, Galaxy A54, a 21:9 Sony or Xperia-class device).
2. **Android gesture-nav swipe-up conflict** with the inventory swipe-up accelerator (§2.10) — needs a device test; if the conflict rate is meaningful, drop the gesture and keep the button only.
3. **Core Haptics / VibrationEffect parity** (§8.7) — needs an OEM matrix pass.
4. **Backdrop blur cost on URP mobile** (§4.2) — profile before committing; the gradient-scrim fallback must be built first so it is never a schedule risk.
5. **Dynamic Type category mapping** to our 5 text steps — confirm the mapping on iOS and the `fontScale` mapping on Android.
6. Everything in the canon's §9 clearance note still applies: no name in this document has been trademark-searched.