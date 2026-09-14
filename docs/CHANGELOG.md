# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Part of the project memory system — see [`PROJECT_HANDOFF.md`](PROJECT_HANDOFF.md) for the
reading order. For what is true *right now* rather than what changed, see
[`CURRENT_STATE.md`](CURRENT_STATE.md).

## [Unreleased]

### Fixed — first editor open

- **`com.unity.inputsystem` pinned at `1.14.0`, which targets Unity `6000.1`, not `6000.6.0f1`.**
  The first attempt to open the project produced 88 × CS0619 inside the package's own
  `HIDDescriptorWindow.cs`, which uses `TreeViewState` / `TreeView` / `TreeViewItem` — deprecated
  in Unity 6.3 and treated as obsolete-as-error. Bumped to `1.19.0`, the version released for
  `6000.6`.
- Not a single error was in project code. Unity halts at the first failing assembly and package
  assemblies compile first, so **our C# still has not been compiled** — the risk audit's verdicts
  are unchanged, neither confirmed nor cleared.

### Added — Phase 1.5: Unity integration

Closes the gap between "the code exists" and "clone, open Unity, press Play".

- **`ForgottenIsle.Editor`** — ADR-0001's fifth assembly, which Phase 1 never created.
- **`Vardholm → Setup Project`** — one idempotent menu command that creates the four scene assets
  through `EditorSceneManager`, writes Build Settings in `SceneKeys.All` order, and verifies the
  localization resource. Plus `Validate Project` (read-only) and `Open Bootstrap Scene`.
- **First-run prompt** — a fresh clone offers setup on first editor load rather than failing
  mysteriously. Offered, not forced: silently writing assets on project open is the kind of
  surprise that makes a toolchain untrustworthy.
- **`ZoneFurnisher`** — builds ground, directional light, entry anchor, player capsule and camera
  for any zone scene that does not provide them. This is what makes an empty scene playable, and
  it is why the scene assets can stay empty and therefore un-corruptible. Authored art always
  wins; the furnisher only fills gaps.
- **`VardholmStartupValidator`** — a development-only startup self-check printing PASS / WARN /
  FAIL for services, scenes in Build Settings, localization, and duplicate bootstrap hosts.
  Editor and development builds only, compiled out of release by `[Conditional]`.
- **Runtime camera-pivot attachment** on `PlayerRig`, removing the last dependency in the project
  that required Inspector assignment. Nothing now needs manual wiring.
- **`docs/SCENE_CONTRACT.md`** — the dependency graph and what each scene must contain.
- **`docs/UNITY_RISK_AUDIT.md`** — per-subsystem compile risk, honestly rated.
- Two new test files: `StartupValidatorTests` (EditMode) and `FirstPlayableFlowTests` (PlayMode),
  the latter walking new game → furnished zone → pause → resume → save → quit → continue →
  restored, which is the loop that had no coverage at all.

### Fixed

- `AppBootstrap` searched a loaded zone for a player rig, found none in an empty Phase 1 scene,
  and **returned silently** — leaving the player in a zone with no body, no camera and nothing
  underfoot. It now furnishes the zone instead.
- The structural validator did not know `UnityEditor` types, so the new Editor assembly tripped
  its undeclared-type check. It now carries a separate `UNITY_EDITOR_TYPES` set, kept distinct so
  the editor-only boundary stays visible.
- `Assets/Scenes/README.md` and `docs/dev-setup.md` told the reader to build four scenes and their
  contents by hand. Both now point at the menu command.

### Added — Project memory system

Eight documents that make the repository the authoritative source of truth, so a session can be
picked up cold without re-deriving the project.

- **`CLAUDE.md`** (root) — operating rules, hard invariants and the source-of-truth ordering.
- **`docs/PROJECT_HANDOFF.md`** — the cold-start entry point and the exact next task.
- **`docs/CURRENT_STATE.md`** — verified present state, the verification ledger, and six live
  CONFLICTS.
- **`docs/DECISIONS.md`** — the decision ledger: 6 owner-locked decisions, 14 ADRs, 9 recovered
  design decisions, 4 producer amendments, 9 open items, and a list of decisions deliberately
  *not* made.
- **`docs/ARCHITECTURE.md`**, **`docs/GAME_VISION.md`**, **`docs/ROADMAP.md`** — the map, the
  pitch, the plan.
- The changelog moved from the repository root to `docs/`, with a pointer left behind.

**Recovered, not invented.** Every decision cites the instruction or document that established it.
Uncertain facts are marked `UNCONFIRMED`; contradictions are marked `CONFLICT` rather than being
silently resolved.

**The headline finding:** ADRs 0001–0014 assigned their document edits to "Phase 1 Task 0", and
**Task 0 was never executed**. Six contradictions are therefore live in the documentation,
including one where the shipped code contradicts the specification — the world clock is 60× in
`Ticker.cs` and 30× in the design documents, and the survival formulas were tuned at 30×.

### Added — Phase 1: Project Spine

The first code in the project. Five assemblies, ~16,000 lines of C# across 79 files.

- **`ForgottenIsle.Core`** — engine-free (`noEngineReferences: true`), so the rules of the game
  are a library that can be tested without opening Unity. Primitives (`Vec3`, `LocKey`,
  `ResultCode`), a seeded PCG random whose state serializes into the save, a signal bus, the
  command pattern with a validate/execute split, game state, the versioned save envelope with a
  real migration seam, engine-free JSON and CSV parsing, and localization.
