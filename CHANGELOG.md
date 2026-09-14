# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
