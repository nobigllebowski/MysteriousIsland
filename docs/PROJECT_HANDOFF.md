# PROJECT HANDOFF

**Read this first in any new session.** Fifteen minutes here saves you from re-deriving the
project — or worse, contradicting a decision that was already made.

---

## 1. What this is, in sixty seconds

**VARDHOLM** — a premium portrait mobile mystery-adventure for iOS and Android. Unity 6
(`6000.6.0f1`), C#, Built-in render pipeline (the design docs say URP — CONFLICT-7), UI Toolkit.

A disgraced marine acoustician is shipwrecked on an island that isn't on any chart, and discovers
a Bronze Age machine built to keep the ocean quiet — and the woman who has been repairing it alone
for thirty-one years. **The mystery is a maintenance problem.** No combat, no creatures, no
weapons; the signature tool is a microphone.

**Phases 1, 1.5, 2, 3 and most of 6 are implemented.** The project spine exists — five
assemblies — Phase 2 added a playable vertical slice (two zones, a walking player, interaction,
progression, objectives, a HUD, save), Phase 3 added items, combination, the inventory tray and
the first maintenance puzzle, a graphics pass added sky, sea, shore and five shaders, and the
Phase 6 slice added the radio and its puzzle, the Field Slate, the hull-line sightline, the hint
ladders, the dry-fire problem and a synthesised audio core. **The project has compiled and run
once**; every change since is unverified, because the environment it was written in has no Unity
and no .NET SDK. See `CURRENT_STATE.md` §2.

## 2. Read in this order

| Order | File | Why |
|---|---|---|
| 1 | `../CLAUDE.md` | Invariants you must not break, and the source-of-truth ordering |
| 2 | **`CURRENT_STATE.md`** | What is actually true now. **Includes the two live CONFLICTS** (the clock, the pipeline). |
| 3 | `DECISIONS.md` | Every locked decision and where it came from |
| 4 | `ROADMAP.md` | Where we are and what is next |
| 5 | `ARCHITECTURE.md` | How the code is shaped and why |
| 6 | `GAME_VISION.md` | What the game is |
| 7 | `SCENE_CONTRACT.md` | The dependency graph and what each scene must contain (nothing). |
| 8 | `UNITY_RISK_AUDIT.md` | What is likely to compile, what needs the editor, what is high risk. |
| 9 | `design/01-story-bible.md` | **The naming authority.** Consult before inventing any noun. |
| 10 | `PHASE_2_REPORT.md` | What the vertical slice contains, and exactly what about it is unverified. |

Deep detail sits under `design/`, `architecture/` and `production/`. **Nine of those documents
predate the ADRs and were never reconciled with them** — Phase 1 Task 0 was specified and never
run. Always check `CURRENT_STATE.md` §Conflicts before trusting an older document.

## 3. The five things most likely to trip you up

1. **Nothing has been compiled.** No Unity, no .NET SDK, no Mono in the dev container, and the
   proxy blocks Microsoft's SDK downloads. 243 tests are written and 0 have run — and the
   PlayMode ones self-skip without the scenes, so *ignored* is not *passed*. Never claim
   otherwise — say what was verified and what needs the editor.
2. **The scenes are generated, not committed.** Run **`Vardholm → Setup Project`** once after
   cloning; it creates all four and writes Build Settings. Never author them by hand. The scene
   assets are deliberately empty — the runtime furnishes everything (`SCENE_CONTRACT.md`).
3. **`ForgottenIsle.Core` must never see `UnityEngine`.** Not `Vector3`, not `ScriptableObject`.
   If you type it under `Assets/Scripts/Core/`, you have made a mistake.
4. **UI must not mutate state.** Tap → controller → command → handler. The gate enforces it.
5. **CONFLICT-6 is unresolved and it is load-bearing.** The docs specify a 30× world clock; the
   shipped `Ticker` uses 60×. Survival was tuned at 30×. **Settle it before Phase 5.**

## 4. Verify before you trust

```bash
python3 ci/validate-structure.py   # compiler substitute — must exit 0
bash ci/check-layering.sh          # architecture invariants — must exit 0
```

Both currently pass. Run them before and after every code change.

## 5. Getting it running

`dev-setup.md` is the clean-machine walkthrough. Short version: Unity Hub → install
`6000.6.0f1` → *Add project from disk* → **`Vardholm → Setup Project`** → Game view 390×844
portrait → open `Bootstrap.unity` → Play, then read the `VARDHOLM STARTUP CHECK` block in the
Console. Tests: **Window → General → Test Runner**.

