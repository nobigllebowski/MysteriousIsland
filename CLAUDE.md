# CLAUDE.md — Vardholm

Operating rules for this repository. Read `docs/PROJECT_HANDOFF.md` at the start of any session.

**Game:** VARDHOLM — premium portrait mobile mystery-adventure. Unity 6 (`6000.6.0f1`), C#, **Built-in render pipeline** (the design docs say URP; the project has no URP package or pipeline asset — CONFLICT-7, shipped code wins).
**Branch:** `claude/keen-darwin-frw656` (the repository's only branch, and its default).
**Phase:** 3 implemented; the Phase 6 radio slice (set, dial, spectrogram, Slate, sightline,
hint ladders with the auto-sweep) is in. The project has compiled and run once; everything since
the graphics pass was traced by reading, not run. Phase 5 is blocked on CONFLICT-6.
See `docs/CURRENT_STATE.md`.
**Setup:** after cloning, run `Vardholm → Setup Project` in Unity. Never create scenes by hand.

---

## Source of truth

`docs/` is authoritative. Where documents disagree, this order wins:

1. **Shipped code** — for anything already implemented.
2. **`docs/DECISIONS.md`** — the decision ledger, including all 25 ADRs.
3. **`docs/architecture/00-decisions.md`** — the ADRs in full.
4. Everything else in `docs/`.

Nine design/architecture documents predate the ADRs and **were never reconciled with them**
(Phase 1 Task 0 was specified and never run). Six live conflicts are catalogued in
`docs/CURRENT_STATE.md` §Conflicts. **Check that list before trusting any older document.**

---

## Hard invariants — never break these

- **`ForgottenIsle.Core` never references `UnityEngine`.** Not `Vector3`, not `ScriptableObject`,
  not `Debug.Log`. `noEngineReferences: true` and an empty references array. (ADR-0002)
- **UI never mutates game state.** UI → controller → `CommandDispatcher` → handler. UI may not
  touch `SessionService`. (ADR-0002, enforced by `ci/check-layering.sh`)
- **No user-facing string literals.** `LocKey` + `ILocalizedText`, every key present in
  `Assets/Localization/en.csv`. A missing key renders `#key#`, never blank.
- **Exactly one `Update()`** in the project (`Ticker.cs`). Use it, don't add another.
- **Assembly references point downward only:** Core ← Game ← UI. Never upward.
- **No `required` members, no `ImmutableArray<T>`** — they do not compile here. (ADR-0003)
- **Every new system implements `ISaveParticipant` in the same PR that adds it.** (ADR-0011)
- **Never write "Unity does X"** without a docs link or a `⚠ VERIFY` tag.

## Game identity — locked, do not drift

The mystery is a **maintenance problem**. Never add: combat, weapons, monsters, chases, zombies,
loot rarity, crafting grind, base-building grind. Nothing supernatural is ever confirmed *or
teased*. The island never acts — people act. The signature tool is a microphone.

## Before you finish any code change

```bash
python3 ci/validate-structure.py   # must exit 0
bash ci/check-layering.sh          # must exit 0
```

Both gates must pass. **There is no Unity, no .NET SDK and no Mono in this environment**, so
nothing can be compiled or tested here. Never claim code compiles or tests pass — say what was
actually verified and what still needs the editor.

## Working style

- Update `docs/CURRENT_STATE.md` and `docs/CHANGELOG.md` when state changes.
- Record every non-obvious choice as an ADR in `docs/architecture/00-decisions.md`.
- Mark uncertain facts `UNCONFIRMED` and contradictions `CONFLICT`. Never invent a decision.
- Commit and push to the branch above. Do not start the next phase without being asked.
