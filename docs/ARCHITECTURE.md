# ARCHITECTURE

Unity 6 (`6000.6.0f1`) · C# 9 · URP · New Input System · UI Toolkit · portrait mobile

The map. Deep detail lives in
[`architecture/01-technical-architecture.md`](architecture/01-technical-architecture.md) (layers,
performance, localization, CI),
[`architecture/02-core-systems.md`](architecture/02-core-systems.md) (all 17 systems) and
[`architecture/03-data-and-save-architecture.md`](architecture/03-data-and-save-architecture.md)
(definitions, runtime state, save).

> **Two of those three predate the ADRs and contain stale specifications.** See
> `CURRENT_STATE.md` §Conflicts before trusting them. The ADRs win.

---

## 1. Assemblies

```
ForgottenIsle.Core      no UnityEngine at all        the rules of the game, as a library
      ▲
ForgottenIsle.Game      UnityEngine + Input System   the game, as a Unity application
      ▲
ForgottenIsle.UI        UnityEngine + UI Toolkit     the game, as pixels and touches
      ▲
ForgottenIsle.Tests.EditMode / .PlayMode
```

**References point downward only.** There are no upward references and no cycles. To go up, you
define an interface below and inject it from above.

### Why `Core` is engine-free (ADR-0002)

`"noEngineReferences": true`, empty references array. No `Vector3`, no `ScriptableObject`, no
`Debug.Log`, no `Time.deltaTime`, no `Random`. This is not purity — it buys four concrete things:

1. The entire rules layer tests in seconds with no domain reload.
2. Determinism you can assert on.
3. The ability to fuzz command sequences in CI.
4. **A structural guarantee that UI cannot mutate state**, because the mutation APIs live in an
   assembly UI can only see through read-only types.

The substitutions: `Vec3`/`Vec3Math` for vector maths; `IClock` + a fixed `Tick(dt)` for time;
`PcgRandom` (seeded, state serialized into `GameState`) for randomness; `ICoreLog` with
`LogCode` enums — not strings — for diagnostics. File I/O is deliberately *not* in Core.

## 2. Layering

```
UI  →  Controllers  →  CommandDispatcher  →  Handlers  →  Systems  →  World State
                                   │
                              SignalBus ──→ back to UI (read-only)
```

**UI never mutates state.** A tap becomes a command; a handler validates then executes; state
changes publish a signal; UI re-renders. Enforced by `ci/check-layering.sh`, not by convention.

`ICommandHandler<T>` splits `Validate(in T) → ResultCode` from `Execute(in T)`, so UI can
speculatively validate (to grey out a button) with no side effects. `CommandDispatcher` never
throws — unknown commands return `NoHandler`, invalid ones return their code without executing.

**Known deviation (O-8):** UI currently calls `GameStateMachine.TryTransition` directly for
navigation rather than dispatching a command. Scheduled for Phase 2.

## 3. Game state machine — 10 legal edges

```
Boot → MainMenu → Loading → InGame ⇄ Paused
                     │  ↘ LoadFailed → MainMenu
                     ↓         
                  MainMenu          InGame → Loading,  Paused → Loading
```

Exactly: `Boot→MainMenu`, `MainMenu→Loading`, `Loading→InGame`, `Loading→MainMenu`,
`Loading→LoadFailed`, `LoadFailed→MainMenu`, `InGame→Paused`, `Paused→InGame`, `Paused→Loading`,
`InGame→Loading`.

There is deliberately **no direct `InGame→MainMenu`**: quit routes through `Loading` so the
curtain covers the scene unload. *(The original Phase 1 plan omitted `Loading→MainMenu` entirely,
which made QUIT TO MENU unreachable — see P-4.)*

## 4. Update strategy

**Exactly one `Update()` in the project** — `Ticker.cs`. It clamps `unscaledDeltaTime` to 0.25 s,
feeds `TickScheduler` (which caps at 12 ticks and discards the surplus), and resets its
accumulator on `OnApplicationPause`/`Focus`. A 40-minute background yields ~2 ticks on resume, not
a spike. `PlayerRig` and `DevOverlay` use `LateUpdate`.

