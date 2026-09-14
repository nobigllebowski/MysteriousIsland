# ROADMAP

**Position: Phase 1 complete. Phase 2 is next and has not started.**

Full per-phase detail — deliverables, exit criteria, playable state, dependencies —
in [`production/02-mvp-scope-and-roadmap.md`](production/02-mvp-scope-and-roadmap.md) Part 2.

> That document still uses the old `Isle.Domain` assembly names (**CONFLICT-1**) and specifies
> tap-to-move locomotion (**CONFLICT-4**). Read it with `CURRENT_STATE.md` §Conflicts open.

---

## Where we are

```
  0 ████ Design, architecture, MVP, story foundation          DONE
  1 ████ Project spine: assemblies, bootstrap, menu, SAVE     DONE (never compiled)
  2 ░░░░ Player, camera, interaction, the beach            ◄── NEXT
  3 ░░░░ Inventory and items
  4 ░░░░ Combination, crafting, discovery
  5 ░░░░ Survival and camp
  6 ░░░░ Environmental puzzles + audio core + radio  ◄── MVP BOUNDARY
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

## Phase 2 — the next phase

**Goal:** a player who can walk around the Ribcage in first person and interact with things.

Deliverables: real first-person controller replacing the placeholder capsule; camera with the
locked 62° vertical FOV and comfort defaults ON; floating joystick + swipe-look per ADR-0008;
`InteractionSystem` with candidate arbitration and hysteresis; the contextual interaction prompt;
the Ribcage greybox; `ZoneEntryAnchor` in a real scene.

**Blocked on scenes existing.** Nothing in Phase 2 can be tested until the four `.unity` scenes
are authored per `Assets/Scenes/README.md`.

**Also in Phase 2, from the open items:** O-8 (route UI navigation through commands) and O-9
(wire the menu backdrop, or delete it from the docs). Plus the VContainer go/no-go (ADR-0012).

**Exit criterion:** a player walks the Ribcage, approaches an object, sees a contextual prompt,
and interacts — on a physical device, at 60 fps, with survival and inventory still absent.

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
