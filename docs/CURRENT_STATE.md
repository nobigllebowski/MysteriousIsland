# CURRENT STATE

**Updated:** 2026-09-14 · **Branch:** `claude/keen-darwin-frw656`
**Phase 1.5 (Unity integration) complete.** Phase 2 not started.

This file records what is *actually true right now*, verified against the repository — not what
was planned. When it disagrees with a design document, this file and the shipped code win.

---

## 1. What exists

| | Files | Lines | State |
|---|---:|---:|---|
| `ForgottenIsle.Core` (engine-free) | 36 | 4,113 | Written, never compiled |
| `ForgottenIsle.Game` | 21 | 6,527 | Written, never compiled |
| `ForgottenIsle.UI` (UI Toolkit) | 16 | 3,814 | Written, never compiled |
| Tests (207 EditMode cases, 4 PlayMode) | 8 | 4,004 | Written, **never executed** |
| `ForgottenIsle.Editor` (new) | 3 | ~550 | Written, never compiled |
| Documentation | 19 | — | 9 predate the ADRs, unreconciled |

**Implemented in Phase 1:** five assemblies; bootstrap and composition root; a 10-edge game state
machine; the single `Ticker`; additive scene loading with a 20 s watchdog; zone registry capped at
2 resident zones; session service; versioned save envelope with migration seam, atomic
write (flush→rename) and `.bak` rotation; 3 save slots + autosave; engine-free JSON/CSV;
localization with `#key#` fallback; New Input System routing; placeholder capsule player rig;
dev overlay; UI Toolkit framework, theme, and menu/pause/settings screens.

**Commands registered (5):** `StartNewGame`, `ResumeSavedRun`, `SaveGame`, `QuitToMenu`,
`TravelToZone`.
**Save participants registered (2):** session, player. ADR-0009 targets 14 at completion; each
later phase adds its own.

