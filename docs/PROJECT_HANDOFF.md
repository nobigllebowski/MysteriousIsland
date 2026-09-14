# PROJECT HANDOFF

**Read this first in any new session.** Fifteen minutes here saves you from re-deriving the
project — or worse, contradicting a decision that was already made.

---

## 1. What this is, in sixty seconds

**VARDHOLM** — a premium portrait mobile mystery-adventure for iOS and Android. Unity 6
(`6000.6.0f1`), C#, URP, UI Toolkit.

A disgraced marine acoustician is shipwrecked on an island that isn't on any chart, and discovers
a Bronze Age machine built to keep the ocean quiet — and the woman who has been repairing it alone
for thirty-one years. **The mystery is a maintenance problem.** No combat, no creatures, no
weapons; the signature tool is a microphone.

**Phase 1 is complete.** The project spine exists — five assemblies, ~18,500 lines — and has
**never been compiled**, because the environment it was written in has no Unity and no .NET SDK.

## 2. Read in this order

| Order | File | Why |
|---|---|---|
| 1 | `../CLAUDE.md` | Invariants you must not break, and the source-of-truth ordering |
| 2 | **`CURRENT_STATE.md`** | What is actually true now. **Includes six live CONFLICTS.** |
| 3 | `DECISIONS.md` | Every locked decision and where it came from |
| 4 | `ROADMAP.md` | Where we are and what is next |
| 5 | `ARCHITECTURE.md` | How the code is shaped and why |
| 6 | `GAME_VISION.md` | What the game is |
| 7 | `design/01-story-bible.md` | **The naming authority.** Consult before inventing any noun. |

Deep detail sits under `design/`, `architecture/` and `production/`. **Nine of those documents
predate the ADRs and were never reconciled with them** — Phase 1 Task 0 was specified and never
run. Always check `CURRENT_STATE.md` §Conflicts before trusting an older document.

## 3. The five things most likely to trip you up

1. **Nothing has been compiled.** No Unity, no .NET SDK, no Mono in the dev container, and the
   proxy blocks Microsoft's SDK downloads. 211 tests are written and 0 have run. Never claim
   otherwise — say what was verified and what needs the editor.
2. **The scenes do not exist.** `.unity` files must be authored by hand in the editor following
   `../Assets/Scenes/README.md`. The project cannot run until they are.
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
`6000.6.0f1` → *Add project from disk* → **author the four scenes** → Game view 390×844 portrait →
open `Bootstrap.unity` → Play. Tests: **Window → General → Test Runner → EditMode → Run All**.

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

**Reconcile the documentation (Phase 1 Task 0), then begin Phase 2.**

Task 0 was specified in the Phase 1 plan and never executed, which is why six conflicts are live.
It is a documentation-only change with no code risk:

1. `production/02-mvp-scope-and-roadmap.md` — `Isle.Domain`/`Isle.Presentation`/`Isle.Tests` →
   `ForgottenIsle.Core`/`.Game`/`.UI` (CONFLICT-1).
2. `architecture/03-data-and-save-architecture.md` — `SurvivalStat` → the ADR-0006 set; delete
   `ResourceDefinition` and the vessel model per ADR-0007 (CONFLICT-2, CONFLICT-3).
3. `design/04-first-30-minutes.md` beat 2:00 and `production/02-mvp-scope-and-roadmap.md` —
   tap-to-move → floating joystick. **This is a re-authoring, not a find-and-replace:** the beat
   teaches movement "by there being exactly one thing worth walking to", which only works for
   tap-to-move (CONFLICT-4).
4. `production/03-phase-1-plan.md` §4 — mark the uGUI decision superseded by ADR-0014
   (CONFLICT-5).
5. **Ask the owner to settle the world clock: 30× or 60×** (CONFLICT-6). Then make code and docs
   agree and pin it with a test.

Only then start Phase 2 — see `ROADMAP.md`.
