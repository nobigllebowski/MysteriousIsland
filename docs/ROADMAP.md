# ROADMAP

**Position: Phases 2 and 3 implemented; the project has compiled and run once. Phase 4's combination step landed with Phase 3 (crafting is excluded by the game's identity). Phase 5 is blocked on CONFLICT-6. Next: the Phase 6 radio slice.**

Full per-phase detail — deliverables, exit criteria, playable state, dependencies —
in [`production/02-mvp-scope-and-roadmap.md`](production/02-mvp-scope-and-roadmap.md) Part 2.

> That document still uses the old `Isle.Domain` assembly names (**CONFLICT-1**) and specifies
> tap-to-move locomotion (**CONFLICT-4**). Read it with `CURRENT_STATE.md` §Conflicts open.

---

## Where we are

```
  0 ████ Design, architecture, MVP, story foundation          DONE
  1 ████ Project spine: assemblies, bootstrap, menu, SAVE     DONE (never compiled)
  2 ████ Player, camera, interaction, the beach            DONE (ran once)
  3 ████ Inventory and items                               DONE (never re-run)
  4 ▓▓░░ Combination landed with 3; crafting excluded (identity); discovery from 2
  5 ░░░░ Survival and camp                    BLOCKED on CONFLICT-6 (world clock)
  6 ░░░░ Environmental puzzles + audio core + radio  ◄── NEXT · MVP BOUNDARY
     ──── playtest gate: 10 sessions, "what do you want to know next?"
  7 ░░░░ Jungle (Fernmaw) + streaming
  8 ░░░░ Weather, day/night, tide     ∥  9 ░░░░ Story, documents, reels
 10 ░░░░ Ruins, caves, Oleander, the Quiet Room, Lorvik, endings
 11 ░░░░ Save hardening  ∥ 12 ░░░░ UI polish + a11y  ∥ 13 ░░░░ Final audio + VO
 14 ░░░░ Performance
 15 ░░░░ Monetization implementation
 16 ░░░░ Store preparation
```

## The order was amended — deliberately

Three changes to the original brief, argued and accepted (see `DECISIONS.md` §D):

- **Save moved 11 → 1.** Retrofitting persistence into ten finished systems is a re-architecture
  wearing a feature's clothes. Phase 11 survives as *save hardening*.
- **Audio split.** Core audio and the spectrogram land in Phase 6; final mix and VO stay at 13.
  The climax is a frequency-matching puzzle; validating it last was wrong.
- **Business model became a Phase 0 exit gate.** It decides whether the prologue must stand alone.
  Settled as D-3.

## Phase 2 — implemented, unverified

**Goal:** a player who can walk around the Ribcage in first person and interact with things.

**Delivered:** `CharacterController`-driven first-person rig with gravity and ground following;
floating joystick + look pad per ADR-0008; `InteractionSystem` with proximity arbitration and a
contextual prompt; two runtime-built zones (Ribcage and Fernmaw) instead of one greybox;
progression, derived objectives, and a HUD; progress as the third save participant.
Full account in `PHASE_2_REPORT.md`.

**No longer blocked on scenes.** `Vardholm → Setup Project` creates all four, and `ZoneBuilder`
furnishes them at runtime.

**Still outstanding from the open items:** O-8 (route UI navigation through commands), O-9 (wire
the menu backdrop, or delete it from the docs), and the VContainer go/no-go (ADR-0012). None
blocks the exit criterion.

**Exit criterion — NOT yet met:** a player walks the Ribcage, approaches an object, sees a
contextual prompt, and interacts — on a physical device, at 60 fps. Everything needed for that is
written; none of it has been compiled, executed or played. That verification is the next task,
ahead of any Phase 3 work.

## MVP boundary — end of Phase 6

A complete mini-experience: **START → SURVIVE → DISCOVER → SOLVE → RECEIVE SIGNAL → UNLOCK
JUNGLE**. The Ribcage only: 18 items, 10 recipes, 16 discoveries, one environmental puzzle, the
radio, the transmission, the locked gully.

## Top risks

| Risk | Status |
|---|---|
| **Nothing has ever been compiled or tested** | Live. First editor open is the real gate. |
| **Art cost of "realistic premium" on mobile** | The elephant. Flagged in the roadmap; unaddressed. A fallback art direction is named there. |
| **Foliage overdraw vs 60 fps** (O-3) | Unbudgeted. Due before Phase 7. |
| **World clock scale** (CONFLICT-6) | Must be settled **before Phase 5** — survival formulas were tuned at 30×, the code ships 60×. |
| **Proper-noun clearance** (O-2) | No register searched. Due before announce. |

## Not yet scheduled

Trademark clearance, art pipeline decisions, price point, launch territories, team size, and
whether the Nation project is retired. All UNCONFIRMED — see `DECISIONS.md` §F.
