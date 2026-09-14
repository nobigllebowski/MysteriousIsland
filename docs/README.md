# THE FORGOTTEN ISLE — Design & Architecture Documentation

**Status:** Phase 0 (design) — awaiting approval before any code is written.
**Recommended title:** VARDHOLM *(see `production/01-title-evaluation.md`)*

> **Original IP notice.** Every proper noun, character, document, zone, story beat and puzzle in
> this project is newly invented. Nothing is derived from *Return to Mysterious Island*, Verne, or
> any other existing work. See Story Bible §9 for the originality statement and §8 for the
> anti-inconsistency contract.

## Reading order

| # | Document | What it settles |
|---|---|---|
| 1 | [`design/01-story-bible.md`](design/01-story-bible.md) | **The naming authority.** Canon, cast, the secret, the five acts, the ending, the 10 rules of the fiction, the glossary. Everything downstream cites this. |
| 2 | [`design/02-game-vision.md`](design/02-game-vision.md) | The one-page pitch: fantasy, audience, differentiation, session length, commercial model. |
| 3 | [`design/06-core-game-loop.md`](design/06-core-game-loop.md) | The loop diagram, the three nested loops, the no-arrow rule and the hint ladder. |
| 4 | [`design/03-world-structure.md`](design/03-world-structure.md) | All 9 zones, the gating graph, the capability table, the 25-beat critical path, the anti-softlock rules. |
| 5 | [`design/04-first-30-minutes.md`](design/04-first-30-minutes.md) | The prologue shooting script, minute by minute. The free product. |
| 6 | [`design/05-mobile-ux-plan.md`](design/05-mobile-ux-plan.md) | Camera decision, controls, HUD, inventory, the combine flow, accessibility. ASCII wireframes throughout. |
| 7 | [`architecture/01-technical-architecture.md`](architecture/01-technical-architecture.md) | Assemblies, layering, commands, events, DI, streaming, performance, localization, CI. **Starts with a verification ledger.** |
| 8 | [`architecture/02-core-systems.md`](architecture/02-core-systems.md) | All 17 systems: API, owned state, dependencies, algorithms, failure modes, test matrix. |
| 9 | [`architecture/03-data-and-save-architecture.md`](architecture/03-data-and-save-architecture.md) | SO-vs-JSON decision, the 10 definition types, runtime state, the versioned save envelope and migration framework. |
| 10 | [`production/01-title-evaluation.md`](production/01-title-evaluation.md) | Five candidates scored, with live store searches and a clearance checklist. |
| 11 | [`production/02-mvp-scope-and-roadmap.md`](production/02-mvp-scope-and-roadmap.md) | MVP content manifest, what is excluded, risks, and Phases 0–16. |
| 12 | [`production/03-phase-1-plan.md`](production/03-phase-1-plan.md) | The executable Phase 1 plan: file manifest, code sketches, tests, acceptance criteria. |

## Standing rules

- **No user-facing string is ever hardcoded.** Localization keys only, enforced in CI.
- **UI never mutates state.** Mutation happens through commands, enforced structurally by assembly
  boundaries — not by convention.
- **`ForgottenIsle.Core` does not reference `UnityEngine`.** The rules of the game are a
  deterministic library that can be tested in seconds without opening the editor.
- **No engineer writes "Unity does X"** in a doc, PR or comment without a docs link or a
  `⚠ VERIFY` tag. The ledger in Technical Architecture §0 is amended, never deleted.
- **Nothing in this repository is legal clearance.** No trademark register has been searched.
