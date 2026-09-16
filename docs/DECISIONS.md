# DECISIONS — the project ledger

Every binding decision, and where it came from. **Nothing here is invented**: each entry cites the
user instruction or the document that established it. Unverifiable claims are `UNCONFIRMED`;
contradictions are `CONFLICT`.

Detail for the ADRs lives in [`architecture/00-decisions.md`](architecture/00-decisions.md).
That file and this one must agree; if they drift, that file wins on rationale and this one on the
index.

---

## A. Locked by the project owner

Approved explicitly. Do not revisit without a new instruction.

| # | Decision | Detail |
|---|---|---|
| **D-1** | **Title is VARDHOLM** | Store subtitle *"Vardholm: A Survival Mystery"*; keyword variant *"Vardholm: Survival Mystery"*. The earlier working title **"The Forgotten Isle" is retired** — it collides with at least five same-titled games on itch.io, one a $4.99 survival-exploration mystery, one built "around immersive sound". Retained in `production/01-title-evaluation.md` only as the rejected candidate. |
| **D-2** | **Save spine in Phase 1, not Phase 11** | Persistence is core architecture from the start; every later system adds its `ISaveParticipant` incrementally. Phase 11 becomes save *hardening*: recovery, corruption, migration, edge cases. (= ADR-0011) |
| **D-3** | **Premium model** | Free playable prologue → one-time full unlock. **No ads, no energy system, no consumable currency, no pay-to-win, no purchase-pressure timers.** Prologue is 30–45 minutes and must deliver: shipwreck → first exploration → first environmental puzzle → the Field Slate → radio repair → mysterious transmission → first major reveal → entry to the next area, all **before** the unlock decision. The unlock must read as *"continue the mystery"*, never *"pay to keep playing"*. |
| **D-4** | **Platform priority** | Android + iOS, portrait-first. Architecture and input stay adaptable for later PC/tablet, but the game is **not** optimised around PC. |
| **D-5** | **Game identity — locked** | Focus: exploration, mystery, environmental puzzles, survival pressure, audio investigation, scientific discovery, atmosphere. **Never introduce:** combat, weapons, monster chasing, zombie mechanics, generic crafting grind, base-building grind, loot rarity. Mystery and exploration remain the primary reason to play. |
| **D-6** | **Phase discipline** | Stop at the end of each phase and report. Do not begin the next phase unasked. |

## B. Architecture decisions (ADRs)

Full text and rationale in [`architecture/00-decisions.md`](architecture/00-decisions.md).