## 6. Repository and history

- Branch **`claude/keen-darwin-frw656`** — the repository's only branch, and its default. There is
  no `main`, so **no pull request is possible** until a base branch is created.
- Six commits, all pushed. `git log --oneline` is a readable history of the project's reasoning.
- The related project **NATION: WORLD ORDER** lives in `nobigllebowski/MobileGame` — a *different*
  repository. 27 files were ported from it with attribution headers. **Nothing in it was
  modified**; access was read-only. See `production/04-migration-plan.md`.

## 7. Working agreements

- **Stop at the end of each phase and report.** Do not begin the next phase unasked.
- **Record every non-obvious choice** as an ADR in `architecture/00-decisions.md`, then index it
  in `DECISIONS.md`.
- **Mark uncertainty `UNCONFIRMED` and contradictions `CONFLICT`.** Never invent a decision to
  fill a gap — an honest gap is recoverable, a fabricated decision is not.
- **Update `CURRENT_STATE.md` and `CHANGELOG.md`** whenever state changes.
- **Every new system implements `ISaveParticipant` in the same PR that adds it.**

## 8. The exact next task

**Re-open the editor and confirm the island is above the water.** The graphics pass built the
island 440–730 m under the sea (`Mathf.SmoothStep` misuse, fixed at 37f5355); the fix has not been
run. Then walk the Phase 3 chain in `CURRENT_STATE.md` §1: spindle → reel → combine in the tray →
sluice key → sluice → tape deck. Run both test suites, confirming the PlayMode ones ran rather
than were ignored.

Then walk the Phase 6 slice: examine the set in the trawler hull twice (found, list), take the
torch off the nail, use it, the multitool and the spring on the set, TUNE, drag to 5.240, read the
transmission, open the Slate from its HUD tab and check UNRESOLVED reads 3. Pause with the dial
open and resume; save and continue; tap NEW GAME twice with every slot full.

Then the observation and hint slice: stand past the near hull's bow and look down the beach (the
chalk should snap in at ±4° and SIX HULLS, ONE LINE appear in the Slate); stand at the third
hull instead (three chalk in; "Try the far end"); leave the set powered up and untouched for
sixteen minutes (tier 1 at 3:00, tier 3 at 10:00, then the dial opens and sweeps itself, locking
the hull on the way and her at about ninety seconds; a tap on the strip must stop it).

Then the fire: take the rope and the multitool, combine them in the tray (the multitool must
survive); tap rocks with the multitool held (knock, then a ring at a pale one; TAKE it); lay
fibre and wood on open sand and strike (it must blow out and the kit stay), then in the lee of
the near hull (it must light, the point light come on, FIRE appear in the Slate, and an autosave
write); lay wet wood by it and wait ninety seconds for dry wood to return to the tray. Leave a
laid kit alone for six minutes and she should pick up the chert herself. Use the multitool on
the rope where it lies: it must say "that does nothing here" and the rope must stay.

Then listen: surf under the Ribcage from the first frame; a click when the set powers up; hiss
when the dial opens, a warble around 5.232, the carrier pitched up around 5.238, ducked hiss and a
clean tone on 5.240; a low knock on basalt and a bright ring on chert; crackle while the fire
burns. The beds must pause with the game and resume where they were. Every constant is a first
guess made without an ear (ADR-0027).

The things most likely to be wrong, in order: a shader that does not compile (magenta); a UI
Toolkit API used with the wrong signature in `RadioPanel` or `SlateScreen`; a pointer-capture
edge in the dial strip; `Mesh.bounds` not being the culling volume for the sea. Everything else
in the last twelve commits was traced by reading, not run. Phase 5 stays blocked on CONFLICT-6.

Still outstanding behind that, unchanged by Phase 2:

**Reconcile the documentation (Phase 1 Task 0) — done except the owner's call.**

Items 1–4 (assembly names, the survival stat set and the deleted resource model, the joystick-first
re-authoring of beat 2:00, the superseded uGUI decision) are in the documents. What remains:

5. **Ask the owner to settle the world clock: 30× or 60×** (CONFLICT-6). Then make code and docs
   agree and pin it with a test. And the render pipeline (CONFLICT-7): the code runs Built-in,
   every document says URP; the graphics pass was written for Built-in, so the cheap answer is to
   make the documents say so.

See `ROADMAP.md` for what comes after.
