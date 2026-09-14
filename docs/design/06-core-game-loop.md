# CORE GAME LOOP

**Cites:** Story Bible v1.0, World Structure v1.0, Core Systems v1.0

---

## 1. THE MOMENT-TO-MOMENT LOOP

```
                    ┌──────────────────────────────────────────┐
                    │                                          │
                    ▼                                          │
             ┌─────────────┐                                   │
             │   EXPLORE   │  move through a zone; portrait FP  │
             └──────┬──────┘  frames the vertical axis          │
                    │                                          │
                    ▼                                          │
             ┌─────────────┐                                   │
   ┌────────▶│   LISTEN    │◀── the signature verb              │
   │         └──────┬──────┘    hydrophone → directional        │
   │                │           spectrogram overlay             │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │   OBSERVE   │  read the SERVICED detail:         │
   │         └──────┬──────┘  brass tags, chalk, fresh cuts     │
   │                │                                          │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │   COLLECT   │  contextual one-tap acquire        │
   │         └──────┬──────┘                                   │
   │                │                                          │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │   ANALYSE   │  Inspect: 3D turntable +           │
   │         └──────┬──────┘  Field Slate entry (revisable)     │
   │                │                                          │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │   COMBINE   │  drag item onto item, or           │
   │         └──────┬──────┘  item onto environment             │
   │                │                                          │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │    SOLVE    │  environmental puzzle:             │
   │         └──────┬──────┘  repair / route / tune / balance   │
   │                │                                          │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │  DISCOVER   │  ✦ cinematic notification          │
   │         └──────┬──────┘  unlocks recipes / abilities /     │
   │                │         locations / interactions / story  │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   │         │   UNLOCK    │  new zone, or new capability that  │
   │         └──────┬──────┘  retro-opens an OLD zone           │
   │                │                                          │
   │                ▼                                          │
   │         ┌─────────────┐                                   │
   └─────────┤   SURVIVE   │  water / warmth / energy force     │
             └──────┬──────┘  a routing decision, never a       │
                    │         menu-management chore             │
                    └─────────────────────────────────────────┘
                              return to camp, or push on
```

## 2. THE THREE NESTED LOOPS

```
┌─ MACRO LOOP ── per act, 1.5–2.5 h ────────────────────────────────────┐
│  New zone opens → new capability earned → the belief ladder advances  │
│  → an old zone becomes re-enterable → the Field Slate is REVISED      │
│                                                                       │
│  ┌─ MID LOOP ── per objective, 12–20 min = one session ────────────┐  │
│  │  Leave camp with a goal → gather what the goal needs →          │  │
│  │  hit the obstacle → find the missing piece → solve →            │  │
│  │  return / bank a Discovery → Field Slate entry → AUTOSAVE       │  │
│  │                                                                 │  │
│  │  ┌─ MICRO LOOP ── 20–90 seconds ──────────────────────────┐     │  │
│  │  │  LISTEN → orient → approach → OBSERVE → interact →     │     │  │
│  │  │  collect or learn → the Slate updates                  │     │  │
│  │  └────────────────────────────────────────────────────────┘     │  │
│  └─────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────┘
```

## 3. WHAT THE LOOP REWARDS

| The loop rewards | Mechanically expressed as |
|---|---|
| **Curiosity** | Every zone has one optional Secret a rushing player misses entirely. |
| **Observation** | The freshest brass tag is the navigation hint. Nothing points; things are *tended*. |
| **Experimentation** | Invalid combinations cost nothing and produce a *characterised* refusal in Nadia's voice, never a buzzer. |
| **Logical thinking** | Puzzles are repairs and routings with stated physical principles — reasoned, not brute-forced. |
| **Revision** | The Field Slate lets the player be *wrong and then correct*. This is the theme as a verb. |

## 4. THE NO-ARROW RULE, AND THE SAFETY NET

There is no quest marker and no objective arrow. Direction is carried diegetically by
**maintenance evidence** — the path someone cut most recently, the lantern most recently
replaced, the tag most recently dated.

The player must never be permanently stuck. Three escalating, **all diegetic**, all on a
stall timer, and the player is never told a hint system exists:

1. **Nadia notices** (~90 s idle near the obstacle) — an interior line restating the
   observation, not the solution.
2. **The Slate cross-references** (~4 min) — an existing entry surfaces itself as relevant.
3. **Nadia reasons aloud** (~8 min) — she states the next physical step, still not the input.

Plus the structural guarantee (World Structure §6): no consumable is ever required twice, no
one-way door closes before a needed item, and every resource node respawns.

## 5. HOW THE LOOP DIES (and the guardrails against it)

| Failure | Guardrail |
|---|---|
| Survival becomes chore-management | Meters are hidden unless `Low` or worse; nothing drains while reading a document; thirst-to-collapse is never under 30 in-game hours. |
| Backtracking becomes padding | Every capability retro-opens something in an *already-visited* zone, and every zone gets a one-way shortcut back to camp. |
| Item combination becomes pixel-hunting | Recipes are gated on Discoveries, so the player only ever holds combinable things once they know the principle. |
| The mystery outstays the reveal | The belief ladder is explicit and load-bearing: hour 1 / hour 3 / hour 7 each have a stated, different worldview. |
| Act 5 becomes a boss fight | The crafting HUD is **stripped entirely** in Act 5. Verbs reduce to walk, listen, speak. Protect this in review. |