| ADR | Decision | Status |
|---|---|---|
| 0001 | Five assemblies: `ForgottenIsle.Core/.Game/.UI/.Editor/.Tests.*` | **Implemented** |
| 0002 | `Core` is engine-free without exception — no `Vector3`, no `ScriptableObject` | **Implemented**, gate-enforced |
| 0003 | C# 9 + `IsExternalInit` polyfill; `required` and `ImmutableArray<T>` banned | **Implemented** |
| 0004 | Zone streaming uses the curtain model; max 2 resident zones | **Implemented** |
| 0005 | ScriptableObject authoring baked to immutable records; catalog ships as an Addressable | Not yet built (Phase 3+) |
| 0006 | `SurvivalStat` = `{Health, Energy, Hydration, Satiation, CoreTemp}` | Decided — data doc reconciled (CONFLICT-2 closed) |
| 0007 | One inventory model: items, not resources. `ResourceDefinition` deleted | **Implemented** (Phase 3) — data doc reconciled (CONFLICT-3 closed) |
| 0008 | Locomotion is a floating joystick; tap-to-move is accessibility-only | **Implemented** — beat 2:00 re-authored, roadmap reconciled (CONFLICT-4 closed) |
| 0009 | 14 save participants at completion; `GameState` must cover all owned state | Partial — 5 registered (session, player, progress, inventory, radio), by design |
| 0010 | Two anti-softlock holes closed: diesel renewable; Ash Throat valve order gets an Act-3-reachable second hint | Design-level, not yet built |
| 0011 | Save spine in Phase 1 (= D-2) | **Implemented** |
| 0012 | Hand-wired composition; no service locator. VContainer is a Phase 2 go/no-go | **Implemented** — go/no-go still not taken |
| 0013 | Save codec: Newtonsoft if `Core` can reference it, else hand-rolled | **Implemented** (hand-rolled `JsonWriter`/`JsonParser`) |
| 0014 | **UI Toolkit, not uGUI** — reverses Phase 1 Plan §4 | **Implemented** — plan §4 marked superseded (CONFLICT-5 closed) |
| 0015 | Objectives are derived from progression, never stored | **Implemented** (Phase 2) |
| 0016 | Zones are furnished at runtime from a recipe; scene assets stay empty | **Implemented** (Phase 2) — the look is unverified |
| 0017 | Interaction is a registry proximity scan, not physics triggers | **Implemented** (Phase 2) |
| 0018 | No placeholder audio binaries; the game is honestly silent | **Implemented** (Phase 2) |
| 0019 | A failed combination consumes nothing — the anti-soft-lock rule | **Implemented** (Phase 3), asserted by name |
| 0020 | Inventory is a top-of-screen tray; combining is tap-then-tap, with tap-again to cancel | **Implemented** (Phase 3) |
| 0021 | The world is drawn with this project's own shaders, kept under `Resources` | **Implemented** — **never compiled** |
| 0022 | The radio: one service (5th save participant), one world object, one shared piece of tuning arithmetic | **Implemented** (Phase 6 slice) |
| 0023 | The Field Slate is a derived view of progression and the radio; not a save participant | **Implemented** (Phase 6 slice) |
| 0024 | A remark is a command that records nothing: hints answer an act through the dispatcher, never through progression | **Implemented** (partial hull line) |
| 0025 | Hint timers count play seconds, forget everything on entering the world, and are not a save participant — the one deliberate exception to ADR-0011 | **Implemented** (hull line 6:00; radio tiers 1, 3 and 4) |
| 0026 | The dry-fire problem ships as a Phase 6 environmental puzzle in play seconds; fuel burn-down stays with the camp system and CONFLICT-6 | **Implemented** (wrack, rocks, three sites, hints) |

**Why 0014 reversed:** the uGUI choice assumed a greenfield start. It was not one — the team's
NATION: WORLD ORDER project already contained a working, code-built UI Toolkit mobile framework at
the same 390×844 portrait reference. The real choice was inherit or rebuild-worse.

## C. Design decisions recovered from the documentation

Established in the Phase 0 documents and not contradicted since.

| # | Decision | Source |
|---|---|---|
| **G-1** | **Canon is fixed.** Island Vardholm; protagonist Nadia Vesk, 41, marine acoustician; four occupation layers 2100 BCE→present; the secret is a Bronze Age acoustic damping machine a 1970s programme broke; Hanne Lorvik has maintained it alone since 1994. `design/01-story-bible.md` is the **naming authority** for every proper noun. | `design/01-story-bible.md` |
| **G-2** | **Ten rules of the fiction.** Nothing supernatural is ever confirmed or teased; the island never acts, people act; no combat/creature threat/weapons ever; the Tolo Vardh are competent, not mystical; technology behaves its age; exactly one other living human; the record is never destroyed by the plot; instruments work normally; the loud state is off-screen and slow; all science is dramatised, not claimed. | `design/01-story-bible.md` §8 |
| **G-3** | **First person**, with an authored third-person "Body Camera" for ~14 cinematic beats. Chosen because portrait is a tall thin frame and the island is built on the vertical axis, and because a full character rig is 30–40% of an art budget better spent on the island. | `design/05-mobile-ux-plan.md` §1 |
| **G-4** | **Listening is the signature verb.** The hydrophone resolves the world into a directional spectrogram — navigation tool, puzzle language, and answer to portrait-FP's blind periphery. It is the **Act 2** mechanic; the prologue rehearses its grammar on the radio dial. | `design/02-game-vision.md`, `design/06-core-game-loop.md` |
| **G-5** | **Nine zones**, hub-and-spoke around Fold Camp, with 12 capabilities that each retroactively open something in an already-visited zone. | `design/03-world-structure.md` |
| **G-6** | **No quest markers.** Direction is carried diegetically by maintenance evidence — the freshest brass tag, the most recently cut path. Three escalating diegetic hint tiers on a stall timer; the player is never told a hint system exists. | `design/06-core-game-loop.md` §4 |
| **G-7** | **Survival is meaningful, never a chore.** Meters hidden unless `Low` or worse; nothing drains while reading a document; **collapse, not death** — there is no Game Over screen in this product. | `architecture/02-core-systems.md` §7 |
| **G-8** | **MVP is the Ribcage only**: 18 items, 10 recipes, 16 discoveries, one environmental puzzle, the radio, the signal, the locked gully. | `production/02-mvp-scope-and-roadmap.md` |
| **G-9** | **Nation infrastructure is ported, not shared.** One-time copy with per-file attribution headers (27 files). No submodule, no package dependency. Nothing in `nobigllebowski/MobileGame` was modified — this session had read-only access. | `production/04-migration-plan.md` |