> **CONFLICT-6:** `Ticker.DefaultWorldSecondsPerRealSecond = 60.0`; the design documents specify
> 30. Unresolved — see `CURRENT_STATE.md`.

## 5. Scenes and streaming (ADR-0004)

`Bootstrap` (persistent) + `MainMenu` + additive zone scenes. **Max 2 resident zones**;
`ZoneRegistry` hard-unloads the outgoing zone on transition, with the cap as a backstop.
`SceneLoader` is additive and async with a reported `Progress` and a 20-second watchdog that
routes to `LoadFailed` rather than hanging.

## 6. Data (ADR-0005) — designed, not yet built

ScriptableObjects are the **authoring** surface; a build-time bake produces immutable C# records
consumed by `Core` through `IDefinitionRepository`. The authoring surface's customer is a designer
in the Inspector; the consumption surface's customers are a mobile loader, a localisation
pipeline, a test runner and a save migrator. One artifact cannot serve both well.

The bake is also **where the gate lives**: "does every recipe have a reachable ingredient set?" is
asked automatically, every build, and blocks it. The catalog ships as an Addressable (a plain file
under `Assets/Data/` would not be included in a player build at all).

## 7. Save (ADR-0011, ADR-0013)

Versioned envelope: schema version, build version, timestamp, CRC32, a metadata header readable
without deserializing the body, and per-participant sections.

**The atomic write, in order:** write `.tmp` → `FileStream.Flush(true)` to force bytes to physical
storage → `File.Replace(tmp, final, bak)`. The flush must precede the rename, or the rename can win
the race and publish a valid filename over an empty file. A failed write never deletes the `.bak`.

`SaveSlotService` falls back to `.bak` on **any** read failure — not just `SaveCorrupt` — because
a mid-write kill surfaces as `SlotEmpty`, which is the exact case the backup exists for.

`SaveMigrator` walks a chain of `v(n)→v(n+1)` migrations. The chain is empty in Phase 1 but the
seam is real and exercised. A save newer than the build fails loudly rather than guessing.

Every system implements `ISaveParticipant` **in the same PR that adds it** (ADR-0011). Target 14
participants; 2 registered today.

## 8. Localization

Every user-facing string is a `LocKey` resolved through `ILocalizedText`. **A missing key renders
`#key#` — never blank, never a throw.** A blank button is unfindable in a shipped build;
`#ui.menu.continue#` is findable from a screenshot. A key present with an *empty value* is treated
as missing, for the same reason.

Authoring copy: `Assets/Localization/en.csv`. Shipped copy: `Assets/Resources/Localization/en.csv`.
They must stay byte-identical; the validator checks it.

## 9. The two gates

```bash
python3 ci/validate-structure.py   # compiler substitute
bash ci/check-layering.sh          # architecture invariants
```

**`validate-structure.py`** exists because there is no compiler in the dev environment. It checks
brace balance (real tokenizer, not a counter), namespace conformance, engine-free Core, asmdef
validity, **cross-file undeclared-type detection**, LocKey coverage against `en.csv`, duplicate
types, and CSV copy sync. It caught 18 real errors on first run. **It is not a compiler and cannot
prove the project builds.**

**`check-layering.sh`** enforces: no `UnityEngine` in Core; `noEngineReferences` set; Core's
references empty; UI never touches `SessionService`; no user-facing literals in UI screens. It
caught a violation two review agents missed.

## 10. Performance targets — UNCONFIRMED

60 fps on iPhone 12 and mid-range Android; 30 fps low-end fallback. Object pooling, LOD, baked
lighting, a GC-allocation ban in gameplay code, and an in-game overlay (`DevOverlay`).

**None of this is measured.** Alpha-tested foliage overdraw — the dominant fragment cost in the
jungle — is budgeted nowhere (O-3), and "native resolution, 60 fps, iPhone 12" is the
least-supported number in the package.

## 11. Ported infrastructure

27 files carry a `// Ported from NATION: WORLD ORDER` header. One-time copy, not a shared
dependency — no submodule, no package reference. What was deliberately **not** inherited: Nation's
`GameContext.Current` static singleton (a service locator, which ADR-0012 forbids) and its cold
blue palette (wrong for a tropical caldera). See
[`production/04-migration-plan.md`](production/04-migration-plan.md).
