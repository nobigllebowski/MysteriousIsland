# VARDHOLM

> **SURVIVE THE ISLAND. DISCOVER THE TRUTH.**

A premium, portrait-first mobile mystery-adventure for iOS and Android. Unity 6 LTS · C# · URP.

*A disgraced marine acoustician, shipwrecked on an island that isn't on any chart, discovers a
Bronze Age machine built to keep the ocean quiet — and the woman who has been repairing it alone
for thirty-one years.*

---

## Status

**Phase 1 — in progress.** Phase 0 design is approved and its decisions are locked (title, save
spine in Phase 1, premium business model, platform priority, game identity). Phase 1 implements
the project spine: assemblies, bootstrap, state machine, save system, scene navigation and the
UI Toolkit menu shell.

Core infrastructure is **ported from the team's NATION: WORLD ORDER project** rather than written
from scratch — see the [migration plan](docs/production/04-migration-plan.md).

| | |
|---|---|
| **Title** | **VARDHOLM** *(locked)*. Store subtitle: *Vardholm: A Survival Mystery*; keyword variant *Vardholm: Survival Mystery*. The earlier working title is retired — see [title evaluation](docs/production/01-title-evaluation.md). |
| **Genre** | Mystery adventure · exploration · environmental puzzle · light survival · crafting |
| **Platform** | iOS + Android, portrait. Landscape tablet is a possible future target. |
| **Engine** | Unity 6 LTS, C#, URP, New Input System |
| **Perspective** | First person, with an authored third-person "Body Camera" for ~14 cinematic beats |
| **Model** | *(locked)* Free 30–45 minute prologue → one-time full unlock. No ads, no energy system, no consumable currency, no pay-to-win, no purchase-pressure timers. |
| **Length** | 8–10 hour golden path, designed around 12–20 minute sessions |

## Start here

**[`docs/PROJECT_HANDOFF.md`](docs/PROJECT_HANDOFF.md)** — cold-start entry point: what this is,
what is true now, and the exact next task. Then
[`docs/README.md`](docs/README.md) for the full index.

`CLAUDE.md` in the repository root carries the operating rules and hard invariants.

The single most important document is the
**[Story Bible](docs/design/01-story-bible.md)**: it is the naming authority for the entire
project, and everything else cites it.

## What makes this different

- **The mystery is a maintenance problem.** Not a monster, not a curse. The final antagonistic
  pressure is an employment contract with a foundation that dissolved in 1994 and never told its
  last employee she could stop.
- **Listening is the mechanic.** The hydrophone resolves the world into a directional spectrogram.
  It is the navigation tool, the puzzle language and the thesis at once.
- **No combat, no creature threat, no weapons — ever.** The signature tool is a microphone.
- **Nothing supernatural is ever confirmed**, or dangled as a tease. Every effect has a stated
  physical cause.
- **Built portrait-first**, not ported.

## Original IP

Every proper noun, character, document, zone, story beat and puzzle in this project is newly
invented. Nothing is derived from *Return to Mysterious Island*, Jules Verne, or any other
existing game, film or book. See Story Bible §9.

**No trademark register has been searched. No name in this repository is cleared for use.**
See the [clearance checklist](docs/production/01-title-evaluation.md#5-clearance-checklist--before-the-name-is-safe-to-commit).

## Repository layout

```
docs/
  design/         Story bible, vision, core loop, world, prologue, mobile UX
  architecture/   Technical architecture, core systems, data & save
  production/     Title evaluation, MVP scope & roadmap, Phase 1 plan
CHANGELOG.md
```

```
Assets/Scripts/Core/    ForgottenIsle.Core   — engine-free domain. No UnityEngine, at all.
Assets/Scripts/Game/    ForgottenIsle.Game   — bootstrap, scenes, saves, input, session
Assets/Scripts/UI/      ForgottenIsle.UI     — UI Toolkit layer (ported framework)
Assets/Tests/           EditMode + PlayMode
ci/                     layering gate + structural validator
```

To open and run the project, follow [`docs/dev-setup.md`](docs/dev-setup.md).