## D. Producer decisions that amended the brief

Raised, argued, and accepted.

| # | Change | Reason |
|---|---|---|
| **P-1** | Save moved Phase 11 → Phase 1 | Retrofitting persistence into ten finished systems is a re-architecture wearing a feature's clothes — and it makes the MVP unshippable by its own Definition of Done. |
| **P-2** | Audio split: core audio + spectrogram to Phase 6; final mix/VO stays Phase 13 | The climax is a frequency-matching puzzle. Deferring the mechanic to Phase 13 validates the most differentiating thing in the product last. |
| **P-3** | Business-model *decision* became a Phase 0 exit gate | It determines whether the prologue must stand alone. Settled as **D-3**. |
| **P-4** | The Phase 1 state table was **wrong** and was corrected | It had no edge into `MainMenu` from `InGame` or `Paused`, making QUIT TO MENU permanently unreachable. Now 10 edges, routing quit through `Loading`. |

## E. Open items — tracked, not decided

From `architecture/00-decisions.md`. **These are not decisions; do not treat them as settled.**

| # | Item | Owner | Due |
|---|---|---|---|
| O-1 | Unacknowledged LOST/DHARMA structural convergence in the originality statement | Narrative | Before public pitch |
| O-2 | No trademark register searched, for any proper noun | Counsel | Before announce |
| O-3 | Alpha-tested foliage overdraw unbudgeted against the 60 fps target | Tech Director | Before Phase 7 |
| O-4 | Three zone specs promise volumetric effects the renderer budget cannot fund | Tech + Art | Before Phase 7 |
| O-5 | No privacy-manifest plan while calling two required-reason APIs | Tech Director | Before Phase 16 |
| O-6 | **World clock scale stated as two different values** → now **CONFLICT-6** | Lead Gameplay | **Before Phase 5** |
| O-7 | Nation port provenance recorded in file headers | Tech Director | Ongoing |
| O-8 | UI transitions the mode machine directly rather than via commands | Lead Gameplay | Phase 2 |
| O-9 | Cinematic menu backdrop documented but not wired | Tech + Art | Phase 2 |

## F. Decisions deliberately NOT made

Recorded so nobody assumes they were settled.

- **Price point.** Never discussed.
- **Launch territories, dates, marketing.** Never discussed.
- **Team size and schedule.** `production/02-mvp-scope-and-roadmap.md` §1.10 states assumptions
  explicitly as assumptions, not commitments. UNCONFIRMED.
- **Whether to retire the Nation project.** Out of scope — a decision in its own repository.
- **VContainer adoption.** Scheduled as a Phase 2 go/no-go (ADR-0012), not pre-decided.
- **Art production pipeline and outsourcing.** The "realistic premium on mobile" cost is flagged
  as the top risk in the roadmap and remains unaddressed.