- **`ForgottenIsle.Game`** — bootstrap and composition root, the state machine, the single
  `Update()` in the project, additive scene loading with a 20-second watchdog, the zone registry,
  the session service, an atomic save store (flush-then-rename with `.bak` rotation), input,
  a placeholder player rig and the dev overlay.
- **`ForgottenIsle.UI`** — UI Toolkit framework ported from NATION: WORLD ORDER and rethemed,
  plus the menu, pause and settings screens and their controllers.
- **Tests** — 7 EditMode files, 1 PlayMode file.
- **`ci/validate-structure.py`** — a static validator standing in for the compiler this
  environment does not have: brace balance, namespace conformance, the engine-free Core rule,
  asmdef validity, undeclared-type detection across files, LocKey coverage and duplicate types.
- **`ci/check-layering.sh`** — the architecture gate. It has already caught a real violation.

### Changed

- Title is **VARDHOLM**; the prior working title is retired from all document titles.
- ADR-0014 reverses the Phase 1 plan's uGUI decision in favour of UI Toolkit, because the team
  already owns a working code-built UI Toolkit framework in the Nation project.
- The Phase 1 plan's state-machine table was wrong: it had no edge into `MainMenu` from `InGame`
  or `Paused`, which made QUIT TO MENU unreachable. Corrected to 10 edges, routing quit through
  `Loading` so the curtain can cover scene unloading.

### Added — Phase 0: Design, Architecture, MVP, Story Foundation

Initial design and architecture documentation. **No game code yet** — Phase 0 is design only,
and implementation is blocked pending approval of the Phase 1 plan.

- **Story Bible v1.0** — canonical original IP: the island of Vardholm, protagonist Nadia Vesk,
  four occupation layers, the five-act structure, both endings, and a 10-rule anti-inconsistency
  contract. Establishes the project's naming authority and glossary.
- **Game Vision** — one-page pitch: player fantasy, audience, differentiation, session length and
  the free-prologue/premium-unlock commercial model.
- **Core Game Loop** — the loop diagram, three nested loops (micro/mid/macro), the no-arrow rule
  and the three-tier diegetic hint ladder.
- **World Structure** — nine zones fully specified, the gating graph, a capability/backtracking
  table, a 25-beat critical path and the anti-softlock rule set.
- **First 30 Minutes** — minute-by-minute prologue shooting script with the complete prologue item
  and recipe manifest, and the radio puzzle specified end to end.
- **Mobile UX Plan** — first-person recommendation with a cinematic Body Camera, full portrait
  control scheme, HUD, inventory and combine flow, and accessibility commitments.
- **Technical Architecture** — five assemblies with an engine-free `Core`, the command pattern,
  event flow, DI, scene streaming, performance and localization architecture, and CI enforcement.
  Opens with a verification ledger tagging every unconfirmed Unity 6 claim.
- **Core Systems** — all 17 systems specified with APIs, owned state, dependency graph, tick
  order, shipping-first-pass survival formulas and a named test matrix.
- **Data & Save Architecture** — ScriptableObject authoring baked to immutable records consumed by
  the engine-free Core, ten definition types, runtime state mirrors, and a versioned save envelope
  with a chain-of-migrations framework.
- **Title Evaluation** — five candidates scored against live store searches. Recommends renaming
  from *The Forgotten Isle* to **Vardholm**, with a clearance checklist.
- **MVP Scope & Phase Roadmap** — MVP content manifest, exclusions, risk register and Phases 0–16.
- **Phase 1 Plan** — the executable plan: preconditions, file manifest, code sketches, test list
  and acceptance criteria.

### Fixed — blockers found by adversarial review

A three-way review pass (IP risk, Unity 6 technical accuracy, cross-document consistency) raised
67 issues, 14 of them blockers. Resolved in this commit:

- **Title research error corrected.** The title evaluation claimed no exact "The Forgotten Isle"
  app existed. That was wrong: at least five games use the exact title on itch.io, including a
  $4.99 "mysterious survival-exploration experience" and one built "around immersive sound."
  Recorded with URLs, risk re-scored 2 → 1, and the working title is now recommended for
  retirement rather than carried.
- **"The Mercator Trust" renamed to "The Orrimond Trust"** throughout. The original collided with
  a real, live Guernsey trust company — and the fiction depicts an identically-named trust as
  negligent, which makes it worse than an ordinary name collision.
- **Core temperature formula corrected.** `k = 0.34` is an exponential rate that reaches
  hypothermia in ~3 minutes, not the ~55 the surrounding prose claimed. Re-derived to
  `k = 0.0187`, verified numerically, and a CI balance test now pins the three stated durations.
- **13 binding ADRs added** (`docs/architecture/00-decisions.md`) resolving the structural
  contradictions between the three architecture documents: one assembly layout, an engine-free
  Core with no exceptions, a C# language-level polyfill for `record`/`init` (the definition layer
  as written did not compile under Unity 6), one streaming model, one `SurvivalStat` enum, one
  inventory model, one locomotion scheme, a complete save participant list, and closure of two
  anti-softlock holes on the critical path.

### Known open items

Tracked in `docs/architecture/00-decisions.md` — unacknowledged LOST/DHARMA structural
convergence, proper-noun clearance, unbudgeted foliage overdraw, over-promised volumetric
effects, no privacy-manifest plan, and one unreconciled world-clock scale.

### Notes

- No trademark register has been searched. No name in this repository is cleared for use.
- All science in the fiction is dramatised, not claimed.