**Added in Phase 1.5:** the `ForgottenIsle.Editor` assembly (ADR-0001's fifth, previously missing);
`Vardholm → Setup Project` / `Validate Project` / `Open Bootstrap Scene` menu commands; a first-run
prompt on a fresh clone; `ZoneFurnisher`, which builds ground, light, entry anchor, player capsule
and camera for any zone scene that lacks them; `VardholmStartupValidator`, a PASS/WARN/FAIL startup
self-check; and runtime camera-pivot attachment, which removed the last Inspector-assigned
dependency in the project.

**Scene assets still do not exist in the repository**, but they are no longer created by hand —
one menu command creates all four. See `SCENE_CONTRACT.md`.

**Not implemented — no gameplay exists yet.** No inventory, items, combination, crafting,
discovery, survival, camp, puzzles, story, quests, weather, audio. The player rig is a greybox
capsule moved by transform, not a real character controller.

## 2. Verification status — read this before trusting anything

| Check | Result |
|---|---|
| `ci/validate-structure.py` | **exit 0** — 88 files, 106 public types, 0 errors, 47 warnings |
| `ci/check-layering.sh` | **exit 0** — all 5 invariants hold |
| Core references `UnityEngine` | **0 occurrences** outside comments |
| `Update()` methods | **exactly 1** (`Ticker.cs`) |
| Assembly reference direction | Core ← Game ← UI, no upward refs |
| Banned C# features | none present |
| **Compilation** | **UNCONFIRMED — never compiled.** No Unity, no .NET SDK, no Mono in the dev environment; the proxy blocks Microsoft SDK downloads. |
| **Tests** | **UNCONFIRMED — 211 tests written, 0 executed.** |

`ci/validate-structure.py` is a deliberate compiler substitute: brace balance, namespace
conformance, engine-free Core, asmdef validity, cross-file undeclared-type detection, LocKey
coverage, duplicate types. It is not a compiler and cannot prove the project builds.

## 3. CONFLICTS — unresolved contradictions in the documentation

**Root cause:** ADRs 0001–0014 declared reconciliations and assigned the edits to "Phase 1 Task 0".
**Task 0 was never executed.** The ADRs are correct; the older documents were never updated.

| # | Conflict | Authority | Stale source |
|---|---|---|---|
| **CONFLICT-1** | Assembly names: `Isle.Domain` / `Isle.Presentation` / `Isle.Tests` | **ADR-0001 + shipped code: `ForgottenIsle.Core/.Game/.UI`** | `production/02-mvp-scope-and-roadmap.md:269,284,379` |
| **CONFLICT-2** | `SurvivalStat` = `{Hydration, Warmth, Fatigue, Morale, RecorderCharge}` | **ADR-0006: `{Health, Energy, Hydration, Satiation, CoreTemp}`** | `architecture/03-data-and-save-architecture.md:333` and its `WarmthDrainPerHour` fields |
| **CONFLICT-3** | `ResourceDefinition` / resource-and-vessel inventory still specified | **ADR-0007 deletes it; water is a charged vessel item** | `architecture/03-data-and-save-architecture.md:252,412+` |
| **CONFLICT-4** | Locomotion is **tap-to-move**, "no virtual stick" | **ADR-0008 + shipped code: floating joystick** (`VardholmControls.Move` is a Vector2 stick) | `design/04-first-30-minutes.md:73`; `production/02-mvp-scope-and-roadmap.md:15,401` |
| **CONFLICT-5** | UI technology is **uGUI + TextMeshPro, "Not UI Toolkit"** | **ADR-0014 + shipped code: UI Toolkit** | `production/03-phase-1-plan.md:294` |
| **CONFLICT-6** | **Code contradicts spec.** World clock: docs say `1 real second = 30 world seconds` (48-min day); `Ticker.DefaultWorldSecondsPerRealSecond = 60.0` (24-min day) | **UNRESOLVED — needs a human decision** | `architecture/02-core-systems.md:523,872` vs `Assets/Scripts/Game/Bootstrap/Ticker.cs:51` |

**CONFLICT-6 is the one that matters most.** The survival tick formulas were tuned against 30×.
At 60× every drain rate is effectively doubled in real time, so the 36-hour water deadline that
Act 1 is built around arrives in half the intended wall-clock. Decide before any survival work.

**CONFLICT-4 has content cost.** The prologue's minute-2:00 beat teaches movement "by there being
exactly one thing worth walking to", which only works for tap-to-move. Adopting the joystick means
re-authoring that beat, not a find-and-replace.

## 4. UNCONFIRMED — believed but not verified

- **The project compiles.** Nothing here has been through a C# compiler.
- **The 211 tests pass.**
- **Every Unity 6 API claim** tagged `⚠ VERIFY` in `architecture/01-technical-architecture.md` §0:
  Render Graph, GPU Resident Drawer, Adaptive Probe Volumes, `Awaitable` allocation profile,
  `ProfilerRecorder` counter names, `AsyncOperation.progress` semantics with
  `allowSceneActivation = false`, Newtonsoft being engine-free, VContainer compatibility,
  TextMeshPro RTL shaping, `com.unity.addressables.android`.
- **`File.Replace` atomicity on Android scoped storage.** Acceptance item 28 is gating.
- **iPhone 12 per-app memory ceiling** (~2 GB assumed; the 1.35 GB working-set target is a design
  decision, not a measurement).
- **60 fps on iPhone 12 at native resolution.** Alpha-tested foliage overdraw — the dominant
  fragment cost in the jungle — is budgeted nowhere (open item O-3).
- **Every proper noun.** No trademark register has been searched, for any name, ever. "Vardholm"
  and "Orrimond" returned no web collisions; that is not clearance.
- **All package versions** in `Packages/manifest.json`.

## 5. Known gaps deliberately left open

- **O-8** — UI calls `GameStateMachine.TryTransition` directly instead of dispatching commands.
  Defensible (mode changes are navigation) but inconsistent with the rule its own screens follow.
- **O-9** — `MainMenu.unity` is documented as the cinematic backdrop but nothing loads it; the
  menu renders on a flat themed ground. `Assets/Scenes/README.md` carries a warning.
- Scene assets are generated by `Vardholm → Setup Project`, not committed. Unity scene files are
  editor-serialized YAML with GUID cross-references; hand-authoring one outside the editor risks a
  corrupt asset, which is worse than a missing one.
- **Two HIGH RISK compile areas** (`UNITY_RISK_AUDIT.md`): code-built input binding strings, which
  the compiler cannot check, and `experimental.animation` in three UI files.

## 6. How to get it running

`docs/dev-setup.md` — clean machine to running build. Summary: Unity Hub → install
`6000.6.0f1` → *Add project from disk* → **`Vardholm → Setup Project`** → Game view 390×844
portrait → open `Bootstrap.unity` → Play → read the `VARDHOLM STARTUP CHECK` block.
